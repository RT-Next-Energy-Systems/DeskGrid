using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeskGrid.Resources;
using Windows.Win32;
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
    // Only the very edge: the pointer stops at the screen's top, so pushing it up reaches the strip, while a pointer on its
    // way to a tab or a title bar a few pixels lower does not. (It was as tall as the gap above the pill, 8 DIP, which
    // caught the top of every window at the centre.)
    internal const double ZoneHeight = 2;
    // The pointer has to rest this long: passing over, or a click on a tab at the top, must not open anything.
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(1000);

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
        AllowDrop = true;
        (Width, Height) = (ZoneWidth, ZoneHeight);
        DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                FilesHovered?.Invoke(this, EventArgs.Empty);
            }
        };

        _dwell.Tick += (_, _) =>
        {
            _dwell.Stop();
            // Not while a button is down: a window being dragged by its title bar or a click on a tab is not a rest.
            if (IsMouseOver && !ButtonDown())
            {
                Dwelled?.Invoke(this, EventArgs.Empty);
            }
        };
        // A taskbar at the top comes back over the strip whenever it is used; lift the strip again now and then.
        var lift = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        lift.Tick += (_, _) =>
        {
            if (IsVisible && !IslandScene.SystemOverlayInFront())
            {
                TopEdge.RaiseAboveTaskbar(new HWND(new WindowInteropHelper(this).Handle));
            }
        };
        lift.Start();
        Closed += (_, _) =>
        {
            lift.Stop();
            _dwell.Stop(); // A dwell under way when the strip goes (turned off) must not open the folders afterwards.
        };
        MouseEnter += (_, _) => _dwell.Start();
        MouseLeave += (_, _) => _dwell.Stop();
        // Resting means keeping still: a move on the strip starts the wait again.
        MouseMove += (_, _) =>
        {
            if (_dwell.IsEnabled)
            {
                _dwell.Stop();
                _dwell.Start();
            }
        };
        PreviewMouseDown += (_, _) => _dwell.Stop();
    }

    private static bool ButtonDown() =>
        (PInvoke.GetAsyncKeyState(0x01) & 0x8000) != 0 || (PInvoke.GetAsyncKeyState(0x02) & 0x8000) != 0; // Left, right.

    /// <summary>Raised when files are dragged to the strip.</summary>
    public event EventHandler? FilesHovered;

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
