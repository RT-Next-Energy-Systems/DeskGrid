using DeskGrid.Desktop;

namespace DeskGrid.Panel;

/// <summary>Which side of its widget a panel opens on.</summary>
internal enum PanelSide
{
    Below,
    Above,
    Left,
    Right,
}

/// <summary>Where a panel opens relative to the widget it belongs to.</summary>
internal static class PanelPlacement
{
    /// <summary>
    /// Centered below <paramref name="anchor"/> if that fits the work area, else above, else to the left,
    /// else to the right; then shifted along the other axis to stay inside the work area.
    /// </summary>
    public static ScreenPoint Place(ScreenRect anchor, int width, int height, ScreenRect workArea, int gap) =>
        Place(anchor, width, height, workArea, gap, Side(anchor, width, height, workArea, gap));

    /// <summary>The first side of below, above, left and right where a panel of this size fits; below if none.</summary>
    public static PanelSide Side(ScreenRect anchor, int width, int height, ScreenRect workArea, int gap)
    {
        anchor = Within(anchor, workArea);
        return anchor.Bottom + gap + height <= workArea.Bottom ? PanelSide.Below
            : anchor.Top - gap - height >= workArea.Top ? PanelSide.Above
            : anchor.Left - gap - width >= workArea.Left ? PanelSide.Left
            : anchor.Right + gap + width <= workArea.Right ? PanelSide.Right
            : PanelSide.Below;
    }

    /// <summary>
    /// <paramref name="anchor"/> moved the least that puts it inside <paramref name="workArea"/>. A panel asked for from
    /// the taskbar is anchored at the cursor, which is on the taskbar, outside the work area: measured from there, the
    /// panel would float above the taskbar by as far as the cursor was from its edge. Measured from the edge, it sits
    /// the same small gap above it wherever the button was clicked.
    /// </summary>
    internal static ScreenRect Within(ScreenRect anchor, ScreenRect workArea)
    {
        var dx = anchor.Left < workArea.Left ? workArea.Left - anchor.Left
            : anchor.Right > workArea.Right ? Math.Max(workArea.Right - anchor.Right, workArea.Left - anchor.Left) : 0;
        var dy = anchor.Top < workArea.Top ? workArea.Top - anchor.Top
            : anchor.Bottom > workArea.Bottom ? Math.Max(workArea.Bottom - anchor.Bottom, workArea.Top - anchor.Top) : 0;
        return new ScreenRect(anchor.Left + dx, anchor.Top + dy, anchor.Right + dx, anchor.Bottom + dy);
    }

    /// <summary>
    /// Centered on <paramref name="side"/> of <paramref name="anchor"/>, then shifted to stay inside the work area
    /// (over the anchor, if it doesn't fit there).
    /// </summary>
    public static ScreenPoint Place(ScreenRect anchor, int width, int height, ScreenRect workArea, int gap, PanelSide side)
    {
        anchor = Within(anchor, workArea);
        var centeredX = (anchor.Left + anchor.Right - width) / 2;
        var centeredY = (anchor.Top + anchor.Bottom - height) / 2;
        ScreenPoint chosen = side switch
        {
            PanelSide.Above => new(centeredX, anchor.Top - gap - height),
            PanelSide.Left => new(anchor.Left - gap - width, centeredY),
            PanelSide.Right => new(anchor.Right + gap, centeredY),
            _ => new(centeredX, anchor.Bottom + gap),
        };

        return new ScreenPoint(
            Math.Clamp(chosen.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - width)),
            Math.Clamp(chosen.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height)));
    }
}
