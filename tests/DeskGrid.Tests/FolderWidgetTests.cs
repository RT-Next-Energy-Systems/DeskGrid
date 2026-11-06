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
}
