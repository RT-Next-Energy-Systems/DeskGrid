using System.IO;

namespace DeskGrid.Tests;

/// <summary>
/// The language policy for documents (AGENTS.md): every version-controlled Markdown file exists in English
/// (<c>NAME.md</c>) and Traditional Chinese (<c>NAME.zh-TW.md</c>), with the same structure.
/// </summary>
public sealed class MarkdownPairsTests
{
    private static readonly string Root = FindRoot();

    // Every pair of version-controlled documents; add new ones here.
    private static readonly string[] Documents =
    [
        "README", "CHANGELOG", "CLAUDE", "AGENTS", "context/decisions", "context/project-summary",
    ];

    public static TheoryData<string> Names => [.. Documents];

    [Theory]
    [MemberData(nameof(Names))]
    public void BothLanguagesExist_WithTheSameHeadings(string name)
    {
        var english = File.ReadAllLines(Path.Combine(Root, name + ".md"));
        var chinese = File.ReadAllLines(Path.Combine(Root, name + ".zh-TW.md"));

        Assert.Equal(Levels(english), Levels(chinese));
    }

    [Fact]
    public void NoOtherMarkdownFileIsMissingItsTwin()
    {
        var tracked = Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("src/") && !path.StartsWith("tests/") && !path.StartsWith("artifacts/") && !path.Contains("/obj/"))
            .Where(path => !IsLocalNote(path))
            .Select(path => path.Replace(".zh-TW.md", "").Replace(".md", ""))
            .Distinct();

        Assert.Empty(tracked.Except(Documents));
    }

    // Heading levels in order: a section added to one language and not the other changes the list. Code blocks are skipped.
    private static List<int> Levels(string[] lines)
    {
        var levels = new List<int>();
        var inCode = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
            }
            else if (!inCode && line.StartsWith('#'))
            {
                levels.Add(line.TakeWhile(c => c == '#').Count());
            }
        }

        return levels;
    }

    // The author's own working notes: not in version control (.gitignore), so not covered by the policy.
    private static bool IsLocalNote(string path) =>
        path is "context/current-task.md" or "context/progress.md" or "context/todo.md" or "context/session-log.md";

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeskGrid.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("DeskGrid.slnx not found above " + AppContext.BaseDirectory);
    }
}
