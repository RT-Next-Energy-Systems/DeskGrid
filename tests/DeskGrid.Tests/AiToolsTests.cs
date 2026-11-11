using System.IO;
using DeskGrid.Island;
using Xunit;

namespace DeskGrid.Tests;

public class AiToolsTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeskGridAi_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Detect_FindsToolsByTheirFolders_AndSaysWhichReportUsage()
    {
        Directory.CreateDirectory(Path.Combine(_root, "h", ".claude"));
        Directory.CreateDirectory(Path.Combine(_root, "h", ".codex"));
        Directory.CreateDirectory(Path.Combine(_root, "r", "Antigravity"));
        var tools = AiTools.Detect(Path.Combine(_root, "h"), Path.Combine(_root, "r"), Path.Combine(_root, "l"));
        Assert.Equal(["Claude Code", "Codex", "Antigravity"], tools.Select(t => t.Name));
        Assert.Equal([true, true, false], tools.Select(t => t.ReportsUsage));
    }

    [Fact]
    public void Detect_NothingInstalled_IsEmpty() =>
        Assert.Empty(AiTools.Detect(Path.Combine(_root, "h"), Path.Combine(_root, "r"), Path.Combine(_root, "l")));

    [Fact]
    public void CodexParseLine_ReadsTheTwoWindows()
    {
        var line = """{"timestamp":"2027-01-15T08:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":1800003600},"secondary":{"used_percent":40,"window_minutes":10080,"resets_at":1800400000}}}}""";
        var plan = CodexLimits.ParseLine(line, Now);
        Assert.NotNull(plan);
        Assert.Equal(12.5, plan.FivePercent);
        Assert.Equal(40, plan.WeekPercent);
    }

    [Fact]
    public void CodexParseLine_AnEndedWindow_OrNoLimits_IsNothing()
    {
        Assert.Null(CodexLimits.ParseLine("""{"payload":{"rate_limits":{"primary":{"used_percent":90,"resets_at":1799999000}}}}""", Now));
        Assert.Null(CodexLimits.ParseLine("""{"payload":{"type":"message"}}""", Now));
    }

    [Fact]
    public void CodexParseLine_TimesOutOfRange_AreNothing_NotAnException()
    {
        // A broken log used to throw out of ParseLine and fail the whole usage reading, Claude Code's included.
        Assert.Null(CodexLimits.ParseLine("""{"payload":{"rate_limits":{"primary":{"used_percent":5,"resets_at":99999999999999999}}}}""", Now));
        Assert.Null(CodexLimits.ParseLine("""{"timestamp":"2027-01-15T08:00:00Z","payload":{"rate_limits":{"primary":{"used_percent":5,"resets_in_seconds":9223372036854775807}}}}""", Now));
        Assert.Equal(5, CodexLimits.ParseLine("""{"payload":{"rate_limits":{"primary":{"used_percent":5,"resets_at":1800003600.5}}}}""", Now)!.FivePercent);
    }

    [Fact]
    public void CodexRead_TakesTheLastEventOfTheNewestLog()
    {
        var day = Path.Combine(_root, ".codex", "sessions", "2027", "01", "15");
        Directory.CreateDirectory(day);
        File.WriteAllLines(Path.Combine(day, "rollout-a.jsonl"),
        [
            """{"payload":{"rate_limits":{"primary":{"used_percent":5,"resets_at":1800003600}}}}""",
            """{"payload":{"rate_limits":{"primary":{"used_percent":9,"resets_at":1800003600}}}}""",
        ]);
        Assert.Equal(9, CodexLimits.Read(_root, Now)!.FivePercent);
    }
}
