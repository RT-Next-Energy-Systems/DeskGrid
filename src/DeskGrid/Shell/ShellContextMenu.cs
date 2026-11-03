using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Shell;

/// <summary>
/// The menu Explorer shows when a file is right-clicked (Open, Send to, Cut, Copy, Delete, Properties, and whatever
/// the file type or installed apps add), asked of the shell itself. Use on the UI thread, which is an STA.
/// </summary>
/// <remarks>
/// Rename is left out (<c>CMF_CANRENAME</c> not set): it needs the view Explorer would host the item in to edit the
/// name, and there is none here.
/// </remarks>
internal sealed partial class ShellContextMenu : IDisposable
{
    // Command ids the shell may use, from QueryContextMenu's range; the one chosen is the verb offset + FirstCommand.
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;
    private const uint ExtendedVerbs = 0x100; // CMF_EXTENDEDVERBS: what Shift+right-click adds.
    private const uint InvokeUnicode = 0x4000; // CMIC_MASK_UNICODE
    private const uint InvokePoint = 0x20000000; // CMIC_MASK_PTINVOKE
    private const int ShiftKey = 0x10; // VK_SHIFT
    private const uint VerbUnicode = 0x4; // GCS_VERBW
    private const int TextLength = 256;

    // BHID_SFUIObject: ask an item for its UI objects (here the context menu).
    private static readonly Guid UiObjectHandler = new("3981E225-F559-11D3-8E3A-00C04F6837D5");

    private readonly IContextMenu _menu;
    private readonly HMENU _handle;

    private ShellContextMenu(IContextMenu menu, HMENU handle)
    {
        _menu = menu;
        _handle = handle;
    }

    /// <summary>How many entries (separators included) the shell put into the menu.</summary>
    public int ItemCount { get; private set; }

