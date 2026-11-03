using System.Windows.Threading;
using DeskGrid.Desktop;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Widgets;

/// <summary>
/// Follows the rubber band Explorer draws when the mouse is dragged over the bare desktop, so the widgets it touches
/// can be selected with the icons. Explorer does the drawing and the selecting of its icons; this only reads the mouse.
/// </summary>
/// <remarks>
/// Any press on the desktop is reported by <see cref="DesktopHost.DesktopPressed"/> (and again when a drag takes over
/// the mouse). A press on an icon is no rubber band; neither is one on a widget, which Explorer never hears of.
/// </remarks>
internal sealed class MarqueeSelector : IDisposable
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(20);

    // Virtual-key codes of the mouse buttons (winuser.h).
    private const int LeftButton = 0x01;
    private const int RightButton = 0x02;

    private readonly DesktopHost _host;
    private readonly DispatcherTimer _watch = new() { Interval = WatchInterval };
    private ScreenPoint _pressedAt;
    private bool _banding;

    public MarqueeSelector(DesktopHost host)
    {
        _host = host;
        _watch.Tick += (_, _) => Watch();
        _host.DesktopPressed += OnPressed;
    }

    /// <summary>The mouse went down on the desktop: whatever was selected before no longer is.</summary>
    public event EventHandler? Started;

    /// <summary>The rubber band is now this big (screen pixels); raised as it grows and shrinks.</summary>
    public event EventHandler<ScreenRect>? Changed;

    public void Dispose()
    {
        _host.DesktopPressed -= OnPressed;
        _watch.Stop();
    }

    private static bool PrimaryButtonDown()
    {
        var swapped = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SWAPBUTTON) != 0;
        return (PInvoke.GetAsyncKeyState(swapped ? RightButton : LeftButton) & 0x8000) != 0;
    }

    private static bool IsBeyondDragThreshold(ScreenPoint from, ScreenPoint to) =>
        Math.Abs(to.X - from.X) >= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG)
        || Math.Abs(to.Y - from.Y) >= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG);

    private void OnPressed(object? sender, EventArgs e)
    {
        // Also raised when the drag takes the mouse: that is the same gesture, not a new press.
        if (_watch.IsEnabled || !PrimaryButtonDown())
        {
            return;
        }

        Started?.Invoke(this, EventArgs.Empty);

        var at = ScreenPoint.AtCursor();
        if (_host.ReadIconGrid(log: false) is { } grid && grid.IconAt(at) is not null)
        {
            return; // On an icon: it will be dragged or opened, not banded around.
        }

        (_pressedAt, _banding) = (at, false);
        _watch.Start();
    }

    private void Watch()
    {
        if (!PrimaryButtonDown())
        {
            _watch.Stop();
            return;
        }

        var cursor = ScreenPoint.AtCursor();
        if (!_banding && !IsBeyondDragThreshold(_pressedAt, cursor))
        {
            return; // Still a click.
        }

        _banding = true;
        Changed?.Invoke(this, ScreenRect.Between(_pressedAt, cursor));
    }
}
