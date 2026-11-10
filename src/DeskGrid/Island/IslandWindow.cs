using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Resources;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Island;

/// <summary>
/// The island: a black pill at the top centre of the primary screen that shows one line of text. The window is a fixed
/// transparent room the pill grows in, so only the pill takes the mouse (the transparent rest of a layered window lets
/// clicks through). It never takes focus. With the pointer over it the pill opens to show the whole text.
/// </summary>
internal sealed class IslandWindow : Window
{
    // Sizes in DIPs.
    internal const double RoomWidth = 420;
    internal const double RoomHeight = 120;
    internal const double TopGap = 8;
    private const double PadX = 16;
    private const double PadY = 9;
    private const double LineHeight = 18;
    private const double PillHeight = LineHeight + 2 * PadY; // Also the pill's smallest width, so it starts and ends as a circle.
    private const double ClosedMaxWidth = 280;
    private const double OpenMaxWidth = 400;
    private const int OpenMaxLines = 3;

    private static readonly QuinticEase Ease = FrozenEase();
    private static readonly Duration Grow = new(TimeSpan.FromMilliseconds(280));
    private static readonly Duration Leave = new(TimeSpan.FromMilliseconds(180));

    private readonly Border _pill;
    private readonly TextBlock _text;
    private bool _present;

    public IslandWindow()
    {
        Title = Strings.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        (Width, Height) = (RoomWidth, RoomHeight);

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            LineHeight = LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pill = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(PillHeight / 2),
            Padding = new Thickness(PadX, PadY, PadX, PadY),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true, // While the pill is still small, the text is cut at its edge, not drawn outside it.
            Opacity = 0,
            Child = _text,
        };
        _pill.MouseEnter += (_, _) => SetOpen(true);
        _pill.MouseLeave += (_, _) => SetOpen(false);
        _pill.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        Content = _pill;
    }

    // Frozen, so the one shared easing can be used by any thread's animations (an unfrozen one belongs to its first thread).
    private static QuinticEase FrozenEase()
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    /// <summary>The pointer is over the pill.</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Raised when <see cref="IsHovered"/> changed.</summary>
    public event EventHandler? HoverChanged;

    /// <summary>Raised when the pill was clicked.</summary>
    public event EventHandler? Clicked;

    /// <summary>Where the window's top-left goes: centred on the work area, <paramref name="gap"/> below its top edge.</summary>
    internal static ScreenPoint Place(ScreenRect workArea, int width, int gap) =>
        new(workArea.Left + (workArea.Width - width) / 2, workArea.Top + gap);

    /// <summary>Shows this line: the pill appears (or, if it is up already, changes to fit it).</summary>
    public void Display(string text)
    {
        _text.Text = text;
        _present = true;
        var entering = !IsVisible;
        if (entering)
        {
            IsHovered = false; // A pill that was hidden under the pointer gets no MouseLeave.
            Reposition(new HWND(new WindowInteropHelper(this).EnsureHandle()));
            var size = PillSize();
            StopAnimations();
            (_pill.Width, _pill.Height, _pill.Opacity) = (size.Height, size.Height, 0);
            Show(); // Not activated: ShowActivated is off.
        }

        Resize();
        Animate(OpacityProperty, 1, Grow);
    }

    /// <summary>Takes the pill away: it shrinks and fades first, or is gone at once.</summary>
    public void Withdraw(bool animated)
    {
        _present = false;
        if (!IsVisible)
        {
            return;
        }

        if (!animated)
        {
            StopAnimations();
            Hide();
            return;
        }

        Animate(WidthProperty, PillHeight, Leave);
        Animate(HeightProperty, PillHeight, Leave);
        Animate(OpacityProperty, 0, Leave, (_, _) =>
        {
            if (!_present)
            {
                Hide();
            }
        });
    }

    // The window must not take focus or show in Alt+Tab; set once, before it is first shown.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var window = new HWND(new WindowInteropHelper(this).Handle);
        var source = HwndSource.FromHwnd(window);
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        _ = PInvoke.SetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (int)(style | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
        source.AddHook(OnWindowMessage);
    }

    // Moves the window to the top centre of the primary screen, in that screen's pixels.
    private static void Reposition(HWND window)
    {
        var monitor = MonitorArea.Primary;
        var width = monitor.ToPixels(RoomWidth);
        var position = Place(monitor.WorkArea, width, monitor.ToPixels(TopGap));
        PInvoke.SetWindowPos(window, new HWND(-1), position.X, position.Y, width, monitor.ToPixels(RoomHeight),
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    // The window was moved to a screen with another DPI, and Windows proposes a size and place of its own (the old ones
    // scaled), which WPF takes; the island puts itself where it belongs for the new DPI.
    private nint OnWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == PInvoke.WM_DPICHANGED)
        {
            Dispatcher.BeginInvoke(() =>
            {
                (Width, Height) = (RoomWidth, RoomHeight);
                Reposition(new HWND(window));
            }, DispatcherPriority.Send);
        }

        return 0;
    }

    private void SetOpen(bool open)
    {
        IsHovered = open;
        if (IsVisible && _present)
        {
            Resize();
        }

        HoverChanged?.Invoke(this, EventArgs.Empty);
    }

    // The size the pill has for this text: one line while closed, up to three while open.
    private Size PillSize()
    {
        _text.Width = double.NaN;
        _text.TextWrapping = IsHovered ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _text.MaxWidth = (IsHovered ? OpenMaxWidth : ClosedMaxWidth) - 2 * PadX;
        _text.MaxHeight = (IsHovered ? OpenMaxLines : 1) * LineHeight;
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var text = _text.DesiredSize;
        _text.Width = text.Width; // Fixed while the pill grows, so the text does not wrap again on the way.
        return new Size(Math.Max(text.Width + 2 * PadX, PillHeight), Math.Max(text.Height + 2 * PadY, PillHeight));
    }

    private void Resize()
    {
        var size = PillSize();
        Animate(WidthProperty, size.Width, Grow);
        Animate(HeightProperty, size.Height, Grow);
    }

    private void StopAnimations()
    {
        foreach (var property in new[] { WidthProperty, HeightProperty, OpacityProperty })
        {
            _pill.BeginAnimation(property, null);
        }
    }

    private void Animate(DependencyProperty property, double to, Duration duration, EventHandler? completed = null)
    {
        var animation = new DoubleAnimation(to, duration) { EasingFunction = Ease };
        if (completed is not null)
        {
            animation.Completed += completed;
        }

        _pill.BeginAnimation(property, animation); // The pill's width, height and opacity, not the window's.
    }
}
