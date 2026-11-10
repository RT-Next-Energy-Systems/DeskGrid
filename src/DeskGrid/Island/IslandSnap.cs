using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DeskGrid.Desktop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Island;

/// <summary>A guide line on the screen while NotiLand is dragged: vertical at <see cref="Position"/> (an x), or horizontal (a y).</summary>
internal readonly record struct SnapLine(bool Vertical, int Position);

/// <summary>Where a dragged NotiLand ends up, and the guide lines to show for what it snapped to.</summary>
internal readonly record struct SnapResult(ScreenPoint Anchor, IReadOnlyList<SnapLine> Lines);

/// <summary>
/// Helps place NotiLand: near enough to a useful place, the dragged pill jumps onto it. Pure, so it can be tested; all in
/// screen pixels. The anchor is the middle of the pill's top edge.
/// </summary>
internal static class IslandSnap
{
    /// <summary>How near (in DIPs of the screen) a place has to be to snap to it.</summary>
    public const double Reach = 12;

    /// <summary>The gap (in DIPs) kept from a screen's edges and the taskbar.</summary>
    public const double Gap = 8;

    /// <summary>
    /// Snaps <paramref name="anchor"/> (the pill <paramref name="width"/> by <paramref name="height"/> pixels) on the screen
    /// <paramref name="screen"/> (whose work area, the screen without the taskbar, is <paramref name="work"/>): across, to the
    /// middle of the screen, a gap from its left or right edge, or under another place of NotiLand; down, a gap below the top
    /// of the screen or of the work area, a gap above the taskbar, in the middle of the taskbar, at the bottom edge, or level
    /// with another place. Then the pill is kept wholly on the screen.
    /// </summary>
    public static SnapResult Snap(
        ScreenPoint anchor, int width, int height, ScreenRect screen, ScreenRect work, IReadOnlyList<ScreenPoint> others, double pixelsPerDip)
    {
        var reach = Reach * pixelsPerDip;
        var gap = (int)Math.Round(Gap * pixelsPerDip);
        var lines = new List<SnapLine>();

        // Across: the place, and where its guide line is drawn.
        var across = new List<(int Value, int Line)>
        {
            ((screen.Left + screen.Right) / 2, (screen.Left + screen.Right) / 2),
            (screen.Left + gap + width / 2, screen.Left + gap),
            (screen.Right - gap - width / 2, screen.Right - gap),
        };
        across.AddRange(others.Where(o => screen.Contains(o)).Select(o => (o.X, o.X)));

        // Down: the pill's top edge, and where its guide line is drawn (the edge it lines up with).
        var down = new List<(int Value, int Line)>
        {
            (screen.Top + gap, screen.Top + gap),
            (work.Top + gap, work.Top + gap),
            (work.Bottom - gap - height, work.Bottom - gap),
            (screen.Bottom - height, screen.Bottom - 1),
        };
        if (work.Bottom < screen.Bottom)
        {
            var middle = (work.Bottom + screen.Bottom) / 2;
            down.Add((middle - height / 2, middle)); // In the middle of a taskbar at the bottom.
        }

        down.AddRange(others.Where(o => screen.Contains(o)).Select(o => (o.Y, o.Y)));

        var x = anchor.X;
        if (Nearest(across, anchor.X, reach) is { } snapX)
        {
            x = snapX.Value;
            lines.Add(new SnapLine(true, snapX.Line));
        }

        var y = anchor.Y;
        if (Nearest(down, anchor.Y, reach) is { } snapY)
        {
            y = snapY.Value;
            lines.Add(new SnapLine(false, snapY.Line));
        }

        return new SnapResult(TopEdge.Clamp(new ScreenPoint(x, y), width, height, screen), lines);
    }

    private static (int Value, int Line)? Nearest(List<(int Value, int Line)> candidates, int value, double reach)
    {
        (int Value, int Line)? best = null;
        var distance = reach;
        foreach (var candidate in candidates)
        {
            var d = Math.Abs(candidate.Value - value);
            if (d <= distance)
            {
                (best, distance) = (candidate, d);
            }
        }

        return best;
    }
}

/// <summary>
/// Draws the guide lines over one screen while NotiLand is dragged: a window the size of the screen that the mouse goes
/// through, never activated, holding a few lines.
/// </summary>
internal sealed class SnapGuideWindow : Window
{
    private static readonly Brush LineBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0xFF)));
    private readonly Canvas _canvas = new();

    // The guides belong to a drag: if no mouse button is down any more (the release went to someone else, the capture was lost,
    // the island was hidden mid-drag) they must not stay on the screen. This looks every 100 ms while they are up.
    private readonly System.Windows.Threading.DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public SnapGuideWindow()
    {
        _watchdog.Tick += (_, _) =>
        {
            var down = (PInvoke.GetAsyncKeyState(0x01) & 0x8000) != 0 || (PInvoke.GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (!down)
            {
                Hide();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _watchdog.Start();
            }
            else
            {
                _watchdog.Stop();
            }
        };
        Closed += (_, _) => _watchdog.Stop();
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Content = _canvas;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>Shows <paramref name="lines"/> on <paramref name="screen"/> (pixels; <paramref name="pixelsPerDip"/> of that screen).</summary>
    public void ShowGuides(ScreenRect screen, double pixelsPerDip, IReadOnlyList<SnapLine> lines)
    {
        var handle = new HWND(new WindowInteropHelper(this).EnsureHandle());
        var (width, height) = (screen.Width / pixelsPerDip, screen.Height / pixelsPerDip);
        (Width, Height) = (width, height);
        PInvoke.SetWindowPos(handle, new HWND(-1), screen.Left, screen.Top, screen.Width, screen.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        _canvas.Children.Clear();
        foreach (var line in lines)
        {
            var at = (line.Position - (line.Vertical ? screen.Left : screen.Top)) / pixelsPerDip;
            _canvas.Children.Add(new Line
            {
                X1 = line.Vertical ? at : 0,
                X2 = line.Vertical ? at : width,
                Y1 = line.Vertical ? 0 : at,
                Y2 = line.Vertical ? height : at,
                Stroke = LineBrush,
                StrokeThickness = 1.5,
                StrokeDashArray = [6, 4],
                Opacity = 0.9,
            });
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    // Through for the mouse, no focus, no Alt+Tab entry.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new HWND(new WindowInteropHelper(this).Handle);
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        _ = PInvoke.SetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (int)(style | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TRANSPARENT));
    }
}
