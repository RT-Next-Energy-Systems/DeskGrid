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
    private readonly string _dir = Directory.CreateTempSubdirectory("shelf").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Add_KeepsExistingFilesOnce_AndTellsHowManyCameIn()
    {
        var shelf = new IslandShelf();
        var changes = 0;
        shelf.Changed += (_, _) => changes++;
        var a = MakeFile("a.txt");

        Assert.Equal(1, shelf.Add([a, a, Path.Combine(_dir, "missing.txt")]));
        Assert.Equal(0, shelf.Add([a]));
        Assert.Equal([a], shelf.Paths);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ATooFullShelf_DropsItsOldest_AndPruneDropsWhatIsGone()
    {
        var shelf = new IslandShelf();
        var files = Enumerable.Range(0, IslandShelf.MaxItems + 2).Select(i => MakeFile($"f{i}.txt")).ToList();
        shelf.Add(files);

        Assert.Equal(IslandShelf.MaxItems, shelf.Paths.Count);
        Assert.Equal(files[2], shelf.Paths[0]);

        File.Delete(files[2]);
        shelf.Prune();
        Assert.Equal(files[3], shelf.Paths[0]);

        shelf.Remove(files[3]);
        Assert.DoesNotContain(files[3], shelf.Paths);
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
