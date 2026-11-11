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
}
