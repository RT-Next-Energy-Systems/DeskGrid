namespace DeskGrid.Island;

/// <summary>
/// How a line that is too long for the pill scrolls: it waits at its start, scrolls to its end at a steady speed, waits
/// there, goes back and starts over. The numbers live here so the window and the time a line stays up agree.
/// </summary>
internal static class Marquee
{
    /// <summary>How long the text rests at its start and at its end.</summary>
    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(1.5);

    /// <summary>The quick trip back to the start after the end has rested.</summary>
    public static readonly TimeSpan Return = TimeSpan.FromMilliseconds(250);

    /// <summary>DIPs per second.</summary>
    public const double Speed = 40;

    // What the pill shows of a line at once (the closed pill's width without its padding), and rough widths of a character.
    private const double Visible = 248;
    private const double NarrowChar = 7.5;
    private const double WideChar = 13.5;

    /// <summary>One run: rest, scroll <paramref name="overflow"/> DIPs, rest, return.</summary>
    public static TimeSpan Cycle(double overflow) => Rest + TimeSpan.FromSeconds(overflow / Speed) + Rest + Return;

    /// <summary>A guess at the width of <paramref name="text"/> in DIPs (East Asian characters are wide).</summary>
    public static double EstimateWidth(string text) => text.Sum(c => c >= 0x2E80 ? WideChar : NarrowChar);

    /// <summary>
    /// How long a line has to stay so that all of it has scrolled by once: at least <paramref name="duration"/>, longer when
    /// the text does not fit the pill.
    /// </summary>
    public static TimeSpan ToRead(string text, TimeSpan duration)
    {
        var overflow = EstimateWidth(text) - Visible;
        return overflow <= 0 ? duration : TimeSpan.FromTicks(Math.Max(duration.Ticks, Cycle(overflow).Ticks));
    }
}
