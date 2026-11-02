using System.IO;
using DeskGrid.Folders;
using DeskGrid.Shell;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose()
    {
        _ui.Dispose();
        _root.Dispose();
    }

    [Fact]
    public async Task Scan_ListsVisibleFilesOfEachSubfolderInNaturalOrder()
    {
        _root.CreateFile(@"Work\notes.txt");
        _root.CreateFile(@"Games\item10.lnk");
        _root.CreateFile(@"Games\item2.url");
        _root.CreateFile(@"Games\hidden.lnk", FileAttributes.Hidden);
        _root.CreateFile(@"Games\Nested\deep.exe");
        _root.CreateFile("loose-file.txt");

        var widgets = await _ui.InvokeAsync(() =>
        {
            using var scanner = new FolderScanner(_root.Path);
            return scanner.Scan();
        });

        Assert.NotNull(widgets);
        Assert.Equal(["Games", "Work"], widgets.Select(w => w.Name));
        Assert.Equal(["item2.url", "item10.lnk"], widgets[0].Items.Select(i => i.Name));
        Assert.Equal(["notes.txt"], widgets[1].Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Scan_ShortcutItemCarriesItsTargetsWriteTime()
    {
        var target = _root.CreateFile("tool.exe");
        var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, written);
        _root.CreateFile(@"Games\notes.txt");

        var widgets = await _ui.InvokeAsync(() =>
        {
            ShortcutFile.Create(_root.Combine(@"Games\tool.lnk"), target);
            using var scanner = new FolderScanner(_root.Path);
            return scanner.Scan();
        });

        var items = widgets!.Single().Items.ToDictionary(i => i.Name);
        Assert.Equal(written, items["tool.lnk"].TargetWriteTimeUtc);
        Assert.Equal(default, items["notes.txt"].TargetWriteTimeUtc);
    }

    // Where a shortcut points is remembered between scans; the target's own write time is not.
    [Fact]
    public async Task Scan_RescanSeesATargetThatChangedUnderAnUnchangedShortcut()
    {
        var target = _root.CreateFile("tool.exe");
        File.SetLastWriteTimeUtc(target, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var updated = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        _root.CreateFile(@"Games\notes.txt");

        var (first, second) = await _ui.InvokeAsync(() =>
        {
            ShortcutFile.Create(_root.Combine(@"Games\tool.lnk"), target);
            using var scanner = new FolderScanner(_root.Path);
            var before = scanner.Scan()!.Single().Items.Single(i => i.Name == "tool.lnk").TargetWriteTimeUtc;
            File.SetLastWriteTimeUtc(target, updated);
            return (before, scanner.Scan()!.Single().Items.Single(i => i.Name == "tool.lnk").TargetWriteTimeUtc);
        });

        Assert.NotEqual(updated, first);
        Assert.Equal(updated, second);
    }

    [Fact]
    public async Task Scan_ReturnsNullWhenRootIsGone()
    {
        var result = await _ui.InvokeAsync(() =>
        {
            using var scanner = new FolderScanner(_root.Path);
            Directory.Delete(_root.Path);
            return scanner.Scan();
        });

        Assert.Null(result);
    }

    [Fact]
    public void Constructor_RequiresSynchronizationContext() =>
        Assert.Throws<InvalidOperationException>(() => new FolderScanner(_root.Path));
}

public sealed class FolderScannerRenameTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose()
    {
        _ui.Dispose();
        _root.Dispose();
    }

    [Fact]
    public async Task MovingAnItemToTheRoot_IsNoWidgetRename()
    {
        _root.CreateFile(@"Games\a.lnk");
        var renames = await WatchAsync(() => File.Move(_root.Combine(@"Games\a.lnk"), _root.Combine("a.lnk")));

        Assert.Empty(renames);
    }

    [Fact]
    public async Task MovingAnItemToAnotherWidget_IsNoItemRename()
    {
        _root.CreateFile(@"Games\a.lnk");
        Directory.CreateDirectory(_root.Combine("Work"));
        var renames = await WatchAsync(() => File.Move(_root.Combine(@"Games\a.lnk"), _root.Combine(@"Work\a.lnk")));

        Assert.Empty(renames);
    }

    [Fact]
    public async Task RenamingAnItemOrAWidget_IsReported()
    {
        _root.CreateFile(@"Games\a.lnk");
        var renames = await WatchAsync(() =>
        {
            File.Move(_root.Combine(@"Games\a.lnk"), _root.Combine(@"Games\b.lnk"));
            Directory.Move(_root.Combine("Games"), _root.Combine("Play"));
        });

        Assert.Equal(["Games/a.lnk>b.lnk", "Games>Play"], renames);
    }

    // Starts a scanner, runs the file system change, and returns the renames it reported once the change has settled.
    private async Task<List<string>> WatchAsync(Action change)
    {
        var renames = new List<string>();
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = await _ui.InvokeAsync(() =>
        {
            var scanner = new FolderScanner(_root.Path);
            scanner.WidgetRenamed += (_, e) => renames.Add($"{e.OldName}>{e.NewName}");
            scanner.ItemRenamed += (_, e) => renames.Add($"{e.WidgetName}/{e.OldName}>{e.NewName}");
            scanner.Changed += (_, _) => settled.TrySetResult();
            scanner.Start();
            return scanner;
        });

        change();
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await _ui.InvokeAsync(scanner.Dispose);
        return renames;
    }
}
