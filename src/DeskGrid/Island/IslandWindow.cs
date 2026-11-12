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

/// <summary>What NotiLand does when the pointer is over it: open at all, show all of a line, the music buttons, the song's time.</summary>
internal sealed record IslandHover(bool Expand = true, bool FullText = true, bool Buttons = true, bool Time = true);

/// <summary>
/// NotiLand: a black pill (at the top centre of the primary screen unless the user put it elsewhere) that shows one line of
/// text, with a cover when it is a song. The window is a fixed transparent room the pill grows in, so only the pill takes
/// the mouse (the transparent rest of a layered window lets clicks through). It never takes focus. With the pointer over it
/// the pill opens as <see cref="Hover"/> says: a line shows all of its text, a song its buttons and time. It can be dragged
/// anywhere, to any screen; while dragged it snaps to helpful places and shows guide lines.
/// </summary>
internal sealed class IslandWindow : Window
{
    // Sizes in DIPs, at size 100 %.
    internal const double RoomWidth = 420;
    internal const double RoomHeight = 120;
    internal const double TopGap = 8;
    private const double PadX = 16;
    private const double PadY = 9;
    private const double LineHeight = 18;
    internal const double PillHeight = LineHeight + 2 * PadY; // Also the pill's smallest width, so it starts and ends as a circle.
    private const double CoverSize = 22;
    private const double CoverGap = 8;
    private const double ButtonSize = 28;
    private const double ButtonsGap = 10;
    private const double ButtonsWidth = 3 * ButtonSize;
    private const double ClosedMaxWidth = 280;
    private const double OpenMaxWidth = 400;
    private const int OpenMaxLines = 3;

