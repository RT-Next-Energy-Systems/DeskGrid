using DeskGrid.Desktop;
using DeskGrid.Widgets;

namespace DeskGrid.Tests;

public sealed class WidgetGridTests
{
    // 10 columns x 3 rows of 100 x 200 cells.
    private readonly WidgetGrid _grid = new(new ScreenRect(0, 0, 1000, 700), 100, 200);

    [Fact]
    public void FirstFree_StartsAtTopRight() =>
        Assert.Equal(new ScreenPoint(900, 0), _grid.FirstFree(Taken()));

    [Fact]
    public void FirstFree_FillsDownTheColumnThenMovesLeft()
    {
        Assert.Equal(new ScreenPoint(900, 200), _grid.FirstFree(Taken((900, 0))));
        Assert.Equal(new ScreenPoint(800, 0), _grid.FirstFree(Taken((900, 0), (900, 200), (900, 400))));
    }

    [Fact]
    public void FirstFree_AnchorsToTheWorkAreaOfAnyMonitor() =>
        Assert.Equal(new ScreenPoint(-100, 100), new WidgetGrid(new ScreenRect(-1000, 100, 0, 800), 100, 200).FirstFree(Taken()));

    [Fact]
    public void NearestFree_SnapsToTheClosestCell() =>
        Assert.Equal(new ScreenPoint(500, 200), _grid.NearestFree(new ScreenPoint(512, 230), Taken()));

    [Fact]
    public void NearestFree_SkipsTakenCells() =>
        Assert.Equal(new ScreenPoint(600, 200), _grid.NearestFree(new ScreenPoint(512, 230), Taken((500, 200))));

    [Fact]
    public void NearestFree_PullsOutsidePositionsBackIntoTheWorkArea() =>
        Assert.Equal(new ScreenPoint(0, 400), _grid.NearestFree(new ScreenPoint(-500, 5000), Taken()));

    [Fact]
    public void NearestFree_AllTaken_FallsBackToTheClosestCell()
    {
        var all = Taken([.. Enumerable.Range(0, 10).SelectMany(c => new[] { (c * 100, 0), (c * 100, 200), (c * 100, 400) })]);

        Assert.Equal(new ScreenPoint(500, 200), _grid.NearestFree(new ScreenPoint(512, 230), all));
    }

    [Fact]
    public void Cells_ExcludePartialColumns()
    {
        // 1050 wide fits 10 whole columns anchored to the right edge, so the leftmost starts at 50.
        var grid = new WidgetGrid(new ScreenRect(0, 0, 1050, 200), 100, 200);

        Assert.Equal(new ScreenPoint(50, 0), grid.NearestFree(new ScreenPoint(0, 0), Taken()));
    }

    [Fact]
    public void Anchor_AlignsCellsToTheIconLattice()
    {
        // Icons start 7 px right of and 30 px below the work area's corner, so cells do too.
        var grid = new WidgetGrid(new ScreenRect(0, 0, 1000, 700), 100, 200, new ScreenPoint(307, 430));

        Assert.Equal(new ScreenPoint(807, 30), grid.FirstFree(Taken()));
        Assert.Equal(new ScreenPoint(507, 230), grid.NearestFree(new ScreenPoint(512, 250), Taken()));
    }

    [Fact]
    public void FollowsIcons_OnlyWithAnAnchor()
    {
        Assert.False(_grid.FollowsIcons);
        Assert.True(new WidgetGrid(new ScreenRect(0, 0, 1000, 700), 100, 200, new ScreenPoint(307, 430)).FollowsIcons);
    }

    [Fact]
    public void Obstacles_AreSkipped()
    {
        // An icon sits on the top-right cell and half-covers the one below it.
        var grid = new WidgetGrid(new ScreenRect(0, 0, 1000, 700), 100, 200, null, [new ScreenRect(900, 0, 1000, 300)]);

        Assert.Equal(new ScreenPoint(900, 400), grid.FirstFree(Taken()));
        Assert.Equal(new ScreenPoint(800, 0), grid.NearestFree(new ScreenPoint(900, 0), Taken()));
    }

    [Fact]
    public void Obstacles_BarelyTouchingACell_DoNotBlockIt()
    {
        var grid = new WidgetGrid(new ScreenRect(0, 0, 1000, 700), 100, 200, null, [new ScreenRect(900, 0, 1000, 210)]);

        Assert.Equal(new ScreenPoint(900, 200), grid.FirstFree(Taken((900, 0))));
    }

    [Fact]
    public void Obstacles_EverywhereButTakenCells_StillGivesACell()
    {
        var grid = new WidgetGrid(new ScreenRect(0, 0, 200, 200), 100, 200, null, [new ScreenRect(0, 0, 200, 200)]);

        Assert.Equal(new ScreenPoint(100, 0), grid.FirstFree(Taken()));
    }

    [Fact]
    public void Obstacles_WhenTheCellsClearOfIconsAreTaken_ACellUnderAnIconBeatsSharingOneWithAWidget()
    {
        // Two cells: (100, 0) is clear but has a widget, (0, 0) is under an icon and has none.
        var grid = new WidgetGrid(new ScreenRect(0, 0, 200, 200), 100, 200, null, [new ScreenRect(0, 0, 100, 200)]);

        Assert.Equal(new ScreenPoint(0, 0), grid.NearestFree(new ScreenPoint(100, 0), Taken((100, 0))));
        Assert.Equal(new ScreenPoint(0, 0), grid.FirstFree(Taken((100, 0))));
    }

    [Theory]
    [InlineData(0, 0, true)] // Same cell.
    [InlineData(30, 60, true)] // Off the grid, still on most of the cell.
    [InlineData(80, 0, false)] // Only a sliver of the width.
    [InlineData(0, 160, false)] // Only a sliver of the height.
    public void Covers_NeedsAQuarterOfTheCellBothWays(int dx, int dy, bool expected)
    {
        var cell = new ScreenRect(100, 200, 200, 400);

        Assert.Equal(expected, WidgetGrid.Covers(new ScreenRect(100 + dx, 200 + dy, 200 + dx, 400 + dy), cell));
    }

    private static HashSet<ScreenPoint> Taken(params (int X, int Y)[] cells) =>
        [.. cells.Select(c => new ScreenPoint(c.X, c.Y))];
}
