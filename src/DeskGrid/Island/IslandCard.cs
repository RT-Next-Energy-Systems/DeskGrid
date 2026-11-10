using System.IO;
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

/// <summary>What the card shows: every player, the usage, the state of the system, the folders and the shelf.</summary>
internal sealed record CardModel(
    IReadOnlyList<MediaTrack> Tracks, string? UsageText, double? UsagePercent, string? SystemText,
    IReadOnlyList<string> Folders, IReadOnlyList<string> Shelf);

/// <summary>
/// The island opened up: a dark card under (or over, near the bottom of the screen) the pill with the players and their
/// progress, the AI usage, the state of the system, the folders to open, and the shelf where files can be parked by dropping
/// them and dragged out of later. Like the pill it never takes focus; it closes a few seconds after the pointer has left it.
/// </summary>
internal sealed class IslandCard : Window
{
    private const double CardWidth = 380;
    private const double Pad = 14;
    private const double Gap = 8; // DIPs between the pill and the card.
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(8);
    private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly QuinticEase Ease = FrozenEase();

    private readonly StackPanel _body = new();
    private readonly Border _frame;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, (MediaTrack Song, Border Fill, Border Rail, TextBlock Time)> _bars = new(StringComparer.Ordinal);
    private readonly MusicActions _music;
    private TextBlock? _system;
    private string _signature = "";
    private Point? _pressed;
    private string[]? _pressedPaths;
    private TimeSpan _idle;
    private ScreenPoint _home;
    private int _pillHeight; // In pixels: the card goes under (or over) the pill.
    private bool _opensAbove;
    private Func<CardModel> _model = () => new CardModel([], null, null, null, [], []);

