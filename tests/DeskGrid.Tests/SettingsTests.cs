using System.Globalization;
using System.IO;
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
    public async Task Ok_ReportsTheBackgroundThatIsChecked(string initialName, string pickedName)
    {
        var initial = Enum.Parse<PanelBackgroundStyle>(initialName);
        var picked = Enum.Parse<PanelBackgroundStyle>(pickedName);
        var choice = await _ui.InvokeAsync(() =>
        {
            if (System.Windows.Application.Current is null)
            {
                // The windows take their card styles from App.xaml.
                var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                app.Resources["Card"] = new System.Windows.Style(typeof(System.Windows.Controls.Border));
                app.Resources["CardTitle"] = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            }

            var config = new AppConfig { RootPath = _root.Path, PanelBackground = new PanelBackground { Style = initial } };
            var window = new SettingsWindow(config);
            SettingsChoice? applied = null;
            window.Applied += (_, c) => applied = c;
            window.Show();
            window.BackgroundRadioFor(picked).IsChecked = true;
            window.OkButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return applied;
        });

        Assert.Equal(picked, choice?.PanelBackground.Style);
    }
}
