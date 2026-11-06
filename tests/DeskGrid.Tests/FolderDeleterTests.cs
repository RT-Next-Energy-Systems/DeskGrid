using System.IO;
using DeskGrid.Config;
using DeskGrid.Folders;
using DeskGrid.Resources;

namespace DeskGrid.Tests;

public sealed class FolderDeleterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly List<string> _recycled = [];

    public FolderDeleterTests()
    {
        Directory.CreateDirectory(Desktop);
        _temp.CreateFile(@"root\Dev\Chrome.lnk");
        _temp.CreateFile(@"root\Dev\Slack.lnk");
        _temp.CreateFile(@"root\Dev\desktop.ini", FileAttributes.Hidden | FileAttributes.System);
        _temp.CreateFile(@"root\Dev\Notes\todo.txt");
    }

    private string Desktop => _temp.Combine("desktop");

    private string Folder => _temp.Combine(@"root\Dev");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Entries_SkipsHiddenAndSystemFiles()
    {
        var names = FolderDeleter.Entries(Folder).Select(Path.GetFileName).Order();

        Assert.Equal(["Chrome.lnk", "Notes", "Slack.lnk"], names);
    }

    [Fact]
    public void MoveToDesktop_PutsItemsOnTheDesktop_ThenRecyclesTheFolder()
    {
        _temp.CreateFile(@"desktop\Chrome.lnk");

        var problem = FolderDeleter.Delete(Folder, DeleteMode.MoveToDesktop, Desktop, Recycle);

        Assert.Null(problem);
        Assert.Equal([Folder], _recycled);
        Assert.True(File.Exists(Path.Combine(Desktop, "Chrome.lnk")));
        Assert.True(File.Exists(Path.Combine(Desktop, "Chrome (2).lnk")));
        Assert.True(File.Exists(Path.Combine(Desktop, "Slack.lnk")));
        Assert.True(File.Exists(Path.Combine(Desktop, "Notes", "todo.txt")));
        Assert.False(File.Exists(Path.Combine(Desktop, "desktop.ini")));
    }

    [Fact]
    public void CopyThenDelete_MovesAFolderWithEverythingInIt()
    {
        _temp.CreateFile(@"root\Dev\Notes\Deep\plan.txt");
        _temp.CreateFile(@"root\Dev\Notes\desktop.ini", FileAttributes.Hidden | FileAttributes.System);
        var source = Path.Combine(Folder, "Notes");
        var target = Path.Combine(Desktop, "Notes");

        FolderDeleter.CopyThenDelete(source, target);

        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Combine(target, "todo.txt")));
        Assert.True(File.Exists(Path.Combine(target, "Deep", "plan.txt")));
        Assert.True(File.Exists(Path.Combine(target, "desktop.ini")));
    }

    [Fact]
    public void CopyThenDelete_WhenACopyFails_KeepsTheOriginalAndLeavesNoPartialCopy()
    {
        var source = Path.Combine(Folder, "Notes");
        var target = Path.Combine(Desktop, "Notes");
        using var locked = new FileStream(Path.Combine(source, "todo.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.ThrowsAny<IOException>(() => FolderDeleter.CopyThenDelete(source, target));

        Assert.True(File.Exists(Path.Combine(source, "todo.txt")));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void DeleteItems_LeavesTheDesktopAlone()
    {
        var problem = FolderDeleter.Delete(Folder, DeleteMode.DeleteItems, Desktop, Recycle);

        Assert.Null(problem);
        Assert.Equal([Folder], _recycled);
        Assert.Empty(Directory.GetFileSystemEntries(Desktop));
    }

    [Fact]
    public void MoveToDesktop_WhenAnItemIsStuck_KeepsTheFolder()
    {
        using var lockedItem = new FileStream(Path.Combine(Folder, "Slack.lnk"), FileMode.Open, FileAccess.Read, FileShare.None);

        var problem = FolderDeleter.Delete(Folder, DeleteMode.MoveToDesktop, Desktop, Recycle);

        Assert.Equal(string.Format(Strings.DeleteMoveFailed, "Slack.lnk"), problem);
        Assert.Empty(_recycled);
        Assert.True(File.Exists(Path.Combine(Folder, "Slack.lnk")));
    }

    [Fact]
    public void MoveToDesktop_WithoutADesktop_KeepsTheFolder()
    {
        var problem = FolderDeleter.Delete(Folder, DeleteMode.MoveToDesktop, _temp.Combine("no-desktop"), Recycle);

        Assert.Equal(Strings.DeleteNoDesktop, problem);
        Assert.Empty(_recycled);
        Assert.True(File.Exists(Path.Combine(Folder, "Chrome.lnk")));
    }

    [Fact]
    public void MoveToDesktop_OfAnEmptyFolder_NeedsNoDesktop()
    {
        var empty = Directory.CreateDirectory(_temp.Combine(@"root\Empty")).FullName;

        var problem = FolderDeleter.Delete(empty, DeleteMode.MoveToDesktop, _temp.Combine("no-desktop"), Recycle);

        Assert.Null(problem);
        Assert.Equal([empty], _recycled);
    }

    [Fact]
    public void WhenTheRecycleBinIsCalledOff_ReportsItWasNotDeleted()
    {
        var problem = FolderDeleter.Delete(Folder, DeleteMode.DeleteItems, Desktop, _ => false);

        Assert.Equal(Strings.DeleteStopped, problem);
    }

    [Fact]
    public void WhenTheRecycleBinFails_ReportsWhy()
    {
        var problem = FolderDeleter.Delete(Folder, DeleteMode.DeleteItems, Desktop, _ => throw new IOException("denied"));

        Assert.Equal(string.Format(Strings.DeleteFailed, "denied"), problem);
    }

    // Stands in for the Recycle Bin: records the path and removes the folder.
    private bool Recycle(string path)
    {
        _recycled.Add(path);
        Directory.Delete(path, recursive: true);
        return true;
    }
}
