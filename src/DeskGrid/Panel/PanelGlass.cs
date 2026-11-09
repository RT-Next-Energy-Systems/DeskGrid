using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Glass;

namespace DeskGrid.Panel;

/// <summary>
/// Paints a panel's background as liquid glass. The screen behind the panel is read once, by the first
/// <see cref="Update"/> (before the panel is shown, or the panel would be in the picture); the glass is drawn again,
/// from that picture, whenever the panel's size or place changes.
/// </summary>
internal sealed class PanelGlass(PanelTheme theme, Action<Brush> apply, Action<bool> brightnessKnown) : IDisposable
{
    // Above this average brightness of the finished glass, dark text reads better than white.
    private const double BrightGlass = 0.5;

    /// <summary>The panel's corner radius in device-independent units, which the glass is shaped to.</summary>
    public const double CornerRadius = 20;

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(40);

    private DispatcherTimer? _timer;
    private ScreenImage? _screen;
    private bool _captured;
    private bool _drawn;
    private (ScreenRect Window, ScreenRect Area, double Scale)? _target;

    /// <summary>
    /// The panel now covers <paramref name="window"/> (screen pixels) on a monitor whose work area is
    /// <paramref name="area"/>. The first call draws at once; later ones, which come in a stream while the panel
    /// grows, wait until it has settled.
    /// </summary>
    public void Update(ScreenRect window, ScreenRect area, double scale)
    {
        _target = (window, area, scale);
        if (!_drawn)
        {
            Draw();
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Debounce };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Draw();
            };
        }

        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _screen = null;
    }

    private void Draw()
    {
        if (_target is not var (window, area, scale))
        {
            return;
        }

        if (!_captured)
        {
            _captured = true;
            _screen = ScreenSampler.Capture(area) is { } bgra ? new ScreenImage(area, bgra) : null;
            if (_screen is null)
            {
                // Nothing to look through: a frosted panel that is still readable.
                Log.Info("The screen could not be read for the panel's glass; drawing it plain.");
                apply(new SolidColorBrush(Color.FromArgb(0xD9, theme.Background.R, theme.Background.G, theme.Background.B)));
            }
        }

        // Another monitor than the one that was read (the panel was moved): keep what is drawn rather than look at itself.
        if (_screen is null || _screen.Area != area || window.Width <= 0 || window.Height <= 0)
        {
            return;
        }

        var surface = (theme.Background.R, theme.Background.G, theme.Background.B);
        var pixels = LiquidGlass.Render(_screen, window, new GlassStyle(scale, CornerRadius, theme.Background.A / 255.0, surface));
        var bitmap = BitmapSource.Create(window.Width, window.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, window.Width * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        apply(brush);

        // Told once, from the first drawing: the text color must not flip when a later drawing is a little different.
        if (!_drawn)
        {
            brightnessKnown(Brightness(pixels) > BrightGlass);
        }

        _drawn = true;
    }

    // Average brightness (0 to 1) of what the glass shows, from premultiplied BGRA.
    internal static double Brightness(byte[] premultipliedBgra)
    {
        double light = 0, covered = 0;
        for (var i = 0; i + 3 < premultipliedBgra.Length; i += 4)
        {
            light += 0.0722 * premultipliedBgra[i] + 0.7152 * premultipliedBgra[i + 1] + 0.2126 * premultipliedBgra[i + 2];
            covered += premultipliedBgra[i + 3];
        }

        return covered == 0 ? 0 : light / covered; // A premultiplied byte is brightness (0-1) times alpha (0-255).
    }
}
