namespace DeskGrid.Desktop;

/// <summary>
/// Finds the lattice the desktop's icons sit on from where they are now. Most icons are on it, but one may not be
/// for a moment (being dragged, just created, in the middle of Explorer arranging the icons) or for good (the desktop
/// does not align icons to a grid); taking the first icon as the lattice would then shift every cell by whatever
/// distance that icon happens to be off, and the widgets could not reach the screen's left and right edges.
/// </summary>
internal static class IconLattice
{
    // Two icons whose cells differ by no more than this on the lattice's axis are on the same line of it.
    private const int Tolerance = 2;

    /// <summary>
    /// The top-left corner of a cell of the lattice most of the <paramref name="cells"/> are on, and how many of them
    /// are not on it. <see langword="null"/> when there is no icon, or when fewer than half of them agree on one lattice
    /// (the icons are scattered: there is no grid to follow).
    /// </summary>
    public static (ScreenPoint? Anchor, int OffLattice) Find(IReadOnlyList<ScreenRect> cells, int cellWidth, int cellHeight)
    {
        if (cells.Count == 0)
        {
            return (null, 0);
        }

        var x = Dominant(cells.Select(c => c.Left).ToList(), cellWidth);
        var y = Dominant(cells.Select(c => c.Top).ToList(), cellHeight);
        if (x is null || y is null)
        {
            return (null, cells.Count);
        }

        var offLattice = cells.Count(c => !Near(c.Left, x.Value, cellWidth) || !Near(c.Top, y.Value, cellHeight));
        return (new ScreenPoint(x.Value, y.Value), offLattice);
    }

    // The value (one of the given ones) that the most of them are on the same line as; ties go to the earliest.
    private static int? Dominant(IReadOnlyList<int> values, int period)
    {
        var best = 0;
        var chosen = 0;
        foreach (var candidate in values)
        {
            var support = values.Count(v => Near(v, candidate, period));
            if (support > best)
            {
                best = support;
                chosen = candidate;
            }
        }

        return best * 2 >= values.Count ? chosen : null;
    }

    private static bool Near(int a, int b, int period)
    {
        var distance = ((a - b) % period + period) % period;
        return Math.Min(distance, period - distance) <= Tolerance;
    }
}
