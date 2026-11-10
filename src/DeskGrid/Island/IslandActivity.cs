using System.Windows.Media;

namespace DeskGrid.Island;

/// <summary>
/// How a line looks beyond its text: a symbol before it, an accent color (for the symbol and the bar), a bar under it
/// that is <see cref="Progress"/> full (0 to 1), and what a click on it does (<see cref="Action"/>: <c>folder:Name</c>,
/// an http(s) address, or a path).
/// </summary>
internal sealed record IslandLook(string? Glyph = null, string? Accent = null, double? Progress = null, string? Action = null)
{
    // Segoe MDL2 Assets. A name from this list, or any single character, is taken as the glyph.
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["check"] = "",
        ["warning"] = "",
        ["error"] = "",
        ["info"] = "",
        ["download"] = "",
        ["folder"] = "",
        ["mic"] = "",
        ["camera"] = "",
        ["bluetooth"] = "",
        ["timer"] = "",
        ["battery"] = "",
        ["system"] = "",
        ["usage"] = "",
    };

    /// <summary>The names that can be given as an icon.</summary>
    public static IEnumerable<string> GlyphNames => Glyphs.Keys;

    /// <summary>The character to draw for <paramref name="name"/>, or <see langword="null"/> if it is neither a known name nor one character.</summary>
    public static string? ResolveGlyph(string? name) =>
        name is null ? null : Glyphs.TryGetValue(name, out var glyph) ? glyph : name.Length == 1 ? name : null;

    /// <summary>The color for <paramref name="text"/> (<c>#RRGGBB</c> or a color name), or <see langword="null"/>.</summary>
    public static Color? ResolveColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return ColorConverter.ConvertFromString(text.Trim()) is Color color ? Color.FromRgb(color.R, color.G, color.B) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>One thing the island shows for a while: a line of text; a song also has its cover and the music buttons.</summary>
internal sealed record IslandActivity(
    string Text, TimeSpan Duration, ImageSource? Cover = null, bool IsMusic = false, string Source = "", IslandLook? Look = null)
{
    /// <summary>How long a line stays when the sender names no time.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    /// <summary>Longer text is not a message for a pill; senders are told so instead of the text being cut.</summary>
    public const int MaxTextLength = 200;
}

/// <summary>
/// A line whose text changes while it is up (a timer, the state of the system): a new text of the same <see cref="Kind"/>
/// replaces the old one in place instead of showing it again.
/// </summary>
internal sealed record LiveLine(string Kind, string Text, IslandLook? Look = null);
