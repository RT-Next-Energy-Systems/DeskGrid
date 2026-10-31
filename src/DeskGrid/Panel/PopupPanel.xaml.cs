using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Folders;
using DeskGrid.Icons;
using DeskGrid.Resources;
using DeskGrid.Shell;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Panel;

/// <summary>
/// The grid of a folder's items that opens next to its widget. Fixed column count, scrolls past
/// <see cref="MaxVisibleRows"/> rows. Closes itself on Esc or when it loses activation. Items can be dragged
/// (the others slide out of the way like on a phone's home screen), and the folder's name can be edited.
/// Opened from the taskbar it can also list all folders instead; clicking one shows that folder's items in
/// the same window, with an arrow back to the list.
/// </summary>
internal partial class PopupPanel : Window
{
    private const int MaxVisibleRows = 4;
    private const double ItemWidth = 76;
    private const double ItemHeight = 96;
    private const double IconCenterY = 28; // button padding 4 + half of the 48 DIP icon
    private const double AnchorGap = 8;

    // Virtual-key codes of the mouse buttons (winuser.h); a drag can be made with either.
    private const int LeftButton = 0x01;
    private const int RightButton = 0x02;

    private static readonly TimeSpan ResizeTime = TimeSpan.FromMilliseconds(180);
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(200));
    // Frozen: shared by every panel, and panels may live on different threads (the tests have several).
    private static readonly IEasingFunction SlideEasing = (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseOut }.GetAsFrozen();

    // Opening, the content grows out of its widget from this size while it fades in.
    private const double OpeningScale = 0.85;

    // During a drag: how long the cursor may be off the panel and its widget before the folder closes (long enough
    // to cross from the widget to the panel), and, once the mouse button is up, how long the drop may take to arrive.
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LeaveDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DropDelay = TimeSpan.FromMilliseconds(250);

    private readonly ScreenRect _anchor;
    private readonly PanelTheme _theme;
    private readonly IconService _icons;
    private readonly int _columns;
    private bool _closing;
    private bool _hiddenForDrag;
    private bool _cloakedUntilFirstFrame;
    private PanelSide? _side;
    private List<PanelItem> _items = [];
    private PanelItem? _pressed;
    private Point _pressPoint;
    private bool _dragging;
    private bool _reordered;
    private bool _renaming;
    private bool _menuOpen;
    private DispatcherTimer? _resize;
    private ScreenCapture? _capture;

    // Watches the cursor while a drag involves this panel: one of its items being dragged, or (for a panel opened
    // by a drag held over its widget) the drag that opened it. See OnWatch.
    private readonly DispatcherTimer _watch = new() { Interval = WatchInterval };
    private readonly System.Diagnostics.Stopwatch _away = new();
    private readonly System.Diagnostics.Stopwatch _released = new();
    private bool _followsDrag;
    private bool _tookDrop;
    private bool _awaitsClick;

    // All folders, if this panel was opened as their list; null for the panel of a single folder.
    private IReadOnlyList<WidgetFolder>? _list;

    // Dragging an item out of this panel: its index, the slot it would land in (-1 outside the panel), the
    // cursor-to-icon offset, and what to do once the drag is over.
    private int _dragIndex = -1;
    private int _slot = -1;
    private (double X, double Y) _grab;
    private DragGhost? _ghost;
    private ScreenPoint? _landing;
    private ItemReorderedEventArgs? _pendingReorder;

    // A rescan that arrived mid-drag (shown when the drag is over), and the item whose icon is still flying
    // back to its slot, which stays empty until it lands.
    private IReadOnlyList<WidgetFolder>? _deferred;
    private string? _settlingItem;

    // Something dragged over this panel from elsewhere: the slot left open for it, or -1.
    private int _gap = -1;

    /// <summary>Call <see cref="ShowFolder"/> or <see cref="ShowList"/> before showing the panel.</summary>
    public PopupPanel(ScreenRect anchor, int columns, PanelTheme theme, IconService icons)
    {
        InitializeComponent();
        _anchor = anchor;
        _theme = theme;
        _icons = icons;
        _columns = columns;

        ApplyTheme();
        EditGlyph.ToolTip = Strings.PanelRename;
        BackButton.ToolTip = Strings.PanelBack;
        Items.Width = columns * ItemWidth;
        Scroller.MaxHeight = MaxVisibleRows * ItemHeight;

        SourceInitialized += OnSourceInitialized;
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            _watch.Stop();
            _capture?.Dispose();
        };
        _watch.Tick += OnWatch;
        // While an item is being dragged the drop target (the desktop, another app) takes activation; a command of
        // its context menu (a confirmation, the properties) does too, and the panel must outlive the command.
        Deactivated += (_, _) =>
        {
            if (_dragging || _menuOpen)
            {
                return;
            }

            if (ScreenCapture.HasForeground())
            {
                // Stay for the screenshot; close once the capture is over, unless it handed activation back.
                _capture?.Dispose();
                _capture = ScreenCapture.WaitUntilDone(foreground =>
                {
                    if (foreground != new HWND(new WindowInteropHelper(this).Handle))
                    {
                        Dismiss();
                    }
                });
                return;
            }

            if (_renaming)
            {
                CommitRename(keepOpenOnError: false);
            }

            Dismiss();
        };
    }

    public event EventHandler<FolderItem>? ItemInvoked;

    public event EventHandler<ItemReorderedEventArgs>? ItemReordered;

    /// <summary>Items from another place were dropped here.</summary>
    public event EventHandler<ItemsDroppedEventArgs>? ItemsDropped;

    /// <summary>An item of this panel was dragged and let go (wherever it landed).</summary>
    public event EventHandler? DragFinished;

    /// <summary>"New folder" was chosen from the panel's menu; the panel has closed.</summary>
    public event EventHandler? NewFolderRequested;

    /// <summary>"Delete folder" was chosen from the panel's menu; the panel has closed.</summary>
    public event EventHandler<WidgetFolder>? DeleteRequested;

    /// <summary>Renames the folder on disk: old name, new name; returns why not, or <see langword="null"/> on success.</summary>
    public Func<string, string, string?>? Renamer { get; set; }

    /// <summary>The folder whose items are shown, or <see langword="null"/> while the list of all folders is.</summary>
    public WidgetFolder? Folder { get; private set; }

    /// <summary>Whether this panel was opened as the list of all folders (it may be showing one of them now).</summary>
    public bool IsFolderList => _list is not null;

    /// <summary>
    /// Whether the panel takes the focus when it appears. Off for one that opens in the middle of a drag, where
    /// the drag keeps the focus; <see cref="Window.Activate"/> it once the drag is over.
    /// </summary>
    public bool ActivateWhenShown { get; set; } = true;

    /// <summary>Whether one of this panel's items is being dragged.</summary>
    public bool IsDragSource => _dragging;

    /// <summary>Whether something dragged in from another place was dropped here.</summary>
    public bool TookDrop => _tookDrop;

    /// <summary>Shows <paramref name="folder"/>'s current items.</summary>
    public void ShowFolder(WidgetFolder folder)
    {
        if (Folder is null || !string.Equals(Folder.Name, folder.Name, StringComparison.OrdinalIgnoreCase))
        {
            _side = null; // Something else to show: it may fit better on another side.
        }

        Folder = folder;
        Heading.Text = folder.Name;
        ShowItems(folder.Items.Select(i => new PanelItem(i)), Strings.PanelEmpty);
    }

    /// <summary>Shows every folder; clicking one shows its items here.</summary>
    public void ShowList(IReadOnlyList<WidgetFolder> folders)
    {
        _list = folders;
        Folder = null;
        _side = null;
        Heading.Text = Strings.AppName;
        ShowItems(folders.Select(f => new PanelItem(f)), Strings.PanelNoFolders);
    }

    /// <summary>
    /// Shows the folders as they are after a rescan. Returns <see langword="false"/> if what this panel shows is
    /// gone (its folder was deleted or renamed elsewhere), so it should close.
    /// </summary>
    public bool Refresh(IReadOnlyList<WidgetFolder> widgets)
    {
        if (_dragging)
        {
            // Rebuilding the items now would pull the dragged one out from under the drag.
            _deferred = widgets;
            return true;
        }

        // Newer than anything held back during a drag.
        _deferred = null;
        if (_list is not null)
        {
            _list = widgets;
        }

        if (Folder is { } shown)
        {
            if (widgets.FirstOrDefault(w => string.Equals(w.Name, shown.Name, StringComparison.OrdinalIgnoreCase)) is { } folder)
            {
                ShowFolder(folder);
                return true;
            }

            if (_list is null)
            {
                return false;
            }
        }

        ShowList(widgets);
        return true;
    }

    /// <summary>Closes the panel unless it is already closing.</summary>
    public void Dismiss()
    {
        if (!_closing)
        {
            Close();
        }
    }

    /// <summary>
    /// Another panel is about to open under the cursor while this panel's item is being dragged: this panel
    /// disappears, but stays alive until the drag is over, since the drag runs inside it.
    /// </summary>
    public void HideForDrag()
    {
        _hiddenForDrag = true;
        Backdrop.SetCloaked(new HWND(new WindowInteropHelper(this).Handle), true);
    }

    /// <summary>
    /// The item being dragged out of this panel came back over its widget: the panel shows again, with the item's
    /// slot open where it was, so the item can be put back.
    /// </summary>
    public void ShowAgainForDrag()
    {
        if (!_hiddenForDrag || _dragIndex < 0)
        {
            return;
        }

        _hiddenForDrag = false;
        _away.Reset();
        _slot = _dragIndex;
        Slide(landAt: _slot);
        Backdrop.SetCloaked(new HWND(new WindowInteropHelper(this).Handle), false);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        if (_hiddenForDrag)
        {
            // Gone from the screen (it may still be hit): nothing to drop on.
            e.Effects = DragDropEffects.None;
        }
        else if (IsOwnItem(e))
        {
            var slot = SlotAt(e.GetPosition(Items), _items.Count - 1) ?? _slot;
            if (slot != _slot)
            {
                _slot = slot;
                Slide(landAt: slot);
            }

            e.Effects = DragDropEffects.Move;
        }
        else if (Folder is null)
        {
            // The list of folders only takes its own folders being rearranged.
            e.Effects = DragDropEffects.None;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // From another panel or another app: open a gap where it would go.
            if (_gap < 0)
            {
                MakeRoomForIncoming(true);
            }

            var gap = SlotAt(e.GetPosition(Items), _items.Count) ?? (_gap < 0 ? _items.Count : _gap);
            if (gap != _gap)
            {
                _gap = gap;
                Slide(gapAt: gap);
            }

            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        // DragLeave also arrives from the children while the cursor is still inside the window. An item of this
        // panel that leaves is taken out by OnWatch, once it has been away for a moment.
        if (_closing || CursorOverWindow())
        {
            return;
        }

        if (_gap >= 0)
        {
            _gap = -1;
            Slide(gapAt: null);
            MakeRoomForIncoming(false);
        }
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (_hiddenForDrag)
        {
            e.Effects = DragDropEffects.None;
        }
        else if (IsOwnItem(e) && e.Data.GetData(PanelDragData.ItemFormat) is string name)
        {
            _reordered = true;
            if (_slot >= 0)
            {
                _landing = SlotCenter(_slot);
                _pendingReorder = new ItemReorderedEventArgs(Folder?.Name ?? ItemReorderedEventArgs.FolderList, name, _slot);
            }

            e.Effects = DragDropEffects.Move;
        }
        else if (Folder is not { } folder)
        {
            e.Effects = DragDropEffects.None;
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            var index = _gap >= 0 ? _gap : _items.Count;
            _gap = -1;
            // Close the gap: if nothing could be added, no refresh rebuilds the items.
            Slide(gapAt: null);
            _resize?.Stop();
            Items.MinHeight = 0; // the refresh that follows sizes the window for the new item
            _tookDrop = true;
            ItemsDropped?.Invoke(this, new ItemsDroppedEventArgs(folder.Name, paths, index));
            FitToContent();

            // We moved or copied the files ourselves; never report Move, or the source would delete them again.
            e.Effects = DragDropEffects.Copy;
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Dismiss();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private bool IsOwnItem(DragEventArgs e) =>
        _dragIndex >= 0 && PanelDragData.IsFrom(e.Data, Folder?.Name ?? ItemReorderedEventArgs.FolderList);

    private static bool MouseButtonDown() =>
        ((PInvoke.GetAsyncKeyState(LeftButton) | PInvoke.GetAsyncKeyState(RightButton)) & 0x8000) != 0;

    // Whether the cursor has been off this panel and its widget for LeaveDelay.
    private bool HasLeft()
    {
        if (CursorOverWindow() || _anchor.Contains(ScreenPoint.AtCursor()))
        {
            _away.Reset();
            return false;
        }

        _away.Start();
        return _away.Elapsed >= LeaveDelay;
    }

    // An item of this panel being dragged: once it has been away from the panel (and its widget) for a moment, the
    // folder closes. DragLeave alone can't tell: it arrives only while the window is the drop target.
    // A panel opened by a drag held over its widget closes when the drag moves away, or ends without a drop here.
    // After a drop it stays and takes the focus; if Windows won't hand it the focus (the drag came from another
    // app), a click elsewhere closes it.
    private void OnWatch(object? sender, EventArgs e)
    {
        if (_closing)
        {
            _watch.Stop();
        }
        else if (_dragging)
        {
            if (!_hiddenForDrag && _dragIndex >= 0 && HasLeft())
            {
                TakeOut();
            }
        }
        else if (!_followsDrag || IsActive)
        {
            _followsDrag = false;
            _watch.Stop();
        }
        else if (_awaitsClick)
        {
            // A click on the widget itself closes the panel through the widget.
            if (MouseButtonDown() && !CursorOverWindow() && !_anchor.Contains(ScreenPoint.AtCursor()))
            {
                Dismiss();
            }
        }
        else if (MouseButtonDown())
        {
            _released.Reset();
            if (HasLeft())
            {
                Dismiss();
            }
        }
        else
        {
            _released.Start();
            if (_released.Elapsed < DropDelay)
            {
                return;
            }

            if (!_tookDrop)
            {
                Dismiss();
                return;
            }

            _awaitsClick = true;
            Activate();
        }
    }

    // Compared in physical pixels, as the window manager does: in DIPs, a cursor that has just left through the
    // right or bottom edge can round back inside, and the panel would miss that the item was taken out.
    private bool CursorOverWindow()
    {
        PInvoke.GetCursorPos(out var cursor);
        PInvoke.GetWindowRect(new HWND(new WindowInteropHelper(this).Handle), out var bounds);
        return cursor.X >= bounds.left && cursor.X < bounds.right && cursor.Y >= bounds.top && cursor.Y < bounds.bottom;
    }

    private void ShowItems(IEnumerable<PanelItem> items, string emptyHint)
    {
        _pressed = null;
        _items = [.. items];
        Items.ItemsSource = _items;
        EmptyHint.Text = emptyHint;
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _list is not null && Folder is not null ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _items)
        {
            _ = item.LoadIconAsync(_icons);
        }

        FitToContent();

        // Measuring has made the item containers. The icon of a dropped item is still on its way to its slot.
        if (_settlingItem is { } name
            && ContainerAt(_items.FindIndex(i => string.Equals(i.Item?.Name ?? i.Folder?.Name, name, StringComparison.OrdinalIgnoreCase))) is { } landing)
        {
            landing.Opacity = 0;
        }
    }

    private void ApplyTheme()
    {
        Foreground = new SolidColorBrush(_theme.Foreground);
        Resources["HoverBrush"] = new SolidColorBrush(_theme.Hover);
        Resources["TileBrush"] = new SolidColorBrush(_theme.Tile);

        if (_theme.UseAcrylic)
        {
            // DWM only draws a system backdrop for windows with a frame. Keep the frame, but extend it
            // over the whole window (no visible non-client area); see OnSourceInitialized for the rest.
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                UseAeroCaptionButtons = false,
            });
        }
        else
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Frame.CornerRadius = new CornerRadius(8);
            Frame.BorderThickness = new Thickness(1);
            Frame.BorderBrush = new SolidColorBrush(_theme.Outline);
            Frame.Background = new SolidColorBrush(_theme.Background);
        }
    }

    // Sized from the content rather than SizeToContent, which adds a non-client frame that
    // WindowChrome has removed and so leaves the window too big.
    private void FitToContent()
    {
        _resize?.Stop();
        var size = MeasureContent();
        Resize(size.Height, size.Width);
    }

    // Moves and sizes the window in one step before WPF takes the new size: grown in place first, a panel near the
    // bottom of the screen would reach past it for a moment, and the part that was off screen stays unpainted (the
    // last row's names cut off) after the panel moves back up.
    private void Resize(double height, double? width = null)
    {
        if (new WindowInteropHelper(this).Handle is not 0 and var window)
        {
            Reposition(new HWND(window), new Size(width ?? Width, height));
        }

        if (width is { } w)
        {
            Width = w;
        }

        Height = height;
    }

    // New items only mark their own panel for measuring, not the elements above it, so a plain Measure of the frame
    // would answer the size of the previous items; the pending layout has to run first.
    private Size MeasureContent()
    {
        UpdateLayout();
        Frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Frame.DesiredSize;
    }

    // Makes room for one more item (a new row if the last one is full) while something is dragged over, or takes
    // the room back, growing or shrinking the window smoothly.
    private void MakeRoomForIncoming(bool incoming)
    {
        var rows = (_items.Count + (incoming ? 1 : 0) + _columns - 1) / _columns;
        var currentRows = Math.Max(1, (_items.Count + _columns - 1) / _columns);
        Items.MinHeight = incoming && rows > currentRows ? rows * ItemHeight : 0;
        var from = Height;
        var to = MeasureContent().Height;
        if (Math.Abs(from - to) < 0.5)
        {
            return;
        }

        _resize?.Stop();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = _resize = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / ResizeTime);
            // Also keeps the panel on the side of its widget it opened on.
            Resize(from + (to - from) * (1 - Math.Pow(1 - t, 3)));

            if (t >= 1)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var window = new HWND(new WindowInteropHelper(this).Handle);
        if (_theme.UseAcrylic)
        {
            HwndSource.FromHwnd(window).CompositionTarget.BackgroundColor = Colors.Transparent;

            // Without a system menu DWM draws no caption buttons in the extended frame.
            var style = (WINDOW_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            PInvoke.SetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (int)(style & ~WINDOW_STYLE.WS_SYSMENU));

            if (Backdrop.TryApplyAcrylic(window, _theme.IsDark))
            {
                // The backdrop and the first frame of content arrive a moment apart; stay invisible until both
                // are there, then appear in one piece.
                Backdrop.SetCloaked(window, true);
                _cloakedUntilFirstFrame = true;
            }
            else
            {
                Frame.Background = new SolidColorBrush(_theme.Background);
            }
        }

        // Before the first show, so the panel never flashes elsewhere.
        Reposition(window);
        PrepareOpening(window);
        ContentRendered += OnFirstFrame;

        if (!ActivateWhenShown)
        {
            _followsDrag = true;
            _watch.Start();
        }
    }

    private void OnFirstFrame(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstFrame;
        if (!_cloakedUntilFirstFrame)
        {
            PlayOpening();
            return;
        }

        // Let DWM compose that frame with the backdrop before the window is revealed.
        Dispatcher.BeginInvoke(() =>
        {
            if (!_closing && !_hiddenForDrag)
            {
                Backdrop.SetCloaked(new HWND(new WindowInteropHelper(this).Handle), false);
                PlayOpening();
                if (ActivateWhenShown)
                {
                    Activate();
                }
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    // Holds the content small and transparent until the window is on screen, scaled toward the widget's center so
    // the folder seems to grow out of it. Skipped when Windows' animation effects are turned off.
    private void PrepareOpening(HWND window)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        PInvoke.GetWindowRect(window, out var bounds);
        var width = Math.Max(1, bounds.right - bounds.left);
        var height = Math.Max(1, bounds.bottom - bounds.top);
        Frame.RenderTransformOrigin = new Point(
            Math.Clamp(((_anchor.Left + _anchor.Right) / 2.0 - bounds.left) / width, 0, 1),
            Math.Clamp(((_anchor.Top + _anchor.Bottom) / 2.0 - bounds.top) / height, 0, 1));
        Frame.RenderTransform = new ScaleTransform(OpeningScale, OpeningScale);
        Frame.Opacity = 0;
    }

    // Grows and fades the content in; a backdrop (acrylic) is already there at full size.
    private void PlayOpening()
    {
        if (Frame.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var grow = new DoubleAnimation(1, SlideDuration) { EasingFunction = SlideEasing };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        Frame.BeginAnimation(OpacityProperty, new DoubleAnimation(1, SlideDuration) { EasingFunction = SlideEasing });
    }

    // Moves the window to where it goes for its current size, or for (and to) a new size in DIPs.
    private void Reposition(HWND window, Size? size = null)
    {
        var monitor = MonitorArea.At(new ScreenPoint((_anchor.Left + _anchor.Right) / 2, (_anchor.Top + _anchor.Bottom) / 2));
        var width = monitor.ToPixels(size?.Width ?? Width);
        var height = monitor.ToPixels(size?.Height ?? Height);
        var gap = monitor.ToPixels(AnchorGap);

        // The side is chosen once for what the panel shows. Growing to make room for something dragged in must not
        // move it to another side: the cursor would be outside it, the room would be taken back, and the panel would
        // jump back and forth.
        _side ??= PanelPlacement.Side(_anchor, width, height, monitor.WorkArea, gap);
        var position = PanelPlacement.Place(_anchor, width, height, monitor.WorkArea, gap, _side.Value);
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED;
        PInvoke.SetWindowPos(window, HWND.Null, position.X, position.Y, width, height,
            size is null ? flags | SET_WINDOW_POS_FLAGS.SWP_NOSIZE : flags);
    }

    // ---- Renaming the folder ----

    private bool CanRename => Folder is not null && Renamer is not null;

    private void OnHeadingEnter(object sender, MouseEventArgs e) => FadeGlyph(CanRename ? 0.8 : 0);

    private void OnHeadingLeave(object sender, MouseEventArgs e) => FadeGlyph(0);

    private void FadeGlyph(double opacity) =>
        EditGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(_renaming ? 0 : opacity, new Duration(TimeSpan.FromMilliseconds(120))));

    private void OnHeadingClick(object sender, MouseButtonEventArgs e)
    {
        if (_renaming || !CanRename)
        {
            return;
        }

        _renaming = true;
        HeadingEditor.Text = Folder!.Name;
        HeadingEditor.ToolTip = null;
        Heading.Visibility = Visibility.Hidden;
        EditGlyph.BeginAnimation(OpacityProperty, null);
        EditGlyph.Opacity = 0;
        HeadingEditor.Visibility = Visibility.Visible;
        HeadingEditor.Focus();
        HeadingEditor.SelectAll();
        e.Handled = true;
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename(keepOpenOnError: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndRename();
            e.Handled = true;
        }
    }

    private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_renaming)
        {
            CommitRename(keepOpenOnError: false);
        }
    }

    private void CommitRename(bool keepOpenOnError)
    {
        var name = HeadingEditor.Text.Trim();
        if (Folder is not { } folder || name == folder.Name || Renamer is null)
        {
            EndRename();
            return;
        }

        if (Renamer(folder.Name, name) is { } problem)
        {
            if (keepOpenOnError)
            {
                HeadingEditor.ToolTip = new ToolTip { Content = problem, IsOpen = true, StaysOpen = false };
                HeadingEditor.SelectAll();
            }
            else
            {
                EndRename();
            }

            return;
        }

        // The folder is renamed on disk; follow it, so the rescan that comes with it still finds this panel's folder.
        Folder = folder with { Name = name, FullPath = Path.Combine(Path.GetDirectoryName(folder.FullPath) ?? "", name) };
        Heading.Text = name;
        EndRename();
    }

    // ---- Right-click ----

    // A file gets the menu Explorer would show for it; a folder of the list gets DeskGrid's own.
    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dragging || _renaming || ((FrameworkElement)sender).DataContext is not PanelItem item)
        {
            return;
        }

        if (item.Item is { } file)
        {
            ShowShellMenu(file.FullPath);
        }
        else if (item.Folder is { } folder)
        {
            ShowFolderMenu(folder);
        }
    }

    // The name bar: DeskGrid's menu for the open folder. While the name is edited the box has its own menu.
    private void OnHeadingRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!_renaming)
        {
            e.Handled = true;
            ShowFolderMenu(Folder);
        }
    }

    private void ShowShellMenu(string path)
    {
        if (ShellContextMenu.Load(path) is not { } menu)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        _menuOpen = true;
        try
        {
            menu.Show(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), new ScreenPoint(cursor.X, cursor.Y));
        }
        finally
        {
            menu.Dispose();
            _menuOpen = false;
        }

        // A command that opened something (the file, a dialog, the properties) has taken the focus: this panel is done.
        if (!IsActive)
        {
            Dismiss();
        }
    }

    // "New folder", and "Delete folder" where a folder is meant. The panel closes first: both ask something.
    private void ShowFolderMenu(WidgetFolder? folder)
    {
        var create = new MenuItem { Header = Strings.TrayNewFolder };
        create.Click += (_, _) =>
        {
            Dismiss();
            NewFolderRequested?.Invoke(this, EventArgs.Empty);
        };

        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint, Items = { create } };
        if (folder is not null)
        {
            var delete = new MenuItem { Header = Strings.WidgetDelete };
            delete.Click += (_, _) =>
            {
                Dismiss();
                DeleteRequested?.Invoke(this, folder);
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(delete);
        }

        menu.IsOpen = true;
    }

    // ---- Going back to the list of all folders ----

    private void OnBackEnter(object sender, MouseEventArgs e) => BackButton.Background = (Brush)Resources["HoverBrush"];

    private void OnBackLeave(object sender, MouseEventArgs e) => BackButton.Background = Brushes.Transparent;

    private void OnBackClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_list is { } list)
        {
            ShowList(list);
        }
    }

    private void EndRename()
    {
        _renaming = false;
        HeadingEditor.Visibility = Visibility.Collapsed;
        Heading.Visibility = Visibility.Visible;
        Keyboard.ClearFocus();
        Focus();
    }

    // ---- Dragging items ----

    private void OnItemPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = ((FrameworkElement)sender).DataContext as PanelItem;
        _pressPoint = e.GetPosition(this);
    }

    private void OnItemRelease(object sender, MouseButtonEventArgs e) => _pressed = null;

    private void OnItemMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed is not { } item)
        {
            return;
        }

        var offset = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(offset.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(offset.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            _pressed = null;
            DragItem(item, (FrameworkElement)sender);
        }
    }

    // Drops on this panel reorder; anywhere else the target (desktop, another widget, a folder) takes the file.
    private void DragItem(PanelItem item, FrameworkElement source)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        // A file of the open folder, or (in the list of all folders) a folder, which can only be rearranged.
        var name = item.Item?.Name ?? item.Folder?.Name;
        var owner = Folder?.Name ?? ItemReorderedEventArgs.FolderList;
        if (name is null)
        {
            return;
        }

        _dragIndex = index;
        _slot = index;
        _dragging = true;
        _reordered = false;
        _landing = null;
        _pendingReorder = null;
        _settlingItem = null;

        // The icon lifts off its slot (which stays, empty) and follows the cursor.
        var origin = SlotCenter(index);
        PInvoke.GetCursorPos(out var cursor);
        _grab = (origin.X - cursor.X, origin.Y - cursor.Y);
        _ghost = new DragGhost(item.Icon, origin, item.IsFolder ? (Brush)Resources["TileBrush"] : null);
        _ghost.Show();
        _ghost.Lift();
        _away.Reset();
        _watch.Start();
        if (ContainerAt(index) is { } container)
        {
            container.Opacity = 0;
        }

        // No file drop for a folder: nothing outside the list may move or copy the widget's folder.
        var data = new DataObject();
        if (item.Item is { } file)
        {
            data.SetData(DataFormats.FileDrop, new[] { file.FullPath });
        }

        data.SetData(PanelDragData.ItemFormat, name);
        data.SetData(PanelDragData.FolderFormat, owner);

        DragDrop.AddQueryContinueDragHandler(source, OnQueryContinueDrag);
        DragDropEffects effect;
        try
        {
            effect = DragDrop.DoDragDrop(source, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            DragDrop.RemoveQueryContinueDragHandler(source, OnQueryContinueDrag);
            _dragging = false;
        }

        FinishDrag(effect, name);
    }

    // Called on every mouse move of the drag, wherever the cursor is.
    private void OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        PInvoke.GetCursorPos(out var cursor);
        _ghost?.Follow(new ScreenPoint((int)Math.Round(cursor.X + _grab.X), (int)Math.Round(cursor.Y + _grab.Y)));
    }

    // The dragged item left the panel: the folder closes. The drag goes on inside this window, so it only disappears.
    private void TakeOut()
    {
        _slot = -1;
        HideForDrag();
    }

    private void FinishDrag(DragDropEffects effect, string itemName)
    {
        var ghost = _ghost;
        var index = _dragIndex;
        var landing = _landing;
        var reorder = _pendingReorder;
        _ghost = null;
        _dragIndex = -1;
        _slot = -1;
        _pendingReorder = null;

        if ((effect != DragDropEffects.None || _hiddenForDrag) && !_reordered)
        {
            // Taken elsewhere (or let go outside the closed folder): the icon shrinks into whatever it was dropped on.
            ghost?.Land(default, vanish: true);
        }
        else if (ghost is not null)
        {
            // Settled in this panel, or let go over nothing: the icon flies to its slot, which stays empty (even if
            // the items are rebuilt in the meantime) until it is there.
            _settlingItem = itemName;
            ghost.Land(landing ?? SlotCenter(index), vanish: false, () =>
            {
                _settlingItem = null;
                RestoreItems();
            });
        }

        if (reorder is not null)
        {
            ItemReordered?.Invoke(this, reorder);
        }

        DragFinished?.Invoke(this, EventArgs.Empty);

        // Gone to another place (or another app has the focus now): this panel has done its job.
        if (_hiddenForDrag || (!_reordered && (effect != DragDropEffects.None || !IsActive)))
        {
            Dismiss();
        }
        else if (_deferred is { } widgets)
        {
            _deferred = null;
            if (!Refresh(widgets))
            {
                Dismiss();
            }
        }
    }

    // ---- Sliding the items ----

    private Point SlotOrigin(int slot) => new(slot % _columns * ItemWidth, slot / _columns * ItemHeight);

    private ScreenPoint SlotCenter(int slot)
    {
        var origin = SlotOrigin(slot);
        var point = Items.PointToScreen(new Point(origin.X + ItemWidth / 2, origin.Y + IconCenterY));
        return new ScreenPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private FrameworkElement? ContainerAt(int index) =>
        index >= 0 ? Items.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement : null;

    // The slot under a point of the item list, or null if it is outside the list.
    private int? SlotAt(Point point, int last)
    {
        if (last < 0 || point.X < 0 || point.Y < 0 || point.X > Items.ActualWidth || point.Y > Items.ActualHeight + ItemHeight)
        {
            return last < 0 ? 0 : null;
        }

        var column = Math.Clamp((int)(point.X / ItemWidth), 0, _columns - 1);
        return Math.Clamp((int)(point.Y / ItemHeight) * _columns + column, 0, last);
    }

    /// <summary>
    /// Moves every item to where it belongs: with the dragged item taken out and, if <paramref name="landAt"/>
    /// is set, put back at that slot; or with a gap at <paramref name="gapAt"/> for something coming in.
    /// </summary>
    private void Slide(int? landAt = null, int? gapAt = null)
    {
        for (var index = 0; index < _items.Count; index++)
        {
            if (ContainerAt(index) is not { } container)
            {
                continue;
            }

            var slot = index;
            if (_dragIndex >= 0)
            {
                if (index == _dragIndex)
                {
                    slot = landAt ?? index;
                }
                else
                {
                    // Position among the others once the dragged one is out of the line.
                    var rank = index > _dragIndex ? index - 1 : index;
                    slot = landAt is { } land && rank >= land ? rank + 1 : rank;
                }
            }
            else if (gapAt is { } gap && index >= gap)
            {
                slot = index + 1;
            }

            var move = SlotOrigin(slot) - SlotOrigin(index);
            SlideTo(container, move.X, move.Y);
        }
    }

    private void RestoreItems()
    {
        for (var index = 0; index < _items.Count; index++)
        {
            if (ContainerAt(index) is { } container)
            {
                container.Opacity = 1;
                SlideTo(container, 0, 0);
            }
        }
    }

    private static void SlideTo(FrameworkElement element, double x, double y)
    {
        if (element.RenderTransform is not TranslateTransform { IsFrozen: false } transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, SlideDuration) { EasingFunction = SlideEasing });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, SlideDuration) { EasingFunction = SlideEasing });
    }

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        switch (((FrameworkElement)sender).DataContext)
        {
            case PanelItem { Folder: { } folder }:
                // Opens in place, the way its widget's panel would.
                ShowFolder(folder);
                break;

            case PanelItem { Item: { } item }:
                ItemInvoked?.Invoke(this, item);
                break;
        }
    }
}
