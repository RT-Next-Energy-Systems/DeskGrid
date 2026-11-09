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
}
