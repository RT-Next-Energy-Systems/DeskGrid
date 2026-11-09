using DeskGrid.Settings;

namespace DeskGrid.Tests;

public sealed class ChangeLogTests
{
    [Fact]
    public void Parse_ReadsReleasesSectionsAndItems()
    {
        const string markdown = """
            # Changelog

            ## 1.2.0

            ### Added

            - First thing.
            - Second thing.

            ### Fixed

            - A bug.

            ## 1.1.0

            ### Added

            - Older thing.
            """;

        var lines = ChangeLog.Parse(markdown);

        Assert.Equal(
            [
                (ChangeLogKind.Release, "1.2.0"),
                (ChangeLogKind.Section, "Added"),
                (ChangeLogKind.Item, "First thing."),
                (ChangeLogKind.Item, "Second thing."),
                (ChangeLogKind.Section, "Fixed"),
                (ChangeLogKind.Item, "A bug."),
                (ChangeLogKind.Release, "1.1.0"),
                (ChangeLogKind.Section, "Added"),
                (ChangeLogKind.Item, "Older thing."),
            ],
            lines.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public void Parse_JoinsAnItemThatWrapsOverSeveralLines_AndDropsMarkdownMarks()
    {
        var lines = ChangeLog.Parse("## 1.0.0\r\n### Added\r\n- Starts with `--autostart` and\r\n  waits for **the desktop**.\r\n- Next.\r\n");

        Assert.Equal("Starts with --autostart and waits for the desktop.", lines[2].Text);
        Assert.Equal("Next.", lines[3].Text);
    }

    [Theory]
    [InlineData("（Ctrl+A 仍只選取\n  檔案總管的圖示。）", "（Ctrl+A 仍只選取檔案總管的圖示。）")]
    [InlineData("取得它的 logo。\n  部分套件", "取得它的 logo。部分套件")]
    [InlineData("現在\n  widget 會停在", "現在 widget 會停在")]
    [InlineData("改用\n  `config.json`", "改用 config.json")]
    public void Parse_JoinsWrappedChineseWithoutASpace_ButKeepsTheOneBesideEnglish(string item, string expected)
    {
        var lines = ChangeLog.Parse("## 1.0.0\n- " + item + "\n");

        Assert.Equal(expected, lines[1].Text);
    }

    [Fact]
    public void Parse_IgnoresTheTitleAndTextOutsideItems()
    {
        var lines = ChangeLog.Parse("# Changelog\n\nSome intro.\n\n  indented, but not under an item\n");

        Assert.Empty(lines);
    }

    [Theory]
    [InlineData("CHANGELOG.md")]
    [InlineData("CHANGELOG.zh-TW.md")]
    public void ReadEmbedded_IsTheRepositorysChangeLog_WithTheCurrentVersionOrAnUnreleasedSectionOnTop(string resource)
    {
        var markdown = ChangeLog.ReadEmbedded(resource);

        Assert.NotNull(markdown);
        var first = ChangeLog.Parse(markdown).First(l => l.Kind == ChangeLogKind.Release);
        var running = typeof(App).Assembly.GetName().Version!;
        Assert.True(
            first.Text == "Unreleased" || first.Text.StartsWith($"{running.Major}.{running.Minor}.{running.Build}", StringComparison.Ordinal),
            $"The newest entry of {resource} is \"{first.Text}\" but this build is {running.ToString(3)}.");
    }

    private const string ThreeVersions = """
        ## 1.2.0
        ### Added
        - New.
        ## 1.1.0
        ### Fixed
        - Fix.
        ## 1.0.0
        ### Added
        - Old.
        """;

    [Theory]
    [InlineData("1.0.0", "1.2.0", new[] { "1.2.0", "1.1.0" })]
    [InlineData("1.1.0", "1.2.0", new[] { "1.2.0" })]
    [InlineData("1.2.0", "1.2.0", new string[0])]
    [InlineData("1.0.0", "1.1.0", new[] { "1.1.0" })] // A version the running build doesn't have yet is not shown.
    [InlineData("2.0.0", "1.2.0", new string[0])] // A downgrade has nothing new.
    [InlineData(null, "1.1.0", new[] { "1.1.0" })] // Nothing recorded yet: only the running version's own entry.
    public void Since_PicksTheVersionsAfterTheOneSeenUpToTheRunningOne(string? seen, string current, string[] expected)
    {
        var picked = ChangeLog.Since(ChangeLog.Parse(ThreeVersions), seen is null ? null : Version.Parse(seen), Version.Parse(current));

        Assert.Equal(expected, picked.Where(l => l.Kind == ChangeLogKind.Release).Select(l => l.Text));
        Assert.All(picked, line => Assert.NotEqual("Old.", line.Text)); // Never entries of an excluded version.
    }

    [Fact]
    public void Since_KeepsEachVersionsSectionsAndItems()
    {
        var picked = ChangeLog.Since(ChangeLog.Parse(ThreeVersions), Version.Parse("1.1.0"), Version.Parse("1.2.0"));

        Assert.Equal(
            [(ChangeLogKind.Release, "1.2.0"), (ChangeLogKind.Section, "Added"), (ChangeLogKind.Item, "New.")],
            picked.Select(l => (l.Kind, l.Text)));
    }

    // Policy (AGENTS.md): a version that adds something says how to use it, in the language of each log.
    [Theory]
    [InlineData("CHANGELOG.md", "How to use")]
    [InlineData("CHANGELOG.zh-TW.md", "使用方法")]
    public void TheNewestVersionThatAddsSomething_HasAHowToUseSection(string resource, string howToUse)
    {
        var lines = ChangeLog.Parse(ChangeLog.ReadEmbedded(resource)!);
        var newest = lines.Skip(1).TakeWhile(l => l.Kind != ChangeLogKind.Release).Prepend(lines[0]).ToList();

        if (newest.Any(l => l is { Kind: ChangeLogKind.Section } && l.Text is "Added" or "新增"))
        {
            Assert.Contains(newest, l => l.Kind == ChangeLogKind.Section && l.Text == howToUse);
        }
    }

    // The two languages are one log: same versions, same number of entries under each.
    [Fact]
    public void ChineseChangeLog_HasTheSameVersionsAndEntriesAsTheEnglishOne()
    {
        static List<(string Release, int Items)> Outline(string markdown)
        {
            var releases = new List<(string Release, int Items)>();
            foreach (var line in ChangeLog.Parse(markdown))
            {
                if (line.Kind == ChangeLogKind.Release)
                {
                    releases.Add((line.Text, 0));
                }
                else if (line.Kind == ChangeLogKind.Item)
                {
                    releases[^1] = (releases[^1].Release, releases[^1].Items + 1);
                }
            }

            return releases;
        }

        Assert.Equal(Outline(ChangeLog.ReadEmbedded("CHANGELOG.md")!), Outline(ChangeLog.ReadEmbedded("CHANGELOG.zh-TW.md")!));
    }
}
