using System.IO;
using DeskGrid.Config;
using DeskGrid.Desktop;

namespace DeskGrid.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string ConfigPath => _temp.Combine("config.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Open_MissingFile_ReturnsDefaults()
    {
        var config = ConfigStore.Open(ConfigPath).Config;

        Assert.Equal(AppConfig.DefaultRootPath, config.RootPath);
        Assert.Equal(AppConfig.DefaultPanelColumns, config.PanelColumns);
        Assert.Equal(DropMode.Move, config.DropMode);
        Assert.Equal(DeleteMode.MoveToDesktop, config.DeleteMode);
        Assert.True(config.CheckForUpdatesAtStartup);
        Assert.Equal(PanelBackgroundStyle.System, config.PanelBackground.Style);
        Assert.Empty(config.Widgets);
    }

    [Fact]
    public void Save_ThenOpen_RoundTrips()
    {
        var store = ConfigStore.Open(ConfigPath);
        store.Config.RootPath = @"D:\Folders";
        store.Config.PanelColumns = 4;
        store.Config.DropMode = DropMode.Copy;
        store.Config.DeleteMode = DeleteMode.DeleteItems;
        store.Config.CheckForUpdatesAtStartup = false;
        store.Config.PanelBackground = new PanelBackground { Style = PanelBackgroundStyle.Custom, CustomColor = "#112233", CustomOpacity = 0.5 };
        store.Config.Widgets["Games"] = new WidgetConfig { Position = new ScreenPoint(10, 20), ItemOrder = ["b.lnk", "a.lnk"] };
        store.Save();

        var reloaded = ConfigStore.Open(ConfigPath).Config;

        Assert.Equal(@"D:\Folders", reloaded.RootPath);
        Assert.Equal(4, reloaded.PanelColumns);
        Assert.Equal(DropMode.Copy, reloaded.DropMode);
        Assert.Equal(DeleteMode.DeleteItems, reloaded.DeleteMode);
        Assert.False(reloaded.CheckForUpdatesAtStartup);
        Assert.Equal((PanelBackgroundStyle.Custom, "#112233", 0.5),
            (reloaded.PanelBackground.Style, reloaded.PanelBackground.CustomColor, reloaded.PanelBackground.CustomOpacity));
        Assert.Equal(new ScreenPoint(10, 20), reloaded.Widgets["GAMES"].Position);
        Assert.Equal(["b.lnk", "a.lnk"], reloaded.Widgets["Games"].ItemOrder);
    }

    [Fact]
    public void Save_WhenTheFileCannotBeReplaced_LogsInsteadOfThrowing()
    {
        Directory.CreateDirectory(ConfigPath); // a directory where the file should go
        var store = ConfigStore.Open(ConfigPath);

        var exception = Record.Exception(store.Save);

        Assert.Null(exception);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "rootPath": null }""")]
    public void Open_InvalidFile_MovesItAsideAndReturnsDefaults(string content)
    {
        File.WriteAllText(ConfigPath, content);

        var config = ConfigStore.Open(ConfigPath).Config;

        Assert.Equal(AppConfig.DefaultRootPath, config.RootPath);
        Assert.False(File.Exists(ConfigPath));
        var backup = Assert.Single(Directory.GetFiles(_temp.Path, "config.json.*.bad"));
        Assert.Equal(content, File.ReadAllText(backup));
    }
}

public sealed class LogTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _original = DeskGrid.Diagnostics.Log.FilePath;

    public void Dispose()
    {
        DeskGrid.Diagnostics.Log.FilePath = _original;
        _temp.Dispose();
    }

    [Fact]
    public void BigLog_IsRotatedToOldFile()
    {
        var path = _temp.Combine("deskgrid.log");
        File.WriteAllText(path, new string('x', (int)DeskGrid.Diagnostics.Log.MaxBytes + 1));
        DeskGrid.Diagnostics.Log.FilePath = path;

        DeskGrid.Diagnostics.Log.Info("after rotation");

        Assert.True(File.Exists(path + ".old"));
        Assert.True(new FileInfo(path).Length < DeskGrid.Diagnostics.Log.MaxBytes);
        Assert.Contains("after rotation", File.ReadAllText(path));
    }
}
