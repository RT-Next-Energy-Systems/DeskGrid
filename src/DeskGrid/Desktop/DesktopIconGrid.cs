using System.Drawing;
using System.Runtime.InteropServices;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Desktop;

/// <summary>One desktop icon.</summary>
/// <param name="Index">The icon's item index in Explorer's list view.</param>
/// <param name="Cell">The icon's cell, in physical screen pixels.</param>
/// <param name="ViewPosition">The icon's position as the list view reports it, in its client coordinates.</param>
/// <param name="Image">Where the icon's picture is (inside the cell, above the name), in physical screen pixels.</param>
internal sealed record DesktopIcon(int Index, ScreenRect Cell, Point ViewPosition, ScreenRect Image);

/// <summary>
/// The desktop's own icon grid, read from Explorer's icon list view: the size of one icon cell and where
/// the icons currently sit. Widgets use it to line up with the icons and to stay off them.
/// </summary>
/// <param name="Anchor">The top-left corner of one icon cell (all cells lie on the lattice through it).</param>
/// <param name="Icons">Every icon on the desktop.</param>
/// <param name="AutoArranged">Explorer arranges the icons itself ("Auto arrange icons"), so they can't be moved.</param>
internal sealed record DesktopIconGrid(int CellWidth, int CellHeight, ScreenPoint? Anchor, IReadOnlyList<DesktopIcon> Icons, bool AutoArranged)
{
    // Messages of the list view (commctrl.h): LVM_FIRST = 0x1000.
    private const uint GetItemCount = 0x1004;
    private const uint GetItemRect = 0x100E;
    private const uint SetItemPosition = 0x100F;
    private const uint GetItemPosition = 0x1010;
    private const int IconRectangle = 1; // LVIR_ICON
    private const uint GetItemSpacing = 0x1033;
    private const uint GetItemTextW = 0x1073;
    private const int AutoArrangeStyle = 0x0100; // LVS_AUTOARRANGE
    private const int MaxIcons = 2000;
    private const int MaxNameLength = 260;
    private const uint Timeout = 500;

