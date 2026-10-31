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
    public static PanelSide Side(ScreenRect anchor, int width, int height, ScreenRect workArea, int gap) =>
        anchor.Bottom + gap + height <= workArea.Bottom ? PanelSide.Below
        : anchor.Top - gap - height >= workArea.Top ? PanelSide.Above
        : anchor.Left - gap - width >= workArea.Left ? PanelSide.Left
        : anchor.Right + gap + width <= workArea.Right ? PanelSide.Right
        : PanelSide.Below;

    /// <summary>
    /// Centered on <paramref name="side"/> of <paramref name="anchor"/>, then shifted to stay inside the work area
    /// (over the anchor, if it doesn't fit there).
    /// </summary>
    public static ScreenPoint Place(ScreenRect anchor, int width, int height, ScreenRect workArea, int gap, PanelSide side)
    {
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
