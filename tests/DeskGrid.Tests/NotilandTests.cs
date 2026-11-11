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
        Assert.Equal(expected, PlayerInFront.Shows(source, song, process, title));

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
