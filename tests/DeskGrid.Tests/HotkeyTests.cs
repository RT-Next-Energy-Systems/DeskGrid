using System.IO;
using System.Windows.Controls.Primitives;
using DeskGrid.Config;
using DeskGrid.Hotkeys;
using DeskGrid.Settings;
using DeskGrid.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DeskGrid.Tests;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+M", "Ctrl+Alt+M")]
    [InlineData("alt+ctrl+m", "Ctrl+Alt+M")]
    [InlineData(" Control + Shift + F5 ", "Ctrl+Shift+F5")]
    [InlineData("Windows+Space", "Win+Space")]
    [InlineData("Ctrl+Alt+PageDown", "Ctrl+Alt+PageDown")]
    [InlineData("Alt+7", "Alt+7")]
    public void Parse_AcceptsAnyOrderAndCase_AndWritesTheUsualForm(string text, string written)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));

        Assert.Equal(written, hotkey.ToString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("M")] // A bare letter would be taken from every program.
    [InlineData("Shift+M")] // So would a capital.
    [InlineData("Ctrl+Alt")] // No key.
    [InlineData("Ctrl+M+K")] // Two keys.
    [InlineData("Ctrl+Esc")] // Not a key shortcuts can use.
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl++")]
    public void Parse_RejectsWhatCannotBeRegistered(string? text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Defaults_AllFoldersIsCtrlAltM()
    {
        var config = new AppConfig();

        Assert.Equal("Ctrl+M", HotkeyBindings.Get(config, HotkeyBindings.AllFolders)?.ToString());
        Assert.Null(HotkeyBindings.Get(config, HotkeyBindings.NewFolder));
        Assert.Equal(HotkeyBindings.DefaultAllFolders, Assert.Single(HotkeyBindings.All(config)).Key);
    }

    [Fact]
    public void Set_ChangesAndClears_AGeneralShortcut()
    {
        var config = new AppConfig();

        HotkeyBindings.Set(config, HotkeyBindings.Settings, Hotkey.Parse("Ctrl+Alt+S"));
        HotkeyBindings.Set(config, HotkeyBindings.AllFolders, null);

        Assert.Equal("Ctrl+Alt+S", HotkeyBindings.Get(config, HotkeyBindings.Settings)?.ToString());
        Assert.Null(HotkeyBindings.Get(config, HotkeyBindings.AllFolders));
        Assert.Equal([HotkeyBindings.Settings], HotkeyBindings.All(config).Select(b => b.Id));
    }

    [Fact]
    public void FolderShortcuts_LiveWithTheFolder_AndUnknownFoldersAreIgnored()
    {
        var config = new AppConfig();
        config.Widgets["Games"] = new WidgetConfig();

        HotkeyBindings.Set(config, HotkeyBindings.FolderId("Games"), Hotkey.Parse("Ctrl+Alt+1"));
        HotkeyBindings.Set(config, HotkeyBindings.FolderId("Gone"), Hotkey.Parse("Ctrl+Alt+2"));

        Assert.Equal("Ctrl+Alt+1", config.Widgets["Games"].Hotkey);
        Assert.False(config.Widgets.ContainsKey("Gone"));
        Assert.Equal("Games", HotkeyBindings.FolderNameOf(HotkeyBindings.All(config).Last().Id));
        Assert.Null(HotkeyBindings.FolderNameOf(HotkeyBindings.AllFolders));
    }

    [Fact]
    public void TextInTheConfigThatIsNotAShortcut_CountsAsNone()
    {
        var config = new AppConfig();
        config.Hotkeys[HotkeyBindings.AllFolders] = "Ctrl+Banana";
        config.Widgets["Games"] = new WidgetConfig { Hotkey = "just M" };

        Assert.Empty(HotkeyBindings.All(config));
    }

    [Fact]
    public void Config_KeepsShortcuts_ThroughSaveAndLoad_IncludingACleared()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("config.json");
        var store = ConfigStore.Open(path);
        store.Config.Widgets["Games"] = new WidgetConfig();
        HotkeyBindings.Set(store.Config, HotkeyBindings.AllFolders, null); // Cleared: must not come back as the default.
        HotkeyBindings.Set(store.Config, HotkeyBindings.NewFolder, Hotkey.Parse("Ctrl+Alt+N"));
        HotkeyBindings.Set(store.Config, HotkeyBindings.FolderId("Games"), Hotkey.Parse("Shift+Win+G"));
        store.Save();

        var loaded = ConfigStore.Open(path).Config;

        Assert.Null(HotkeyBindings.Get(loaded, HotkeyBindings.AllFolders));
        Assert.Equal("Ctrl+Alt+N", HotkeyBindings.Get(loaded, HotkeyBindings.NewFolder)?.ToString());
        Assert.Equal("Shift+Win+G", HotkeyBindings.Get(loaded, HotkeyBindings.FolderId("Games"))?.ToString());
    }

    [Fact]
    public void Config_WithoutShortcuts_GetsTheDefault()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("config.json");
        File.WriteAllText(path, """{ "panelColumns": 4 }""");

        var loaded = ConfigStore.Open(path).Config;

        Assert.Equal(HotkeyBindings.DefaultAllFolders, HotkeyBindings.Get(loaded, HotkeyBindings.AllFolders));
    }
}

