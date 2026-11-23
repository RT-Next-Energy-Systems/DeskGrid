using System.IO;
using DeskGrid.Taskbar;

namespace DeskGrid.Tests;

public sealed class OpenLauncherTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DeskGrid.Tests." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Install_WritesTheLauncher_AndWhereDeskGridIs()
    {
        var path = OpenLauncher.Install(_folder, @"C:\Apps\DeskGrid.exe");

        Assert.Equal(Path.Combine(_folder, "DeskGridOpen.exe"), path);
        var bytes = File.ReadAllBytes(path!);
        Assert.True(bytes.Length is > 1000 and < 100_000, $"{bytes.Length} bytes");
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'Z', bytes[1]);
        Assert.Equal(@"C:\Apps\DeskGrid.exe", File.ReadAllText(Path.Combine(_folder, "DeskGridOpen.target")));
    }

    [Fact]
    public void Install_AgainWithTheSameThings_ChangesNothing()
    {
        var path = OpenLauncher.Install(_folder, @"C:\Apps\DeskGrid.exe")!;
        var written = File.GetLastWriteTimeUtc(path);
        Thread.Sleep(50);

        Assert.Equal(path, OpenLauncher.Install(_folder, @"C:\Apps\DeskGrid.exe"));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Install_AfterDeskGridMoved_UpdatesWhereItIs()
    {
        OpenLauncher.Install(_folder, @"C:\Old\DeskGrid.exe");
        OpenLauncher.Install(_folder, @"D:\New\DeskGrid.exe");

        Assert.Equal(@"D:\New\DeskGrid.exe", File.ReadAllText(Path.Combine(_folder, "DeskGridOpen.target")));
    }

    [Fact]
    public void Install_ReplacesAnOutdatedLauncher()
    {
        Directory.CreateDirectory(_folder);
        var launcher = Path.Combine(_folder, "DeskGridOpen.exe");
        File.WriteAllBytes(launcher, [1, 2, 3]);

        OpenLauncher.Install(_folder, @"C:\Apps\DeskGrid.exe");

        Assert.True(File.ReadAllBytes(launcher).Length > 1000);
        Assert.False(File.Exists(launcher + ".old"));
        Assert.False(File.Exists(launcher + ".new"));
    }
}
