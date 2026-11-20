using System.Globalization;
using System.Windows.Controls;
using DeskGrid.Resources;
using DeskGrid.Threading;
using DeskGrid.Widgets;

namespace DeskGrid.Tests;

// Switches the interface language, which every test that reads a text shares: not alongside the others.
[Collection(nameof(ShellCollection))]
public sealed class FolderWidgetTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task Menu_SpeaksTheLanguageChosenAfterTheWidgetWasMade()
    {
        var before = Strings.Culture;
        try
        {
            var texts = await _ui.InvokeAsync(() =>
            {
                Strings.Culture = new CultureInfo("en");
                var widget = new FolderWidget();

                Strings.Culture = new CultureInfo("zh-TW");
                widget.LocalizeMenu(); // What opening the menu does.
                return widget.ContextMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header).ToList();
            });

            Strings.Culture = new CultureInfo("zh-TW");
            Assert.Equal([Strings.WidgetPinToTaskbar, Strings.TrayNewFolder, Strings.WidgetDelete], texts);
            Strings.Culture = new CultureInfo("en");
            Assert.DoesNotContain(Strings.WidgetDelete, texts);
        }
        finally
        {
            Strings.Culture = before;
        }
    }

    [Fact]
    public async Task Tile_FollowsTheDesktopIconPicture_AndFallsBackToTheStandardSize()
    {
        var (followed, placed, unknown, outOfRange) = await _ui.InvokeAsync(() =>
        {
            var widget = new FolderWidget();
            widget.SetTile(46, 5);
            var followed = widget.Tile.Width;
            var placed = widget.Highlight.Padding.Top;
            widget.SetTile(null, 5);
            var unknown = (widget.Tile.Width, widget.Highlight.Padding.Top);
            widget.SetTile(500, 5);
            return (followed, placed, unknown, widget.Tile.Width);
        });

        Assert.Equal(46, followed);
        Assert.Equal(5, placed);
        Assert.Equal((48d, 0d), unknown);
        Assert.Equal(48, outOfRange);
    }
}
