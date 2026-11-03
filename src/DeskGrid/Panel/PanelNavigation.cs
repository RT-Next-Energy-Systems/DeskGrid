using System.Windows.Input;

namespace DeskGrid.Panel;

/// <summary>Moving the keyboard selection around the grid of a panel.</summary>
internal static class PanelNavigation
{
    /// <summary>
    /// The slot an arrow, Home or End key moves to from <paramref name="current"/> (<see langword="null"/> when nothing
    /// is selected yet), or <see langword="null"/> if <paramref name="key"/> is no navigation key or there is nothing
    /// to select. The selection stops at the edges instead of wrapping.
    /// </summary>
    public static int? NextSlot(Key key, int? current, int count, int columns)
    {
        if (count <= 0 || key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))
        {
            return null;
        }

        var last = count - 1;
        if (current is not { } slot)
        {
            return key is Key.Left or Key.Up or Key.End ? last : 0;
        }

        return key switch
        {
            Key.Left => Math.Max(slot - 1, 0),
            Key.Right => Math.Min(slot + 1, last),
            Key.Up => slot - columns >= 0 ? slot - columns : slot,
            // A short last row can be missing the slot straight below: go to its last one instead.
            Key.Down => slot + columns <= last ? slot + columns : slot / columns < last / columns ? last : slot,
            Key.Home => 0,
            _ => last,
        };
    }
}
