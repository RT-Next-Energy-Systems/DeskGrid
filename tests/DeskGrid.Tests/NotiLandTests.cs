using System.IO;
using System.Windows;
using System.Windows.Controls;
using DeskGrid.Config;
using DeskGrid.Settings;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

// NotiLand (BrimDeck) runs inside DeskGrid's process: its styles must reach its own windows and nothing else.
[Collection(nameof(ShellCollection))]
public sealed class NotiLandTests : IDisposable
{
    private readonly DispatcherThread _ui = TestApplication.Ui;

    public void Dispose()
    {
        // SharedUi lives until the test run ends.
    }

    [Fact]
    public async Task BrimDeckStyles_AreMergedIntoItsOwnWindows_AndNotIntoTheApplication()
    {
        var (merged, applicationHasButtonStyle, applicationHasBrimDeckKey, holds, doesNotHold) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new global::BrimDeck.DeckWindow();
            var own = new Window();
            return (
                window.Resources.MergedDictionaries.Contains(global::BrimDeck.DeckResources.Dictionary),
                Application.Current.Resources.Contains(typeof(Button)),
                Application.Current.Resources.Contains("PanelSurface") || Application.Current.Resources.Contains("FontIcon"),
                global::BrimDeck.DeckWindow.HoldsForTest(window),
                global::BrimDeck.DeckWindow.HoldsForTest(own));
        });

        Assert.True(merged);
        Assert.False(applicationHasButtonStyle);
        Assert.False(applicationHasBrimDeckKey);
        Assert.True(holds);
        Assert.False(doesNotHold);
    }

    [Fact]
    public async Task BrimDeck_CanBeStartedAndStoppedAgain_InsideTheHostProcess()
    {
        var stopped = new TaskCompletionSource();
        var windowsAfterStart = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var app = global::BrimDeck.App.StartHosted(true);
            Assert.NotNull(app);
            app.Stopped += () => stopped.TrySetResult();
            return Application.Current.Windows.OfType<global::BrimDeck.DeckWindow>().Count();
        });
        await _ui.InvokeAsync(() => global::BrimDeck.App.Instance!.ExitApplication());
        var done = await Task.WhenAny(stopped.Task, Task.Delay(8000, TestContext.Current.CancellationToken)) == stopped.Task;
        var (left, gone) = await _ui.InvokeAsync(() => (Application.Current.Windows.OfType<global::BrimDeck.DeckWindow>().Count(), global::BrimDeck.App.Instance is null));

        Assert.True(windowsAfterStart >= 1);
        Assert.True(done);
        Assert.Equal(0, left);
        Assert.True(gone);
    }

    // NotiLand's own settings (BrimDeck's pages) are entries of the list below "NotiLand" while it runs, shown on the right, and gone when it stops.
    [Fact]
    public async Task NotiLandPages_AreListedBelowNotiLand_WhileItRuns_AndGoWhenItStops()
    {
        var (whileRunning, afterStop) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var host = new DeskGrid.NotiLand.NotiLandHost();
            host.Apply(true);
            Assert.True(host.IsRunning);
            var window = new SettingsWindow(new AppConfig { RootPath = Path.GetTempPath(), NotiLandEnabled = true }) { NotiLand = host };
            window.Show();

            string[] Entries() => [.. window.Pages.Items.OfType<System.Windows.Controls.ListBoxItem>().Select(item => item.Tag switch
            {
                DeskGrid.NotiLand.NotiLandPage => "page",
                string => "caption",
                _ => "own",
            })];
            var expectedPages = global::BrimDeck.SettingsPages.Pages().Count;
            var entries = Entries();
            var pageItems = window.Pages.Items.OfType<System.Windows.Controls.ListBoxItem>().Where(item => item.Tag is DeskGrid.NotiLand.NotiLandPage).ToList();

            // A page of NotiLand is shown in its own place, and counts as the NotiLand page.
            window.Pages.SelectedItem = pageItems[1];
            var shown = (window.PageNotiLand.Visibility, window.PageNotiLand.Child is not null, window.CurrentPage, window.PageGeneral.Visibility, window.PageIsland.Visibility);

            // Choosing a caption (arrowing down onto it) goes on to the page under it.
            window.ShowPage(SettingsWindow.Page.Island);
            var caption = window.Pages.Items.OfType<System.Windows.Controls.ListBoxItem>().First(item => item.Tag is string);
            window.Pages.SelectedItem = caption;
            var afterCaption = window.Pages.SelectedItem is System.Windows.Controls.ListBoxItem { Tag: DeskGrid.NotiLand.NotiLandPage };

            window.ShowPage(SettingsWindow.Page.General);
            var left = window.PageNotiLand.Visibility;

            window.Pages.SelectedItem = pageItems[0];
            host.Apply(false);
            window.RefreshNotiLandPages();
            var after = (Entries(), window.CurrentPage, window.PageNotiLand.Visibility, window.PageNotiLand.Child);
            window.CloseDiscarding();
            host.Dispose();
            return (new { entries, expectedPages, shown, afterCaption, left }, after);
        });

        Assert.Equal(["own", "own", "own", "own", "caption"], whileRunning.entries[..5]);
        Assert.Equal("own", whileRunning.entries[^1]);
        Assert.Equal(whileRunning.expectedPages, whileRunning.entries.Count(entry => entry == "page"));
        Assert.Equal((Visibility.Visible, true, SettingsWindow.Page.Island, Visibility.Collapsed, Visibility.Collapsed), whileRunning.shown);
        Assert.True(whileRunning.afterCaption);
        Assert.Equal(Visibility.Collapsed, whileRunning.left);
        Assert.Equal(["own", "own", "own", "own", "own"], afterStop.Item1);
        Assert.Equal(SettingsWindow.Page.Island, afterStop.CurrentPage);
        Assert.Equal(Visibility.Collapsed, afterStop.Item3);
        Assert.Null(afterStop.Item4);
    }

    // The pages below NotiLand fold away and back; the chosen one gives way to NotiLand itself, and a request from NotiLand unfolds them.
    [Fact]
    public async Task NotiLandPages_FoldAway_AndBack()
    {
        var result = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var host = new DeskGrid.NotiLand.NotiLandHost();
            host.Apply(true);
            var window = new SettingsWindow(new AppConfig { RootPath = Path.GetTempPath(), NotiLandEnabled = true }) { NotiLand = host };
            window.Show();
            IEnumerable<ListBoxItem> Entries() => window.Pages.Items.OfType<ListBoxItem>().Where(item => item.Tag is not null);
            var shownAtStart = Entries().All(item => item.Visibility == Visibility.Visible) && window.NotiLandChevron.Visibility == Visibility.Visible;

            window.Pages.SelectedItem = Entries().First(item => item.Tag is DeskGrid.NotiLand.NotiLandPage);
            window.SetNotiLandExpanded(false);
            var folded = (Entries().All(item => item.Visibility == Visibility.Collapsed), window.CurrentPage, window.Pages.SelectedItem == window.PageIslandItem, window.NotiLandExpanded);

            window.ShowNotiLandPage(3);
            var unfolded = (Entries().All(item => item.Visibility == Visibility.Visible), window.NotiLandExpanded, window.PageNotiLand.Visibility);
            window.CloseDiscarding();
            host.Dispose();
            return (shownAtStart, folded, unfolded);
        });

        Assert.True(result.shownAtStart);
        Assert.Equal((true, SettingsWindow.Page.Island, true, false), result.folded);
        Assert.Equal((true, true, Visibility.Visible), result.unfolded);
    }
}
