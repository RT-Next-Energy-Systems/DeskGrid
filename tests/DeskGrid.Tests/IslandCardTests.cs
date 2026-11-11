using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskGrid.Desktop;
using DeskGrid.Island;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

[Collection(nameof(ShellCollection))]
public sealed class IslandCardTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Card UI");

    public void Dispose() => _ui.Dispose();

    private static MediaTrack Song(MediaTimeline? timeline) =>
        new("Song", "Artist", true, null, "player", timeline);

    // The bar a song is scrubbed on: the hand-cursor border that holds the bar and its dot.
    private static Border ScrubBar(DependencyObject root) =>
        Descendants(root).OfType<Border>().First(b => b.Cursor == Cursors.Hand && b.Child is Grid);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static void Press(UIElement on, RoutedEvent kind) =>
        on.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = kind });

    [Fact]
    public async Task LettingGoOfTheBar_AfterTheSongLostItsTimeline_EndsTheDrag()
    {
        var (capturedWhileDown, capturedAfter, seeks) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var seeks = 0;
            var track = Song(new MediaTimeline(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30), DateTimeOffset.UtcNow));
            var card = new IslandCard(new MusicActions(_ => { }, _ => { }, _ => { }, (_, _) => seeks++));
            try
            {
                var work = MonitorArea.Primary.WorkArea;
                card.Open(() => new CardModel([track], null, null, null, [], [], [CardSections.Players]), new ScreenPoint((work.Left + work.Right) / 2, work.Top + 8), 36);
                card.UpdateLayout();
                var bar = ScrubBar(card);
                Press(bar, UIElement.MouseLeftButtonDownEvent);
                var down = bar.IsMouseCaptured;

                track = Song(null); // The player stops telling the song's length while it is being scrubbed.
                card.Refresh();
                Press(bar, UIElement.MouseLeftButtonUpEvent);
                return (down, bar.IsMouseCaptured, seeks);
            }
            finally
            {
                card.Close();
            }
        });

        Assert.True(capturedWhileDown);
        Assert.False(capturedAfter); // Used to keep the mouse until something else took it.
        Assert.Equal(0, seeks); // Nothing to seek in.
    }

    // What the drag system hands the window a file is let go over (its constructor is not public).
    private static DragEventArgs DropOf(IDataObject data, DependencyObject target)
    {
        var args = (DragEventArgs)Activator.CreateInstance(
            typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            [data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Link, target, new Point(10, 10)], null)!;
        args.RoutedEvent = UIElement.DropEvent;
        args.Effects = DragDropEffects.Copy;
        return args;
    }

    [Fact]
    public async Task AFileFromTheShelf_LetGoOverTheCard_IsAnsweredNone_SoTheShelfKeepsIt()
    {
        var file = Path.GetTempFileName();
        try
        {
            var (shelfEffect, shelfAdds, otherEffect, otherAdds) = await _ui.InvokeAsync(() =>
            {
                TestApplication.Ensure();
                var card = new IslandCard(new MusicActions(_ => { }, _ => { }, _ => { }));
                var adds = 0;
                card.ShelfDropped += _ => adds++;
                try
                {
                    var work = MonitorArea.Primary.WorkArea;
                    card.Open(() => new CardModel([], null, null, null, [], [file], [CardSections.Shelf]), new ScreenPoint((work.Left + work.Right) / 2, work.Top + 8), 36);
                    card.UpdateLayout();

                    var fromShelf = new DataObject(DataFormats.FileDrop, new[] { file });
                    fromShelf.SetData(IslandCard.ShelfFormat, true);
                    var back = DropOf(fromShelf, card);
                    card.RaiseEvent(back);
                    var afterShelf = adds;

                    var brought = DropOf(new DataObject(DataFormats.FileDrop, new[] { file }), card);
                    card.RaiseEvent(brought);
                    return (back.Effects, afterShelf, brought.Effects, adds - afterShelf);
                }
                finally
                {
                    card.Close();
                }
            });

            Assert.Equal(DragDropEffects.None, shelfEffect); // Copy told the shelf the file went somewhere, and it dropped it.
            Assert.Equal(0, shelfAdds);
            Assert.NotEqual(DragDropEffects.None, otherEffect); // A file brought from elsewhere is still taken.
            Assert.Equal(1, otherAdds);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

[Collection(nameof(ShellCollection))]
public sealed class PlayerInFrontTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Player UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task ASongHiddenBecauseItsPageIsInFront_ComesBackWhenThePageLeaves()
    {
        IslandHost? host = null;
        var front = ("spotify", "Spotify Premium");
        var (hiddenLine, refreshingWhileHidden) = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            host = new IslandHost { UseSystemMedia = false, Foreground = () => front };
            host.Apply(new IslandOptions(true, Config.IslandMusicMode.WhilePlaying, false));
            host.SetTracksForTests([new MediaTrack("Song", "Artist", true, null, "Spotify.exe")]);
            return (host.State()["line"]?.GetValue<string>(), host.Refreshing);
        });

        try
        {
            await _ui.InvokeAsync(() => front = ("notepad", "Untitled - Notepad")); // The user leaves the player.
            await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken); // Three refreshes' worth.
            var backLine = await _ui.InvokeAsync(() => host!.State()["line"]?.GetValue<string>());

            Assert.Null(hiddenLine); // Its page is in front: NotiLand says nothing of it.
            Assert.True(refreshingWhileHidden); // Used to stay stopped, so the song never came back.
            Assert.Equal("Song — Artist", backLine);
        }
        finally
        {
            await _ui.InvokeAsync(() => host?.Dispose());
        }
    }
}

[Collection(nameof(ShellCollection))]
public sealed class IslandHotZoneTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Hot zone UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task ClosingTheStrip_WhileThePointerRestsOnIt_StopsTheDwell()
    {
        // A dwell under way when the top edge is turned off must not open the folder list afterwards.
        var running = await _ui.InvokeAsync(() =>
        {
            TestApplication.Ensure();
            var zone = new IslandHotZone();
            zone.Place();
            zone.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            var dwell = (System.Windows.Threading.DispatcherTimer)typeof(IslandHotZone)
                .GetField("_dwell", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(zone)!;
            var started = dwell.IsEnabled;
            zone.Close();
            return (started, dwell.IsEnabled);
        });

        Assert.True(running.started);
        Assert.False(running.Item2);
    }
}