    /// <summary>
    /// Asks the shell for the menu of <paramref name="path"/>. Returns <see langword="null"/> if the shell has none
    /// (the file is gone, or its handlers failed).
    /// </summary>
    public static unsafe ShellContextMenu? Load(string path)
    {
        IContextMenu? menu = null;
        var handle = HMENU.Null;
        try
        {
            PInvoke.SHCreateItemFromParsingName(path, null, out IShellItem item).ThrowOnFailure();
            try
            {
                var handler = UiObjectHandler;
                var contextMenuId = typeof(IContextMenu).GUID;
                item.BindToHandler(null, &handler, &contextMenuId, out var raw);
                try
                {
                    menu = (IContextMenu)Marshal.GetObjectForIUnknown((nint)raw);
                }
                finally
                {
                    Marshal.Release((nint)raw);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }

            handle = PInvoke.CreatePopupMenu();
            var flags = IsShiftDown() ? ExtendedVerbs : 0;
            menu.QueryContextMenu(handle, 0, FirstCommand, LastCommand, flags).ThrowOnFailure();

            return new ShellContextMenu(menu, handle) { ItemCount = PInvoke.GetMenuItemCount(handle) };
        }
        catch (Exception exception) when (exception is COMException or System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error($"No shell context menu for \"{path}\"", exception);
            if (!handle.IsNull)
            {
                PInvoke.DestroyMenu(handle);
            }

            if (menu is not null)
            {
                Marshal.ReleaseComObject(menu);
            }

            return null;
        }
    }

    /// <summary>
    /// Shows the menu at <paramref name="at"/> (screen pixels), owned by <paramref name="owner"/>, and runs the command
    /// that is chosen. Returns when the menu is closed and the command has been started (or finished, for the ones that
    /// ask something first, like Delete).
    /// </summary>
    /// <returns><see langword="true"/> if a command was chosen.</returns>
    public unsafe bool Show(HwndSource owner, ScreenPoint at)
    {
        var window = new HWND(owner.Handle);
        MenuTheme.FollowSystem();

        // Submenus (Send to, Open with) fill themselves and owner-draw their items through these messages.
        var messages = new MenuMessages(_menu as IContextMenu2, _menu as IContextMenu3);
        owner.AddHook(messages.Hook);
        uint chosen;
        try
        {
            PInvoke.SetForegroundWindow(window);
            chosen = (uint)PInvoke.TrackPopupMenuEx(_handle,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
                at.X, at.Y, window, null).Value;
            PInvoke.PostMessage(window, PInvoke.WM_NULL, 0, 0);
        }
        finally
        {
            owner.RemoveHook(messages.Hook);
        }

        if (chosen < FirstCommand)
        {
            return false;
        }

        Invoke(chosen, window, at);
        return true;
    }

    /// <summary>
    /// The plain commands at the top level of the menu that have a canonical name (open, cut, copy, delete,
    /// properties...), for a menu of our own that offers some of them. Submenus, owner-drawn items and the
    /// ones that add themselves without a name (most extensions) are not included.
    /// </summary>
    public unsafe IReadOnlyList<Entry> ReadEntries()
    {
        var entries = new List<Entry>();
        var text = stackalloc char[TextLength];
        var verb = stackalloc char[TextLength];
        for (var position = 0u; position < ItemCount; position++)
        {
            var id = PInvoke.GetMenuItemID(_handle, (int)position);

            // 0 is a separator, -1 a submenu.
            if (id is 0 or uint.MaxValue || id < FirstCommand || id > LastCommand)
            {
                continue;
            }

            *verb = '\0';
            if (!TryGetVerb(id - FirstCommand, verb))
            {
                continue; // Not every handler can name its commands.
            }

            var length = PInvoke.GetMenuString(_handle, position, new PWSTR(text), TextLength, MENU_ITEM_FLAGS.MF_BYPOSITION);
            var name = new string(verb);
            if (name.Length > 0 && length > 0)
            {
                entries.Add(new Entry(name, DisplayText(new string(text, 0, length)), id));
            }
        }

        return entries;
    }

    // IContextMenu::GetCommandString through the vtable (slot 5, after IUnknown's three and QueryContextMenu and
    // InvokeCommand): the wrapper generated for it throws NotImplementedException.
    private unsafe bool TryGetVerb(uint offset, char* verb)
    {
        var self = Marshal.GetComInterfaceForObject<IContextMenu, IContextMenu>(_menu);
        try
        {
            var getCommandString = (delegate* unmanaged[Stdcall]<nint, nuint, uint, uint*, char*, uint, int>)(*(nint**)self)[5];
            return getCommandString(self, offset, VerbUnicode, null, verb, TextLength) >= 0;
        }
        finally
        {
            Marshal.Release(self);
        }
    }

    /// <summary>Runs a command read by <see cref="ReadEntries"/>, as if it had been chosen from the shell's own menu.</summary>
    public void Run(Entry entry, HwndSource owner, ScreenPoint at) => Invoke(entry.Command, new HWND(owner.Handle), at);

    /// <summary>One command of the shell's menu: its canonical name, the text the menu shows, and its id.</summary>
    public sealed record Entry(string Verb, string Text, uint Command);

    // "Cop&y\tCtrl+C" and "複製(&C)" both become the plain name.
    private static string DisplayText(string text)
    {
        var tab = text.IndexOf('\t');
        if (tab >= 0)
        {
            text = text[..tab];
        }

        return MnemonicMarks().Replace(text, "").Trim();
    }

    [GeneratedRegex(@"\(&.\)|&")]
    private static partial Regex MnemonicMarks();

    private unsafe void Invoke(uint command, HWND window, ScreenPoint at)
    {
        // The id is the verb's offset in the lower word, as MAKEINTRESOURCE.
        var verb = command - FirstCommand;
        var invoke = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
            fMask = InvokeUnicode | InvokePoint,
            hwnd = window,
            lpVerb = new PCSTR((byte*)verb),
            lpVerbW = new PCWSTR((char*)verb),
            nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            ptInvoke = new System.Drawing.Point(at.X, at.Y),
        };

        try
        {
            _menu.InvokeCommand((CMINVOKECOMMANDINFO*)&invoke);
        }
        catch (Exception exception) when (exception is COMException or System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error("Running a shell context menu command failed", exception);
        }
    }

    // The menu object itself is left to the finalizer: a command that runs on after Show returns (the properties
    // window, Open with) may still be using what it holds.
    public void Dispose() => PInvoke.DestroyMenu(_handle);

    private static bool IsShiftDown() => (PInvoke.GetAsyncKeyState(ShiftKey) & 0x8000) != 0;

    /// <summary>Hands the menu messages of the owner window to the shell's menu handlers.</summary>
    private sealed class MenuMessages(IContextMenu2? menu2, IContextMenu3? menu3)
    {
        public unsafe nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            var isMenuMessage = (uint)message is PInvoke.WM_INITMENUPOPUP or PInvoke.WM_MENUCHAR
                || ((uint)message is PInvoke.WM_DRAWITEM or PInvoke.WM_MEASUREITEM && wParam == 0);
            if (!isMenuMessage)
            {
                return 0;
            }

            try
            {
                if (menu3 is not null)
                {
                    LRESULT result = default;
                    menu3.HandleMenuMsg2((uint)message, new WPARAM((nuint)wParam), new LPARAM(lParam), &result);
                    handled = true;
                    return result;
                }

                if (menu2 is not null)
                {
                    menu2.HandleMenuMsg((uint)message, new WPARAM((nuint)wParam), new LPARAM(lParam));
                    handled = true;

                    // Owner-draw messages are answered with TRUE.
                    return (uint)message is PInvoke.WM_DRAWITEM or PInvoke.WM_MEASUREITEM ? 1 : 0;
                }
            }
            catch (COMException)
            {
                // A handler answers a failure code for a message that isn't for one of its items (several handlers
                // share the menu): the window's own handling goes on.
            }

            return 0;
        }
    }
}
