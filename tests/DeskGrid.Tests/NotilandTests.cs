using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Island;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

public sealed class IslandSnapTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1080);
    private static readonly ScreenRect Work = new(0, 0, 1920, 1032); // A 48-pixel taskbar at the bottom.

    private static SnapResult Snap(int x, int y, params ScreenPoint[] others) =>
        IslandSnap.Snap(new ScreenPoint(x, y), 200, 36, Screen, Work, others, pixelsPerDip: 1);

    [Fact]
    public void NearTheMiddleAndTheTop_ItSnapsThere_WithTwoGuideLines()
    {
        var result = Snap(955, 12);

        Assert.Equal(new ScreenPoint(960, 8), result.Anchor);
        Assert.Contains(new SnapLine(true, 960), result.Lines);
        Assert.Contains(new SnapLine(false, 8), result.Lines);
    }

    [Fact]
    public void NearTheBottom_ItSnapsOntoTheTaskbar_OrJustAboveIt()
    {
        Assert.Equal(1056 - 18, Snap(500, 1040).Anchor.Y); // The middle of the taskbar.
        Assert.Equal(1032 - 8 - 36, Snap(500, 990).Anchor.Y); // A gap above the taskbar.
        Assert.Equal(1080 - 36, Snap(500, 1049).Anchor.Y); // Right at the bottom edge.
    }

    [Fact]
    public void ASmallPill_SnapsToTheSides_AndNeverLeavesTheScreen()
    {
        Assert.Equal(8 + 100, Snap(115, 400).Anchor.X);
        Assert.Equal(1920 - 8 - 100, Snap(1805, 400).Anchor.X);
        Assert.Equal(new ScreenPoint(100, 0), Snap(-500, -500).Anchor); // Kept on the screen.
    }

    [Fact]
    public void ItLinesUpWithItsOtherPlaces_AndFarFromAnything_StaysWhereItIs()
    {
        Assert.Equal(new ScreenPoint(700, 300), Snap(705, 296, new ScreenPoint(700, 300)).Anchor);
        var free = Snap(600, 500);
        Assert.Equal(new ScreenPoint(600, 500), free.Anchor);
        Assert.Empty(free.Lines);
    }
}

public sealed class IslandPlacesTests
{
    [Fact]
    public void ASituationWithoutAPlace_UsesTheDefaultPlace_AndWithoutOneEither_None()
    {
        var places = new Dictionary<string, ScreenPoint>
        {
            [IslandScene.Default] = new(100, 10),
            [IslandScene.Maximized] = new(900, 1040),
        };
        var extras = new IslandExtras(Places: places);

        Assert.Equal(new ScreenPoint(900, 1040), extras.PlaceFor(IslandScene.Maximized));
        Assert.Equal(new ScreenPoint(100, 10), extras.PlaceFor(IslandScene.Desktop));
        Assert.Null(new IslandExtras(Places: new Dictionary<string, ScreenPoint>()).PlaceFor(IslandScene.Desktop));
    }

    [Fact]
    public void TheConfig_GivesTheDefaultPlace_AndEachSituations()
    {
        var config = new AppConfig { IslandX = 5, IslandY = 6 };
        config.IslandPlaces[IslandScene.Desktop] = new IslandPlace { X = 1, Y = 2 };

        var places = IslandOptions.PlacesOf(config);

        Assert.Equal(new ScreenPoint(5, 6), places[IslandScene.Default]);
        Assert.Equal(new ScreenPoint(1, 2), places[IslandScene.Desktop]);
    }

    [Theory]
    [InlineData("screenclippinghost", true)]
    [InlineData("snippingtool", true)]
    [InlineData("startmenuexperiencehost", true)]
    [InlineData("chrome", false)]
    public void TheScreenSnipAndStart_AreOverlaysItStaysUnder(string app, bool overlay) =>
        Assert.Equal(overlay, IslandScene.IsOverlay(app));

    [Theory]
    [InlineData("AppleInc.AppleMusicWin_abc!App", "Song", "applemusic", "Apple Music", true)] // A music app: its window is the page.
    [InlineData("Spotify.exe", "Song", "chrome", "Other tab - Google Chrome", false)]
    [InlineData("Chrome", "Song - Artist", "chrome", "Song - YouTube - Google Chrome", true)] // A browser: the tab in front carries the song.
    [InlineData("Chrome", "Song - Artist", "chrome", "Mail - Google Chrome", false)]
    [InlineData("308046B0AF4A39CB", "Song", "firefox", "Song - YouTube", true)] // A browser id that does not name the browser.
    [InlineData("Chrome", "Song", "", "", false)]
    public void ThePageOfASong_IsInFront_OnlyWhenItsWindowIs(string source, string song, string process, string title, bool expected) =>
        Assert.Equal(expected, PlayerInFront.Shows(source, song, new FrontWindow(process, title)));

