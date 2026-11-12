using System.IO;
using DeskGrid.Island;

namespace DeskGrid.Tests;

public sealed class IslandTimerTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(1000);

    [Fact]
    public void ATimer_CountsDown_ReportsDoneOnce_AndFillsUp()
    {
        var timer = new IslandTimer();
        timer.Start(TimeSpan.FromMinutes(25), T0);

        Assert.True(timer.Running);
        Assert.Equal(TimeSpan.FromMinutes(24), timer.Left(T0 + TimeSpan.FromMinutes(1)));
        Assert.Equal(0.5, timer.Elapsed(T0 + TimeSpan.FromMinutes(12.5)), 3);
        Assert.False(timer.TakeDone(T0 + TimeSpan.FromMinutes(24.9)));
        Assert.True(timer.TakeDone(T0 + TimeSpan.FromMinutes(25)));
        Assert.False(timer.Running);
        Assert.False(timer.TakeDone(T0 + TimeSpan.FromMinutes(26)));
    }

    [Theory]
    [InlineData(1499, "24:59")]
    [InlineData(59, "0:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    public void Format_WritesMinutesAndSeconds(int seconds, string expected) =>
        Assert.Equal(expected, IslandTimer.Format(TimeSpan.FromSeconds(seconds)));
}

public sealed class IslandRulesTests
{
    [Theory]
    [InlineData("22:00", "07:00", "23:30", true)] // Across midnight.
    [InlineData("22:00", "07:00", "03:00", true)]
    [InlineData("22:00", "07:00", "12:00", false)]
    [InlineData("09:00", "17:00", "12:00", true)]
    [InlineData("09:00", "17:00", "17:00", false)]
    [InlineData("", "07:00", "03:00", false)] // No start: no quiet hours.
    public void QuietHours_FollowFromAndTo_AcrossMidnightToo(string from, string to, string now, bool quiet) =>
        Assert.Equal(quiet, IslandRules.InQuietHours(TimeOnly.Parse(now), IslandRules.Parse(from), IslandRules.Parse(to)));

    [Fact]
    public void SplitApps_TakesNames_WithoutExe_LowerCase_NoDuplicates() =>
        Assert.Equal(["chrome", "zoom", "teams"], IslandRules.SplitApps(" Chrome.exe, zoom ;Teams, chrome "));
}

public sealed class IslandLookTests
{
    [Theory]
    [InlineData("check", "\uE73E")]
    [InlineData("MIC", "\uE720")]
    [InlineData("★", "★")]
    public void Glyph_IsAKnownNameOrOneCharacter(string name, string glyph) => Assert.Equal(glyph, IslandLook.ResolveGlyph(name));

    [Fact]
    public void Glyph_ForAnythingElse_IsNone() => Assert.Null(IslandLook.ResolveGlyph("not-an-icon"));

    [Fact]
    public void Color_TakesHexAndNames_AndRefusesTheRest()
    {
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xE8, 0x4D, 0x4D), IslandLook.ResolveColor("#E84D4D"));
        Assert.NotNull(IslandLook.ResolveColor("orange"));
        Assert.Null(IslandLook.ResolveColor("not a color"));
    }

    [Fact]
    public void ActionTargets_AreFoldersAddressesAndExistingPathsOnly()
    {
        Assert.True(IslandAction.IsAllowed("folder:Games"));
        Assert.True(IslandAction.IsAllowed("https://example.com/a"));
        Assert.True(IslandAction.IsAllowed(Path.GetTempPath()));
        Assert.False(IslandAction.IsAllowed("file:///C:/Windows/System32/cmd.exe"));
        Assert.False(IslandAction.IsAllowed("cmd /c calc"));
        Assert.False(IslandAction.IsAllowed(@"C:\definitely\not\here"));
        Assert.Equal("Games", IslandAction.FolderOf("folder:Games"));
        Assert.Null(IslandAction.FolderOf("https://example.com"));
    }
}

