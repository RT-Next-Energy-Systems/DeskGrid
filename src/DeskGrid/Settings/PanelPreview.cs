using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Glass;
using DeskGrid.Panel;
using DeskGrid.Resources;
using Shapes = System.Windows.Shapes;

namespace DeskGrid.Settings;

/// <summary>
/// A small sample of a folder's panel on a sample wallpaper, drawn from the settings being edited, so a look can be
/// judged before it is applied. The panel opens on the real desktop with the same colors; liquid glass is drawn by the
/// same code, from the sample wallpaper instead of the screen.
/// </summary>
internal sealed class PanelPreview : Border
{
    private const double StageWidth = 380;
    private const double StageHeight = 200;
    private const double PanelWidth = 250;
    private const double PanelHeight = 156;
    private const int SampleRows = 2;

    private static readonly Color[] TileColors =
    [
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x64, 0x74, 0x8B),
    ];

    private readonly Border _panel = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12) };
    private readonly TextBlock _heading = new()
    {
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private readonly Grid _items = new();
    private (double Scale, BitmapSource Picture, byte[] Bgra)? _wallpaper;

    public PanelPreview()
    {
        Height = StageHeight;
        Width = StageWidth;
        HorizontalAlignment = HorizontalAlignment.Left;
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        BorderThickness = new Thickness(1);

        var content = new StackPanel();
        content.Children.Add(_heading);
        content.Children.Add(_items);
        _panel.Child = content;
        Child = _panel;
        Localize();
    }

    /// <summary>Sets the heading again, in the interface language now in force.</summary>
    public void Localize() => _heading.Text = Strings.SettingsPreviewHeading;

    /// <summary>Draws the sample for <paramref name="background"/>, with <paramref name="columns"/> items across.</summary>
    public void Update(PanelBackground background, int columns)
    {
        var theme = PanelTheme.Resolve(background, PanelTheme.SystemUsesDarkTheme, acrylicSupported: true);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var wallpaper = Wallpaper(scale);
        Background = new ImageBrush(wallpaper.Picture) { Stretch = Stretch.Fill };

        var pixelWidth = (int)Math.Round(PanelWidth * scale);
        var pixelHeight = (int)Math.Round(PanelHeight * scale);
        _panel.Width = pixelWidth / scale;
        _panel.Height = pixelHeight / scale;

        if (theme.Glass)
        {
            var stage = new ScreenRect(0, 0, wallpaper.Picture.PixelWidth, wallpaper.Picture.PixelHeight);
            var left = (stage.Width - pixelWidth) / 2;
            var top = (stage.Height - pixelHeight) / 2;
            var window = new ScreenRect(left, top, left + pixelWidth, top + pixelHeight);
            var surface = (theme.Background.R, theme.Background.G, theme.Background.B);
            var pixels = LiquidGlass.Render(new ScreenImage(stage, wallpaper.Bgra), window,
                new GlassStyle(scale, PanelGlass.CornerRadius, theme.Background.A / 255.0, surface, theme.GlassShade));
            var bitmap = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, pixelWidth * 4);
            bitmap.Freeze();
            _panel.Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            _panel.CornerRadius = new CornerRadius(PanelGlass.CornerRadius);
            theme = theme.WithTextFor(PanelGlass.Brightness(pixels) > PanelGlass.BrightGlass);
        }
        else
        {
            // The system acrylic can't be drawn here; its surface color a little translucent stands for it.
            var color = theme.UseAcrylic ? Color.FromArgb(0xD9, theme.Background.R, theme.Background.G, theme.Background.B) : theme.Background;
            _panel.Background = new SolidColorBrush(color);
            _panel.CornerRadius = new CornerRadius(8);
        }

        _heading.Foreground = new SolidColorBrush(theme.Foreground);
        FillItems(columns, theme);
    }

    private void FillItems(int columns, PanelTheme theme)
    {
        columns = Math.Max(columns, 1);
        _items.Children.Clear();
        _items.ColumnDefinitions.Clear();
        _items.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
        {
            _items.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var row = 0; row < SampleRows; row++)
        {
            _items.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var tile = Math.Min(40, (PanelWidth - 24) / columns * 0.66);
        var label = new SolidColorBrush(theme.Foreground) { Opacity = 0.5 };
        for (var index = 0; index < columns * SampleRows; index++)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
            cell.Children.Add(new Border
            {
                Width = tile,
                Height = tile,
                CornerRadius = new CornerRadius(tile * 0.22),
                Background = new SolidColorBrush(TileColors[index % TileColors.Length]),
            });
            cell.Children.Add(new Shapes.Rectangle { Width = tile * 0.8, Height = 4, RadiusX = 2, RadiusY = 2, Fill = label, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(cell, index % columns);
            Grid.SetRow(cell, index / columns);
            _items.Children.Add(cell);
        }
    }

    // A colorful picture with a bright and a dark patch, so frosting, bending and the text color all have something to show.
    private (double Scale, BitmapSource Picture, byte[] Bgra) Wallpaper(double scale)
    {
        if (_wallpaper is { } cached && cached.Scale == scale)
        {
            return cached;
        }

        var width = (int)Math.Round(StageWidth * scale);
        var height = (int)Math.Round(StageHeight * scale);
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double u = (double)x / width, v = (double)y / height;
                var t = Math.Clamp(u * 0.65 + v * 0.35, 0, 1);
                var (r, g, b) = t < 0.5
                    ? Mix((30, 70, 170), (150, 70, 170), t * 2)
                    : Mix((150, 70, 170), (245, 150, 80), (t - 0.5) * 2);
                (r, g, b) = Blend((r, g, b), (255, 240, 200), Patch(u, v, 0.22, 0.75, 0.2));
                (r, g, b) = Blend((r, g, b), (10, 20, 50), Patch(u, v, 0.82, 0.25, 0.2));
                var at = (y * width + x) * 4;
                bgra[at] = (byte)b;
                bgra[at + 1] = (byte)g;
                bgra[at + 2] = (byte)r;
                bgra[at + 3] = 255;
            }
        }

        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        picture.Freeze();
        _wallpaper = (scale, picture, bgra);
        return _wallpaper.Value;
    }

    // 1 at the middle of a soft round patch, falling to 0 at its edge.
    private static double Patch(double u, double v, double centerX, double centerY, double radius)
    {
        var distance = Math.Sqrt(Math.Pow((u - centerX) * StageWidth, 2) + Math.Pow((v - centerY) * StageHeight, 2)) / (radius * StageHeight);
        return Math.Clamp(1 - distance, 0, 1);
    }

    private static (double R, double G, double B) Mix((int R, int G, int B) from, (int R, int G, int B) to, double amount) =>
        (from.R + (to.R - from.R) * amount, from.G + (to.G - from.G) * amount, from.B + (to.B - from.B) * amount);

    private static (double R, double G, double B) Blend((double R, double G, double B) from, (int R, int G, int B) to, double amount) =>
        (from.R + (to.R - from.R) * amount, from.G + (to.G - from.G) * amount, from.B + (to.B - from.B) * amount);
}