// Each test registers real system-wide shortcuts, so these use a combination nobody binds (Ctrl+Alt+Shift+F23/F24).
[Collection(nameof(ShellCollection))]
public sealed class HotkeyServiceTests : IDisposable
{
    private static readonly Hotkey Rare = Hotkey.Parse("Ctrl+Alt+Shift+F23")!.Value;
    private static readonly Hotkey Rarer = Hotkey.Parse("Ctrl+Alt+Shift+F24")!.Value;

    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task Apply_ReportsACombinationThatIsTaken_AndKeepsTheOthers()
    {
        var (first, second) = await _ui.InvokeAsync(() =>
        {
            using var owner = new HotkeyService();
            using var other = new HotkeyService();
            var first = owner.Apply([new HotkeyBinding("a", Rare)]);
            var second = other.Apply([new HotkeyBinding("b", Rare), new HotkeyBinding("c", Rarer)]);
            return (first, second);
        });

        Assert.Empty(first);
        Assert.Equal(["b"], second.Select(b => b.Id));
    }

    [Fact]
    public async Task Suspend_FreesTheCombination_AndApplyTakesItAgain()
    {
        var (whileSuspended, afterwards) = await _ui.InvokeAsync(() =>
        {
            using var owner = new HotkeyService();
            using var other = new HotkeyService();
            owner.Apply([new HotkeyBinding("a", Rare)]);
            owner.Suspend();
            var whileSuspended = other.Apply([new HotkeyBinding("b", Rare)]);
            other.Suspend();
            return (whileSuspended, owner.Apply([new HotkeyBinding("a", Rare)]));
        });

        Assert.Empty(whileSuspended);
        Assert.Empty(afterwards);
    }

