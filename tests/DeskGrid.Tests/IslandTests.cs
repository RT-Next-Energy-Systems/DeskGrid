using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Island;
using DeskGrid.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Tests;

public sealed class ActivityQueueTests
{
    private static readonly TimeSpan Start = TimeSpan.FromSeconds(100);

    private static IslandActivity Line(string text, double seconds = 5) => new(text, TimeSpan.FromSeconds(seconds));

    [Fact]
    public void AnActivityEndsWhenItsTimeIsUp()
    {
        var queue = new ActivityQueue();
        queue.Show(Line("a"), Start);

        Assert.False(queue.Tick(Start + TimeSpan.FromSeconds(4.9)));
        Assert.Equal("a", queue.Current?.Text);
        Assert.True(queue.Tick(Start + TimeSpan.FromSeconds(5)));
        Assert.Null(queue.Current);
    }

    [Fact]
    public void Others_WaitTheirTurn_InOrder()
    {
        var queue = new ActivityQueue();
        queue.Show(Line("a", 2), Start);
        queue.Show(Line("b", 2), Start);
        queue.Show(Line("c", 2), Start);

        Assert.Equal("a", queue.Current?.Text);
        Assert.Equal(2, queue.Waiting);
        Assert.True(queue.Tick(Start + TimeSpan.FromSeconds(2)));
        Assert.Equal("b", queue.Current?.Text);
        // b's time counts from when it started, not from when it was queued.
        Assert.False(queue.Tick(Start + TimeSpan.FromSeconds(3)));
        Assert.True(queue.Tick(Start + TimeSpan.FromSeconds(4)));
        Assert.Equal("c", queue.Current?.Text);
    }

    [Fact]
    public void WhileHeld_TheCountdownStops_AndResumesWithTheTimeThatWasLeft()
    {
        var queue = new ActivityQueue();
        queue.Show(Line("a"), Start);

        queue.Hold(true, Start + TimeSpan.FromSeconds(3)); // 2 s left.
        Assert.False(queue.Tick(Start + TimeSpan.FromSeconds(60)));
        queue.Hold(false, Start + TimeSpan.FromSeconds(60));

        Assert.False(queue.Tick(Start + TimeSpan.FromSeconds(61.9)));
        Assert.True(queue.Tick(Start + TimeSpan.FromSeconds(62)));
    }

    [Fact]
    public void Dismiss_EndsTheCurrentOne_AndStartsTheNext()
    {
        var queue = new ActivityQueue();
        queue.Show(Line("a"), Start);
        queue.Show(Line("b"), Start);

        queue.Dismiss(Start + TimeSpan.FromSeconds(1));

        Assert.Equal("b", queue.Current?.Text);
        queue.Dismiss(Start + TimeSpan.FromSeconds(1));
        Assert.Null(queue.Current);
    }

    [Fact]
    public void Clear_DropsTheCurrentAndTheWaiting()
    {
        var queue = new ActivityQueue();
        queue.Show(Line("a"), Start);
        queue.Show(Line("b"), Start);

        queue.Clear();

        Assert.Null(queue.Current);
        Assert.Equal(0, queue.Waiting);
    }

    [Fact]
    public void TooManyWaiting_DropsTheOldest()
    {
        var queue = new ActivityQueue();
        queue.Show(Line("now"), Start);
        for (var i = 0; i < ActivityQueue.MaxWaiting + 2; i++)
        {
            queue.Show(Line($"w{i}"), Start);
        }

        Assert.Equal(ActivityQueue.MaxWaiting, queue.Waiting);
        queue.Dismiss(Start);
        Assert.Equal("w2", queue.Current?.Text); // w0 and w1 were dropped.
    }

    [Fact]
    public void ANewSong_ReplacesTheOneBeforeIt_ShownOrWaiting()
    {
        var queue = new ActivityQueue();
        IslandActivity Song(string text) => new(text, TimeSpan.FromSeconds(5), null, IsMusic: true);
        queue.Show(Line("message", 5), Start);
        queue.Show(Song("one"), Start);
        queue.Show(Song("two"), Start);

        Assert.Equal(1, queue.Waiting); // The message is shown; only the newest song waits.
        queue.Dismiss(Start);
        Assert.Equal("two", queue.Current?.Text);
    }

