using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskGrid.Desktop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Island;

/// <summary>
/// What the island's windows share: they sit at the top centre of the primary screen (or where the user put the island, on
/// any screen), above other windows, and never take focus.
/// </summary>
internal static class TopEdge
{
    private static readonly HWND Topmost = new(-1);

    /// <summary>The height in DIPs at the top of the island's window that holds the pill (one line).</summary>
    internal const double PillRoom = 36;

    /// <summary>The top-left of a window <paramref name="width"/> pixels wide: centred on the work area, <paramref name="gap"/> pixels below its top edge.</summary>
    internal static ScreenPoint Center(ScreenRect workArea, int width, int gap) =>
        new(workArea.Left + (workArea.Width - width) / 2, workArea.Top + gap);

    /// <summary>
    /// Makes the window a tool window that is never activated (no Alt+Tab entry, no focus taken) and has it follow a change of
    /// DPI: Windows then proposes a size and place of its own (the old ones scaled), which WPF takes, and the window
    /// puts itself back (with <paramref name="reposition"/> if given, else at the top centre). Call it once the window has
    /// its handle (<c>OnSourceInitialized</c>).
    /// </summary>
    public static void Prepare(Window window, double width, double height, double gap, Action? reposition = null)
    {
        var handle = new HWND(new WindowInteropHelper(window).Handle);
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        _ = PInvoke.SetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (int)(style | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
        HwndSource.FromHwnd(handle).AddHook((nint _, int message, nint _, nint _, ref bool _) =>
        {
            if ((uint)message == PInvoke.WM_DPICHANGED)
            {
                window.Dispatcher.BeginInvoke(() =>
                {
                    (window.Width, window.Height) = (width, height);
                    if (reposition is not null)
                    {
                        reposition();
                    }
                    else
                    {
                        Place(handle, width, height, gap);
                    }
                }, DispatcherPriority.Send);
            }

            return 0;
        });
    }

    /// <summary>Moves the window to the top centre of the primary screen, topmost, in that screen's pixels; sizes are in DIPs.</summary>
    public static void Place(HWND window, double width, double height, double gap)
    {
        var monitor = MonitorArea.Primary;
        var pixels = monitor.ToPixels(width);
        var position = Center(Screen(), pixels, monitor.ToPixels(gap));
        PInvoke.SetWindowPos(window, Topmost, position.X, position.Y, pixels, monitor.ToPixels(height), SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Moves the window to where the user put the island: <paramref name="anchor"/>, in physical screen pixels, is the
    /// middle of the window's top edge. The size is that of the screen it is on (each screen has its own DPI). An anchor that
    /// is on no screen any more (one was unplugged) is brought onto the nearest one.
    /// </summary>
    public static void PlaceAt(HWND window, double width, double height, ScreenPoint anchor)
    {
        var monitor = MonitorArea.At(anchor);
        var (pixels, heightPixels) = (monitor.ToPixels(width), monitor.ToPixels(height));
        anchor = Clamp(anchor, 0, monitor.ToPixels(PillRoom), BoundsAt(anchor));
        PInvoke.SetWindowPos(window, Topmost, anchor.X - pixels / 2, anchor.Y, pixels, heightPixels, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    // The whole primary screen, not its work area: the island sits over the taskbar when that is at the top.
    private static ScreenRect Screen() => new(0, 0, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN));

    /// <summary>The whole of the screen (taskbar included) that is nearest to <paramref name="point"/>.</summary>
    internal static unsafe ScreenRect BoundsAt(ScreenPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, ref info);
        return new ScreenRect(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
    }

    /// <summary>
    /// Puts the window above every other topmost window, the taskbar included (which jumps up whenever it is used). It
    /// takes no focus and does not move.
    /// </summary>
    public static void RaiseAboveTaskbar(HWND window) => PInvoke.SetWindowPos(window, Topmost, 0, 0, 0, 0,
        SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// The anchor (middle of the top edge of the pill's room) kept so that a pill <paramref name="pillWidth"/> wide and
    /// <paramref name="pillHeight"/> tall lies wholly inside <paramref name="screen"/>. The pill is at the top of its window,
    /// so the rest of the window may hang off the bottom edge: the pill can be put over the taskbar, down to the screen's edge.
    /// </summary>
    internal static ScreenPoint Clamp(ScreenPoint anchor, int pillWidth, int pillHeight, ScreenRect screen) => new(
        Math.Clamp(anchor.X, screen.Left + pillWidth / 2, Math.Max(screen.Left + pillWidth / 2, screen.Right - pillWidth / 2)),
        Math.Clamp(anchor.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - pillHeight)));
}
