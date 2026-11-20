using System.Windows;
using System.Windows.Controls;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

// NotiLand (BrimDeck) runs inside DeskGrid's process: its styles must reach its own windows and nothing else.
[Collection(nameof(ShellCollection))]
public sealed class NotiLandTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose() => _ui.Dispose();

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
            var app = global::BrimDeck.App.StartHosted();
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
}
