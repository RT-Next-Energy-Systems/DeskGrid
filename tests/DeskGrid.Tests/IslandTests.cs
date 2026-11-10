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

public sealed class FullscreenGuardTests
{
    // The Shell's values (the enum is internal to the app): 1 not present, 2 busy, 3 D3D full screen, 4 presentation,
    // 5 accepts notifications, 6 quiet time, 7 app.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
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
        Assert.Equal(new ScreenPoint(750, 8), IslandWindow.Place(new ScreenRect(0, 0, 1920, 1040), 420, 8));

    [Fact]
    public void Place_OnASecondMonitor_StaysInsideIt() =>
        Assert.Equal(new ScreenPoint(2670, 48), IslandWindow.Place(new ScreenRect(1920, 40, 3840, 1080), 420, 8));
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
    public async Task TheHost_StaysAwayWhileOff_AndHidesTheWindowWhenTurnedOff()
    {
        var (whileOff, whileOn, afterwards) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            using var host = new IslandHost();
            host.Show("ignored", TimeSpan.FromSeconds(5)); // Off: nothing is made.
            var off = host.HasWindow;
            host.Enabled = true;
            host.Show("hello", TimeSpan.FromSeconds(5));
            var on = host.HasWindow;
            host.Enabled = false;
            return (off, on, host.HasWindow);
        });

        Assert.False(whileOff);
        // While a full-screen app is up on the machine running the tests the window is held back and not made at all.
        Assert.Equal(!FullscreenGuard.IsActive(), whileOn);
        Assert.False(afterwards);
    }
}