public sealed class IslandShelfTests : IDisposable
{
    // The sources stand for files anywhere (a USB stick); the shelf keeps its copies in its own folder.
    private readonly string _dir = Directory.CreateTempSubdirectory("shelf").FullName;
    private string Sources => Directory.CreateDirectory(Path.Combine(_dir, "sources")).FullName;
    private string Staging => Path.Combine(_dir, "staging");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private IslandShelf NewShelf() => new(Staging);

    private string MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(Sources, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Add_CopiesExistingFilesOnce_AndTellsHowManyCameIn()
    {
        var shelf = NewShelf();
        var changes = 0;
        shelf.Changed += (_, _) => changes++;
        var a = MakeFile("a.txt", "hello");

        Assert.Equal(1, await shelf.AddAsync([a, a, Path.Combine(Sources, "missing.txt")]));
        Assert.Equal(0, await shelf.AddAsync([a]));

        var copy = Assert.Single(shelf.Paths);
        Assert.NotEqual(a, copy);
        Assert.StartsWith(Staging, copy);
        Assert.Equal("a.txt", Path.GetFileName(copy));
        Assert.Equal("hello", File.ReadAllText(copy));
        Assert.True(File.Exists(a), "the original is left where it was");
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ACopy_OutlivesItsSource_AndAFolderIsCopiedWhole()
    {
        var shelf = NewShelf();
        var file = MakeFile("gone.txt");
        var folder = Directory.CreateDirectory(Path.Combine(Sources, "Photos", "2025")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "2025", "a.jpg"), "jpg");

        Assert.Equal(2, await shelf.AddAsync([file, folder]));
        File.Delete(file); // The USB stick is pulled.
        Directory.Delete(folder, recursive: true);

        shelf.Prune();
        Assert.Equal(2, shelf.Paths.Count);
        Assert.True(File.Exists(shelf.Paths[0]));
        Assert.Equal("jpg", File.ReadAllText(Path.Combine(shelf.Paths[1], "2025", "a.jpg")));
    }

    [Fact]
    public async Task TwoFilesOfTheSameName_BothStayAsTheyAreCalled()
    {
        var shelf = NewShelf();
        var first = MakeFile("report.pdf", "one");
        var second = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "usb")).FullName, "report.pdf");
        File.WriteAllText(second, "two");

        await shelf.AddAsync([first, second]);

        Assert.Equal(["report.pdf", "report.pdf"], shelf.Paths.Select(Path.GetFileName));
        Assert.Equal(["one", "two"], shelf.Paths.Select(File.ReadAllText));
    }

    [Fact]
    public async Task Remove_Clear_AndAnOverflow_DeleteTheCopies()
    {
        var shelf = NewShelf();
        var files = Enumerable.Range(0, IslandShelf.MaxItems + 1).Select(i => MakeFile($"f{i}.txt")).ToList();
        await shelf.AddAsync(files);

        Assert.Equal(IslandShelf.MaxItems, shelf.Paths.Count);
        Assert.Equal("f1.txt", Path.GetFileName(shelf.Paths[0]));
        Assert.Equal(IslandShelf.MaxItems, Directory.GetDirectories(Staging).Length); // f0's copy went with the overflow.

        var removed = shelf.Paths[0];
        shelf.Remove(removed);
        Assert.False(File.Exists(removed));
        Assert.False(Directory.Exists(Path.GetDirectoryName(removed)));

        shelf.Clear();
        Assert.Empty(shelf.Paths);
        Assert.Empty(Directory.GetDirectories(Staging));
        Assert.True(files.All(File.Exists), "no original was touched");
    }

    [Fact]
    public async Task Prune_DropsACopyThatWasMovedAway()
    {
        var shelf = NewShelf();
        await shelf.AddAsync([MakeFile("a.txt"), MakeFile("b.txt")]);
        File.Move(shelf.Paths[0], Path.Combine(_dir, "taken.txt")); // A drop target that moved it.

        shelf.Prune();

        Assert.Equal("b.txt", Path.GetFileName(Assert.Single(shelf.Paths)));
        Assert.Single(Directory.GetDirectories(Staging));
    }

    [Fact]
    public async Task AFileFromTheShelfItself_IsNotCopiedAgain()
    {
        var shelf = NewShelf();
        await shelf.AddAsync([MakeFile("a.txt")]);

        Assert.Equal(0, await shelf.AddAsync(shelf.Paths));
        Assert.Single(shelf.Paths);
    }

    [Fact]
    public async Task PurgeLeftovers_DeletesWhatAnEarlierRunLeft_ButNotWhatTheShelfHolds()
    {
        var shelf = NewShelf();
        await shelf.AddAsync([MakeFile("a.txt")]);
        var leftover = Directory.CreateDirectory(Path.Combine(Staging, "left-by-a-crash")).FullName;
        File.WriteAllText(Path.Combine(leftover, "old.bin"), "x");

        await shelf.PurgeLeftoversAsync();

        Assert.False(Directory.Exists(leftover));
        Assert.True(File.Exists(Assert.Single(shelf.Paths)));
    }
}

