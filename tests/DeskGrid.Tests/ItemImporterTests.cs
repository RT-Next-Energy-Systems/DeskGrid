using System.IO;
using DeskGrid.Config;
using DeskGrid.Folders;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

public sealed class ItemImporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DispatcherThread _sta = new("Test STA");

    public ItemImporterTests() => Directory.CreateDirectory(Widget);

    private string Desktop => _temp.Combine("desktop");

    private string Widget => _temp.Combine(@"root\Dev");

    private string[] Desktops => [Desktop];

    private string Root => _temp.Combine("root");

    public void Dispose()
    {
        _sta.Dispose();
        _temp.Dispose();
    }

    [Theory]
    [InlineData(@"desktop\Chrome.lnk", "move", "Move")]
    [InlineData(@"desktop\Chrome.lnk", "copy", "Copy")]
    [InlineData(@"desktop\Site.url", "move", "Move")]
    [InlineData(@"elsewhere\Chrome.lnk", "move", "Copy")]
    [InlineData(@"desktop\sub\Chrome.lnk", "move", "Copy")]
    [InlineData(@"desktop\tool.exe", "move", "Move")]
    [InlineData(@"desktop\tool.exe", "copy", "CreateShortcut")]
    [InlineData(@"desktop\report.pdf", "move", "Move")]
    [InlineData(@"desktop\report.pdf", "copy", "CreateShortcut")]
    [InlineData(@"desktop\sub\tool.exe", "move", "CreateShortcut")]
    [InlineData(@"elsewhere\tool.exe", "move", "CreateShortcut")]
    [InlineData(@"elsewhere\report.pdf", "move", "CreateShortcut")]
    [InlineData(@"root\Dev\Chrome.lnk", "move", "None")]
    [InlineData(@"root\Other\Chrome.lnk", "copy", "Move")]
    [InlineData(@"root\Other\report.pdf", "copy", "Move")]
    public void Plan_FollowsTheDropRules(string relativePath, string mode, string expected)
    {
        var path = _temp.CreateFile(relativePath);

        var action = ItemImporter.Plan(path, Widget, Enum.Parse<DropMode>(mode, ignoreCase: true), Desktops, Root);

        Assert.Equal(Enum.Parse<ImportAction>(expected), action);
    }

    [Fact]
    public void Plan_Folders_MoveFromTheDesktop_AndGetAShortcutFromElsewhere()
    {
        var other = Directory.CreateDirectory(_temp.Combine("Photos")).FullName;
        var onDesktop = Directory.CreateDirectory(_temp.Combine(@"desktop\Trip")).FullName;
        var otherWidget = Directory.CreateDirectory(_temp.Combine(@"root\Other")).FullName;
        var inOtherWidget = Directory.CreateDirectory(_temp.Combine(@"root\Other\Sub")).FullName;

        Assert.Equal(ImportAction.CreateShortcut, ItemImporter.Plan(other, Widget, DropMode.Move, Desktops, Root));
        Assert.Equal(ImportAction.Move, ItemImporter.Plan(onDesktop, Widget, DropMode.Move, Desktops, Root));
        Assert.Equal(ImportAction.CreateShortcut, ItemImporter.Plan(onDesktop, Widget, DropMode.Copy, Desktops, Root));
        Assert.Equal(ImportAction.CreateShortcut, ItemImporter.Plan(otherWidget, Widget, DropMode.Move, Desktops, Root));
        Assert.Equal(ImportAction.Move, ItemImporter.Plan(inOtherWidget, Widget, DropMode.Move, Desktops, Root));
        Assert.Equal(ImportAction.None, ItemImporter.Plan(Widget, Widget, DropMode.Move, Desktops, Root));
        Assert.Equal(ImportAction.None, ItemImporter.Plan(_temp.Combine("root"), Widget, DropMode.Move, Desktops, Root));
        Assert.Equal(ImportAction.None, ItemImporter.Plan(_temp.Combine("missing.txt"), Widget, DropMode.Move, Desktops, Root));
    }

    // The report that started this: a folder dragged in from the desktop was linked, so it showed in both places.
    [Fact]
    public async Task Import_FromTheDesktop_LeavesNothingOnTheDesktop()
    {
        var folder = Directory.CreateDirectory(_temp.Combine(@"desktop\Trip")).FullName;
        _temp.CreateFile(@"desktop\Trip\photo.jpg");
        var empty = Directory.CreateDirectory(_temp.Combine(@"desktop\Empty")).FullName;
        var document = _temp.CreateFile(@"desktop\report.pdf");
        var program = _temp.CreateFile(@"desktop\tool.exe");
        var shortcut = _temp.CreateFile(@"desktop\Chrome.lnk");

        var added = await _sta.InvokeAsync(() =>
            ItemImporter.ImportTo([folder, empty, document, program, shortcut], Widget, DropMode.Move, Desktops, Root));

        Assert.Empty(Directory.GetFileSystemEntries(Desktop));
        Assert.Equal(
            new[] { "Trip", "Empty", "report.pdf", "tool.exe", "Chrome.lnk" }.Select(name => Path.Combine(Widget, name)), added);
        Assert.True(File.Exists(Path.Combine(Widget, @"Trip\photo.jpg")));
        Assert.True(Directory.Exists(Path.Combine(Widget, "Empty")));
        Assert.Equal(5, Directory.GetFileSystemEntries(Widget).Length); // The things themselves, no shortcuts to them.
    }

    [Fact]
    public async Task Import_FromTheDesktop_InCopyMode_KeepsTheDesktopAsItIs()
    {
        var folder = Directory.CreateDirectory(_temp.Combine(@"desktop\Trip")).FullName;
        var program = _temp.CreateFile(@"desktop\tool.exe");

        await _sta.InvokeAsync(() => ItemImporter.Import([folder, program], Widget, DropMode.Copy, Desktops, Root));

        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(program));
        Assert.Equal(folder, ShortcutTarget(Path.Combine(Widget, "Trip.lnk")), ignoreCase: true);
        Assert.Equal(program, ShortcutTarget(Path.Combine(Widget, "tool.lnk")), ignoreCase: true);
    }

    [Fact]
    public async Task Import_AFolderWhoseNameIsTaken_IsNumbered()
    {
        Directory.CreateDirectory(Path.Combine(Widget, "Trip"));
        var folder = Directory.CreateDirectory(_temp.Combine(@"desktop\Trip")).FullName;

        var added = await _sta.InvokeAsync(() => ItemImporter.ImportTo([folder], Widget, DropMode.Move, Desktops, Root));

        Assert.Equal([Path.Combine(Widget, "Trip (2)")], added);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Import_MovesDesktopShortcuts_CopiesOthers_LinksPrograms()
    {
        var desktopShortcut = _temp.CreateFile(@"desktop\Chrome.lnk");
        var otherShortcut = _temp.CreateFile(@"elsewhere\Slack.lnk");
        var program = _temp.CreateFile(@"elsewhere\tool.exe");

        var added = await _sta.InvokeAsync(() =>
            ItemImporter.Import([desktopShortcut, otherShortcut, program], Widget, DropMode.Move, Desktops, Root));

        Assert.Equal(3, added);
        Assert.False(File.Exists(desktopShortcut));
        Assert.True(File.Exists(Path.Combine(Widget, "Chrome.lnk")));
        Assert.True(File.Exists(otherShortcut));
        Assert.True(File.Exists(Path.Combine(Widget, "Slack.lnk")));
        Assert.True(File.Exists(program));
        Assert.Equal(program, ShortcutTarget(Path.Combine(Widget, "tool.lnk")), ignoreCase: true);
    }

    [Fact]
    public async Task Import_NumbersNamesThatAreTaken()
    {
        _temp.CreateFile(@"root\Dev\report.pdf.lnk");
        var document = _temp.CreateFile(@"elsewhere\report.pdf");

        await _sta.InvokeAsync(() => ItemImporter.Import([document], Widget, DropMode.Move, Desktops, Root));

        Assert.Equal(document, ShortcutTarget(Path.Combine(Widget, "report.pdf (2).lnk")), ignoreCase: true);
    }

    [Fact]
    public async Task ImportTo_ReturnsThePathsOfTheNewItems()
    {
        _temp.CreateFile(@"root\Dev\report.pdf.lnk");
        var document = _temp.CreateFile(@"elsewhere\report.pdf");
        var ignored = _temp.CreateFile(@"root\Dev\inside.txt");

        var added = await _sta.InvokeAsync(() => ItemImporter.ImportTo([document, ignored], Widget, DropMode.Move, Desktops, Root));

        Assert.Equal([Path.Combine(Widget, "report.pdf (2).lnk")], added);
    }

    [Theory]
    [InlineData(@"C:\Windows\notepad.exe", "notepad.lnk")]
    [InlineData(@"C:\Docs\report.pdf", "report.pdf.lnk")]
    [InlineData(@"D:\Photos", "Photos.lnk")]
    [InlineData(@"D:\Photos\", "Photos.lnk")]
    [InlineData(@"D:\", "D.lnk")]
    [InlineData(@"\\server\share\", "server share.lnk")]
    public void ShortcutName_IsAFileName_EvenForADrive(string target, string expected)
    {
        var name = ItemImporter.ShortcutName(target);

        Assert.Equal(expected, name);
        Assert.Equal(name, Path.GetFileName(name));
    }

    [Fact]
    public void UniquePath_AddsANumberBeforeTheExtension()
    {
        _temp.CreateFile(@"root\Dev\a.lnk");
        _temp.CreateFile(@"root\Dev\a (2).lnk");

        Assert.Equal(Path.Combine(Widget, "a (3).lnk"), ItemImporter.UniquePath(Widget, "a.lnk"));
        Assert.Equal(Path.Combine(Widget, "b.lnk"), ItemImporter.UniquePath(Widget, "b.lnk"));
    }

    private static string ShortcutTarget(string shortcutPath)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        return shell.CreateShortcut(shortcutPath).TargetPath;
    }
}
