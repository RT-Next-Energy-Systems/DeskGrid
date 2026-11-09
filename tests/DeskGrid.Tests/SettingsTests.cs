using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DeskGrid.Folders;
using DeskGrid.Resources;
using DeskGrid.Tray;
using DeskGrid.Updates;
using DeskGrid.Config;
using DeskGrid.Panel;
using DeskGrid.Settings;
using DeskGrid.Threading;
using Microsoft.Win32;

namespace DeskGrid.Tests;

public sealed class PrivacyLinkTests
{
    // Each language has its own README; a missing translation would fall back to the English address.
    [Theory]
    [InlineData("en", "#privacy")]
    [InlineData("zh-TW", "README.zh-TW.md#")]
    public void SettingsPrivacyUrl_PointsAtThePrivacySectionOfTheReadme(string culture, string expectedPart)
    {
        var url = Strings.ResourceManager.GetString("SettingsPrivacyUrl", new CultureInfo(culture));

        Assert.NotNull(url);
        Assert.StartsWith(UpdateChecker.RepositoryUrl, url);
        Assert.Contains(expectedPart, url);
    }
}

public sealed class NoticesLinkTests
{
    // The MIT licenses of the glass code ask for their notice to travel with it: Settings links to it in the README.
    [Theory]
    [InlineData("en", "#third-party-notices")]
    [InlineData("zh-TW", "README.zh-TW.md#%E7%AC%AC%E4%B8%89%E6%96%B9%E8%81%B2%E6%98%8E")]
    public void SettingsNoticesUrl_PointsAtTheNoticesSectionOfTheReadme(string culture, string expectedPart)
    {
        var url = Strings.ResourceManager.GetString("SettingsNoticesUrl", new CultureInfo(culture));

        Assert.NotNull(url);
        Assert.StartsWith(UpdateChecker.RepositoryUrl, url);
        Assert.Contains(expectedPart, url);
    }
}

