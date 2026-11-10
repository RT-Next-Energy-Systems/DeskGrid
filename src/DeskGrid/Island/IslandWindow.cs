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
/// The island: a black pill at the top centre of the primary screen that shows one line of text, with a cover when it is a
/// song. The window is a fixed transparent room the pill grows in, so only the pill takes the mouse (the transparent rest
/// of a layered window lets clicks through). It never takes focus. With the pointer over it the pill opens: a line shows all
/// of its text, a song shows the buttons for the previous song, play / pause and the next song.
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
    private const double CoverSize = 22;
    private const double CoverGap = 8;
    private const double ButtonSize = 28;
    private const double ButtonsGap = 10;
    private const double ButtonsWidth = 3 * ButtonSize;
    private const double ClosedMaxWidth = 280;
    private const double OpenMaxWidth = 400;
    private const int OpenMaxLines = 3;

    // Segoe MDL2 Assets.
    private const string PreviousGlyph = "\uE892";
    private const string NextGlyph = "\uE893";
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";

    private static readonly QuinticEase Ease = FrozenEase();
    private static readonly Duration Grow = new(TimeSpan.FromMilliseconds(280));
    private static readonly Duration Leave = new(TimeSpan.FromMilliseconds(180));

    private readonly Border _pill;
    private readonly StackPanel _row;
    private readonly Border _coverBox;
    private readonly Image _cover;
    private readonly TextBlock _text;
    private readonly Border _textBox;
    private readonly TranslateTransform _scroll = new();
    private readonly StackPanel _buttons;
    private readonly TextBlock _playGlyph;
    private MusicActions? _music;
    private string _source = ""; // The player the music buttons act on.
    private bool _present;
    private double _overflow; // How far the text is wider than the room it has, in DIPs.
    private ScreenPoint? _anchor; // Where the user put the island: the middle of its top edge, in screen pixels.
    private (int X, int Y, int AnchorX, int Top)? _drag; // The pointer and the island when a press began.
    private bool _dragged;

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

        _cover = new Image { Stretch = Stretch.UniformToFill };
        _coverBox = new Border
        {
            Width = CoverSize,
            Height = CoverSize,
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, CoverGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _cover,
        };
        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            LineHeight = LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _scroll,
        };
        // Shows as much of the text as fits; a longer line scrolls inside it (see RestartMarquee).
        _textBox = new Border { ClipToBounds = true, Child = _text, VerticalAlignment = VerticalAlignment.Center };
        _playGlyph = Glyph(PlayGlyph);
        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(ButtonsGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Children =
            {
                Button(Glyph(PreviousGlyph), () => _music?.Previous(_source)),
                Button(_playGlyph, () => _music?.PlayPause(_source)),
                Button(Glyph(NextGlyph), () => _music?.Next(_source)),
            },
        };
        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _coverBox, _textBox, _buttons },
        };
        _pill = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(PillHeight / 2),
            Padding = new Thickness(PadX, PadY, PadX, PadY),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true, // While the pill is still small, the content is cut at its edge, not drawn outside it.
            Opacity = 0,
            Child = _row,
        };
        _pill.MouseEnter += (_, _) => SetOpen(true);
        _pill.MouseLeave += (_, _) => SetOpen(false);
        _pill.MouseLeftButtonDown += OnPillDown;
        _pill.MouseMove += OnPillMove;
        _pill.MouseLeftButtonUp += OnPillUp;
        Content = _pill;
    }

    /// <summary>Raised when the user dropped the island somewhere: the middle of its top edge, in screen pixels.</summary>
    public event Action<int, int>? PositionChanged;

    /// <summary>Puts the island where the user wants it (the middle of its top edge, in screen pixels); <see langword="null"/> is the top centre of the primary screen.</summary>
    public void SetAnchor(int? x, int? y)
    {
        _anchor = x is { } ax && y is { } ay ? new ScreenPoint(ax, ay) : null;
        if (new WindowInteropHelper(this).Handle != 0)
        {
            Reposition();
        }
    }

    // A press on the pill may become a drag of the whole island (the window is a room around the pill, so the window
    // moves, to any screen); one that stays put is a click. The buttons keep their own clicks.
    private void OnPillDown(object sender, MouseButtonEventArgs e)
    {
        if (_buttons.IsMouseOver)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        PInvoke.GetWindowRect(new HWND(new WindowInteropHelper(this).Handle), out var rect);
        _drag = (cursor.X, cursor.Y, (rect.left + rect.right) / 2, rect.top);
        _dragged = false;
        _pill.CaptureMouse();
    }

    private void OnPillMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } start)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        var (dx, dy) = (cursor.X - start.X, cursor.Y - start.Y);
        if (!_dragged && Math.Abs(dx) < PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG) && Math.Abs(dy) < PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG))
        {
            return;
        }

        _dragged = true;
        var window = new HWND(new WindowInteropHelper(this).Handle);
        PInvoke.GetWindowRect(window, out var rect);

        // The pill is kept wholly on the screen the pointer is on, as wide as it gets when it opens under the pointer.
        var scale = (rect.right - rect.left) / RoomWidth; // Pixels per DIP of the screen the window is on now.
        var anchor = TopEdge.Clamp(new ScreenPoint(start.AnchorX + dx, start.Top + dy), (int)Math.Round(OpenMaxWidth * scale),
            (int)Math.Round(Math.Max(_pill.ActualHeight, TopEdge.PillRoom) * scale), TopEdge.BoundsAt(new ScreenPoint(cursor.X, cursor.Y)));
        PInvoke.SetWindowPos(window, HWND.Null, anchor.X - (rect.right - rect.left) / 2, anchor.Y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    private void OnPillUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        _pill.ReleaseMouseCapture();
        if (_dragged)
        {
            PInvoke.GetWindowRect(new HWND(new WindowInteropHelper(this).Handle), out var rect);
            _anchor = new ScreenPoint((rect.left + rect.right) / 2, rect.top);
            Reposition(); // The size of the screen it landed on.
            PositionChanged?.Invoke(_anchor.Value.X, _anchor.Value.Y);
        }
        else if (_music is null)
        {
            Clicked?.Invoke(this, EventArgs.Empty); // A song has its buttons; a line is dismissed by a click.
        }
    }

    // Frozen, so the one shared easing can be used by any thread's animations (an unfrozen one belongs to its first thread).
    private static QuinticEase FrozenEase()
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 14,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // A round-ish hit area around a glyph; clicking does not reach the pill's own click.
    private static Border Button(TextBlock glyph, Action action)
    {
        var button = new Border { Width = ButtonSize, Height = ButtonSize, Background = Brushes.Transparent, Opacity = 0.75, Child = glyph };
        button.MouseEnter += (_, _) => button.Opacity = 1;
        button.MouseLeave += (_, _) => button.Opacity = 0.75;
        button.MouseLeftButtonUp += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        return button;
    }

    /// <summary>The pointer is over the pill.</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Raised when <see cref="IsHovered"/> changed.</summary>
    public event EventHandler? HoverChanged;

    /// <summary>Raised when a line (not a song) was clicked.</summary>
    public event EventHandler? Clicked;

    /// <summary>Whether the play / pause button shows "pause" (the song plays) or "play".</summary>
    public void SetPlaying(bool playing) => _playGlyph.Text = playing ? PauseGlyph : PlayGlyph;

    /// <summary>
    /// Shows this: the pill appears (or, if it is up already, changes to fit it). A song comes with its cover (if it has
    /// one) and the actions of the buttons that show while the pointer is over the pill.
    /// </summary>
    public void Display(string text, ImageSource? cover = null, MusicActions? music = null, string source = "")
    {
        _source = source;
        _text.Text = text;
        _cover.Source = cover;
        _coverBox.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        _music = music;
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

        Resize(); // Also starts the scrolling of a long line.
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
        TopEdge.Prepare(this, RoomWidth, RoomHeight, TopGap, () => { if (_drag is null) { Reposition(); } });
    }

    /// <summary>Lifts the island above the taskbar, which comes back on top whenever it is used.</summary>
    public void RaiseAboveTaskbar()
    {
        if (IsVisible)
        {
            TopEdge.RaiseAboveTaskbar(new HWND(new WindowInteropHelper(this).Handle));
        }
    }

    /// <summary>Puts the window back at the top centre of the primary screen (the screens changed).</summary>
    public void Reposition() => Reposition(new HWND(new WindowInteropHelper(this).EnsureHandle()));

    private void Reposition(HWND window)
    {
        if (_anchor is { } anchor)
        {
            TopEdge.PlaceAt(window, RoomWidth, RoomHeight, anchor);
        }
        else
        {
            TopEdge.Place(window, RoomWidth, RoomHeight, TopGap);
        }
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

    // The size the pill has for what it shows. Closed: one line. Open: a line shows up to three; a song stays one line
    // and makes room for its buttons.
    private Size PillSize()
    {
        var open = IsHovered;
        var buttons = open && _music is not null;
        var wraps = open && _music is null;
        _buttons.Visibility = buttons ? Visibility.Visible : Visibility.Collapsed;
        var extra = (_cover.Source is null ? 0 : CoverSize + CoverGap) + (buttons ? ButtonsWidth + ButtonsGap : 0);

        var room = (open ? OpenMaxWidth : ClosedMaxWidth) - 2 * PadX - extra;
        _text.Width = double.NaN;
        _text.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _text.MaxHeight = (wraps ? OpenMaxLines : 1) * LineHeight;
        _text.MaxWidth = wraps ? room : double.PositiveInfinity; // A line that does not wrap is as wide as it is, and scrolls.
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = _text.DesiredSize.Width;
        _text.Width = natural; // Fixed while the pill grows, so the text does not wrap again on the way.
        _textBox.Width = wraps ? natural : Math.Min(natural, room);
        _overflow = wraps ? 0 : Math.Max(0, natural - room);
        _row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var content = _row.DesiredSize;
        return new Size(Math.Max(content.Width + 2 * PadX, PillHeight), Math.Max(content.Height + 2 * PadY, PillHeight));
    }

    private void Resize()
    {
        var size = PillSize();
        Animate(WidthProperty, size.Width, Grow);
        Animate(HeightProperty, size.Height, Grow);
        RestartMarquee();
    }

    // A line wider than the pill rests at its start, scrolls to its end, rests there, goes back and starts over. It does not
    // run all the time: the first rest is what lets the reader begin to read.
    private void RestartMarquee()
    {
        _scroll.BeginAnimation(TranslateTransform.XProperty, null);
        _scroll.X = 0;
        if (_overflow < 1)
        {
            return;
        }

        var scrolled = Marquee.Rest + TimeSpan.FromSeconds(_overflow / Marquee.Speed);
        var atEnd = scrolled + Marquee.Rest;
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, Marquee.Rest));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-_overflow, scrolled));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-_overflow, atEnd));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, atEnd + Marquee.Return));
        _scroll.BeginAnimation(TranslateTransform.XProperty, animation);
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
