using System.IO;
using DeskGrid.Config;
using DeskGrid.Desktop;

namespace DeskGrid.Tests;

public sealed class ConfigPorterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void SuggestedFileName_IsDeskGridAndTheTimeDownToTheMinute()
    {
        Assert.Equal("DeskGrid_2026_11_09_14_05.json", ConfigPorter.SuggestedFileName(new DateTime(2026, 11, 9, 14, 5, 59)));
        Assert.Equal("DeskGrid_2026_01_02_03_04.json", ConfigPorter.SuggestedFileName(new DateTime(2026, 1, 2, 3, 4, 0)));
    }

    [Fact]
    public void ExportThenImport_GivesBackTheSameSettings()
    {
        var config = new AppConfig { PanelColumns = 5, Language = AppLanguage.TraditionalChinese, PanelOpenMode = PanelOpenMode.Centered };
        config.Hotkeys["newFolder"] = "Ctrl+Alt+N";
        config.FolderOrder.AddRange(["B", "A"]);
        config.Widgets["Games"] = new WidgetConfig { Position = new ScreenPoint(120, 340), ItemOrder = ["x.lnk", "y.lnk"], Hotkey = "Ctrl+Alt+1" };
        var path = _temp.Combine("out.json");

        ConfigPorter.Export(config, path);
        var imported = ConfigPorter.TryImport(path, out _);

        Assert.NotNull(imported);
        Assert.Equal(5, imported.PanelColumns);
        Assert.Equal(AppLanguage.TraditionalChinese, imported.Language);
        Assert.Equal(PanelOpenMode.Centered, imported.PanelOpenMode);
        Assert.Equal("Ctrl+Alt+N", imported.Hotkeys["newFolder"]);
        Assert.Equal(["B", "A"], imported.FolderOrder);
        Assert.Equal(new ScreenPoint(120, 340), imported.Widgets["games"].Position); // case-insensitive like the original
        Assert.Equal(["x.lnk", "y.lnk"], imported.Widgets["Games"].ItemOrder);
        Assert.Equal("Ctrl+Alt+1", imported.Widgets["Games"].Hotkey);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2]")]
    [InlineData("{}")]
    [InlineData("{\"unrelated\": true}")]
    public void TryImport_SomethingThatIsNotASettingsFile_IsRefusedWithAReason(string content)
    {
        var path = _temp.Combine("bad.json");
        File.WriteAllText(path, content);

        Assert.Null(ConfigPorter.TryImport(path, out var problem));
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void TryImport_MissingFile_IsRefusedWithAReason()
    {
        Assert.Null(ConfigPorter.TryImport(_temp.Combine("nope.json"), out var problem));
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void ReplaceWith_TakesTheSettings_ButKeepsThisComputersHistory()
    {
        var current = new AppConfig { AutoStartAsked = true, LastSeenVersion = "0.5.3", SkippedUpdateVersion = "v0.6.0" };
        current.Widgets["Old"] = new WidgetConfig();
        current.Hotkeys["settings"] = "Ctrl+Alt+S";
        var other = new AppConfig { PanelColumns = 6, RootPath = @"D:\Elsewhere", AutoStartAsked = false, LastSeenVersion = "0.1.0" };
        other.Widgets["New"] = new WidgetConfig { ItemOrder = ["a"] };

        current.ReplaceWith(other);

        Assert.Equal(6, current.PanelColumns);
        Assert.Equal(@"D:\Elsewhere", current.RootPath);
        Assert.Equal(["New"], current.Widgets.Keys);
        Assert.False(current.Hotkeys.ContainsKey("settings"));
        Assert.True(current.AutoStartAsked);
        Assert.Equal("0.5.3", current.LastSeenVersion);
        Assert.Equal("v0.6.0", current.SkippedUpdateVersion);
    }

    // Every setting property on AppConfig (except this computer's own history) must round-trip through Export/TryImport and ReplaceWith.
    // Adding a new setting to AppConfig without adding it to ReplaceWith fails this test immediately.
    [Fact]
    public void ExportImport_AndReplaceWith_CoverEverySettingPropertyOfAppConfig()
    {
        HashSet<string> computerHistory = [nameof(AppConfig.SkippedUpdateVersion), nameof(AppConfig.AutoStartAsked), nameof(AppConfig.LastSeenVersion)];
        var defaults = new AppConfig();
        var custom = new AppConfig
        {
            RootPath = @"D:\CustomRoot",
            PanelColumns = 6,
            DropMode = DropMode.Copy,
            DeleteMode = DeleteMode.DeleteItems,
            PanelBackground = new PanelBackground { Style = PanelBackgroundStyle.LiquidGlass, CustomColor = "#112233", CustomOpacity = 0.5, Opacity = 0.4, GlassShade = 0.3 },
            PanelOpenMode = PanelOpenMode.Centered,
            Language = AppLanguage.TraditionalChinese,
            AutoBackupEnabled = true,
            AutoBackupHour = 18,
            AutoBackupDays = 14,
            CheckForUpdatesAtStartup = false,
            AllowControl = true,
            IslandEnabled = true,
            IslandMusic = IslandMusicMode.OnChange,
            IslandBattery = true,
            IslandX = 500,
            IslandY = 12,
            IslandShowSeconds = 15,
            IslandUsage = true,
            UsageLimitMillions = 12.5,
            UsageHours = 8,
            UsageWarnPercent = 75,
            IslandPrivacy = true,
            IslandBluetooth = true,
            IslandDownloads = true,
            IslandSystem = true,
            IslandTimerMinutes = 45,
            IslandQuietFrom = "23:00",
            IslandQuietTo = "07:30",
            IslandHideInApps = "vlc,mpv",
            IslandShowOverApps = "obs64",
            IslandFollowPointer = true,
            IslandScale = 1.25,
            IslandHoverExpand = false,
            IslandHoverFullText = false,
            IslandHoverButtons = false,
            IslandHoverTime = false,
            IslandFolders = false,
            FolderOrder = ["Work", "Games"],
        };
        custom.IslandPlaces["desktop"] = new IslandPlace { X = 300, Y = 10 };
        custom.Hotkeys["newFolder"] = "Ctrl+Alt+N";
        custom.Widgets["Work"] = new WidgetConfig { Position = new ScreenPoint(10, 20), ItemOrder = ["a.lnk"], Hotkey = "Ctrl+Alt+W" };

        var path = _temp.Combine("all.json");
        ConfigPorter.Export(custom, path);
        var imported = ConfigPorter.TryImport(path, out _);
        Assert.NotNull(imported);

        var target = new AppConfig();
        target.ReplaceWith(imported);

        var properties = typeof(AppConfig).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsStatic: false } && !computerHistory.Contains(p.Name));
        foreach (var property in properties)
        {
            var defaultJson = System.Text.Json.JsonSerializer.Serialize(property.GetValue(defaults));
            var customJson = System.Text.Json.JsonSerializer.Serialize(property.GetValue(custom));
            var targetJson = System.Text.Json.JsonSerializer.Serialize(property.GetValue(target));
            Assert.NotEqual(defaultJson, customJson); // Every property must have a non-default value in this test.
            Assert.Equal(customJson, targetJson);
        }
    }

    [Fact]
    public void AutoBackup_RunsAtOrAfterTheConfiguredHour_OncePerDay_AndPrunesBeyondTheConfiguredDays()
    {
        var dir = _temp.Combine("Backups");
        var config = new AppConfig { AutoBackupEnabled = true, AutoBackupHour = 14, AutoBackupDays = 3, PanelColumns = 5 };

        // Before 14:00: not due yet.
        Assert.Null(AutoBackup.RunIfDue(config, new DateTime(2026, 11, 1, 13, 59, 0), dir));

        // At 14:00: writes today's backup.
        var day1 = AutoBackup.RunIfDue(config, new DateTime(2026, 11, 1, 14, 0, 0), dir);
        Assert.NotNull(day1);
        Assert.True(File.Exists(day1));

        // Later the same day: does not write again.
        Assert.Null(AutoBackup.RunIfDue(config, new DateTime(2026, 11, 1, 18, 0, 0), dir));

        // Three more days (4 total, with AutoBackupDays = 3): the oldest (Nov 1) is automatically deleted.
        AutoBackup.RunIfDue(config, new DateTime(2026, 11, 2, 15, 0, 0), dir);
        AutoBackup.RunIfDue(config, new DateTime(2026, 11, 3, 15, 0, 0), dir);
        AutoBackup.RunIfDue(config, new DateTime(2026, 11, 4, 15, 0, 0), dir);

        var list = AutoBackup.List(dir);
        Assert.Equal(["2026-11-04", "2026-11-03", "2026-11-02"], list.Select(e => e.Label));
        Assert.False(File.Exists(day1));
    }
}
