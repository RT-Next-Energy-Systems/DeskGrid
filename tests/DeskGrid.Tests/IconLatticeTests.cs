using DeskGrid.Desktop;
using DeskGrid.Widgets;

namespace DeskGrid.Tests;

public sealed class IconLatticeTests
{
    private static ScreenRect Cell(int left, int top) => new(left, top, left + 100, top + 200);

    [Fact]
    public void IconsOnOneLattice_GiveThatLattice()
    {
        var (anchor, off) = IconLattice.Find([Cell(10, 30), Cell(10, 230), Cell(910, 30)], 100, 200);

        Assert.Equal(new ScreenPoint(10, 30), anchor);
        Assert.Equal(0, off);
    }

    // The first icon may be off the lattice for a moment (being dragged, just created, Explorer arranging): it must not decide.
    [Fact]
    public void AFirstIconOffTheLattice_DoesNotShiftIt()
    {
        var (anchor, off) = IconLattice.Find([Cell(47, 71), Cell(10, 30), Cell(10, 230), Cell(910, 30)], 100, 200);

        Assert.Equal(10, anchor!.Value.X % 100);
        Assert.Equal(30, anchor.Value.Y % 200);
        Assert.Equal(1, off);
    }

    [Fact]
    public void OneOffIconDoesNotMoveTheEdgeCells()
    {
        // The widgets can still stand against both edges of a 1000 px wide screen, as the icons themselves do.
        var cells = new[] { Cell(47, 71), Cell(0, 0), Cell(0, 200), Cell(900, 0) };
        var (anchor, _) = IconLattice.Find(cells, 100, 200);
        var grid = new WidgetGrid(new ScreenRect(0, 0, 1000, 800), 100, 200, anchor);

        var free = Enumerable.Range(0, 10).Select(c => grid.NearestFree(new ScreenPoint(c * 100, 0), new HashSet<ScreenPoint>())).ToList();
        Assert.Equal(0, free.Min(p => p.X));
        Assert.Equal(900, free.Max(p => p.X));
    }

    [Fact]
    public void AnIconAPixelOff_StillCountsAsOnTheLattice()
    {
        var (anchor, off) = IconLattice.Find([Cell(10, 30), Cell(11, 30), Cell(10, 231)], 100, 200);

        Assert.NotNull(anchor);
        Assert.Equal(0, off);
    }

    [Fact]
    public void ScatteredIcons_HaveNoLattice()
    {
        var (anchor, off) = IconLattice.Find([Cell(10, 30), Cell(40, 70), Cell(70, 110), Cell(25, 150)], 100, 200);

        Assert.Null(anchor);
        Assert.Equal(4, off);
    }

    [Fact]
    public void NoIcons_HaveNoLattice()
    {
        Assert.Equal((null, 0), IconLattice.Find([], 100, 200));
    }

    [Fact]
    public void NegativeCoordinates_AreOnTheSameLatticeAsPositiveOnes()
    {
        // A monitor left of the primary one: its icons have negative screen x.
        var (anchor, off) = IconLattice.Find([Cell(-90, 30), Cell(10, 30), Cell(-90, 230)], 100, 200);

        Assert.NotNull(anchor);
        Assert.Equal(0, off);
    }
}
