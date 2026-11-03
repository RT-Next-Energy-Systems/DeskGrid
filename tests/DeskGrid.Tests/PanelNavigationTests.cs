using System.Windows.Input;
using DeskGrid.Panel;

namespace DeskGrid.Tests;

public sealed class PanelNavigationTests
{
    // Seven items in three columns:  0 1 2 / 3 4 5 / 6
    [Theory]
    [InlineData(Key.Right, 1, 2)]
    [InlineData(Key.Right, 6, 6)] // Stops at the last item.
    [InlineData(Key.Left, 1, 0)]
    [InlineData(Key.Left, 0, 0)] // And at the first.
    [InlineData(Key.Down, 1, 4)]
    [InlineData(Key.Down, 4, 6)] // Nothing below slot 4: the short last row's only item.
    [InlineData(Key.Down, 6, 6)]
    [InlineData(Key.Up, 4, 1)]
    [InlineData(Key.Up, 1, 1)] // Already on the top row.
    [InlineData(Key.Home, 5, 0)]
    [InlineData(Key.End, 2, 6)]
    public void NextSlot_MovesWithinTheGrid(Key key, int from, int expected) =>
        Assert.Equal(expected, PanelNavigation.NextSlot(key, from, count: 7, columns: 3));

    [Theory]
    [InlineData(Key.Down, 0)]
    [InlineData(Key.Right, 0)]
    [InlineData(Key.Home, 0)]
    [InlineData(Key.Up, 6)]
    [InlineData(Key.Left, 6)]
    [InlineData(Key.End, 6)]
    public void NextSlot_StartsAtAnEdge_WhenNothingIsSelected(Key key, int expected) =>
        Assert.Equal(expected, PanelNavigation.NextSlot(key, current: null, count: 7, columns: 3));

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Tab)]
    public void NextSlot_IgnoresOtherKeys(Key key) =>
        Assert.Null(PanelNavigation.NextSlot(key, current: 2, count: 7, columns: 3));

    // Seven items in three columns of 76 x 96: 0 1 2 / 3 4 5 / 6
    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(80, 10, 1)]
    [InlineData(227, 95, 2)]
    [InlineData(80, 100, 4)]
    [InlineData(10, 200, 6)]
    [InlineData(80, 200, null)] // The empty cell after the last item.
    [InlineData(228, 10, null)] // Right of the grid.
    [InlineData(-1, 10, null)]
    [InlineData(10, -1, null)]
    public void SlotAtPoint_FindsTheItemUnderAPoint(double x, double y, int? expected) =>
        Assert.Equal(expected, PanelNavigation.SlotAtPoint(x, y, columns: 3, count: 7, cellWidth: 76, cellHeight: 96));

    [Fact]
    public void NextSlot_IsNull_ForAnEmptyPanel() =>
        Assert.Null(PanelNavigation.NextSlot(Key.Down, current: null, count: 0, columns: 3));
}