public sealed class WidgetFolderNameTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Suggest_NumbersTheDefaultNameWhenTaken()
    {
        Assert.Equal(Strings.NewFolderDefaultName, WidgetFolderName.Suggest(_root.Path));

        Directory.CreateDirectory(_root.Combine(Strings.NewFolderDefaultName));
        Assert.Equal($"{Strings.NewFolderDefaultName} (2)", WidgetFolderName.Suggest(_root.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("trailing.")]
    [InlineData("CON")]
    [InlineData("com1.txt")]
    public void Validate_RejectsNamesWindowsWontCreate(string name) =>
        Assert.Equal(Strings.NewFolderInvalidName, WidgetFolderName.Validate(_root.Path, name));

    [Fact]
    public void Validate_RejectsExistingNames_AcceptsNewOnes()
    {
        Directory.CreateDirectory(_root.Combine("Games"));

        Assert.Equal(Strings.NewFolderExists, WidgetFolderName.Validate(_root.Path, "games"));
        Assert.Null(WidgetFolderName.Validate(_root.Path, "  Work  "));
    }
}

public sealed class AutoStartTests : IDisposable
{
    private const string Command = "\"C:\\Apps\\DeskGrid.exe\"";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    // A throwaway stand-in for HKEY_CURRENT_USER.
    private readonly string _rootPath = $@"Software\DeskGrid.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public AutoStartTests() => _root = Registry.CurrentUser.CreateSubKey(_rootPath);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void SetEnabled_WritesAndRemovesTheRunValue()
    {
        var autoStart = new AutoStart(_root, Command);
        Assert.False(autoStart.IsEnabled);

        autoStart.SetEnabled(true);
        Assert.True(autoStart.IsEnabled);
        Assert.Equal(Command, RunValue());

        autoStart.SetEnabled(false);
        Assert.False(autoStart.IsEnabled);
        Assert.Null(RunValue());
    }

    [Fact]
    public void TaskManagerSwitch_IsHonored_AndClearedWhenReEnabled()
    {
        var autoStart = new AutoStart(_root, Command);
        autoStart.SetEnabled(true);
        using (var approved = _root.CreateSubKey(ApprovedKey))
        {
            approved.SetValue(AutoStart.ValueName, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.False(autoStart.IsEnabled);

        autoStart.SetEnabled(true);
        Assert.True(autoStart.IsEnabled);
    }

    [Fact]
    public void UpdatePath_FixesAStalePath_OnlyWhenEnabled()
    {
        new AutoStart(_root, "\"C:\\Old\\DeskGrid.exe\"").SetEnabled(true);

        new AutoStart(_root, Command).UpdatePath();
        Assert.Equal(Command, RunValue());

        new AutoStart(_root, Command).SetEnabled(false);
        new AutoStart(_root, "\"C:\\Other\\DeskGrid.exe\"").UpdatePath();
        Assert.Null(RunValue());
    }

    [Fact]
    public void CommandFor_QuotesTheExe_AndMarksAStartByWindows()
    {
        var command = AutoStart.CommandFor(@"C:\Program Files\DeskGrid\DeskGrid.exe");

        Assert.Equal("\"C:\\Program Files\\DeskGrid\\DeskGrid.exe\" --autostart", command);
        Assert.True(AutoStart.WasStartedByWindows([AutoStart.Switch]));
    }

    [Theory]
    [InlineData]
    [InlineData("--open", "DEV")]
    [InlineData("--all")]
    public void WasStartedByWindows_IsFalseForOtherLaunches(params string[] args) =>
        Assert.False(AutoStart.WasStartedByWindows(args));

    [Fact]
    public void UpdatePath_AddsTheSwitchToAnEntryFromAnOlderVersion()
    {
        var older = new AutoStart(_root, "\"C:\\Apps\\DeskGrid.exe\"");
        older.SetEnabled(true);
        var current = new AutoStart(_root, AutoStart.CommandFor(@"C:\Apps\DeskGrid.exe"));

        current.UpdatePath();

        Assert.Equal("\"C:\\Apps\\DeskGrid.exe\" --autostart", RunValue());
    }

    private string? RunValue()
    {
        using var run = _root.OpenSubKey(RunKey);
        return run?.GetValue(AutoStart.ValueName) as string;
    }
}

[Collection(nameof(ShellCollection))]
public sealed class SettingsWindowTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Test UI");
    private readonly TempDirectory _root = new();

    public void Dispose()
    {
        _ui.Dispose();
        _root.Dispose();
    }

    [Theory]
    [InlineData("System", "Custom")]
    [InlineData("Dark", "Light")]
    [InlineData("Custom", "System")]
    [InlineData("Light", "LiquidGlass")]
    [InlineData("LiquidGlass", "Dark")]
    public async Task Apply_ReportsTheBackgroundThatIsChecked(string initialName, string pickedName)
    {
        var initial = Enum.Parse<PanelBackgroundStyle>(initialName);
        var picked = Enum.Parse<PanelBackgroundStyle>(pickedName);
        var choice = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var config = new AppConfig { RootPath = _root.Path, PanelBackground = new PanelBackground { Style = initial } };
            var window = new SettingsWindow(config);
            SettingsChoice? applied = null;
            window.Applied += (_, c) => applied = c;
            window.Show();
            window.BackgroundRadioFor(picked).IsChecked = true;
            window.ApplyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return applied;
        });

        Assert.Equal(picked, choice?.PanelBackground.Style);
    }

    // The opacity slider works for every style: a value is reported for the style chosen, the style's default is left unset.
    [Theory]
    [InlineData("Dark", 0.5, 0.5)]
    [InlineData("Light", 0.3, 0.3)]
    [InlineData("System", 0.6, 0.6)]
    [InlineData("Dark", 0.82, null)] // The default, shown as the nearest 5 % step, is not a choice.
    [InlineData("System", 0.85, null)] // Stays the system acrylic.
    public async Task Apply_ReportsTheOpacityOfAPreset_AndLeavesTheDefaultUnset(string styleName, double slider, double? expected)
    {
        var style = Enum.Parse<PanelBackgroundStyle>(styleName);
        var choice = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path, PanelBackground = new PanelBackground { Style = style } });
            SettingsChoice? applied = null;
            window.Applied += (_, c) => applied = c;
            window.Show();
            window.OpacitySlider.Value = slider;
            window.ApplyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return applied;
        });

        Assert.Equal(expected is null ? null : Math.Round(expected.Value, 2), choice?.PanelBackground.Opacity);
        Assert.Equal(new PanelBackground().CustomOpacity, choice?.PanelBackground.CustomOpacity); // The custom one is untouched.
    }

    // The edge-shading slider is the liquid glass's: a value is reported, the default is left unset.
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.8, 0.8)]
    [InlineData(0.5, null)]
    public async Task Apply_ReportsTheEdgeShading_AndLeavesTheDefaultUnset(double slider, double? expected)
    {
        var choice = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path, PanelBackground = new PanelBackground { Style = PanelBackgroundStyle.LiquidGlass } });
            SettingsChoice? applied = null;
            window.Applied += (_, c) => applied = c;
            window.Show();
            window.ShadeSlider.Value = slider;
            window.ApplyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return applied;
        });

        Assert.Equal(expected, choice?.PanelBackground.GlassShade);
    }

    [Fact]
    public async Task EdgeShading_StartsAtTheSavedValue_AndIsOnlyEnabledForLiquidGlass()
    {
        var (shown, onGlass, onDark) = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path, PanelBackground = new PanelBackground { GlassShade = 0.2 } });
            window.Show();
            var shown = window.ShadeSlider.Value;
            window.BackgroundRadioFor(PanelBackgroundStyle.LiquidGlass).IsChecked = true;
            var onGlass = window.ShadeSlider.IsEnabled;
            window.BackgroundRadioFor(PanelBackgroundStyle.Dark).IsChecked = true;
            return (shown, onGlass, window.ShadeSlider.IsEnabled);
        });

        Assert.Equal(0.2, shown, 2);
        Assert.True(onGlass);
        Assert.False(onDark);
    }

    // One page at a time on the right, chosen by the list on the left; every setting lives on some page.
    [Fact]
    public async Task Pages_ShowOneAtATime_StartingWithGeneral()
    {
        var states = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path });
            window.Show();
            var all = new FrameworkElement[] { window.PageGeneral, window.PagePanel, window.PageShortcuts, window.PageAbout };
            var result = new List<(SettingsWindow.Page Page, int Visible, bool Right)>();
            foreach (var page in Enum.GetValues<SettingsWindow.Page>())
            {
                window.ShowPage(page);
                result.Add((window.CurrentPage, all.Count(p => p.Visibility == Visibility.Visible), all[(int)page].Visibility == Visibility.Visible));
            }

            return (Start: new SettingsWindow(new AppConfig { RootPath = _root.Path }).CurrentPage, result);
        });

        Assert.Equal(SettingsWindow.Page.General, states.Start);
        Assert.All(states.result, state => Assert.True(state.Visible == 1 && state.Right));
        Assert.Equal(Enum.GetValues<SettingsWindow.Page>(), states.result.Select(state => state.Page));
    }

    [Fact]
    public async Task Slider_FollowsTheStyleUnlessTheUserMovedIt()
    {
        var (followed, kept) = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path });
            window.Show();
            window.BackgroundRadioFor(PanelBackgroundStyle.Light).IsChecked = true; // Untouched: takes Light's own 80 %.
            var followed = window.OpacitySlider.Value;
            window.OpacitySlider.Value = 0.4;
            window.BackgroundRadioFor(PanelBackgroundStyle.Dark).IsChecked = true; // Moved: stays.
            return (followed, window.OpacitySlider.Value);
        });

        Assert.Equal(0.8, followed, 2);
        Assert.Equal(0.4, kept, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllowControl_ShowsTheSetting_AndApplyReportsItFlipped(bool initial)
    {
        var (shown, applied) = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path, AllowControl = initial });
            SettingsChoice? choice = null;
            window.Applied += (_, c) => choice = c;
            window.Show();
            var shown = window.AllowControlBox.IsChecked == true;
            window.AllowControlBox.IsChecked = !initial;
            window.ApplyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return (shown, choice?.AllowControl);
        });

        Assert.Equal(initial, shown);
        Assert.Equal(!initial, applied);
    }

    // Apply keeps the window open and counts as saved; changing a control again makes it "changed" again.
    [Fact]
    public async Task Apply_KeepsTheWindowOpen_AndClearsTheUnsavedState()
    {
        var (before, afterEdit, afterApply, stillOpen, again) = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path });
            window.Show();
            var before = window.HasUnsavedChanges;
            window.AllowControlBox.IsChecked = true;
            var afterEdit = window.HasUnsavedChanges;
            window.ApplyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var afterApply = window.HasUnsavedChanges;
            var stillOpen = window.IsVisible;
            window.AllowControlBox.IsChecked = false;
            var again = window.HasUnsavedChanges;
            window.CloseDiscarding();
            return (before, afterEdit, afterApply, stillOpen, again);
        });

        Assert.False(before);
        Assert.True(afterEdit);
        Assert.False(afterApply);
        Assert.True(stillOpen);
        Assert.True(again);
    }

    // Done with nothing changed closes at once, with no question.
    [Fact]
    public async Task Done_ClosesWithoutAsking_WhenNothingChanged()
    {
        var closed = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var window = new SettingsWindow(new AppConfig { RootPath = _root.Path });
            window.Show();
            window.DoneButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return !window.IsVisible;
        });

        Assert.True(closed);
    }

    // The sample panel is drawn for every style, and for as many columns as asked, without throwing.
    [Theory]
    [InlineData("System", 3)]
    [InlineData("Dark", 1)]
    [InlineData("Light", 8)]
    [InlineData("Custom", 4)]
    [InlineData("LiquidGlass", 3)]
    public async Task PanelPreview_DrawsEveryStyle(string styleName, int columns)
    {
        var drawn = await _ui.InvokeAsync(() =>
        {
            EnsureApplication();
            var preview = new PanelPreview();
            preview.Update(new PanelBackground { Style = Enum.Parse<PanelBackgroundStyle>(styleName) }, columns);
            preview.Measure(new System.Windows.Size(1000, 1000));
            return preview.DesiredSize.Height > 0 && preview.Background is not null;
        });

        Assert.True(drawn);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-TW")]
    public async Task GitHubRow_FitsBesideTheChangeLogButton(string culture)
    {
        var before = Strings.Culture;
        try
        {
            var (overlaps, oneLine) = await _ui.InvokeAsync(() =>
            {
                EnsureApplication();
                Strings.Culture = new CultureInfo(culture);
                var window = new SettingsWindow(new AppConfig { RootPath = _root.Path });
                try
                {
                    window.Show();
                    window.ShowPage(SettingsWindow.Page.About); // The links are on that page; a hidden one is not laid out.
                    window.UpdateLayout();
                    var button = Bounds(window.ChangeLogButton, window);
                    UIElement[] parts = [window.GitHubLabel, (UIElement)window.GitHubLink.Parent, (UIElement)window.PrivacyLink.Parent];
                    var boxes = parts.Select(part => Bounds(part, window)).ToList();
                    return (boxes.Any(box => box.IntersectsWith(button)), boxes.All(box => Math.Abs(box.Top - boxes[0].Top) < 1));
                }
                finally
                {
                    window.Close();
                }
            });

            Assert.False(overlaps);
            Assert.True(oneLine);
        }
        finally
        {
            Strings.Culture = before;
        }
    }

    // After an update: the window of what's new, in the interface language, with the running version's entry and its "how to use".
    [Theory]
    [InlineData("en", "How to use")]
    [InlineData("zh-TW", "使用方法")]
    public async Task WhatsNewWindow_ShowsTheRunningVersionsEntryIncludingHowToUse(string culture, string howToUse)
    {
        var before = Strings.Culture;
        try
        {
            var (title, hint, texts) = await _ui.InvokeAsync(() =>
            {
                EnsureApplication();
                Strings.Culture = new CultureInfo(culture);
                var current = UpdateChecker.CurrentVersion;
                var window = new ChangeLogWindow(null, current);
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var all = AllText(window.Entries).ToList();
                    return (window.Title, window.Hint.Text, all);
                }
                finally
                {
                    window.Close();
                }
            });

            Assert.Contains(UpdateChecker.CurrentVersion.ToString(3), title);
            Assert.Equal(Strings.ResourceManager.GetString("WhatsNewHint", new CultureInfo(culture)), hint);
            Assert.Contains(howToUse, texts);
            Assert.Single(texts, t => t == UpdateChecker.CurrentVersion.ToString(3)); // Only that version, not the older ones.
        }
        finally
        {
            Strings.Culture = before;
        }
    }

    private static IEnumerable<string> AllText(DependencyObject root)
    {
        if (root is System.Windows.Controls.TextBlock block)
        {
            yield return block.Text;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var text in AllText(VisualTreeHelper.GetChild(root, i)))
            {
                yield return text;
            }
        }
    }

    private static Rect Bounds(UIElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void EnsureApplication() => TestApplication.Ensure();
}
