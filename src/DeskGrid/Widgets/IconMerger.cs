using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Folders;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Widgets;

/// <param name="TargetPath">The file whose desktop icon the items were dropped on.</param>
/// <param name="TargetCell">The cell of that icon, in physical pixels.</param>
/// <param name="DroppedPaths">What was dropped.</param>
/// <param name="Drop">The drop; the handler sets its effect.</param>
internal sealed record IconMergeEventArgs(string TargetPath, ScreenRect TargetCell, string[] DroppedPaths, DragEventArgs Drop);

/// <summary>
/// Drag a desktop icon onto another one, like on a phone, and the two become a folder. Explorer takes such a drop
/// itself (as "open with" or not at all), so while icons are being dragged, an invisible drop target sits over the
/// icon under the cursor; letting go over it raises <see cref="MergeRequested"/>.
/// </summary>
/// <remarks>
/// Nothing is asked of Explorer while the drag runs (it waits for us in each drag callback): the icons are read when
/// the mouse goes down, the target is put up once the mouse has moved far enough to be dragging, and taken down
/// a moment after the button comes up. One window follows the cursor from icon to icon: making a window for every
/// icon takes about 25 ms each, which would freeze the start of the drag.
/// </remarks>
internal sealed class IconMerger : IDisposable
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(20);

    // The drop is delivered while the button comes up; the target stays a little longer so it can arrive.
    private const long ReleaseGraceMs = 500;

    // Virtual-key codes of the mouse buttons (winuser.h).
    private const int LeftButton = 0x01;
    private const int RightButton = 0x02;

    // Far outside any screen: the window is a child of the desktop, so it is clipped away there.
    private static readonly ScreenPoint Parked = new(-30000, -30000);

    private static readonly Brush IdleBrush = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))); // Invisible, but hit.
    private static readonly Brush HoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));

    private readonly DesktopHost _host;
    private readonly Func<DragEventArgs, DragDropEffects> _dropEffect;
    private readonly DispatcherTimer _watch = new() { Interval = WatchInterval };
    private readonly Border _zone = new() { CornerRadius = new CornerRadius(8), Background = IdleBrush, AllowDrop = true };

    // The mouse went down on this icon (of this grid) and may start dragging it.
    private DesktopIconGrid? _grid;
    private DesktopIcon? _pressed;
    private ScreenPoint _pressedAt;

    // Once dragging: where a drop makes a folder, the window that takes the drop, and which of those it is over now.
    private List<Target> _targets = [];
    private DesktopSurface? _surface;
    private Target? _current;
    private long _releasedAt;

    /// <param name="dropEffect">The cursor to show for a drop that would make a folder.</param>
    public IconMerger(DesktopHost host, Func<DragEventArgs, DragDropEffects> dropEffect)
    {
        _host = host;
        _dropEffect = dropEffect;
        _watch.Tick += (_, _) => Watch();
        _zone.DragEnter += (_, e) => Hover(e);
        _zone.DragOver += (_, e) => Hover(e);
        _zone.DragLeave += (_, _) => _zone.Background = IdleBrush;
        _zone.Drop += (_, e) => Drop(e);
        _host.DesktopPressed += OnPressed;
    }

    public event EventHandler<IconMergeEventArgs>? MergeRequested;

    public void Dispose()
    {
        _host.DesktopPressed -= OnPressed;
        Stop();
    }

    // An icon that a drop makes a folder with, and the screen area where the drop counts.
    private sealed record Target(DesktopIcon Icon, string Path, ScreenRect Area);

    private static bool PrimaryButtonDown()
    {
        var swapped = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SWAPBUTTON) != 0;
        return (PInvoke.GetAsyncKeyState(swapped ? RightButton : LeftButton) & 0x8000) != 0;
    }

    private static bool IsBeyondDragThreshold(ScreenPoint from, ScreenPoint to) =>
        Math.Abs(to.X - from.X) >= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG)
        || Math.Abs(to.Y - from.Y) >= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG);

    private static string[] Dropped(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];

    // Dropping an icon on itself (or on another one dragged along with it) makes no folder.
    private static bool Accepts(string target, DragEventArgs e) =>
        Dropped(e) is { Length: > 0 } paths
        && !paths.Any(path => string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase));

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    // Called for every mouse press on the desktop, and again when a drag takes over the mouse.
    private void OnPressed(object? sender, EventArgs e)
    {
        if (_watch.IsEnabled || !PrimaryButtonDown())
        {
            return;
        }

        var at = ScreenPoint.AtCursor();
        if (_host.ReadIconGrid(log: false) is not { } grid || grid.IconAt(at) is not { } pressed)
        {
            return; // Not on an icon: a click on the background, or a rubber band.
        }

        (_grid, _pressed, _pressedAt, _releasedAt) = (grid, pressed, at, 0);
        _watch.Start();
    }

    private void Watch()
    {
        if (PrimaryButtonDown())
        {
            if (_releasedAt != 0)
            {
                // Pressed again before the last drag's target came down: that drag is over, this may be a new one
                // (whose press was let pass while this watch was still running).
                Stop();
                OnPressed(this, EventArgs.Empty);
                return;
            }

            if (_grid is not null && IsBeyondDragThreshold(_pressedAt, ScreenPoint.AtCursor()))
            {
                BeginDrag(_grid, _pressed!);
            }

            Follow();
            return;
        }

        if (_surface is null)
        {
            Stop(); // Only a click.
        }
        else if (_releasedAt == 0)
        {
            _releasedAt = Environment.TickCount64;
        }
        else if (Environment.TickCount64 - _releasedAt >= ReleaseGraceMs)
        {
            Stop();
        }
    }

    private void BeginDrag(DesktopIconGrid grid, DesktopIcon pressed)
    {
        _grid = null; // Whatever happens, once.
        var names = _host.ReadIconNames(grid.Icons.Count);
        if (names is null)
        {
            Log.Info("Could not read the names of the desktop icons; dragging an icon onto another makes no folder.");
            return;
        }

        var files = DesktopFiles.ByDisplayName(FolderPaths.Desktops);
        _targets =
        [
            .. from icon in grid.Icons
               where icon.Index != pressed.Index
               let path = files.GetValueOrDefault(names[icon.Index])
               // A folder on the desktop stays what it is to Explorer: something to drop things into.
               where path is not null && !Directory.Exists(path)
               // The icon's cell down to the bottom of its picture; the name below it is left to Explorer.
               select new Target(icon, path,
                   new ScreenRect(icon.Cell.Left, icon.Cell.Top, icon.Cell.Right, Math.Max(icon.Image.Bottom, icon.Cell.Top + 1))),
        ];
        Log.Info($"Dragging a desktop icon; {_targets.Count} icon(s) can be dropped on to make a folder.");

        if (_targets.Count > 0)
        {
            _surface = new DesktopSurface(_zone, Parked);
            _host.Add(_surface);
        }
    }

    // Puts the drop target over the icon the cursor is on, or out of the way if it is on none.
    private void Follow()
    {
        if (_surface is null)
        {
            return;
        }

        var cursor = ScreenPoint.AtCursor();
        var target = _targets.FirstOrDefault(t => t.Area.Contains(cursor));
        if (target == _current)
        {
            return;
        }

        _current = target;
        _zone.Background = IdleBrush;
        if (target is null)
        {
            _surface.MoveTo(Parked);
            return;
        }

        var scale = 96.0 / MonitorArea.At(new ScreenPoint(target.Area.Left, target.Area.Top)).Dpi;
        (_zone.Width, _zone.Height) = (target.Area.Width * scale, target.Area.Height * scale);
        _surface.MoveTo(new ScreenPoint(target.Area.Left, target.Area.Top));
    }

    private void Hover(DragEventArgs e)
    {
        var accepted = _current is { } target && Accepts(target.Path, e);
        e.Effects = accepted ? _dropEffect(e) : DragDropEffects.None;
        _zone.Background = accepted ? HoverBrush : IdleBrush;
        e.Handled = true;
    }

    private void Drop(DragEventArgs e)
    {
        _zone.Background = IdleBrush;
        if (_current is { } target && Accepts(target.Path, e))
        {
            MergeRequested?.Invoke(this, new IconMergeEventArgs(target.Path, target.Icon.Cell, Dropped(e), e));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Stop()
    {
        _watch.Stop();
        if (_surface is { } surface)
        {
            _host.Remove(surface);
        }

        _surface = null;
        _targets = [];
        (_grid, _pressed, _current, _releasedAt) = (null, null, null, 0);
        _zone.Background = IdleBrush;
    }
}