public sealed class IslandWatcherHelperTests
{
    [Theory]
    [InlineData(@"C:#Program Files#Zoom#bin#Zoom.exe", "Zoom")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "WindowsCamera")]
    [InlineData("chrome.exe", "chrome")]
    public void PrivacyNames_AreTheProgram(string key, string name) => Assert.Equal(name, PrivacyWatcher.NameOf(key));

    [Fact]
    public void PrivacyWatcher_ReadsTheRegistry_WithoutThrowing() => _ = PrivacyWatcher.Read().ToList();

    [Theory]
    [InlineData("a.crdownload", true)]
    [InlineData("a.PART", true)]
    [InlineData("report.pdf", false)]
    public void PartialDownloads_AreRecognisedByTheirName(string name, bool partial) => Assert.Equal(partial, DownloadWatcher.IsPartial(name));

    [Theory]
    [InlineData(500, "500 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(1_572_864, "1.5 MB/s")]
    public void SpeedsAreWrittenShort(double bytes, string text) => Assert.Equal(text, SystemStatus.Rate(bytes));

    [Fact]
    public void TheSystemLine_HasProcessorAndMemory_AndSpeedFromTheSecondReading()
    {
        var status = new SystemStatus();
        var first = status.Read(TimeSpan.FromSeconds(10));
        var second = status.Read(TimeSpan.FromSeconds(12));

        Assert.Matches(@"^CPU \d+% · RAM \d+%$", first);
        Assert.Matches(@"^CPU \d+% · RAM \d+% · ↓.+ ↑.+$", second);
    }

    [Fact]
    public async Task ADownload_IsToldOnce_WhenItsSizeHasSettled()
    {
        var dir = Directory.CreateTempSubdirectory("dl").FullName;
        try
        {
            using var ui = new DeskGrid.Threading.DispatcherThread("Download UI");
            var finished = new TaskCompletionSource<string>();
            DownloadWatcher? watcher = null;
            await ui.InvokeAsync(() =>
            {
                watcher = new DownloadWatcher(dir);
                watcher.Finished += path => finished.TrySetResult(path);
            });

            File.WriteAllText(Path.Combine(dir, "x.crdownload"), "partial"); // Ignored.
            var done = Path.Combine(dir, "x.zip");
            File.WriteAllText(done, "data");

            var winner = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Same(finished.Task, winner);
            Assert.Equal(done, await finished.Task);
            await ui.InvokeAsync(() => watcher!.Dispose());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ADownloadFinishingAfterTheWatcherWasTurnedOff_IsNotTold()
    {
        var dir = Directory.CreateTempSubdirectory("dl").FullName;
        try
        {
            using var ui = new DeskGrid.Threading.DispatcherThread("Download UI");
            var told = 0;
            await ui.InvokeAsync(() =>
            {
                var watcher = new DownloadWatcher(dir);
                watcher.Finished += _ => told++;
                File.WriteAllText(Path.Combine(dir, "late.zip"), "data");
                Thread.Sleep(500); // The watcher's event is queued for this (blocked) thread meanwhile...
                watcher.Dispose(); // ...and the option is turned off before it is handled.
            });

            await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); // Two checks' worth.
            Assert.Equal(0, await ui.InvokeAsync(() => told));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class MediaTimelineTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AfterASeek_TheMovedPositionCounts_UntilThePlayerReportsNewer()
    {
        // Media Player moved to 1:30 when asked but kept reporting its old timeline (0:33 at the last report): the card
        // showed 0:39 a few seconds later while the song was at 1:36.
        var stale = new MediaTimeline(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(33), At);
        var seeked = (TimeSpan.FromSeconds(90), At.AddSeconds(10));
        var line = MediaTimeline.AfterSeek(stale, seeked)!;
        Assert.Equal(TimeSpan.FromSeconds(96), line.PositionAt(At.AddSeconds(16), playing: true));

        var fresh = new MediaTimeline(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(100), At.AddSeconds(12));
        Assert.Same(fresh, MediaTimeline.AfterSeek(fresh, seeked)); // The player's newer word wins.
        Assert.Null(MediaTimeline.AfterSeek(null, seeked));
    }

    [Fact]
    public void ThePosition_RunsOnWithTheClock_WhilePlaying_AndStaysWhenPaused()
    {
        var line = new MediaTimeline(TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(30), At);

        Assert.Equal(TimeSpan.FromSeconds(40), line.PositionAt(At.AddSeconds(10), playing: true));
        Assert.Equal(TimeSpan.FromSeconds(30), line.PositionAt(At.AddSeconds(10), playing: false));
        Assert.Equal(TimeSpan.FromMinutes(4), line.PositionAt(At.AddMinutes(20), playing: true));
        Assert.Equal(30.0 / 240, line.Fraction(At, playing: true)!.Value, 4);
    }

    [Fact]
    public void ALiveStream_HasNoFraction() =>
        Assert.Null(new MediaTimeline(TimeSpan.Zero, TimeSpan.Zero, At).Fraction(At, playing: true));

    [Fact]
    public void SeekTarget_MapsFractionOntoDuration_AndClampsToEnds()
    {
        var line = new MediaTimeline(TimeSpan.FromMinutes(4), TimeSpan.Zero, At);

        Assert.Equal(TimeSpan.FromMinutes(1), line.SeekTarget(0.25));
        Assert.Equal(TimeSpan.Zero, line.SeekTarget(-0.5));
        Assert.Equal(TimeSpan.FromMinutes(4), line.SeekTarget(1.5));
        Assert.Equal(TimeSpan.Zero, new MediaTimeline(TimeSpan.Zero, TimeSpan.Zero, At).SeekTarget(0.5));
    }

    [Fact]
    public void DominantColor_FindsAColoredCover_AndSkipsGreyOrNull()
    {
        Assert.Null(MediaTrack.DominantColor(null));

        var grey = new byte[4 * 4 * 4];
        for (var i = 0; i < grey.Length; i += 4)
        {
            (grey[i], grey[i + 1], grey[i + 2], grey[i + 3]) = (120, 120, 120, 255);
        }

        var greyBmp = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, grey, 16);
        Assert.Null(MediaTrack.DominantColor(greyBmp));

        var blue = new byte[4 * 4 * 4];
        for (var i = 0; i < blue.Length; i += 4)
        {
            (blue[i], blue[i + 1], blue[i + 2], blue[i + 3]) = (220, 80, 30, 255); // BGRA: strong blue.
        }

        var blueBmp = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, blue, 16);
        var color = MediaTrack.DominantColor(blueBmp);
        Assert.NotNull(color);
        Assert.True(color!.Value.B > color.Value.R && color.Value.B > color.Value.G);
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Format_WritesTimes(int seconds, string text) => Assert.Equal(text, MediaTimeline.Format(TimeSpan.FromSeconds(seconds)));
}
