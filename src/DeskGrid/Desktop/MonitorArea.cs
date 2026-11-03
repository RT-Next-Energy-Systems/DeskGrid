using System.Drawing;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace DeskGrid.Desktop;

/// <summary>A monitor's work area (the desktop minus the taskbar) and its current DPI.</summary>
internal readonly record struct MonitorArea(ScreenRect WorkArea, uint Dpi)
{
    /// <summary>The primary monitor always contains the virtual-screen origin.</summary>
    public static MonitorArea Primary => At(new ScreenPoint(0, 0));

    /// <summary>The monitor containing <paramref name="point"/>, or the nearest one.</summary>
    public static MonitorArea At(ScreenPoint point) =>
        Of(PInvoke.MonitorFromPoint(new Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    /// <summary>
    /// The monitor whose work area contains <paramref name="point"/>, or <see langword="null"/> if none does: the point
    /// is on no monitor (one was unplugged or moved) or under the taskbar.
    /// </summary>
    public static MonitorArea? Containing(ScreenPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (monitor.IsNull)
        {
            return null;
        }

        var area = Of(monitor);
        return area.WorkArea.Contains(point) ? area : null;
    }

    private static unsafe MonitorArea Of(HMONITOR monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, ref info);

        // A monitor that was unplugged a moment ago answers with a failure; assume the standard DPI rather than throw
        // out of a timer or an event handler.
        if (!PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Succeeded || dpi == 0)
        {
            dpi = 96;
        }

        var work = info.rcWork;
        return new MonitorArea(new ScreenRect(work.left, work.top, work.right, work.bottom), dpi);
    }

    /// <summary>Converts device-independent units (1/96 inch) to this monitor's pixels.</summary>
    public int ToPixels(double dip) => (int)Math.Round(dip * Dpi / 96);

    /// <summary>Converts this monitor's pixels to device-independent units.</summary>
    public double ToDips(int pixels) => pixels * 96.0 / Dpi;
}
