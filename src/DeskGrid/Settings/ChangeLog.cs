using System.IO;
using DeskGrid.Resources;

namespace DeskGrid.Settings;

internal enum ChangeLogKind
{
    /// <summary>A version heading ("0.3.1").</summary>
    Release,

    /// <summary>A group inside a version ("Added", "Fixed").</summary>
    Section,

    /// <summary>One change.</summary>
    Item,
}

internal sealed record ChangeLogLine(ChangeLogKind Kind, string Text);

/// <summary>
/// The change log (<c>CHANGELOG.md</c> and its Chinese twin, built into the exe) as lines the settings window can lay
/// out. It reads only the subset of Markdown those files use: <c>##</c> and <c>###</c> headings and <c>-</c> items that may wrap.
/// </summary>
internal static class ChangeLog
{
    /// <summary>The change log built into this exe in the interface language, or <see langword="null"/> if the build has none.</summary>
    public static string? ReadEmbedded() => ReadEmbedded(Strings.ChangeLogResource);

    /// <summary>The embedded change log file named <paramref name="resourceName"/>, or <see langword="null"/> if there is none.</summary>
    public static string? ReadEmbedded(string resourceName)
    {
        using var stream = typeof(ChangeLog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<ChangeLogLine> Parse(string markdown)
    {
        var lines = new List<ChangeLogLine>();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                lines.Add(new ChangeLogLine(ChangeLogKind.Section, Plain(line[4..])));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                lines.Add(new ChangeLogLine(ChangeLogKind.Release, Plain(line[3..])));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                lines.Add(new ChangeLogLine(ChangeLogKind.Item, Plain(line[2..])));
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0 && lines is [.., { Kind: ChangeLogKind.Item }])
            {
                // The rest of an item that was wrapped over several lines.
                lines[^1] = lines[^1] with { Text = JoinWrapped(lines[^1].Text, Plain(line)) };
            }
        }

        return lines;
    }

    /// <summary>
    /// The entries of the versions the user has not seen: those newer than <paramref name="seen"/> up to <paramref name="current"/>.
    /// With no <paramref name="seen"/> (settings from before the version was recorded) only <paramref name="current"/>'s own entry.
    /// </summary>
    public static IReadOnlyList<ChangeLogLine> Since(IReadOnlyList<ChangeLogLine> lines, Version? seen, Version current)
    {
        var picked = new List<ChangeLogLine>();
        var inRange = false;
        foreach (var line in lines)
        {
            if (line.Kind == ChangeLogKind.Release)
            {
                inRange = Version.TryParse(line.Text, out var version)
                    && version <= current
                    && (seen is null ? version == current : version > seen);
            }

            if (inRange)
            {
                picked.Add(line);
            }
        }

        return picked;
    }

    // A line break is a space between English words, but nothing between two Chinese characters, which have no spaces
    // between them; next to an English word, Chinese text has a space, as the Chinese change log writes it.
    private static string JoinWrapped(string first, string rest) =>
        first.Length > 0 && rest.Length > 0 && IsWide(first[^1]) && IsWide(rest[0]) ? first + rest : $"{first} {rest}";

    // CJK characters and punctuation, and the full-width forms (，（）).
    private static bool IsWide(char c) => c is >= '\u2E80' and <= '\u9FFF' or >= '\uFF00' and <= '\uFFEF';

    // The window shows plain text: no code or bold marks.
    private static string Plain(string text) => text.Replace("`", "").Replace("**", "").Trim();
}