    [Theory]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "microsoft.media.player", "Microsoft.ZuneMusic_8wekyb3d8bbwe", true)] // Media Player: no name in common, the package tells.
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe", false)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "microsoft.media.player", "", false)] // Without the package it used to stay shown.
    public void APackagedPlayerInFront_IsToldByItsPackage(string source, string process, string package, bool expected) =>
        Assert.Equal(expected, PlayerInFront.Shows(source, "dg-test-song", new FrontWindow(process, "Media Player", package)));

    [Fact]
    public void TheSituationInFront_CanBeAsked() => Assert.Contains(IslandScene.Detect(IslandScene.Default), IslandScene.All);
}

[Collection(nameof(ShellCollection))]
public sealed class NotilandWindowTests : IDisposable
{
    private readonly DispatcherThread _ui = new("NotiLand UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task ALongLine_IsCutToThePill_AndScrollsByWhatDoesNotFit()
    {
        var (overflow, room, again) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new IslandWindow();
            var text = new string('x', 120);
            window.Display(text);
            var first = (window.Overflow, window.TextRoom);
            window.Display(text); // The same line again (a player reporting): nothing starts over.
            var second = window.Overflow;
            window.Withdraw(animated: false);
            window.Close();
            return (first.Overflow, first.TextRoom, second);
        });

        Assert.True(room > 100 && room < 280, $"room {room}");
        Assert.True(overflow > 100, $"overflow {overflow}");
        Assert.Equal(overflow, again);
    }

    [Fact]
    public async Task PinnedOpen_MakesThePillMedium_WithoutThePointer_AndBackAgain()
    {
        var (closed, pinned, unpinned) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new IslandWindow();
            window.Display(new string('x', 60));
            var small = window.PillTarget;
            window.PinOpen = true;
            var medium = window.PillTarget;
            window.PinOpen = false;
            var back = window.PillTarget;
            window.Withdraw(animated: false);
            window.Close();
            return (small, medium, back);
        });

        Assert.True(pinned.Width > closed.Width || pinned.Height > closed.Height, $"{closed} -> {pinned}");
        Assert.Equal(closed, unpinned);
    }

    [Fact]
    public async Task SettingAPlace_ShowsTheGuideLine_EnterKeepsIt_EscLeavesItAsItWas()
    {
        var (editing, saved, cancelledSaved, ended) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            using var host = new IslandHost();
            host.Apply(new IslandOptions(true, IslandMusicMode.Off, false));
            var changes = new List<(string Scene, ScreenPoint? Place)>();
            host.PlaceChanged += (scene, place) => changes.Add((scene, place));
            var endings = 0;

            host.BeginPlaceEdit(IslandScene.Maximized, () => endings++);
            var during = host.EditingPlace;
            host.Dismiss(); // Esc: nothing saved.
            var afterCancel = changes.Count;

            host.BeginPlaceEdit(IslandScene.Desktop, () => endings++);
            host.EndPlaceEdit(save: true); // Not dragged: nothing to save either.
            return (during, changes.Count, afterCancel, endings);
        });

        Assert.True(editing);
        Assert.Equal(0, cancelledSaved);
        Assert.Equal(0, saved);
        Assert.Equal(2, ended);
    }

    [Fact]
    public async Task TurningTheIslandOff_WhileAPlaceIsBeingSet_EndsTheSetting()
    {
        // The ending is what gives the user the windows back that were minimized for the setting.
        var (ended, editing) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            using var host = new IslandHost();
            host.Apply(new IslandOptions(true, IslandMusicMode.Off, false));
            var endings = 0;
            host.BeginPlaceEdit(IslandScene.Desktop, () => endings++);
            host.Apply(new IslandOptions(false, IslandMusicMode.Off, false));
            return (endings, host.EditingPlace);
        });

        Assert.Equal(1, ended);
        Assert.False(editing);
    }

    [Fact]
    public async Task AReleaseOnAButton_ThatWasNotPressedThere_DoesNothing()
    {
        // The press was on the pill (or the buttons slid under the pointer as it opened): releasing on "pause" must not pause.
        var (calls, downHandled) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new IslandWindow();
            var calls = 0;
            window.Display("song", music: new MusicActions(_ => { }, _ => calls++, _ => { }), source: "player");
            var field = typeof(IslandWindow).GetField("_buttons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var play = (System.Windows.UIElement)((System.Windows.Controls.StackPanel)field.GetValue(window)!).Children[1];

            System.Windows.Input.MouseButtonEventArgs Args(System.Windows.RoutedEvent routed) =>
                new(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = routed, Source = play };

            play.RaiseEvent(Args(System.Windows.UIElement.MouseLeftButtonUpEvent)); // A release alone.
            var down = Args(System.Windows.UIElement.MouseLeftButtonDownEvent);
            play.RaiseEvent(down);
            window.Withdraw(animated: false);
            window.Close();
            return (calls, down.Handled);
        });

        Assert.Equal(0, calls);
        Assert.True(downHandled); // The press belongs to the button: the pill does not start a drag from it.
    }
}