    /// <summary>Reads the grid, or returns <see langword="null"/> if Explorer doesn't answer.</summary>
    public static unsafe DesktopIconGrid? Read(IconLayer layer)
    {
        var listView = layer.FindListView();
        if (listView.IsNull)
        {
            return null;
        }

        // Large icon view; the result packs the cell's width and height into one value.
        if (!TrySend(listView, GetItemSpacing, 0, 0, out var spacing))
        {
            return null;
        }

        var (cellWidth, cellHeight) = ((int)(spacing & 0xFFFF), (int)((spacing >> 16) & 0xFFFF));
        if (cellWidth < 16 || cellHeight < 16 || !TrySend(listView, GetItemCount, 0, 0, out var count))
        {
            return null;
        }

        var origin = new Point(0, 0);
        PInvoke.ClientToScreen(listView, ref origin);
        var positions = ReadPositions(listView, (int)Math.Min(count, MaxIcons));
        if (positions is null)
        {
            return null;
        }

        // The list view reports where each icon image is; the cell is centered on it horizontally and starts at its top.
        var icons = positions
            .Select((p, index) =>
            {
                var left = origin.X + (p.Icon.left + p.Icon.right - cellWidth) / 2;
                var cell = new ScreenRect(left, origin.Y + p.Position.Y, left + cellWidth, origin.Y + p.Position.Y + cellHeight);
                var image = new ScreenRect(origin.X + p.Icon.left, origin.Y + p.Icon.top, origin.X + p.Icon.right, origin.Y + p.Icon.bottom);
                return new DesktopIcon(index, cell, p.Position, image);
            })
            .ToList();

        var anchor = icons.Count > 0 ? new ScreenPoint(icons[0].Cell.Left, icons[0].Cell.Top) : (ScreenPoint?)null;
        var autoArranged = (PInvoke.GetWindowLong(listView, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & AutoArrangeStyle) != 0;
        return new DesktopIconGrid(cellWidth, cellHeight, anchor, icons, autoArranged);
    }

    /// <summary>The height of the rectangle the list view reports for an icon's picture (physical pixels; the picture plus a little padding), or <see langword="null"/> without icons.</summary>
    public int? ImageHeight => Icons.Count > 0 ? Icons[0].Image.Height : null;

    /// <summary>How far that rectangle is below the top of its cell (physical pixels).</summary>
    public int ImageTop => Icons.Count > 0 ? Math.Max(0, Icons[0].Image.Top - Icons[0].Cell.Top) : 0;

    /// <summary>The icon whose cell <paramref name="point"/> is in, or <see langword="null"/>.</summary>
    public DesktopIcon? IconAt(ScreenPoint point) => Icons.FirstOrDefault(icon => icon.Cell.Contains(point));

    /// <summary>Moves <paramref name="icon"/> so its cell's top-left corner is at <paramref name="cell"/>.</summary>
    public static bool Move(IconLayer layer, DesktopIcon icon, ScreenPoint cell)
    {
        var listView = layer.FindListView();
        var x = icon.ViewPosition.X + cell.X - icon.Cell.Left;
        var y = icon.ViewPosition.Y + cell.Y - icon.Cell.Top;

        // The position travels in the message itself (MAKELPARAM), so nothing has to be written into Explorer.
        var position = (nint)(uint)((ushort)x | ((ushort)y << 16));
        return !listView.IsNull && TrySend(listView, SetItemPosition, (nuint)icon.Index, position, out _);
    }

    /// <summary>
    /// The name Explorer shows under each of the first <paramref name="count"/> icons (by item index), or
    /// <see langword="null"/> if Explorer doesn't answer.
    /// </summary>
    public static unsafe string[]? ReadNames(IconLayer layer, int count)
    {
        var listView = layer.FindListView();
        if (listView.IsNull)
        {
            return null;
        }

        // The item description first, the text it points to right behind it.
        var textOffset = (nuint)sizeof(LVITEMW);
        using var remote = RemoteBuffer.Allocate(listView, textOffset + MaxNameLength * sizeof(char));
        if (remote is null)
        {
            return null;
        }

        var names = new string[count];
        var text = new char[MaxNameLength];
        for (var index = 0; index < count; index++)
        {
            var item = new LVITEMW
            {
                mask = LIST_VIEW_ITEM_FLAGS.LVIF_TEXT,
                iItem = index,
                pszText = new PWSTR((char*)(remote.Address + (nint)textOffset)),
                cchTextMax = MaxNameLength,
            };
            if (!remote.Write(item)
                || !TrySend(listView, GetItemTextW, (nuint)index, remote.Address, out var length)
                || !remote.Read(textOffset, text, (int)Math.Min(length, MaxNameLength)))
            {
                return null;
            }

            names[index] = new string(text, 0, (int)Math.Min(length, MaxNameLength));
        }

        return names;
    }

    // LVM_GETITEMPOSITION and LVM_GETITEMRECT write into the caller's memory, which must be in Explorer's address space.
    private static List<(Point Position, RECT Icon)>? ReadPositions(HWND listView, int count)
    {
        if (count == 0)
        {
            return [];
        }

        using var remote = RemoteBuffer.Allocate(listView, (nuint)Marshal.SizeOf<RECT>());
        if (remote is null)
        {
            return null;
        }

        var items = new List<(Point, RECT)>(count);
        for (var index = 0; index < count; index++)
        {
            // The rectangle's left field says which part of the item is asked for.
            var iconRequest = new RECT { left = IconRectangle };
            if (!TrySend(listView, GetItemPosition, (nuint)index, remote.Address, out var found) || found == 0
                || !remote.Read(0, out Point position)
                || !remote.Write(iconRequest)
                || !TrySend(listView, GetItemRect, (nuint)index, remote.Address, out found) || found == 0
                || !remote.Read(0, out RECT icon))
            {
                return null;
            }

            items.Add((position, icon));
        }

        return items;
    }

    // Never block on a hung Explorer.
    private static bool TrySend(HWND window, uint message, nuint wParam, nint lParam, out nuint result)
    {
        var sent = PInvoke.SendMessageTimeout(window, message, wParam, lParam, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, Timeout, out result);
        return sent != 0;
    }

    /// <summary>A block of memory in Explorer's address space, for messages that write into the caller's memory.</summary>
    private sealed unsafe class RemoteBuffer : IDisposable
    {
        private readonly SafeHandle _process;
        private readonly void* _memory;

        private RemoteBuffer(SafeHandle process, void* memory)
        {
            _process = process;
            _memory = memory;
        }

        public nint Address => (nint)_memory;

        /// <summary>Allocates <paramref name="size"/> bytes in the process that owns <paramref name="window"/>.</summary>
        public static RemoteBuffer? Allocate(HWND window, nuint size)
        {
            if (PInvoke.GetWindowThreadProcessId(window, out var processId) == 0)
            {
                return null;
            }

            var process = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ | PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE,
                false, processId);
            if (process.IsInvalid)
            {
                Log.Info("Could not open Explorer to read the desktop icons.");
                process.Dispose();
                return null;
            }

            var memory = PInvoke.VirtualAllocEx(process, null, size,
                VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);
            if (memory is null)
            {
                process.Dispose();
                return null;
            }

            return new RemoteBuffer(process, memory);
        }

        public bool Write<T>(in T value)
            where T : unmanaged
        {
            fixed (T* source = &value)
            {
                return PInvoke.WriteProcessMemory(Handle, _memory, source, (nuint)sizeof(T), null);
            }
        }

        public bool Read<T>(nuint offset, out T value)
            where T : unmanaged
        {
            T result;
            var read = PInvoke.ReadProcessMemory(Handle, (byte*)_memory + offset, &result, (nuint)sizeof(T), null);
            value = result;
            return read;
        }

        /// <summary>Reads <paramref name="count"/> characters starting at byte <paramref name="offset"/>.</summary>
        public bool Read(nuint offset, char[] destination, int count)
        {
            fixed (char* target = destination)
            {
                return PInvoke.ReadProcessMemory(Handle, (byte*)_memory + offset, target, (nuint)count * sizeof(char), null);
            }
        }

        public void Dispose()
        {
            PInvoke.VirtualFreeEx(Handle, _memory, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE);
            _process.Dispose();
        }

        private HANDLE Handle => new(_process.DangerousGetHandle());
    }
}
