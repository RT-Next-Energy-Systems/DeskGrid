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
                lines[^1] = lines[^1] with { Text = $"{lines[^1].Text} {Plain(line)}" };
            }
        }

        return lines;
    }

    // The window shows plain text: no code or bold marks.
    private static string Plain(string text) => text.Replace("`", "").Replace("**", "").Trim();
}