    [Fact]
    public async Task Pressed_NamesTheActionOfTheShortcutThatWasHit()
    {
        var pressed = new TaskCompletionSource<string>();
        var service = await _ui.InvokeAsync(() =>
        {
            var service = new HotkeyService();
            service.Apply([new HotkeyBinding("first", Rare), new HotkeyBinding("second", Rarer)]);
            service.Pressed += (_, id) => pressed.TrySetResult(id);

            // What Windows posts when the keys are hit: the registration id (they count up from 1) in wParam.
            PInvoke.PostMessage(new HWND(service.Handle), PInvoke.WM_HOTKEY, 2, 0);
            return service;
        });

        try
        {
            Assert.Equal("second", await pressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            await _ui.InvokeAsync(service.Dispose);
        }
    }
}

[Collection(nameof(ShellCollection))]
public sealed class HotkeysWindowTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task TheActions_AreGroupedAsDeskGridFoldersAndIsland_EachWithACountOfWhatIsSet()
    {
        var (titles, counts, afterEdit) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var config = new AppConfig();
            config.Widgets["Games"] = new WidgetConfig { Hotkey = "Ctrl+Alt+1" };
            var window = new HotkeysWindow(config, ["Games", "Work"]);
            string[] Read(Func<System.Windows.Controls.StackPanel, IEnumerable<string>> pick) =>
                [.. window.Groups.Children.OfType<System.Windows.Controls.Border>().SelectMany(b => pick((System.Windows.Controls.StackPanel)b.Child))];
            string Heading(System.Windows.Controls.StackPanel card, bool title) =>
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.DockPanel)card.Children[0]).Children[title ? 1 : 0]).Text;

            var titlesNow = Read(card => [Heading(card, title: true)]);
            var countsNow = Read(card => [Heading(card, title: false)]);
            window.BoxFor(HotkeyBindings.IslandTimer).Value = Hotkey.Parse("Ctrl+Alt+T");
            window.RefreshCounts();
            return (titlesNow, countsNow, Read(card => [Heading(card, title: false)]));
        });

        string Count(int set, int of) => string.Format(DeskGrid.Resources.Strings.HotkeysGroupCount, set, of);

        Assert.Equal([DeskGrid.Resources.Strings.HotkeysGroupGeneral, DeskGrid.Resources.Strings.HotkeysGroupIsland, DeskGrid.Resources.Strings.HotkeysGroupFolders], titles);
        Assert.Equal([Count(1, 3), Count(0, 3), Count(1, 2)], counts); // All folders (Ctrl+M) in DeskGrid, Games in Folders.
        Assert.Equal(Count(1, 3), afterEdit[1]);
    }

    [Fact]
    public async Task ShowsTheCurrentShortcuts_ForTheGeneralActionsAndEachFolder()
    {
        var (all, newFolder, games) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var config = new AppConfig();
            config.Widgets["Games"] = new WidgetConfig { Hotkey = "Ctrl+Alt+1" };
            var window = new HotkeysWindow(config, ["Games", "Work"]);
            return (window.BoxFor(HotkeyBindings.AllFolders).Value, window.BoxFor(HotkeyBindings.NewFolder).Value, window.BoxFor(HotkeyBindings.FolderId("Games")).Value);
        });

        Assert.Equal(HotkeyBindings.DefaultAllFolders, all);
        Assert.Null(newFolder);
        Assert.Equal("Ctrl+Alt+1", games?.ToString());
    }

    [Fact]
    public async Task ARecordedShortcut_IsRefusedWhenAnotherActionHasIt()
    {
        var (taken, free) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new HotkeysWindow(new AppConfig(), []);
            var box = window.BoxFor(HotkeyBindings.NewFolder);
            return (box.Check!(HotkeyBindings.DefaultAllFolders), box.Check!(Hotkey.Parse("Ctrl+Alt+N")!.Value));
        });

        Assert.Contains("Ctrl+M", taken);
        Assert.Null(free);
    }

    [Fact]
    public async Task Ok_ReportsEveryShortcut_AsEdited()
    {
        var choices = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var config = new AppConfig();
            config.Widgets["Games"] = new WidgetConfig();
            var window = new HotkeysWindow(config, ["Games"]);
            IReadOnlyList<HotkeyChoice>? applied = null;
            window.Applied += (_, c) => applied = c;
            window.Show();
            window.BoxFor(HotkeyBindings.AllFolders).Value = null;
            window.BoxFor(HotkeyBindings.FolderId("Games")).Value = Hotkey.Parse("Ctrl+Alt+G");
            window.OkButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
            return applied;
        });

        Assert.NotNull(choices);
        Assert.Null(choices.Single(c => c.Id == HotkeyBindings.AllFolders).Key);
        Assert.Equal("Ctrl+Alt+G", choices.Single(c => c.Id == HotkeyBindings.FolderId("Games")).Key?.ToString());
        Assert.Equal(HotkeyBindings.GeneralIds.Count + 1, choices.Count);
    }

    [Fact]
    public async Task Reset_BringsBackTheDefaults_AndClearsTheRest()
    {
        var choices = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var config = new AppConfig();
            HotkeyBindings.Set(config, HotkeyBindings.AllFolders, Hotkey.Parse("Ctrl+Alt+K"));
            HotkeyBindings.Set(config, HotkeyBindings.Settings, Hotkey.Parse("Ctrl+M")); // Taken by the default: must not end up twice.
            var window = new HotkeysWindow(config, []);
            IReadOnlyList<HotkeyChoice>? applied = null;
            window.Applied += (_, c) => applied = c;
            window.Show();
            window.ResetButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
            window.OkButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
            return applied;
        });

        Assert.NotNull(choices);
        Assert.Equal(HotkeyBindings.DefaultAllFolders, choices.Single(c => c.Id == HotkeyBindings.AllFolders).Key);
        Assert.All(choices.Where(c => c.Id != HotkeyBindings.AllFolders), c => Assert.Null(c.Key));
    }
}