    public IslandCard(MusicActions music)
    {
        _music = music;
        Title = Strings.AppName;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        AllowDrop = true;
        Width = CardWidth;

        _frame = new Border
        {
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(Pad),
            Child = _body,
            Margin = new Thickness(0),
            RenderTransform = _scale,
        };
        Content = _frame;
        _tick.Tick += (_, _) => OnTick();
        DragOver += (_, e) =>
        {
            _idle = TimeSpan.Zero;
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                ShelfDropped?.Invoke(paths);
            }

            e.Handled = true;
        };
        MouseMove += (_, _) => _idle = TimeSpan.Zero;
        Closed += (_, _) => _tick.Stop();
    }

    private static QuinticEase FrozenEase()
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    /// <summary>Raised with the name of the folder the user chose.</summary>
    public event Action<string>? FolderChosen;

    /// <summary>Raised with the paths of files dropped on the card: they go on the shelf.</summary>
    public event Action<string[]>? ShelfDropped;

    /// <summary>Raised when the user took a file off the shelf (the cross), or dragged it out to somewhere that took it.</summary>
    public event Action<string>? ShelfRemoved;

    /// <summary>Raised when the user cleared all files from the shelf, or dragged them all out at once.</summary>
    public event Action? ShelfCleared;

    /// <summary>Raised when the user clicked a file on the shelf.</summary>
    public event Action<string>? ShelfOpened;

    /// <summary>Opens the card under the island, whose top edge is at <paramref name="home"/> (the middle of it, in screen pixels).</summary>
    public void Open(Func<CardModel> model, ScreenPoint home, int pillHeight)
    {
        _model = model;
        _home = home;
        _pillHeight = pillHeight;
        _signature = "";
        Build(model());
        var handle = new HWND(new WindowInteropHelper(this).EnsureHandle());
        Place(handle);
        if (!IsVisible)
        {
            _frame.RenderTransformOrigin = new Point(0.5, _opensAbove ? 1.0 : 0.0);
            _frame.Opacity = 0;
            (_scale.ScaleX, _scale.ScaleY) = (0.94, 0.94);
            Show();
            _frame.BeginAnimation(OpacityProperty, new DoubleAnimation(1, EnterDuration) { EasingFunction = Ease });
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, EnterDuration) { EasingFunction = Ease });
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, EnterDuration) { EasingFunction = Ease });
        }

        TopEdge.RaiseAboveTaskbar(handle);
        _idle = TimeSpan.Zero;
        _tick.Start();
    }

    // The tool-window styles so it takes no focus and has no Alt+Tab entry; and a fresh place when the screen's DPI differs.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TopEdge.Prepare(this, CardWidth, double.NaN, 0, () => Place(new HWND(new WindowInteropHelper(this).Handle)));
    }

    /// <summary>Brings the content up to date (the shelf changed, a song moved on).</summary>
    public void Refresh()
    {
        var model = _model();
        if (Signature(model) != _signature)
        {
            Build(model);
            Place(new HWND(new WindowInteropHelper(this).Handle));
        }
        else
        {
            UpdateProgress(model);
        }
    }

    private void OnTick()
    {
        Refresh();
        _idle = IsMouseOver ? TimeSpan.Zero : _idle + _tick.Interval;
        if (!IslandScene.SystemOverlayInFront())
        {
            TopEdge.RaiseAboveTaskbar(new HWND(new WindowInteropHelper(this).Handle));
        }

        if (_idle >= IdleClose)
        {
            Close();
        }
    }

    // ---- Content ----

    private static string Signature(CardModel m) => string.Join('|',
        m.Tracks.Select(t => $"{t.Source}:{t.Title}:{t.Artist}:{t.Playing}"), m.UsageText is null, m.SystemText is null,
        string.Join(',', m.Folders), string.Join(',', m.Shelf));

    private void Build(CardModel model)
    {
        _signature = Signature(model);
        _body.Children.Clear();
        _bars.Clear();
        _system = null;
        var any = false;

        foreach (var track in model.Tracks.Take(3))
        {
            _body.Children.Add(TrackRow(track));
            any = true;
        }

        if (model.UsageText is { } usage)
        {
            _body.Children.Add(Label(usage, new Thickness(0, any ? 12 : 0, 0, 4)));
            _body.Children.Add(Bar(model.UsagePercent ?? 0, UsageBrush(model.UsagePercent ?? 0)).Track);
            any = true;
        }

        if (model.SystemText is not null)
        {
            _system = Label(model.SystemText, new Thickness(0, any ? 12 : 0, 0, 0));
            _body.Children.Add(_system);
            any = true;
        }

        if (model.Folders.Count > 0)
        {
            _body.Children.Add(Heading(Strings.CardFolders, any));
            var panel = new WrapPanel();
            foreach (var folder in model.Folders.Take(14))
            {
                panel.Children.Add(Chip("", folder, () => FolderChosen?.Invoke(folder)));
            }

            _body.Children.Add(panel);
            any = true;
        }

        _body.Children.Add(ShelfHeading(model.Shelf.Count, any));
        if (model.Shelf.Count == 0)
        {
            _body.Children.Add(Label(Strings.CardShelfHint, new Thickness(0), dim: true));
        }
        else
        {
            var shelf = new WrapPanel();
            if (model.Shelf.Count > 1)
            {
                shelf.Children.Add(ShelfBundleChip([.. model.Shelf]));
            }

            foreach (var path in model.Shelf)
            {
                shelf.Children.Add(ShelfChip(path));
            }

            _body.Children.Add(shelf);
        }
    }

    private UIElement ShelfHeading(int count, bool spaced)
    {
        var title = Heading(Strings.CardShelf, spaced);
        if (count == 0)
        {
            return title;
        }

        var clear = new TextBlock
        {
            Text = Strings.CardShelfClear,
            Foreground = Dim,
            FontSize = 11,
            Margin = new Thickness(0, spaced ? 14 : 0, 0, 6),
            Background = Brushes.Transparent,
            Opacity = 0.85,
        };
        clear.MouseEnter += (_, _) => clear.Opacity = 1;
        clear.MouseLeave += (_, _) => clear.Opacity = 0.85;
        clear.MouseLeftButtonDown += (_, e) => e.Handled = true;
        clear.MouseLeftButtonUp += (_, e) =>
        {
            ShelfCleared?.Invoke();
            e.Handled = true;
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(clear, Dock.Right);
        row.Children.Add(clear);
        row.Children.Add(title);
        return row;
    }

    private UIElement TrackRow(MediaTrack track)
    {
        var cover = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Child = track.Cover is null ? null : new Image { Source = track.Cover, Stretch = Stretch.UniformToFill },
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = track.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (track.Artist.Length > 0)
        {
            texts.Children.Add(new TextBlock { Text = track.Artist, Foreground = Dim, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var play = Glyph(track.Playing ? "" : "", () => _music.PlayPause(track.Source));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Glyph("", () => _music.Previous(track.Source)), play, Glyph("", () => _music.Next(track.Source)) },
        };
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(cover, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(cover);
        top.Children.Add(buttons);
        top.Children.Add(texts);

        var accent = track.Accent is { } color ? new SolidColorBrush(color) : Brushes.White;
        var (barTrack, fill) = Bar(0, accent);
        var time = new TextBlock { Foreground = Dim, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        _bars[track.Source] = (track, fill, barTrack, time);
        UpdateBar(track);

        var hitBar = new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(0, 4, 0, 4),
            Background = Brushes.Transparent,
            Child = barTrack,
        };
        hitBar.MouseLeftButtonUp += (_, e) =>
        {
            if (barTrack.ActualWidth > 0 && _bars.TryGetValue(track.Source, out var state) && state.Song.Timeline is { } line && line.Duration > TimeSpan.Zero)
            {
                var fraction = e.GetPosition(barTrack).X / barTrack.ActualWidth;
                _music.Seek?.Invoke(track.Source, line.SeekTarget(fraction));
                e.Handled = true;
            }
        };

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(top);
        row.Children.Add(hitBar);
        row.Children.Add(time);
        return row;
    }

    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));

    private static Brush UsageBrush(double percent) =>
        new SolidColorBrush(percent >= 100 ? Color.FromRgb(0xE8, 0x4D, 0x4D) : percent >= 80 ? Color.FromRgb(0xF0, 0xA0, 0x30) : Color.FromRgb(0x4C, 0xC3, 0x8A));

    // A thin bar: the track and the fill in it (the fill's width is set from the track's, see Fill).
    private static (Border Track, Border Fill) Bar(double percentOrFraction, Brush brush)
    {
        var fill = new Border { Background = brush, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Child = fill };
        var fraction = percentOrFraction > 1 ? Math.Min(percentOrFraction, 100) / 100 : percentOrFraction;
        fill.Tag = fraction;
        track.SizeChanged += (_, _) => Fill(track, fill);
        return (track, fill);
    }

    private static void Fill(Border track, Border fill) => fill.Width = track.ActualWidth * Math.Clamp(fill.Tag is double f ? f : 0, 0, 1);

    private void UpdateProgress(CardModel model)
    {
        foreach (var track in model.Tracks)
        {
            if (_bars.ContainsKey(track.Source))
            {
                UpdateBar(track);
            }
        }

        if (_system is not null && model.SystemText is not null)
        {
            _system.Text = model.SystemText;
        }
    }

    private void UpdateBar(MediaTrack track)
    {
        if (!_bars.TryGetValue(track.Source, out var bar))
        {
            return;
        }

        var (_, fill, barTrack, time) = bar;
        _bars[track.Source] = (track, fill, barTrack, time);
        var now = DateTimeOffset.UtcNow;
        if (track.Timeline is { } line)
        {
            fill.Tag = line.Fraction(now, track.Playing) ?? 0;
            time.Text = MediaTimeline.Format(line.PositionAt(now, track.Playing)) + " / " + MediaTimeline.Format(line.Duration);
        }
        else
        {
            fill.Tag = 0.0;
            time.Text = "";
        }

        Fill(barTrack, fill);
    }

    private static TextBlock Label(string text, Thickness margin, bool dim = false) =>
        new() { Text = text, Foreground = dim ? Dim : Brushes.White, FontSize = 12, Margin = margin, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Heading(string text, bool spaced) =>
        new() { Text = text, Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, spaced ? 14 : 0, 0, 6) };

    private static TextBlock Glyph(string glyph, Action click)
    {
        var text = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Brushes.White, Width = 30,
            TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent, Opacity = 0.8,
        };
        text.MouseEnter += (_, _) => text.Opacity = 1;
        text.MouseLeave += (_, _) => text.Opacity = 0.8;
        text.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return text;
    }

    private static Border Chip(string glyph, string text, Action click)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        chip.MouseLeftButtonUp += (_, _) => click();
        return chip;
    }

    // A chip that drags every file on the shelf at once.
    private UIElement ShelfBundleChip(string[] paths)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = Brushes.White, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = string.Format(Strings.CardShelfAll, paths.Length), Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        WireShelfDrag(chip, paths, null);
        return chip;
    }

    // A file on the shelf: a click opens it, a drag takes it out (to a folder's panel, a widget, Explorer), the cross removes it.
    private UIElement ShelfChip(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        var label = new TextBlock { Text = name.Length > 24 ? name[..23] + "…" : name, Foreground = Brushes.White, FontSize = 12, ToolTip = path };
        var cross = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 9, Foreground = Dim, Margin = new Thickness(8, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
        };
        cross.MouseLeftButtonDown += (_, e) => e.Handled = true;
        cross.MouseLeftButtonUp += (_, e) =>
        {
            ShelfRemoved?.Invoke(path);
            e.Handled = true;
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, cross } },
        };
        WireShelfDrag(chip, [path], () => ShelfOpened?.Invoke(path));
        return chip;
    }

    private void WireShelfDrag(Border chip, string[] paths, Action? onClick)
    {
        chip.MouseLeftButtonDown += (_, e) =>
        {
            (_pressed, _pressedPaths) = (e.GetPosition(this), paths);
            chip.CaptureMouse();
        };
        chip.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pressed is not { } start || !ReferenceEquals(_pressedPaths, paths))
            {
                return;
            }

            var now = e.GetPosition(this);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _pressed = null;
            _pressedPaths = null;
            chip.ReleaseMouseCapture();
            var data = new DataObject(DataFormats.FileDrop, paths);
            var effect = DragDrop.DoDragDrop(chip, data, DragDropEffects.Copy | DragDropEffects.Link);
            if (effect != DragDropEffects.None)
            {
                if (paths.Length > 1)
                {
                    ShelfCleared?.Invoke();
                }
                else
                {
                    ShelfRemoved?.Invoke(paths[0]); // It went somewhere: it is not waiting here any more.
                }
            }
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            chip.ReleaseMouseCapture();
            if (_pressed is not null && ReferenceEquals(_pressedPaths, paths))
            {
                _pressed = null;
                _pressedPaths = null;
                onClick?.Invoke();
            }
        };
    }

    // ---- Place ----

    // Under the pill, centred on it; over it when there is no room below. The size is that of the screen it is on.
    private void Place(HWND window)
    {
        var monitor = MonitorArea.At(_home);
        var screen = TopEdge.BoundsAt(_home);
        _frame.Measure(new Size(CardWidth, double.PositiveInfinity));
        var height = _frame.DesiredSize.Height;
        (Width, Height) = (CardWidth, height);
        var (w, h) = (monitor.ToPixels(CardWidth), monitor.ToPixels(height));
        var below = _home.Y + _pillHeight + monitor.ToPixels(Gap);
        _opensAbove = below + h > screen.Bottom;
        var y = !_opensAbove ? below : Math.Max(screen.Top, _home.Y - monitor.ToPixels(Gap) - h);
        var x = Math.Clamp(_home.X - w / 2, screen.Left, Math.Max(screen.Left, screen.Right - w));
        PInvoke.SetWindowPos(window, new HWND(-1), x, y, w, h, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
}