[Collection(nameof(ShellCollection))]
public sealed class NotilandSizeTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Probe UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task AShortLineAfterALongOne_IsSmall()
    {
        var (longWidth, shortWidth) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var window = new IslandWindow { PillScale = 1.3 };
            window.Display(new string('x', 120), look: new IslandLook("info"));
            var first = window.PillTarget.Width;
            window.Display("Edge test", look: new IslandLook("check"));
            var second = window.PillTarget.Width;
            window.Withdraw(animated: false);
            window.Close();
            return (first, second);
        });

        Assert.True(shortWidth < 140, $"long {longWidth}, short {shortWidth}");
    }
}

public sealed class SceneSettlerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(900);

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void ANewSituation_CountsOnlyAfterItHasStayed()
    {
        var settler = new SceneSettler(Delay);

        Assert.Equal(IslandScene.Maximized, settler.Settle(IslandScene.Desktop, IslandScene.Maximized, false, false, At(0)));
        Assert.Equal(IslandScene.Maximized, settler.Settle(IslandScene.Desktop, IslandScene.Maximized, false, false, At(500)));
        Assert.Equal(IslandScene.Desktop, settler.Settle(IslandScene.Desktop, IslandScene.Maximized, false, false, At(900)));
    }

    [Fact]
    public void AFlicker_ThatGoesBack_NeverMovesIt()
    {
        var settler = new SceneSettler(Delay);

        // Alt+Tab: the desktop for a moment, then the same window again.
        Assert.Equal(IslandScene.Maximized, settler.Settle(IslandScene.Desktop, IslandScene.Maximized, false, false, At(0)));
        Assert.Equal(IslandScene.Maximized, settler.Settle(IslandScene.Maximized, IslandScene.Maximized, false, false, At(400)));

        // The next flicker starts its wait anew, it does not inherit the first one's time.
        Assert.Equal(IslandScene.Maximized, settler.Settle(IslandScene.Desktop, IslandScene.Maximized, false, false, At(1000)));
        Assert.Equal(IslandScene.Maximized, settler.Settle(IslandScene.Desktop, IslandScene.Maximized, false, false, At(1500)));
    }

    [Fact]
    public void ADifferentSituation_RestartsTheWait()
    {
        var settler = new SceneSettler(Delay);

        settler.Settle(IslandScene.Desktop, IslandScene.Window, false, false, At(0));
        Assert.Equal(IslandScene.Window, settler.Settle(IslandScene.FullScreen, IslandScene.Window, false, false, At(800)));
        Assert.Equal(IslandScene.Window, settler.Settle(IslandScene.FullScreen, IslandScene.Window, false, false, At(1600)));
        Assert.Equal(IslandScene.FullScreen, settler.Settle(IslandScene.FullScreen, IslandScene.Window, false, false, At(1700)));
    }

    [Fact]
    public void WhileTheUserHandlesIt_ItStaysPut_AndAHiddenPillMovesAtOnce()
    {
        var settler = new SceneSettler(Delay);

        Assert.Equal(IslandScene.Window, settler.Settle(IslandScene.Desktop, IslandScene.Window, false, true, At(0)));
        Assert.Equal(IslandScene.Window, settler.Settle(IslandScene.Desktop, IslandScene.Window, false, true, At(5000)));
        Assert.Equal(IslandScene.Desktop, settler.Settle(IslandScene.Desktop, IslandScene.Window, true, false, At(5001)));
    }
}
