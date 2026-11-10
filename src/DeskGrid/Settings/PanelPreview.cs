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
    private const double CellWidth = 42;
    private const double CellHeight = 52;
    private const double TileSize = 26;
    private const double MinContentWidth = 72;
    private const double ChromeWidth = 16;
    private const double ChromeHeight = 38;
    private const int SampleRows = 2;
    private const double StageMargin = 14; // Room kept around the sample panel, in DIPs.
    private const double MaxZoom = 2;

    private double _zoom = 1; // How much larger than the real panel's proportions the sample is drawn.

    private static readonly Color[] TileColors =
    [
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x64, 0x74, 0x8B),
    ];

    private readonly Border _panel = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Thickness(8, 7, 8, 8),
    };

    private readonly TextBlock _heading = new()
    {
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private readonly Grid _items = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private (double Scale, BitmapSource Picture, ImageBrush Brush, byte[] Bgra)? _wallpaper;
    private GlassJob? _glass; // The newest glass asked for; null while another look is shown.

    private sealed record GlassJob(ScreenImage Stage, ScreenRect Window, GlassStyle Style, PanelTheme Theme, int Columns);

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

    /// <summary>The drawing of the glass that is under way (finished when there is none), for tests to wait on.</summary>
    internal Task Drawing { get; private set; } = Task.CompletedTask;

    /// <summary>Sets the heading again, in the interface language now in force.</summary>
    public void Localize() => _heading.Text = Strings.SettingsPreviewHeading;

    /// <summary>Draws the sample for <paramref name="background"/>, with <paramref name="columns"/> items across.</summary>
    public void Update(PanelBackground background, int columns)
    {
        columns = Math.Clamp(columns, AppConfig.MinPanelColumns, AppConfig.MaxPanelColumns);
        var theme = PanelTheme.Resolve(background, PanelTheme.SystemUsesDarkTheme, acrylicSupported: true);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var wallpaper = Wallpaper(scale);
        Background = wallpaper.Brush;

        // The sample is drawn as large as the stage allows (the same proportions as the real panel), so the folder is easy to read.
        var baseWidth = Math.Max(columns * CellWidth, MinContentWidth) + ChromeWidth;
        var baseHeight = SampleRows * CellHeight + ChromeHeight;
        _zoom = Math.Clamp(Math.Min((StageWidth - StageMargin) / baseWidth, (StageHeight - StageMargin) / baseHeight), 1, MaxZoom);
        var panelWidth = baseWidth * _zoom;
        var panelHeight = baseHeight * _zoom;
        var pixelWidth = (int)Math.Round(panelWidth * scale);
        var pixelHeight = (int)Math.Round(panelHeight * scale);
        _panel.Width = pixelWidth / scale;
        _panel.Height = pixelHeight / scale;
        _panel.Padding = new Thickness(8 * _zoom, 7 * _zoom, 8 * _zoom, 8 * _zoom);
        _heading.FontSize = 11 * _zoom;
        _heading.Margin = new Thickness(0, 0, 0, 6 * _zoom);
        var cornerRadius = PanelGlass.CornerRadius * _zoom;

        if (!theme.Glass)
        {
            _glass = null; // A glass still being drawn is for a look that is no longer wanted.

            // The system acrylic can't be drawn here; its surface color a little translucent stands for it.
            _panel.Background = new SolidColorBrush(theme.UseAcrylic ? Translucent(theme.Background) : theme.Background);
            _panel.CornerRadius = new CornerRadius(8 * _zoom);
            _panel.BorderThickness = new Thickness(1);
            _panel.BorderBrush = new SolidColorBrush(theme.Outline);
            ShowContent(columns, theme);
            return;
        }

        _panel.BorderThickness = new Thickness(0);
        _panel.BorderBrush = null;
        _panel.CornerRadius = new CornerRadius(cornerRadius);
        if (_glass is null)
        {
            // Until the glass is drawn: its surface color, so the sample is never empty.
            _panel.Background = new SolidColorBrush(Translucent(theme.Background));
            ShowContent(columns, theme);
        }
        else if (_glass.Columns != columns || _glass.Window.Width != pixelWidth)
        {
            ShowContent(columns, theme);
        }

        var stage = new ScreenRect(0, 0, wallpaper.Picture.PixelWidth, wallpaper.Picture.PixelHeight);
        var left = (stage.Width - pixelWidth) / 2;
        var top = (stage.Height - pixelHeight) / 2;
        _glass = new GlassJob(
            new ScreenImage(stage, wallpaper.Bgra),
            new ScreenRect(left, top, left + pixelWidth, top + pixelHeight),
            new GlassStyle(scale, cornerRadius, theme.Background.A / 255.0, (theme.Background.R, theme.Background.G, theme.Background.B), theme.GlassShade),
            theme,
            columns);
        if (Drawing.IsCompleted)
        {
            Drawing = DrawGlassAsync();
        }
    }

    // Drawing the glass takes tens of milliseconds, and a dragged slider asks for it at every step. Drawn on the UI
    // thread, every step held the window up and the steps piled up behind the mouse (reported as the computer hanging).
    // So it is drawn on another thread, one at a time, and of the requests that come meanwhile only the newest is drawn.
    private async Task DrawGlassAsync()
    {
        GlassJob? drawn = null;
        while (_glass is { } job && !ReferenceEquals(job, drawn))
        {
            var pixels = await Task.Run(() => LiquidGlass.Render(job.Stage, job.Window, job.Style));
            drawn = job;
            if (_glass is null)
            {
                return; // Another look was chosen while this was drawn.
            }

            var bitmap = BitmapSource.Create(job.Window.Width, job.Window.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, job.Window.Width * 4);
            bitmap.Freeze();
            _panel.Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            _panel.CornerRadius = new CornerRadius(job.Style.CornerRadius);
            ShowContent(job.Columns, job.Theme.WithTextFor(PanelGlass.Brightness(pixels) > PanelGlass.BrightGlass));
        }
    }

    private static Color Translucent(Color color) => Color.FromArgb(0xD9, color.R, color.G, color.B);

    private void ShowContent(int columns, PanelTheme theme)
    {
        _heading.Foreground = new SolidColorBrush(theme.Foreground);
        FillItems(columns, theme);
    }

    private void FillItems(int columns, PanelTheme theme)
    {
        columns = Math.Max(columns, 1);
        var (cellWidth, cellHeight, tile) = (CellWidth * _zoom, CellHeight * _zoom, TileSize * _zoom);
        _items.Width = columns * cellWidth;
        _items.Height = SampleRows * cellHeight;
        _items.Children.Clear();
        _items.ColumnDefinitions.Clear();
        _items.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
        {
            _items.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellWidth) });
        }

        for (var row = 0; row < SampleRows; row++)
        {
            _items.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellHeight) });
        }

        var label = new SolidColorBrush(theme.Foreground) { Opacity = 0.55 };
        for (var index = 0; index < columns * SampleRows; index++)
        {
            var cell = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            cell.Children.Add(new Border
            {
                Width = tile,
                Height = tile,
                CornerRadius = new CornerRadius(tile * 0.22),
                Background = new SolidColorBrush(TileColors[index % TileColors.Length]),
            });
            cell.Children.Add(new Shapes.Rectangle
            {
                Width = tile * 0.85,
                Height = 4 * _zoom,
                RadiusX = 2 * _zoom,
                RadiusY = 2 * _zoom,
                Fill = label,
                Margin = new Thickness(0, 5 * _zoom, 0, 0),
            });
            Grid.SetColumn(cell, index % columns);
            Grid.SetRow(cell, index / columns);
            _items.Children.Add(cell);
        }
    }

    // A colorful picture with a bright and a dark patch, so frosting, bending and the text color all have something to show.
    private (double Scale, BitmapSource Picture, ImageBrush Brush, byte[] Bgra) Wallpaper(double scale)
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
        var brush = new ImageBrush(picture) { Stretch = Stretch.Fill };
        brush.Freeze();
        _wallpaper = (scale, picture, brush, bgra);
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
