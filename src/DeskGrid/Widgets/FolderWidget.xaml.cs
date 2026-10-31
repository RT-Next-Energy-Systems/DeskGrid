using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Resources;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Widgets;

/// <summary>
/// A folder on the desktop: the 2×2 preview on a translucent tile, with the folder name below.
/// Turns mouse input into a click or a drag and highlights as a drop target; moving the window and
/// handling drops are up to the owner.
/// </summary>
internal partial class FolderWidget : UserControl
{
    /// <summary>The widget's footprint, which is also the grid cell it snaps to (DIP).</summary>
    public const double CellWidth = 76;
    public const double CellHeight = 96;

    private const int MaxLabelLines = 2;
    private static readonly Brush HoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));

    // Invisible, but it makes the whole cell take clicks and drops: fully transparent pixels of the layered window
    // let them through to the desktop, which would put a dropped icon on the widget's cell.
    private static readonly Brush IdleBrush = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    private static readonly Effect HaloEffect = Frozen(new DropShadowEffect { ShadowDepth = 0, BlurRadius = 5, Opacity = 1, Color = Colors.Black });

    private Task<BitmapSource>? _preview;
    private ScreenPoint _pressPoint;
    private bool _dragging;

    public FolderWidget()
    {
        InitializeComponent();
        Width = CellWidth;
        Height = CellHeight;
        Label.MaxHeight = Label.FontSize * Label.FontFamily.LineSpacing * MaxLabelLines;
        Highlight.Background = IdleBrush;

        // The owner decides what a drop means; the widget only shows that it is a target.
        AllowDrop = true;

        var pin = new MenuItem { Header = Strings.WidgetPinToTaskbar };
        pin.Click += (_, _) => PinRequested?.Invoke(this, EventArgs.Empty);
        var create = new MenuItem { Header = Strings.TrayNewFolder };
        create.Click += (_, _) => NewFolderRequested?.Invoke(this, EventArgs.Empty);
        var delete = new MenuItem { Header = Strings.WidgetDelete };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        ContextMenu = new ContextMenu { Items = { pin, create, new Separator(), delete } };
    }

    public event EventHandler? Clicked;

    public event EventHandler? DragStarted;

    /// <summary>Cursor offset from where the drag began, in physical pixels.</summary>
    public event EventHandler<ScreenPoint>? Dragged;

    public event EventHandler? DragCompleted;

    public event EventHandler? PinRequested;

    public event EventHandler? NewFolderRequested;

    public event EventHandler? DeleteRequested;

    /// <summary>Whether the name has a dark halo for legibility over a light wallpaper.</summary>
    public bool HasHalo
    {
        get => Halo.Effect is not null;
        set => Halo.Effect = value ? HaloEffect : null;
    }

    /// <summary>Where the name is on screen (physical pixels), or <see langword="null"/> while the widget isn't shown.</summary>
    public ScreenRect? LabelBounds() => ScreenBounds(Halo);

    /// <summary>
    /// Whether the cursor is on the widget. Drag events also come up from its parts while the cursor moves from one
    /// to another; only this tells that it has left.
    /// </summary>
    public bool IsCursorOver() => ScreenBounds(this) is { } bounds && bounds.Contains(ScreenPoint.AtCursor());

    /// <summary>Sets the cell the widget fills (DIP); the content stays centered in it.</summary>
    public void SetCellSize(double width, double height) => (Width, Height) = (width, height);

    public void Show(string name, Task<BitmapSource> preview)
    {
        Label.Text = name;
        if (preview != _preview)
        {
            _preview = preview;
            _ = LoadPreviewAsync(preview);
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) => Highlight.Background = HoverBrush;

    protected override void OnMouseLeave(MouseEventArgs e) => Highlight.Background = IdleBrush;

    protected override void OnDragEnter(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            Highlight.Background = HoverBrush;
        }

        base.OnDragEnter(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        if (!IsCursorOver())
        {
            Highlight.Background = IdleBrush;
        }

        base.OnDragLeave(e);
    }

    protected override void OnDrop(DragEventArgs e)
    {
        Highlight.Background = IdleBrush;
        base.OnDrop(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _pressPoint = ScreenPoint.AtCursor();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        var cursor = ScreenPoint.AtCursor();
        var offset = new ScreenPoint(cursor.X - _pressPoint.X, cursor.Y - _pressPoint.Y);
        if (!_dragging && IsBeyondDragThreshold(offset))
        {
            _dragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        }

        if (_dragging)
        {
            Dragged?.Invoke(this, offset);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        var wasDragging = _dragging;
        _dragging = false;
        ReleaseMouseCapture();
        (wasDragging ? DragCompleted : Clicked)?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Capture taken away mid-drag (e.g. a window popped up): finish where we are.
        if (_dragging)
        {
            _dragging = false;
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsBeyondDragThreshold(ScreenPoint offset) =>
        Math.Abs(offset.X) >= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG)
        || Math.Abs(offset.Y) >= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG);

    // Where an element is on screen (physical pixels), or null while it isn't shown.
    private static ScreenRect? ScreenBounds(FrameworkElement element)
    {
        if (PresentationSource.FromVisual(element) is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }

        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new ScreenRect((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private async Task LoadPreviewAsync(Task<BitmapSource> preview)
    {
        try
        {
            var image = await preview;
            if (preview == _preview)
            {
                Preview.Source = image;
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Preview of \"{Label.Text}\" failed", exception);
        }
    }
}
