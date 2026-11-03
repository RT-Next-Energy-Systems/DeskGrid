using DeskGrid.Desktop;

namespace DeskGrid.Widgets;

/// <summary>
/// The cells widgets snap to: widget-sized, filled down each column, right to left. A cell is identified
/// by its top-left corner.
/// </summary>
/// <remarks>
/// Without an <paramref name="anchor"/> the cells start at the work area's right and top edges (away from
/// the icons Windows arranges from the top-left). With one, the cells lie on the lattice through that
/// point, so they line up with the desktop's own icon grid. Cells that <paramref name="obstacles"/> (the
/// desktop's icons) cover are skipped unless nothing else is left.
/// </remarks>
/// <param name="anchor">The top-left corner of any cell of the lattice to follow.</param>
/// <param name="obstacles">Screen areas widgets should stay clear of.</param>
internal sealed class WidgetGrid(
    ScreenRect workArea, int cellWidth, int cellHeight, ScreenPoint? anchor = null, IReadOnlyList<ScreenRect>? obstacles = null)
{
    /// <summary>Whether the cells follow the desktop's icon lattice (there was an <c>anchor</c>) instead of the work area's edges.</summary>
    public bool FollowsIcons => anchor is not null;

    private int FirstColumnX => anchor is { } a
        ? workArea.Left + Mod(a.X - workArea.Left, cellWidth)
        : workArea.Right - Columns(workArea.Left) * cellWidth;

    private int FirstRowY => anchor is { } a
        ? workArea.Top + Mod(a.Y - workArea.Top, cellHeight)
        : workArea.Top;

    private int Columns(int firstX) => Math.Max(1, (workArea.Right - firstX) / cellWidth);

    private int Rows => Math.Max(1, (workArea.Bottom - FirstRowY) / cellHeight);

    /// <summary>The first cell in fill order that isn't <paramref name="taken"/>.</summary>
    public ScreenPoint FirstFree(IReadOnlySet<ScreenPoint> taken) =>
        Candidates(taken).First();

    /// <summary>The cell closest to <paramref name="position"/> that isn't <paramref name="taken"/>.</summary>
    public ScreenPoint NearestFree(ScreenPoint position, IReadOnlySet<ScreenPoint> taken) =>
        Candidates(taken).MinBy(c => DistanceSquared(c, position));

    // Free cells; else cells not covered by icons; else all cells.
    private List<ScreenPoint> Candidates(IReadOnlySet<ScreenPoint> taken)
    {
        var cells = Cells().ToList();
        var clear = cells.Where(c => !IsCovered(c)).ToList();
        var free = clear.Where(c => !taken.Contains(c)).ToList();
        if (free.Count > 0)
        {
            return free;
        }

        return clear.Count > 0 ? clear : cells;
    }

    /// <summary>
    /// The part of a widget's <paramref name="cell"/> a rubber band has to touch to select it: the picture and the name,
    /// not the margin around them (like a desktop icon, which isn't picked by brushing its cell).
    /// </summary>
    public static ScreenRect IconArea(ScreenRect cell)
    {
        var insetX = cell.Width * 15 / 100;
        var insetY = cell.Height * 10 / 100;
        return new ScreenRect(cell.Left + insetX, cell.Top + insetY, cell.Right - insetX, cell.Bottom - insetY);
    }

    /// <summary>Whether <paramref name="obstacle"/> covers enough of <paramref name="cell"/> to make it unusable.</summary>
    public static bool Covers(ScreenRect obstacle, ScreenRect cell)
    {
        // Brushing a neighbour's edge doesn't count; sharing a quarter of the cell does.
        var overlapWidth = Math.Min(cell.Right, obstacle.Right) - Math.Max(cell.Left, obstacle.Left);
        var overlapHeight = Math.Min(cell.Bottom, obstacle.Bottom) - Math.Max(cell.Top, obstacle.Top);
        return overlapWidth * 4 > cell.Width && overlapHeight * 4 > cell.Height;
    }

    private bool IsCovered(ScreenPoint cell)
    {
        var rect = new ScreenRect(cell.X, cell.Y, cell.X + cellWidth, cell.Y + cellHeight);
        return (obstacles ?? []).Any(obstacle => Covers(obstacle, rect));
    }

    private IEnumerable<ScreenPoint> Cells()
    {
        var firstX = FirstColumnX;
        var firstY = FirstRowY;
        var columns = Columns(firstX);
        var rows = Rows;

        // Columns count from the right edge.
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                yield return new ScreenPoint(firstX + (columns - 1 - column) * cellWidth, firstY + row * cellHeight);
            }
        }
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static long DistanceSquared(ScreenPoint a, ScreenPoint b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
