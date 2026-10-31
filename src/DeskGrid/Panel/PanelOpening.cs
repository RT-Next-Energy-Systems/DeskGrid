using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DeskGrid.Desktop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Panel;

/// <summary>
/// A folder growing out of its widget into its panel, like a phone's: a click-through window over both, in which the
/// panel's background grows from the widget's tile to the panel's bounds while its content zooms along and fades in.
/// The panel stays hidden meanwhile; once <see cref="Grown"/> it is shown under this window, which fades out.
/// </summary>
internal sealed class PanelOpening : Window
{
    private static readonly Duration GrowDuration = new(TimeSpan.FromMilliseconds(320));
    private static readonly Duration ContentFadeDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(120));
    private static readonly IEasingFunction Easing = (IEasingFunction)new QuadraticEase { EasingMode = EasingMode.EaseOut }.GetAsFrozen();
    private static readonly HWND TopmostBand = new(-1);

    private readonly ScreenRect _bounds;

    /// <param name="from">The widget's tile, in physical pixels.</param>
    /// <param name="to">The panel's window, in physical pixels.</param>
    /// <param name="dpi">The DPI of the monitor both are on.</param>
    /// <param name="frame">The panel's frame (sized like its window); its child is drawn live, so icons still loading show up.</param>
    /// <param name="background">The panel's background, or what stands in for it (acrylic can't be drawn here).</param>
    public PanelOpening(ScreenRect from, ScreenRect to, uint dpi, Border frame, Brush background, Brush outline, CornerRadius corners)
    {
        _bounds = new ScreenRect(Math.Min(from.Left, to.Left), Math.Min(from.Top, to.Top), Math.Max(from.Right, to.Right), Math.Max(from.Bottom, to.Bottom));

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;

        double Dip(int pixels) => pixels * 96.0 / dpi;
        Left = Dip(_bounds.Left);
        Top = Dip(_bounds.Top);
        Width = Dip(_bounds.Right - _bounds.Left);
        Height = Dip(_bounds.Bottom - _bounds.Top);
        var start = new Rect(Dip(from.Left - _bounds.Left), Dip(from.Top - _bounds.Top), Dip(from.Right - from.Left), Dip(from.Bottom - from.Top));
        var end = new Rect(Dip(to.Left - _bounds.Left), Dip(to.Top - _bounds.Top), Dip(to.Right - to.Left), Dip(to.Bottom - to.Top));

        // The content, laid out as in the panel, scaled as a whole to fit the growing background.
        var child = (FrameworkElement)frame.Child;
        var offset = child.TranslatePoint(new Point(), frame);
        var content = new Canvas { Width = frame.ActualWidth, Height = frame.ActualHeight };
        var box = new Rect(0, 0, child.ActualWidth, child.ActualHeight);
        var copy = new Rectangle
        {
            Width = box.Width,
            Height = box.Height,
            Fill = new VisualBrush(child) { Viewbox = box, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill },
        };
        Canvas.SetLeft(copy, offset.X);
        Canvas.SetTop(copy, offset.Y);
        content.Children.Add(copy);
        var zoom = new Viewbox { Stretch = Stretch.Uniform, Child = content, Opacity = 0 };

        var folder = new Grid { Width = start.Width, Height = start.Height };
        folder.Children.Add(new Border { Background = background, BorderBrush = outline, BorderThickness = new Thickness(1), CornerRadius = corners });
        folder.Children.Add(zoom);
        Canvas.SetLeft(folder, start.X);
        Canvas.SetTop(folder, start.Y);
        Content = new Canvas { Children = { folder } };

        SourceInitialized += (_, _) =>
        {
            var window = new HWND(new WindowInteropHelper(this).Handle);
            var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            PInvoke.SetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
                (int)(style | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
            PInvoke.SetWindowPos(window, TopmostBand, _bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        };

        ContentRendered += (_, _) =>
        {
            var grow = new DoubleAnimation(end.Width, GrowDuration) { EasingFunction = Easing };
            grow.Completed += (_, _) => Grown?.Invoke(this, EventArgs.Empty);
            folder.BeginAnimation(WidthProperty, grow);
            folder.BeginAnimation(HeightProperty, new DoubleAnimation(end.Height, GrowDuration) { EasingFunction = Easing });
            folder.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(end.X, GrowDuration) { EasingFunction = Easing });
            folder.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(end.Y, GrowDuration) { EasingFunction = Easing });
            zoom.BeginAnimation(OpacityProperty, new DoubleAnimation(1, ContentFadeDuration) { EasingFunction = Easing });
        };
    }

    /// <summary>The folder has grown to the panel's size: show the panel, then <see cref="FadeOut"/>.</summary>
    public event EventHandler? Grown;

    /// <summary>Fades away over the panel (which has just been shown, maybe above this window) and closes.</summary>
    public void FadeOut()
    {
        // Back above the panel, which comes to the front when it is activated.
        PInvoke.SetWindowPos(new HWND(new WindowInteropHelper(this).Handle), TopmostBand, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        var fade = new DoubleAnimation(0, FadeOutDuration);
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
