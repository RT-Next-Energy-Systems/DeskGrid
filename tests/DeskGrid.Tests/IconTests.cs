using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskGrid.Folders;
using DeskGrid.Icons;
using DeskGrid.Shell;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

public sealed class PreviewComposerTests : IDisposable
{
    private readonly DispatcherThread _sta = new("Test STA");

    public void Dispose() => _sta.Dispose();

    [Fact]
    public async Task Compose_FillsCellsRowByRow_AndLeavesGapsTransparent()
    {
        Color[] colors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow];

        var preview = await _sta.InvokeAsync(() => PreviewComposer.Compose([.. colors.Select(SolidIcon)]));

        Assert.Equal(PreviewComposer.Size, preview.PixelWidth);
        for (var slot = 0; slot < colors.Length; slot++)
        {
            Assert.Equal(colors[slot], PixelAt(preview, CellCenter(slot)));
        }

        Assert.Equal(0, PixelAt(preview, (PreviewComposer.Size / 2, PreviewComposer.Size / 2)).A);
    }

    [Fact]
    public async Task Compose_MissingIcons_LeaveTheirCellsEmpty()
    {
        var preview = await _sta.InvokeAsync(() => PreviewComposer.Compose([SolidIcon(Colors.Red), null]));

        Assert.Equal(Colors.Red, PixelAt(preview, CellCenter(0)));
        Assert.All([1, 2, 3], slot => Assert.Equal(0, PixelAt(preview, CellCenter(slot)).A));
    }

    internal static BitmapSource SolidIcon(Color color)
    {
        const int size = IconService.IconSize;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (color.B, color.G, color.R, color.A);
        }

        var icon = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        icon.Freeze();
        return icon;
    }

    internal static Color PixelAt(BitmapSource image, (int X, int Y) point)
    {
        var pixel = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)
            .CopyPixels(new System.Windows.Int32Rect(point.X, point.Y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static (int X, int Y) CellCenter(int slot)
    {
        var cell = PreviewComposer.CellBounds(slot);
        return ((int)(cell.X + cell.Width / 2), (int)(cell.Y + cell.Height / 2));
    }
}

public sealed class ShellImageTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DispatcherThread _sta = new("Test STA");

    public void Dispose()
    {
        _sta.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task GetIcon_ReturnsSquareIconWithAlpha()
    {
        var path = _temp.CreateFile("notes.txt");

        var icon = await _sta.InvokeAsync(() => ShellImage.GetIcon(path, IconService.IconSize));

        Assert.NotNull(icon);
        Assert.Equal((IconService.IconSize, IconService.IconSize), (icon.PixelWidth, icon.PixelHeight));
        Assert.True(icon.IsFrozen);
        Assert.Equal(0, PreviewComposerTests.PixelAt(icon, (0, 0)).A);
        Assert.Equal(255, PreviewComposerTests.PixelAt(icon, (IconService.IconSize / 2, IconService.IconSize / 2)).A);
    }

    [Fact]
    public async Task GetIcon_MissingFile_ReturnsNull() =>
        Assert.Null(await _sta.InvokeAsync(() => ShellImage.GetIcon(_temp.Combine("missing.txt"), IconService.IconSize)));
}

[Collection(nameof(ShellCollection))]
public sealed class IconServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly IconService _icons = new();

    public void Dispose()
    {
        _icons.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task GetIconAsync_IsCachedUntilTheFileChanges()
    {
        var item = Item("a.txt");

        var first = _icons.GetIconAsync(item);

        Assert.Same(first, _icons.GetIconAsync(item with { }));
        Assert.NotSame(first, _icons.GetIconAsync(item with { LastWriteTimeUtc = item.LastWriteTimeUtc.AddTicks(1) }));
        Assert.NotNull(await first);
    }

    [Fact]
    public void GetIconAsync_ShortcutIsRedrawnWhenItsTargetChanges()
    {
        var item = Item("a.txt");
        var icon = _icons.GetIconAsync(item);
        var changed = item with { TargetWriteTimeUtc = DateTime.UtcNow };

        Assert.NotSame(icon, _icons.GetIconAsync(changed));
        Assert.NotSame(
            _icons.GetPreviewAsync(new WidgetFolder("W", _temp.Path, [item])),
            _icons.GetPreviewAsync(new WidgetFolder("W", _temp.Path, [changed])));
    }

    [Fact]
    public async Task GetPreviewAsync_IsRebuiltOnlyWhenAShownItemChanges()
    {
        var items = new[] { "1.txt", "2.txt", "3.txt", "4.txt", "5.txt" }.Select(Item).ToList();
        var widget = new WidgetFolder("Games", _temp.Path, items);

        var preview = _icons.GetPreviewAsync(widget);
        var fifthChanged = widget with { Items = [.. items[..4], items[4] with { LastWriteTimeUtc = DateTime.UtcNow }] };
        var firstChanged = widget with { Items = [items[0] with { LastWriteTimeUtc = DateTime.UtcNow }, .. items[1..]] };

        Assert.Same(preview, _icons.GetPreviewAsync(fifthChanged));
        Assert.NotSame(preview, _icons.GetPreviewAsync(firstChanged));
        Assert.Equal(PreviewComposer.Size, (await preview).PixelWidth);
    }

    [Fact]
    public void Retain_DropsIconsOfItemsThatAreGone()
    {
        var item = Item("a.txt");
        var first = _icons.GetIconAsync(item);

        _icons.Retain([]);

        Assert.NotSame(first, _icons.GetIconAsync(item));
    }

    private FolderItem Item(string name)
    {
        var path = _temp.CreateFile(name);
        return new FolderItem(name, path, File.GetLastWriteTimeUtc(path));
    }
}
