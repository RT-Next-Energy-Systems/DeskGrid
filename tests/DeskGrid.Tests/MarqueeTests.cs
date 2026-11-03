using DeskGrid.Desktop;
using DeskGrid.Widgets;

namespace DeskGrid.Tests;

public sealed class MarqueeTests
{
    [Theory]
    [InlineData(10, 20, 50, 60, 10, 20, 51, 61)] // Dragged down and right.
    [InlineData(50, 60, 10, 20, 10, 20, 51, 61)] // Dragged up and left: the same band.
    [InlineData(30, 30, 30, 30, 30, 30, 31, 31)] // No movement: the pixel itself.
    public void Between_NormalisesTheCorners(int ax, int ay, int bx, int by, int left, int top, int right, int bottom) =>
        Assert.Equal(new ScreenRect(left, top, right, bottom), ScreenRect.Between(new ScreenPoint(ax, ay), new ScreenPoint(bx, by)));

    [Fact]
    public void Intersects_NeedsSharedArea()
    {
        var rect = new ScreenRect(0, 0, 100, 100);

        Assert.True(rect.Intersects(new ScreenRect(99, 99, 200, 200)));
        Assert.True(rect.Intersects(new ScreenRect(-50, -50, 1, 1)));
        Assert.False(rect.Intersects(new ScreenRect(100, 0, 200, 100))); // Touching edges only.
        Assert.False(rect.Intersects(new ScreenRect(0, 100, 100, 200)));
    }

    [Fact]
    public void IconArea_LeavesOutTheMarginOfTheCell()
    {
        // 150 x 200 cell: 15% of the width and 10% of the height are the margin on each side.
        Assert.Equal(new ScreenRect(122, 20, 228, 180), WidgetGrid.IconArea(new ScreenRect(100, 0, 250, 200)));
    }

    [Fact]
    public void ABandThroughOnlyTheMarginOfACell_SelectsNothing()
    {
        var cell = new ScreenRect(0, 0, 150, 200);
        var edgeBand = ScreenRect.Between(new ScreenPoint(-50, 0), new ScreenPoint(10, 200));

        Assert.False(edgeBand.Intersects(WidgetGrid.IconArea(cell)));
        Assert.True(ScreenRect.Between(new ScreenPoint(-50, 0), new ScreenPoint(60, 200)).Intersects(WidgetGrid.IconArea(cell)));
    }
}
