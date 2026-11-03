using System.IO;
using System.Windows;
using DeskGrid.Folders;
using DeskGrid.Shell;
using DeskGrid.Threading;
using Windows.Win32;
using Windows.Win32.UI.Shell.Common;

namespace DeskGrid.Tests;

public sealed class ShellIdListTests
{
    // Two entries ending in a zero-length one, as the Shell writes them.
    private static readonly byte[] Folder = [4, 0, 0xF1, 0xF2, 0, 0]; // Entry of 4 bytes, then the terminator.
    private static readonly byte[] FirstItem = [3, 0, 0xA1, 0, 0];
    private static readonly byte[] SecondItem = [5, 0, 0xB1, 0xB2, 0xB3, 0, 0];

    [Fact]
    public void Parse_GivesEachItemAsParentFollowedByItsOwnEntries()
    {
        var items = ShellIdList.Parse(Build(Folder, FirstItem, SecondItem));

        Assert.Equal(2, items.Count);
        Assert.Equal([4, 0, 0xF1, 0xF2, 3, 0, 0xA1, 0, 0], items[0]);
        Assert.Equal([4, 0, 0xF1, 0xF2, 5, 0, 0xB1, 0xB2, 0xB3, 0, 0], items[1]);
    }

    [Fact]
    public void Parse_AnEmptyParent_LeavesTheItemAsItIs()
    {
        var items = ShellIdList.Parse(Build([0, 0], FirstItem));

        Assert.Equal(FirstItem, Assert.Single(items));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(11)]
    public void Parse_ReturnsNothing_ForDataCutShort(int length)
    {
        var whole = Build(Folder, FirstItem, SecondItem);

        Assert.Empty(ShellIdList.Parse(whole.AsSpan(0, Math.Min(length, whole.Length))));
    }

    [Fact]
    public void Parse_ReturnsNothing_ForAnEntryThatRunsOffTheData()
    {
        var data = Build(Folder, [200, 0, 1, 2]); // Says 200 bytes, has 4.

        Assert.Empty(ShellIdList.Parse(data));
    }

    [Fact]
    public void Parse_ReturnsNothing_ForAHugeCount() =>
        Assert.Empty(ShellIdList.Parse([0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0]));

    // The CIDA layout: a count, the offsets of the parent and of each item, then the lists themselves.
    private static byte[] Build(byte[] parent, params byte[][] items)
    {
        var lists = new[] { parent }.Concat(items).ToList();
        var offset = (uint)(4 * (2 + items.Length));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)items.Length);
        foreach (var list in lists)
        {
            writer.Write(offset);
            offset += (uint)list.Length;
        }

        foreach (var list in lists)
        {
            writer.Write(list);
        }

        return stream.ToArray();
    }
}

public sealed class DroppedAppsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DispatcherThread _sta = new("Test STA");

    public void Dispose()
    {
        _sta.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Describe_TellsAVirtualItemFromAFile()
    {
        var file = _temp.CreateFile("notes.txt");

        var (thisPc, folder) = await _sta.InvokeAsync(() => (ShellItem.Describe(ThisPc()), ShellItem.Describe(Absolute(file))));

        Assert.False(thisPc.IsFile);
        Assert.NotEmpty(thisPc.Name);
        Assert.True(folder.IsFile);
    }

    [Fact]
    public async Task Read_MakesAShortcutForAnItemWithoutAFilePath_AndRemovesItOnDispose()
    {
        var data = new DataObject();
        data.SetData(ShellIdList.Format, new MemoryStream(ClipboardArray(ThisPc())));

        var (offers, offersApps, paths, existedWhileOpen, existedAfter, target) = await _sta.InvokeAsync(() =>
        {
            var offers = DroppedItems.Offers(data);
            var offersApps = DroppedItems.OffersApps(data);
            var items = DroppedItems.Read(data);
            var path = items.Paths.SingleOrDefault();
            var existed = path is not null && File.Exists(path);
            var linkTarget = path is null ? null : ShortcutFile.TryGetTarget(path);
            items.Dispose();
            return (offers, offersApps, items.Paths, existed, path is not null && File.Exists(path), linkTarget);
        });

        Assert.True(offers);
        Assert.True(offersApps);
        Assert.Single(paths);
        Assert.EndsWith(".lnk", paths[0]);
        Assert.True(existedWhileOpen);
        Assert.False(existedAfter);
        Assert.Null(target); // A virtual item has no path to point to; the shortcut holds its Shell identifier.
    }

    [Fact]
    public async Task ADroppedAppEndsUpAsAShortcutInTheFolder_AndNothingIsLeftInTheTempFolder()
    {
        var data = new DataObject();
        data.SetData(ShellIdList.Format, new MemoryStream(ClipboardArray(ThisPc())));
        var widget = Directory.CreateDirectory(_temp.Combine("Widget")).FullName;

        var (added, staged) = await _sta.InvokeAsync(() =>
        {
            using var items = DroppedItems.Read(data);
            var added = ItemImporter.Import(items.Paths, widget, Config.DropMode.Move, [], _temp.Path);
            return (added, items.Paths.ToList());
        });

        Assert.Equal(1, added);
        Assert.EndsWith(".lnk", Assert.Single(Directory.GetFiles(widget)));
        Assert.False(File.Exists(staged[0]));
    }

    [Fact]
    public async Task Read_LeavesFilesToTheFileDropFormat()
    {
        var file = _temp.CreateFile("notes.txt");
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { file });
        data.SetData(ShellIdList.Format, new MemoryStream(ClipboardArray(Absolute(file))));

        var (offersApps, paths) = await _sta.InvokeAsync(() =>
        {
            using var items = DroppedItems.Read(data);
            return (DroppedItems.OffersApps(data), items.Paths.ToList());
        });

        Assert.False(offersApps);
        Assert.Equal([file], paths);
    }

    [Fact]
    public async Task CreateFromIdList_MakesAShortcutToTheItem()
    {
        var file = _temp.CreateFile("tool.exe");
        var shortcut = _temp.Combine("tool.lnk");

        var target = await _sta.InvokeAsync(() =>
        {
            ShortcutFile.CreateFromIdList(shortcut, Absolute(file));
            return ShortcutFile.TryGetTarget(shortcut);
        });

        Assert.Equal(file, target, ignoreCase: true);
    }

    // "This PC": a Shell item that is no file or folder, on every Windows.
    private static byte[] ThisPc() => Absolute("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");

    private static unsafe byte[] Absolute(string parsingName)
    {
        PInvoke.SHParseDisplayName(parsingName, null, out var pidl, 0, out _).ThrowOnFailure();
        try
        {
            var length = 0;
            for (var entry = (byte*)pidl; ; entry += *(ushort*)entry)
            {
                length += *(ushort*)entry;
                if (*(ushort*)entry == 0)
                {
                    length += sizeof(ushort);
                    break;
                }
            }

            return new ReadOnlySpan<byte>(pidl, length).ToArray();
        }
        finally
        {
            PInvoke.ILFree(pidl);
        }
    }

    // A "Shell IDList Array" with an empty parent folder and the item (absolute) as its only entry.
    private static byte[] ClipboardArray(byte[] absoluteItem)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1u);
        writer.Write(12u); // The parent: an empty list (its terminator only), right after the three offsets.
        writer.Write(14u);
        writer.Write((ushort)0);
        writer.Write(absoluteItem);
        return stream.ToArray();
    }
}
