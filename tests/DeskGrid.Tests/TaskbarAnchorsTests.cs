using System.IO;
using System.Runtime.InteropServices;
using DeskGrid.Shell;
using DeskGrid.Taskbar;
using DeskGrid.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DeskGrid.Tests;

[Collection(nameof(ShellCollection))]
public sealed class TaskbarAnchorsTests : IDisposable
{
    private const uint WmSysCommand = 0x0112;
    private const uint ScRestore = 0xF120;
    private const uint ScMinimize = 0xF020;

    private readonly TempDirectory _pins = new();
    private readonly DispatcherThread _ui = new("TaskbarAnchorsTests");

    public void Dispose()
    {
        _ui.Dispose();
        _pins.Dispose();
    }

    private void Pin(string fileName, string arguments, string? appId) =>
        ShortcutFile.Create(_pins.Combine(fileName), Environment.ProcessPath!, arguments, null, appId);

    [Fact]
    public async Task ReadPinned_ListsTheButtonsOfDeskGridOnly()
    {
        var all = PinShortcut.AppIdPrefix + "AllFolders";
        var dev = PinShortcut.AppId("Dev tools");
        await _ui.InvokeAsync(() =>
        {
            Pin("DeskGrid.lnk", OpenRequest.AllFoldersArguments, all);
            Pin("Dev tools.lnk", OpenRequest.Arguments("Dev tools"), dev);
            Pin("Other.lnk", "--open \"Dev tools\"", "Someone.Else");        // another program's id
            Pin("Plain.lnk", "--open \"Dev tools\"", null);                  // no id
            Pin("Odd.lnk", "--something", PinShortcut.AppIdPrefix + "Odd");  // not an open request
        });

        var pinned = await _ui.InvokeAsync(() => TaskbarAnchors.ReadPinned(_pins.Path));

        Assert.Equal(2, pinned.Count);
        Assert.Equal(OpenRequest.AllFolders, pinned[all]);
        Assert.Equal("Dev tools", pinned[dev]);
    }

    [Fact]
    public void ReadPinned_OfAFolderThatIsNotThere_IsEmpty() =>
        Assert.Empty(TaskbarAnchors.ReadPinned(_pins.Combine("nothing")));

    [Fact]
    public async Task EnablingItGivesEveryPinnedButtonAWindow_AndDisablingItTakesThemAway()
    {
        var (enabled, disabled, windowsBefore, windowsAfter) = await _ui.InvokeAsync(() =>
        {
            Pin("DeskGrid.lnk", OpenRequest.AllFoldersArguments, PinShortcut.AppIdPrefix + "AllFolders");
            Pin("Dev.lnk", OpenRequest.Arguments("Dev"), PinShortcut.AppId("Dev"));
            using var anchors = new TaskbarAnchors(_ => { }, _pins.Path);

            var windowsBefore = AnchorWindows().Count;
            anchors.Enabled = true;
            var enabled = anchors.Count;
            var windowsAfter = AnchorWindows().Count;
            anchors.Enabled = false;
            return (enabled, anchors.Count, windowsBefore, windowsAfter);
        });

        Assert.Equal(2, enabled);
        Assert.Equal(0, disabled);
        Assert.Equal(2, windowsAfter - windowsBefore);
    }

    [Fact]
    public async Task AClickOnTheButton_OpensItsPanel_WhereTheCursorIs_AndLeavesTheWindowMinimized()
    {
        var (opened, minimized) = await await _ui.InvokeAsync(async () =>
        {
            Pin("Dev.lnk", OpenRequest.Arguments("Dev"), PinShortcut.AppId("Dev"));
            var requests = new List<OpenMessage>();
            using var anchors = new TaskbarAnchors(requests.Add, _pins.Path) { Enabled = true };
            var window = AnchorWindows().Last();

            Send(window, ScRestore);
            Send(window, ScMinimize); // not a click: nothing is asked for
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

            return (requests.ToList(), PInvoke.IsIconic(new HWND(window)));
        });

        var request = Assert.Single(opened);
        Assert.Equal("Dev", request.Name);
        Assert.True(minimized);
    }

    [Fact]
    public async Task APinThatAppearsLater_GetsAWindowOnceTheFolderIsQuiet()
    {
        var count = await await _ui.InvokeAsync(async () =>
        {
            using var anchors = new TaskbarAnchors(_ => { }, _pins.Path) { Enabled = true };
            Pin("Dev.lnk", OpenRequest.Arguments("Dev"), PinShortcut.AppId("Dev"));
            for (var i = 0; i < 60 && anchors.Count == 0; i++)
            {
                await Task.Delay(100);
            }

            return anchors.Count;
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AFolderOfPinsThatIsNotThereYet_IsWatchedOnceItAppears()
    {
        var folder = _pins.Combine("later");
        var count = await await _ui.InvokeAsync(async () =>
        {
            using var anchors = new TaskbarAnchors(_ => { }, folder) { Enabled = true };
            Directory.CreateDirectory(folder);
            ShortcutFile.Create(Path.Combine(folder, "Dev.lnk"), Environment.ProcessPath!, OpenRequest.Arguments("Dev"), null, PinShortcut.AppId("Dev"));
            for (var i = 0; i < 300 && anchors.Count == 0; i++)
            {
                await Task.Delay(100);
            }

            return anchors.Count;
        });

        Assert.Equal(1, count);
    }

    // The windows this process has made for the buttons.
    private static List<nint> AnchorWindows()
    {
        var found = new List<nint>();
        var thread = PInvoke.GetCurrentThreadId();
        PInvoke.EnumThreadWindows(thread, (window, _) =>
        {
            if (WindowClassName(window) == "DeskGrid.TaskbarAnchor")
            {
                found.Add(window);
            }

            return true;
        }, 0);
        found.Sort();
        return found;
    }

    private static unsafe string WindowClassName(HWND window)
    {
        var name = stackalloc char[64];
        return new string(name, 0, PInvoke.GetClassName(window, name, 64));
    }

    // What Explorer sends a window when its taskbar button is clicked.
    private static void Send(nint window, uint command) =>
        PInvoke.SendMessageTimeout(new HWND(window), WmSysCommand, command, 0, global::Windows.Win32.UI.WindowsAndMessaging.SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, 1000, out _);
}
