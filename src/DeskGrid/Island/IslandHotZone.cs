using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeskGrid.Resources;
using Windows.Win32.Foundation;
using System.Windows.Interop;

namespace DeskGrid.Island;

/// <summary>
/// A thin invisible strip at the very top centre of the primary screen. The pointer pushed against the top edge and
/// held there for a moment raises <see cref="Dwelled"/>. A window of its own, so nothing watches the pointer while it
/// is elsewhere (an almost transparent layered window is still hit by the mouse, a fully transparent one is not).
/// </summary>
internal sealed class IslandHotZone : Window
{
    internal const double ZoneWidth = 240; // DIPs.
    // As tall as the gap above the pill, so pushing the pointer up from the pill reaches it without aiming at a few pixels.
    internal const double ZoneHeight = IslandWindow.TopGap;
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _dwell = new() { Interval = Delay };

    public IslandHotZone()
    {
        Title = Strings.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // Alpha 1: invisible, but not click-through.
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        (Width, Height) = (ZoneWidth, ZoneHeight);

        _dwell.Tick += (_, _) =>
        {
            _dwell.Stop();
            if (IsMouseOver)
            {
                Dwelled?.Invoke(this, EventArgs.Empty);
            }
        };
        // A taskbar at the top comes back over the strip whenever it is used; lift the strip again now and then.
        var lift = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        lift.Tick += (_, _) =>
        {
            if (IsVisible)
            {
                TopEdge.RaiseAboveTaskbar(new HWND(new WindowInteropHelper(this).Handle));
            }
        };
        lift.Start();
        Closed += (_, _) => lift.Stop();
        MouseEnter += (_, _) => _dwell.Start();
        MouseLeave += (_, _) => _dwell.Stop();
    }

    /// <summary>Raised when the pointer has stayed on the strip for <see cref="Delay"/>.</summary>
    public event EventHandler? Dwelled;

    /// <summary>Shows the strip at the top centre of the primary screen, or puts it back there.</summary>
    public void Place()
    {
        TopEdge.Place(new HWND(new WindowInteropHelper(this).EnsureHandle()), ZoneWidth, ZoneHeight, 0);
        if (!IsVisible)
        {
            Show();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TopEdge.Prepare(this, ZoneWidth, ZoneHeight, 0);
    }
}