    // Segoe MDL2 Assets.
    private const string PreviousGlyph = "";
    private const string NextGlyph = "";
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    // iOS-like curve: leaves quickly but arrives softly, with no hard stop at the end (a quintic ease-out jumped most of the way
    // in the first frames and then crept).
    private static readonly KeySpline Spline = FrozenSpline();
    // Short on purpose: the pill opens and closes under the pointer all the time, and a long animation reads as lag. The fade is
    // shorter still, because a transparent pixel of a layered window lets clicks through (a pill still fading in cannot be clicked).
    private static readonly Duration Grow = new(TimeSpan.FromMilliseconds(260));
    private static readonly Duration Leave = new(TimeSpan.FromMilliseconds(160));
    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(120));

    // A press that moves less than this (DIPs, and never less than the system's drag distance) is a click, not a drag: the
    // system's 4 pixels are 2 DIPs at 200 % scaling, which a hand on a trackpad passes without meaning to.
    private const double DragSlop = 8;
    private const long ClickWindowMs = 1500; // A press and its release this close together are one click.

    // The pointer must have stayed away this long before the pill closes again: grazing the edge while it resizes, or the
    // trip from the pill to the card, does not make it flutter.
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(140);

    private readonly Border _pill;
    private readonly StackPanel _row;
    private readonly Border _coverBox;
    private readonly Image _cover;
    private readonly TextBlock _text;
    private readonly Canvas _textBox; // A canvas does not cut its child to its own size: only its clip does, so a moved text shows its tail.
    private readonly TextBlock _time;
    private readonly TextBlock _symbol;
    private readonly StackPanel _content;
    private readonly Border _barTrack;
    private readonly Border _barFill;
    private readonly TranslateTransform _scroll = new();
    private readonly DispatcherTimer _closeTimer;
    private readonly StackPanel _buttons;
    private readonly TextBlock _playGlyph;
    private SnapGuideWindow? _guides;
    private (string Text, double Overflow)? _marquee; // What the scrolling runs for now.
    private IslandLook? _look;
    private bool _live; // The pill shows a line that changes (a timer, the system's state): a click opens the card.
    private double _progress;
    private MusicActions? _music;
    private string _source = ""; // The player the music buttons act on.
    private bool _present;
    private double _overflow; // How far the text is wider than the room it has, in DIPs.
    private Size _pillSize = new(PillHeight, PillHeight); // The size the pill is going to, in DIPs at 100 %.
    private ScreenPoint? _anchor; // Where the user put the island: the middle of its top edge, in screen pixels.
    private ScreenPoint? _followHome; // At its default place on the pointer's screen: where it appeared.
    private (int X, int Y, int AnchorX, int Top)? _drag; // The pointer and the island when a press began.
    private bool _dragged;
    private double _scale = 1;
    private IslandHover _hover = new();

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
            RenderTransform = _scroll,
        };

        // Shows as much of the text as fits; a longer line scrolls inside it (see RestartMarquee).
        _textBox = new Canvas { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Children = { _text } };
        _time = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _symbol = Glyph("");
        _symbol.Margin = new Thickness(0, 0, 8, 0);
        _symbol.Visibility = Visibility.Collapsed;
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
            Children = { _symbol, _coverBox, _textBox, _time, _buttons },
        };
        _barFill = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _barTrack = new Border
        {
            Height = 3,
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            Visibility = Visibility.Collapsed,
            Child = _barFill,
        };
        _barTrack.SizeChanged += (_, _) => FillBar();
        _content = new StackPanel { Children = { _row, _barTrack } };
        _pill = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(PillHeight / 2),
            Padding = new Thickness(PadX, PadY, PadX, PadY),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true, // While the pill is still small, the content is cut at its edge, not drawn outside it.
            Opacity = 0,
            Child = _content,
            AllowDrop = true,
        };
        _pill.DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                FilesHovered?.Invoke(this, EventArgs.Empty);
            }
        };
        _closeTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = CloseDelay };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!_pill.IsMouseOver)
            {
                SetOpen(false);
            }
        };
        _pill.MouseEnter += (_, _) =>
        {
            _closeTimer.Stop();
            SetOpen(true);
        };
        _pill.MouseLeave += (_, _) =>
        {
            if (_dragged || !IsHovered)
            {
                SetOpen(false);
            }
            else
            {
                _closeTimer.Stop();
                _closeTimer.Start();
            }
        };
        _pill.MouseLeftButtonDown += OnPillDown;
        _pill.MouseMove += OnPillMove;
        _pill.MouseLeftButtonUp += OnPillUp;
        Content = _pill;
        Closed += (_, _) => _guides?.Close();

        // A press that ends without a release reaching the pill (capture taken away, window hidden) is the end of the drag too.
        _pill.LostMouseCapture += (_, _) => EndDragState();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                EndDragState();
            }
        };
    }

    /// <summary>Whether the island, at its default place, appears on the screen the pointer is on (not always the primary one).</summary>
    public bool FollowPointer { get; set; }

    /// <summary>Other places the island has (other situations), to snap to while it is dragged.</summary>
    public IReadOnlyList<ScreenPoint> SnapTargets { get; set; } = [];

    /// <summary>The pointer is over the pill.</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Whether a press on the pill is being dragged now.</summary>
    public bool IsDragging => _drag is not null && _dragged;

    /// <summary>Raised when <see cref="IsHovered"/> changed.</summary>
    public event EventHandler? HoverChanged;

    /// <summary>Raised when a line (not a song) was clicked.</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised when the pill was clicked twice quickly (ends the placing of the island in Settings).</summary>
    public event EventHandler? DoubleClicked;

    /// <summary>Raised when a line with an action (<see cref="IslandLook.Action"/>) was clicked.</summary>
    public event Action<string>? ActionRequested;

    /// <summary>Raised when a song or other line without an action was clicked: the card is wanted.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>Raised when files are dragged over the pill.</summary>
    public event EventHandler? FilesHovered;

    /// <summary>Raised when the user dropped the island somewhere: the middle of its top edge, in screen pixels.</summary>
    public event Action<int, int>? PositionChanged;

    /// <summary>The size of the pill, 1 = 100 %.</summary>
    public double PillScale
    {
        get => _scale;
        set
        {
            var scale = Math.Clamp(value, 0.5, 2);
            if (Math.Abs(scale - _scale) < 0.001)
            {
                return;
            }

            _scale = scale;
            var transform = new ScaleTransform(scale, scale);
            transform.Freeze();
            _pill.LayoutTransform = transform;
            if (new WindowInteropHelper(this).Handle != 0)
            {
                Reposition();
            }
        }
    }

    /// <summary>What the pill does under the pointer.</summary>
    public IslandHover Hover
    {
        get => _hover;
        set
        {
            _hover = value;
            if (IsVisible && _present)
            {
                Resize();
            }
        }
    }

    /// <summary>Puts the island where the user wants it (the middle of its top edge, in screen pixels); <see langword="null"/> is its default place.</summary>
    public void SetAnchor(ScreenPoint? anchor)
    {
        if (_anchor == anchor)
        {
            return;
        }

        _anchor = anchor;
        if (new WindowInteropHelper(this).Handle != 0 && !IsDragging)
        {
            Reposition();
        }
    }

    public void SetAnchor(int? x, int? y) => SetAnchor(x is { } ax && y is { } ay ? new ScreenPoint(ax, ay) : null);

    // ---- Dragging ----

    // A press on the pill may become a drag of the whole island (the window is a room around the pill, so the window
    // moves, to any screen); one that stays put is a click. The buttons keep their own clicks.
    private void OnPillDown(object sender, MouseButtonEventArgs e)
    {
        if (_buttons.IsMouseOver)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            // Only setting a place cares about it. The press goes on as a normal one: the second click of two quick ones used
            // to be swallowed here, so the card (or a dismiss) did not answer it.
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        }

        PInvoke.GetCursorPos(out var cursor);
        PInvoke.GetWindowRect(Handle, out var rect);
        _drag = (cursor.X, cursor.Y, (rect.left + rect.right) / 2, rect.top);
        _dragged = false;
        _pressedAt = Environment.TickCount64;
        _pill.CaptureMouse();
    }

    private long _pressedAt = long.MinValue / 2; // When the pill was last pressed (TickCount64), until that press is answered.

    private void OnPillMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } start)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        var (dx, dy) = (cursor.X - start.X, cursor.Y - start.Y);
        var slop = (int)Math.Round(DragSlop * VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (!_dragged
            && Math.Abs(dx) < Math.Max(slop, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG))
            && Math.Abs(dy) < Math.Max(slop, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG)))
        {
            return;
        }

        if (!_dragged)
        {
            _dragged = true;
            SetOpen(false); // Closed while dragged: it is the small pill that is put in its place, right up to an edge.
        }

        PInvoke.GetWindowRect(Handle, out var rect);
        var roomPixels = rect.right - rect.left;
        var perUnit = roomPixels / RoomWidth; // Pixels per DIP of the pill (its size and the screen's DPI).
        var perDip = perUnit / _scale; // Pixels per DIP of the screen.
        var point = new ScreenPoint(cursor.X, cursor.Y);
        var snap = IslandSnap.Snap(
            new ScreenPoint(start.AnchorX + dx, start.Top + dy),
            (int)Math.Round(_pillSize.Width * perUnit), (int)Math.Round(_pillSize.Height * perUnit),
            TopEdge.BoundsAt(point), MonitorArea.At(point).WorkArea, SnapTargets, perDip);
        PInvoke.SetWindowPos(Handle, HWND.Null, snap.Anchor.X - roomPixels / 2, snap.Anchor.Y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        _guides ??= new SnapGuideWindow();
        _guides.ShowGuides(TopEdge.BoundsAt(point), perDip, snap.Lines);
    }

    // Forgets a drag that is no longer going on and takes its guide lines away.
    private void EndDragState()
    {
        _guides?.Hide();
        if (_drag is not null && !_pill.IsMouseCaptured)
        {
            _drag = null;
            _dragged = false;
        }
    }

    private void OnPillUp(object sender, MouseButtonEventArgs e)
    {
        // A press whose capture was lost before the release (the pill changed under the pointer) is still a click on it: the
        // release comes if the pointer is still over the pill and the press was a moment ago.
        if (_drag is null && !(_pill.IsMouseOver && Environment.TickCount64 - _pressedAt < ClickWindowMs))
        {
            return;
        }

        _pressedAt = long.MinValue / 2;
        var dragged = _dragged;
        _drag = null;
        _dragged = false;
        _pill.ReleaseMouseCapture();
        _guides?.Hide();
        if (dragged)
        {
            PInvoke.GetWindowRect(Handle, out var rect);
            _anchor = new ScreenPoint((rect.left + rect.right) / 2, rect.top);
            Reposition(); // The size of the screen it landed on.
            SetOpen(_pill.IsMouseOver);
            PositionChanged?.Invoke(_anchor.Value.X, _anchor.Value.Y);
        }
        else if (_look?.Action is { } action)
        {
            ActionRequested?.Invoke(action);
            Clicked?.Invoke(this, EventArgs.Empty);
        }
        else if (_music is null && !_live)
        {
            Clicked?.Invoke(this, EventArgs.Empty); // A line is dismissed by a click.
        }
        else
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty); // A song (or a timer, the state of the system): the card.
        }
    }

    private HWND Handle => new(new WindowInteropHelper(this).EnsureHandle());

    // Frozen, so the one shared curve can be used by any thread's animations (an unfrozen one belongs to its first thread).
    private static KeySpline FrozenSpline()
    {
        var spline = new KeySpline(0.32, 0.72, 0, 1);
        spline.Freeze();
        return spline;
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

        // A button acts only on a click that began on it and ends on it. A release alone (the press was on the pill, or the
        // buttons slid under the pointer while the pill opened) must not pause the song.
        var pressed = false;
        button.MouseLeftButtonDown += (_, e) =>
        {
            pressed = true;
            button.CaptureMouse();
            e.Handled = true;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!pressed)
            {
                return;
            }

            pressed = false;
            button.ReleaseMouseCapture();
            if (button.IsMouseOver)
            {
                action();
            }
        };
        button.LostMouseCapture += (_, _) => pressed = false;
        return button;
    }

    // ---- What it shows ----

    public void MarkLive(bool live) => _live = live;

    /// <summary>
    /// Fills the bar under the text to <paramref name="fraction"/> (0 to 1); <see langword="null"/> takes the bar away.
    /// </summary>
    public void SetProgress(double? fraction)
    {
        var shown = fraction is not null;
        var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        var changedShape = _barTrack.Visibility != visibility;
        _barTrack.Visibility = visibility;
        _progress = Math.Clamp(fraction ?? 0, 0, 1);
        FillBar();
        if (changedShape && IsVisible && _present)
        {
            Resize();
        }
    }

    private void FillBar() => _barFill.Width = _barTrack.ActualWidth * _progress;

    /// <summary>The song's time ("1:05 / 3:12"), shown while the pill is open if the hover settings say so; <see langword="null"/> for none.</summary>
    public void SetTime(string? time)
    {
        var before = _time.Text;
        _time.Text = time ?? "";
        if ((before.Length == 0) != (_time.Text.Length == 0) && IsVisible && _present && IsOpen)
        {
            Resize();
        }
    }

    /// <summary>Changes the text of the line that is up, in place (a timer ticking), and its bar.</summary>
    public void Update(string text, double? progress)
    {
        if (_text.Text != text)
        {
            _text.Text = text;
            Resize();
        }

        SetProgress(progress);
    }

    /// <summary>Whether the play / pause button shows "pause" (the song plays) or "play".</summary>
    public void SetPlaying(bool playing) => _playGlyph.Text = playing ? PauseGlyph : PlayGlyph;

    /// <summary>
    /// Shows this: the pill appears (or, if it is up already, changes to fit it). A song comes with its cover (if it has
    /// one) and the actions of the buttons that show while the pointer is over the pill.
    /// </summary>
    public void Display(string text, ImageSource? cover = null, MusicActions? music = null, string source = "", IslandLook? look = null)
    {
        _source = source;
        _look = look;
        _live = false;
        var glyph = IslandLook.ResolveGlyph(look?.Glyph);
        var accent = IslandLook.ResolveColor(look?.Accent);
        _symbol.Text = glyph ?? "";
        _symbol.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        var brush = accent is { } color ? new SolidColorBrush(color) : Brushes.White;
        _symbol.Foreground = accent is null ? Brushes.White : brush;
        _barFill.Background = brush;
        SetProgress(look?.Progress);
        _text.Text = text;
        _cover.Source = cover;
        _coverBox.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        if (music is null)
        {
            _time.Text = "";
        }

        _music = music;
        var fresh = !_present || !IsVisible; // Appearing (or coming back while it leaves), not changing what an up pill shows.
        _present = true;
        if (!IsVisible)
        {
            IsHovered = false; // A pill that was hidden under the pointer gets no MouseLeave.
            _followHome = FollowPointer && _anchor is null && PInvoke.GetCursorPos(out var cursor)
                ? TopEdge.DefaultAnchor(TopGap, new ScreenPoint(cursor.X, cursor.Y))
                : null;
            _pillSize = PillSize();
            Place(Handle);
            StopAnimations();
            (_pill.Width, _pill.Height, _pill.Opacity) = (_pillSize.Height, _pillSize.Height, 0);
            Show(); // Not activated: ShowActivated is off.
        }

        // A line that replaces another one only changes size if it needs to (nothing animates for the same size), and an
        // opaque pill is not faded again.
        Resize(force: fresh); // Also starts the scrolling of a long line.
        if (fresh || _pill.Opacity < 0.999)
        {
            Animate(OpacityProperty, 1, Fade);
        }
    }

    /// <summary>Takes the pill away: it shrinks and fades first, or is gone at once.</summary>
    public void Withdraw(bool animated)
    {
        _present = false;
        _marquee = null; // Shown again, a long line starts from its beginning.
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
        Animate(OpacityProperty, 0, Fade, (_, _) =>
        {
            if (!_present)
            {
                Hide();
            }
        });
    }

    // ---- Place ----

    // The window must not take focus or show in Alt+Tab; set once, before it is first shown.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TopEdge.Prepare(this, RoomWidth, RoomHeight, TopGap, () =>
        {
            if (!IsDragging)
            {
                Reposition();
            }
        });
    }

    private ScreenPoint? Pointer() => PInvoke.GetCursorPos(out var cursor) ? new ScreenPoint(cursor.X, cursor.Y) : null;

    /// <summary>The middle of the top edge of the island: where the user put it, or its default place.</summary>
    public ScreenPoint HomePoint() =>
        _anchor ?? (IsVisible ? _followHome : null) ?? TopEdge.DefaultAnchor(TopGap, FollowPointer ? Pointer() : null);

    /// <summary>Where the pill is now on the screen, in pixels (its top edge's middle and its size), for the card.</summary>
    public (ScreenPoint Home, int PillHeight) PillOnScreen()
    {
        var home = HomePoint();
        var monitor = MonitorArea.At(home);
        return (home, monitor.ToPixels(PillHeight * _scale));
    }

    /// <summary>Where the pill is on the screen now (pixels: left, top, width, height), or <see langword="null"/> while hidden.</summary>
    public (int Left, int Top, int Width, int Height)? PillRect()
    {
        if (!IsVisible)
        {
            return null;
        }

        PInvoke.GetWindowRect(Handle, out var rect);
        var perUnit = (rect.right - rect.left) / RoomWidth;
        var (width, height) = ((int)Math.Round(_pillSize.Width * perUnit), (int)Math.Round(_pillSize.Height * perUnit));
        return (rect.left + (rect.right - rect.left - width) / 2, rect.top, width, height);
    }

    /// <summary>Lifts the island above the taskbar, which comes back on top whenever it is used.</summary>
    public void RaiseAboveTaskbar()
    {
        if (IsVisible)
        {
            TopEdge.RaiseAboveTaskbar(Handle);
        }
    }

    /// <summary>Puts the window back where it belongs (the screens changed, or its place or size did).</summary>
    public void Reposition() => Place(Handle);

    // The room goes where the pill's home is, in the size of that screen and of the pill. Near a screen's edge the room is
    // moved so all of the pill stays on the screen while it is open (it grows from its middle); it goes back when it closes.
    private void Place(HWND window)
    {
        var home = HomePoint();
        var monitor = MonitorArea.At(home);
        var perUnit = monitor.ToPixels(1000) / 1000.0 * _scale;
        var (roomWidth, roomHeight) = ((int)Math.Round(RoomWidth * perUnit), (int)Math.Round(RoomHeight * perUnit));
        var on = TopEdge.Clamp(home, (int)Math.Round(_pillSize.Width * perUnit), (int)Math.Round(_pillSize.Height * perUnit), TopEdge.BoundsAt(home));
        (Width, Height) = (RoomWidth * _scale, RoomHeight * _scale);
        PInvoke.SetWindowPos(window, new HWND(-1), on.X - roomWidth / 2, on.Y, roomWidth, roomHeight, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    // ---- Open, size, scrolling ----

    /// <summary>Whether the pill is currently expanded (hovered with hover-expand enabled).</summary>
    public bool IsExpanded => IsOpen;

    /// <summary>Collapses the pill back to its compact size.</summary>
    public void Collapse() => SetOpen(false);

    private bool IsOpen => (IsHovered || _pinnedOpen) && _hover.Expand && !IsDragging;

    private bool _pinnedOpen;

    /// <summary>
    /// Keeps the pill open (medium size, as under the pointer) without the pointer: while the card is up the pill it belongs
    /// to must not shrink back to its smallest.
    /// </summary>
    public bool PinOpen
    {
        get => _pinnedOpen;
        set
        {
            if (_pinnedOpen == value)
            {
                return;
            }

            _pinnedOpen = value;
            if (IsVisible && _present)
            {
                Resize();
            }
        }
    }

    private void SetOpen(bool open)
    {
        if (open && IsDragging)
        {
            return;
        }

        IsHovered = open;
        if (IsVisible && _present)
        {
            Resize();
        }

        HoverChanged?.Invoke(this, EventArgs.Empty);
    }

    // The size the pill has for what it shows. Closed: one line. Open (as the hover settings say): a line shows up to three;
    // a song stays one line and makes room for its time and buttons.
    private Size PillSize()
    {
        var open = IsOpen;
        var buttons = open && _music is not null && _hover.Buttons;
        var time = open && _music is not null && _hover.Time && _time.Text.Length > 0;
        var wraps = open && _music is null && _hover.FullText;
        _buttons.Visibility = buttons ? Visibility.Visible : Visibility.Collapsed;
        _time.Visibility = time ? Visibility.Visible : Visibility.Collapsed;
        _time.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _symbol.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var extra = (_symbol.Visibility == Visibility.Visible ? _symbol.DesiredSize.Width + 8 : 0)
            + (_cover.Source is null ? 0 : CoverSize + CoverGap)
            + (buttons ? ButtonsWidth + ButtonsGap : 0)
            + (time ? _time.DesiredSize.Width + 8 : 0);

        var room = Math.Max(40, (open ? OpenMaxWidth : ClosedMaxWidth) - 2 * PadX - extra);
        _text.Width = double.NaN;
        _text.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _text.MaxHeight = (wraps ? OpenMaxLines : 1) * LineHeight;
        _text.MaxWidth = wraps ? room : double.PositiveInfinity; // A line that does not wrap is as wide as it is, and scrolls.
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = _text.DesiredSize.Width;
        _text.Width = natural; // Fixed while the pill grows, so the text does not wrap again on the way.
        _textBox.Width = wraps ? natural : Math.Min(natural, room);
        _textBox.Height = _text.DesiredSize.Height;
        _overflow = wraps ? 0 : Math.Max(0, natural - room);
        // Measured here, outside a layout pass: the panels above the text are not marked as changed by it, and would answer
        // with the size they had for the line before (a short line after a long one kept the long one's width).
        foreach (var panel in new UIElement[] { _textBox, _row, _content })
        {
            panel.InvalidateMeasure();
        }

        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var content = _content.DesiredSize;
        return new Size(Math.Max(content.Width + 2 * PadX, PillHeight), Math.Max(content.Height + 2 * PadY, PillHeight));
    }

    private void Resize(bool force = false)
    {
        var size = PillSize();
        // Only what changed moves: a line that wraps changes the height alone, one that scrolls nothing at all.
        var widthChanged = force || Math.Abs(size.Width - _pillSize.Width) >= 0.5;
        var heightChanged = force || Math.Abs(size.Height - _pillSize.Height) >= 0.5;
        _pillSize = size;
        if (widthChanged || heightChanged)
        {
            // A fresh animation per change, so a pointer that goes in and out mid-way turns the pill around smoothly.
            if (widthChanged)
            {
                Animate(WidthProperty, _pillSize.Width, Grow);
            }

            if (heightChanged)
            {
                Animate(HeightProperty, _pillSize.Height, Grow);
            }

            if (!IsDragging && new WindowInteropHelper(this).Handle != 0)
            {
                Place(Handle); // Near an edge, the open pill stays on the screen.
            }
        }

        RestartMarquee();
    }

    // A line wider than the pill rests at its start, scrolls to its end, rests there, goes back and starts over. It does not
    // run all the time: the first rest is what lets the reader begin to read.
    private void RestartMarquee()
    {
        // The same text with the same room keeps scrolling where it is: a song's player reports often (its position), and
        // each report used to start the scrolling over, so it never got past its first rest.
        var key = (_text.Text, Math.Round(_overflow));
        if (_marquee == key)
        {
            return;
        }

        _marquee = key;
        _scroll.BeginAnimation(TranslateTransform.XProperty, null);
        _scroll.X = 0;
        if (_overflow < 1)
        {
            return;
        }

        var scrolled = Marquee.Rest + TimeSpan.FromSeconds(_overflow / Marquee.Speed);
        var atEnd = scrolled + Marquee.Rest;
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(animation, 30); // A slow scroll of text does not need 60 frames, and it runs for as long as the line is up.
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, Marquee.Rest));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-_overflow, scrolled));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-_overflow, atEnd));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, atEnd + Marquee.Return));
        _scroll.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    /// <summary>The size the pill is going to, in DIPs at 100 %; for the tests.</summary>
    internal Size PillTarget => _pillSize;

    /// <summary>What the text scrolls by now, in DIPs; for the tests.</summary>
    internal double Overflow => _overflow;

    /// <summary>The width the text is cut to, in DIPs; for the tests.</summary>
    internal double TextRoom => _textBox.Width;

    private void StopAnimations()
    {
        foreach (var property in new[] { WidthProperty, HeightProperty, OpacityProperty })
        {
            _pill.BeginAnimation(property, null);
        }
    }

    private void Animate(DependencyProperty property, double to, Duration duration, EventHandler? completed = null)
    {
        // One key frame on the spline: it starts from wherever the property is now, so a change in the middle of another
        // continues smoothly instead of jumping.
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration, KeyFrames = { new SplineDoubleKeyFrame(to, KeyTime.FromPercent(1), Spline) } };
        if (completed is not null)
        {
            animation.Completed += completed;
        }

        _pill.BeginAnimation(property, animation); // The pill's width, height and opacity, not the window's.
    }
}