    [Fact]
    public void ANewSong_ReplacesTheSongThatIsShown_AtOnce()
    {
        var queue = new ActivityQueue();
        queue.Show(new IslandActivity("one", TimeSpan.FromSeconds(5), null, IsMusic: true), Start);

        queue.Show(new IslandActivity("two", TimeSpan.FromSeconds(5), null, IsMusic: true), Start + TimeSpan.FromSeconds(1));

        Assert.Equal("two", queue.Current?.Text);
        Assert.Equal(0, queue.Waiting);
        // The new one has its own five seconds.
        Assert.False(queue.Tick(Start + TimeSpan.FromSeconds(5.9)));
        Assert.True(queue.Tick(Start + TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public void CurrentChanged_IsRaisedForEachChange()
    {
        var queue = new ActivityQueue();
        var seen = new List<string?>();
        queue.CurrentChanged += (_, _) => seen.Add(queue.Current?.Text);

        queue.Show(Line("a", 1), Start);
        queue.Show(Line("b", 1), Start); // Waits: no change yet.
        queue.Tick(Start + TimeSpan.FromSeconds(1));
        queue.Tick(Start + TimeSpan.FromSeconds(2));

        Assert.Equal(["a", "b", null], seen);
    }
}

public sealed class MusicPolicyTests
{
    private static MediaTrack Song(string title = "Song", string artist = "Artist", bool playing = true) => new(title, artist, playing, null);

    [Theory]
    [InlineData("WhilePlaying", true, true)]
    [InlineData("WhilePlaying", false, false)] // Paused: nothing stays up.
    [InlineData("OnChange", true, false)]
    [InlineData("Off", true, false)]
    public void ShowsWhilePlaying_OnlyInThatMode_AndOnlyWhilePlaying(string modeName, bool playing, bool shown) =>
        Assert.Equal(shown, MusicPolicy.ShowsWhilePlaying(Enum.Parse<IslandMusicMode>(modeName), Song(playing: playing)));

    [Fact]
    public void ShowsWhilePlaying_IsFalseWithoutASong() =>
        Assert.False(MusicPolicy.ShowsWhilePlaying(IslandMusicMode.WhilePlaying, null));

    [Fact]
    public void Announces_ANewSong_ButNotTheSameOneAgain()
    {
        var first = Song("A");

        Assert.True(MusicPolicy.Announces(IslandMusicMode.OnChange, null, first));
        Assert.False(MusicPolicy.Announces(IslandMusicMode.OnChange, first, first with { Playing = true }));
        Assert.True(MusicPolicy.Announces(IslandMusicMode.OnChange, first, Song("B")));
        Assert.True(MusicPolicy.Announces(IslandMusicMode.OnChange, first, Song("A", "Someone else")));
    }

    [Fact]
    public void Announces_NotWhenPausedOrInAnotherMode()
    {
        Assert.False(MusicPolicy.Announces(IslandMusicMode.OnChange, null, Song(playing: false)));
        Assert.False(MusicPolicy.Announces(IslandMusicMode.WhilePlaying, null, Song()));
        Assert.False(MusicPolicy.Announces(IslandMusicMode.Off, null, Song()));
        Assert.False(MusicPolicy.Announces(IslandMusicMode.OnChange, null, null));
    }

    [Fact]
    public void Label_LeavesOutAnEmptyArtist()
    {
        Assert.Equal("Song — Artist", MusicPolicy.Label(Song()));
        Assert.Equal("Song", MusicPolicy.Label(Song(artist: "")));
    }
}

public sealed class MusicRotationTests
{
    private static MediaTrack Track(string source, bool playing = true) => new(source, "", playing, null, source);

    [Fact]
    public void PlayingToShow_KeepsEveryPlayer_ThatPlays_OnlyWhileThePlayingModeIsOn()
    {
        MediaTrack[] all = [Track("music"), Track("video"), Track("paused", playing: false)];

        Assert.Equal(["music", "video"], MusicPolicy.PlayingToShow(IslandMusicMode.WhilePlaying, all).Select(t => t.Source));
        Assert.Empty(MusicPolicy.PlayingToShow(IslandMusicMode.OnChange, all));
    }

    [Fact]
    public void Rotate_GivesEachPlayerItsTurn_ByTheClock()
    {
        MediaTrack[] playing = [Track("a"), Track("b")];
        var turn = TimeSpan.FromSeconds(4);

        Assert.Equal("a", MusicPolicy.Rotate(playing, TimeSpan.FromSeconds(0), turn)?.Source);
        Assert.Equal("b", MusicPolicy.Rotate(playing, TimeSpan.FromSeconds(4), turn)?.Source);
        Assert.Equal("a", MusicPolicy.Rotate(playing, TimeSpan.FromSeconds(8), turn)?.Source);
        Assert.Null(MusicPolicy.Rotate<MediaTrack>([], TimeSpan.Zero, turn));
    }
}

public sealed class IslandMarqueeTests
{
    [Fact]
    public void AShortLine_StaysAsLongAsAsked_ALongOneUntilItHasScrolledBy()
    {
        var five = TimeSpan.FromSeconds(5);

        Assert.Equal(five, Marquee.ToRead("Build done", five));
        var long200 = new string('x', 200);
        Assert.True(Marquee.ToRead(long200, five) > TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(60), Marquee.ToRead(long200, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void AnEastAsianCharacter_IsCountedWider() =>
        Assert.True(Marquee.EstimateWidth("提示島提示島") > Marquee.EstimateWidth("island"));

    [Fact]
    public void ACycle_RestsAtBothEnds() =>
        Assert.Equal(TimeSpan.FromSeconds(1.5 + 2 + 1.5 + 0.25), Marquee.Cycle(80));
}

public sealed class UsageTests
{
    private static string Line(string id, int input, int output, string time, string type = "assistant") =>
        "{\"type\":\"" + type + "\",\"timestamp\":\"" + time + "\",\"requestId\":\"r\",\"message\":{\"id\":\"" + id
        + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output
        + ",\"cache_creation_input_tokens\":10,\"cache_read_input_tokens\":5000}}}";

    private static readonly DateTimeOffset Since = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACounter_CountsAMessageOnce_AtItsLargestSize_AndSkipsOldOnes()
    {
        var counter = new UsageCounter(Since);
        counter.Add(Line("m1", 100, 5, "2026-11-10T09:00:00Z"));
        counter.Add(Line("m1", 100, 50, "2026-11-10T09:00:01Z")); // The same message, streamed further.
        counter.Add(Line("m2", 200, 20, "2026-11-10T10:00:00Z"));
        counter.Add(Line("old", 999, 999, "2026-11-10T07:00:00Z"));
        counter.Add(Line("user", 999, 999, "2026-11-10T10:00:00Z", type: "user"));
        counter.Add("not json with \"usage\" in it");

        Assert.Equal(160 + 230, counter.Total); // The cache-creation tokens count (10 each), the cache reads do not.
    }

    [Fact]
    public void TheLevel_FollowsTheWarningPercentAndTheLimit()
    {
        var options = new UsageOptions(5, 5, 80);

        Assert.Equal(UsageLevel.Fine, UsageRules.LevelOf(3_999_999, options));
        Assert.Equal(UsageLevel.Warn, UsageRules.LevelOf(4_000_000, options));
        Assert.Equal(UsageLevel.Full, UsageRules.LevelOf(5_000_000, options));
    }

    [Fact]
    public void Short_WritesTokensTheShortWay()
    {
        Assert.Equal("4.1M", UsageRules.Short(4_100_000));
        Assert.Equal("820K", UsageRules.Short(820_000));
        Assert.Equal("950", UsageRules.Short(950));
    }
}

public sealed class TopEdgeClampTests
{
    [Fact]
    public void TheWholePill_StaysOnTheScreen_AndCanReachTheBottomEdge()
    {
        var screen = new ScreenRect(0, 0, 1920, 1080);

        Assert.Equal(new ScreenPoint(400, 0), TopEdge.Clamp(new ScreenPoint(-50, -9), 800, 36, screen));
        Assert.Equal(new ScreenPoint(1520, 1044), TopEdge.Clamp(new ScreenPoint(5000, 5000), 800, 36, screen)); // Down onto the taskbar.
        Assert.Equal(new ScreenPoint(900, 40), TopEdge.Clamp(new ScreenPoint(900, 40), 800, 36, screen));
    }

    [Fact]
    public void OnASecondScreen_ThePillStaysInsideThatScreen() =>
        Assert.Equal(new ScreenPoint(2320, 0), TopEdge.Clamp(new ScreenPoint(1930, -5), 800, 36, new ScreenRect(1920, 0, 3840, 1080)));
}

public sealed class BatteryRulesTests
{
    private static BatteryEvent Between(int before, bool beforeCharging, int now, bool nowCharging) =>
        BatteryRules.Between(new BatteryReading(before, beforeCharging), new BatteryReading(now, nowCharging));

    [Fact]
    public void TheCharger_IsTold_BothWays()
    {
        Assert.Equal(BatteryEvent.ChargerConnected, Between(50, false, 50, true));
        Assert.Equal(BatteryEvent.ChargerRemoved, Between(80, true, 80, false));
    }

    [Fact]
    public void LowAndCritical_AreTold_OnceWhenCrossed_AndNotWhileCharging()
    {
        Assert.Equal(BatteryEvent.Low, Between(21, false, 20, false));
        Assert.Equal(BatteryEvent.None, Between(20, false, 19, false));
        Assert.Equal(BatteryEvent.Critical, Between(11, false, 10, false));
        Assert.Equal(BatteryEvent.None, Between(21, true, 19, true));
    }
}

public sealed class FullscreenGuardTests
{
    // The Shell's values (the enum is internal to the app): 1 not present, 2 busy, 3 D3D full screen, 4 presentation,
    // 5 accepts notifications, 6 quiet time, 7 app.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)] // BUSY: also reported for an ordinary maximized window.
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void Hides_OnlyForAFullScreenApp(int state, bool hides) =>
        Assert.Equal(hides, FullscreenGuard.Hides((QUERY_USER_NOTIFICATION_STATE)state));

    [Fact]
    public void IsActive_CanBeAsked() => _ = FullscreenGuard.IsActive(); // The answer depends on the machine; it must not throw.
}

public sealed class IslandPlacementTests
{
    [Fact]
    public void Place_CentresOnTheWorkArea_BelowItsTopEdge() =>
        Assert.Equal(new ScreenPoint(750, 8), TopEdge.Center(new ScreenRect(0, 0, 1920, 1040), 420, 8));

    [Fact]
    public void Place_OnASecondMonitor_StaysInsideIt() =>
        Assert.Equal(new ScreenPoint(2670, 48), TopEdge.Center(new ScreenRect(1920, 40, 3840, 1080), 420, 8));
}

[Collection(nameof(ShellCollection))]
public sealed class IslandWindowTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Island UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task Display_ShowsAPillThatNeverTakesFocus_AtTheTopCentre()
    {
        var (shown, style, rect, work) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new IslandWindow();
            window.Display("Build done");
            var hwnd = new HWND(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            var exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            PInvoke.GetWindowRect(hwnd, out var r);
            var area = MonitorArea.Primary.WorkArea;
            var visible = window.IsVisible;
            window.Withdraw(animated: false);
            return (visible && !window.IsVisible, exStyle, new ScreenRect(r.left, r.top, r.right, r.bottom), area);
        });

        Assert.True(shown, "The window should show and then hide.");
        Assert.True(style.HasFlag(WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
        Assert.True(style.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
        Assert.True(style.HasFlag(WINDOW_EX_STYLE.WS_EX_TOPMOST));
        Assert.Equal(work.Top + MonitorArea.Primary.ToPixels(IslandWindow.TopGap), rect.Top);
        Assert.InRange(Math.Abs((rect.Left + rect.Right) / 2 - (work.Left + work.Right) / 2), 0, 1);
    }

    [Fact]
    public async Task Escape_ClosesTheCard_ThenOneLineAtATime_ThenTheTimersDisplay_ThenTheInfo()
    {
        var steps = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            using var host = new IslandHost();
            host.Apply(new IslandOptions(true, IslandMusicMode.Off, false));
            var log = new List<string?>();
            host.Show("a", TimeSpan.FromSeconds(30));
            host.Show("b", TimeSpan.FromSeconds(30));
            log.Add(host.CurrentLine);
            host.Dismiss();
            log.Add(host.CurrentLine); // The next line.
            host.Dismiss();
            log.Add(host.CurrentLine); // Nothing left.

            host.ToggleTimer();
            host.ShowInfo();
            host.Dismiss(); // The timer's display goes first (it keeps running) ...
            log.Add($"{host.TimerRunning}/{host.TimerHidden}/{host.InfoUp}");
            host.Dismiss(); // ... then the info.
            log.Add($"{host.TimerRunning}/{host.TimerHidden}/{host.InfoUp}");
            return log;
        });

        Assert.Equal(["a", "b", null], steps.Take(3));
        Assert.Equal("True/True/True", steps[3]);
        Assert.Equal("True/True/False", steps[4]);
    }

    [Fact]
    public async Task TheHost_StaysAwayWhileOff_AndHidesTheWindowWhenTurnedOff()
    {
        var (whileOff, whileOn, afterwards) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            using var host = new IslandHost();
            host.Show("ignored", TimeSpan.FromSeconds(5)); // Off: nothing is made.
            var off = host.HasWindow;
            host.Apply(new IslandOptions(true, IslandMusicMode.Off, false));
            host.Show("hello", TimeSpan.FromSeconds(5));
            var on = host.HasWindow;
            host.Apply(new IslandOptions(false, IslandMusicMode.Off, false));
            return (off, on, host.HasWindow);
        });

        Assert.False(whileOff);
        // While a full-screen app is up on the machine running the tests the window is held back and not made at all.
        Assert.Equal(!FullscreenGuard.IsActive(), whileOn);
        Assert.False(afterwards);
    }
}
