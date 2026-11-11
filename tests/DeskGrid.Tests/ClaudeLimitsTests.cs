using System.IO;
using DeskGrid.Island;
using Xunit;

namespace DeskGrid.Tests;

public class ClaudeLimitsTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private readonly string _home = Path.Combine(Path.GetTempPath(), "DeskGridLimits_" + Guid.NewGuid().ToString("N"));

    public ClaudeLimitsTests() => Directory.CreateDirectory(Path.Combine(_home, ".claude"));

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void Parse_ReadsBothWindows()
    {
        var plan = ClaudeLimits.Parse("""{"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1800003600},"seven_day":{"used_percentage":41,"resets_at":1800400000}}}""", Now);
        Assert.NotNull(plan);
        Assert.Equal(23.5, plan.FivePercent);
        Assert.Equal(Now.AddHours(1), plan.FiveResets);
        Assert.Equal(41, plan.WeekPercent);
    }

    [Fact]
    public void Parse_AnEndedFiveHourWindow_OrNoLimits_IsNothing()
    {
        Assert.Null(ClaudeLimits.Parse("""{"rate_limits":{"five_hour":{"used_percentage":90,"resets_at":1799999000}}}""", Now));
        Assert.Null(ClaudeLimits.Parse("""{"model":"x"}""", Now));
        Assert.Null(ClaudeLimits.Parse("not json", Now));
    }

    [Fact]
    public void Percent_PrefersClaudesOwnNumber()
    {
        var plan = new PlanUsage(37, Now.AddHours(2), null, null);
        var reading = new UsageReading(50_000_000, Now.AddHours(2), 10_000_000, plan);
        Assert.Equal(37, UsageRules.Percent(reading, new UsageOptions(0, 5, 80)));
    }

    [Fact]
    public void ConnectAndDisconnect_KeepTheOldStatusLine_AndTheOtherSettings()
    {
        var settings = Path.Combine(_home, ".claude", "settings.json");
        File.WriteAllText(settings, """{"model":"opus","statusLine":{"type":"command","command":"echo hi"}}""");

        ClaudeStatusLine.Connect(_home);
        Assert.True(ClaudeStatusLine.IsConnected(_home));
        Assert.Equal("echo hi", File.ReadAllText(Path.Combine(_home, ".claude", "deskgrid-statusline-original.txt")));
        Assert.Contains("opus", File.ReadAllText(settings));
        Assert.True(File.Exists(settings + ".deskgrid-backup"));

        ClaudeStatusLine.Connect(_home); // Again: the old command is not replaced by DeskGrid's own.
        Assert.Equal("echo hi", File.ReadAllText(Path.Combine(_home, ".claude", "deskgrid-statusline-original.txt")));

        ClaudeStatusLine.Disconnect(_home);
        Assert.False(ClaudeStatusLine.IsConnected(_home));
        var text = File.ReadAllText(settings);
        Assert.Contains("echo hi", text);
        Assert.Contains("opus", text);
        Assert.False(File.Exists(Path.Combine(_home, ".claude", "deskgrid-statusline.ps1")));
    }

    [Fact]
    public void Parse_AFractionalOrOutOfRangeReset_DoesNotThrow()
    {
        Assert.Equal(Now.AddHours(1), ClaudeLimits.Parse("""{"rate_limits":{"five_hour":{"used_percentage":10,"resets_at":1800003600.7}}}""", Now)!.FiveResets);
        Assert.Null(ClaudeLimits.Parse("""{"rate_limits":{"five_hour":{"used_percentage":10,"resets_at":1e300}}}""", Now));
    }

    [Fact]
    public void ConnectAndDisconnect_KeepTheStatusLinesOwnSettings()
    {
        var settings = Path.Combine(_home, ".claude", "settings.json");
        File.WriteAllText(settings, """{"statusLine":{"type":"command","command":"echo hi","padding":2}}""");

        ClaudeStatusLine.Connect(_home);
        Assert.Contains("\"padding\": 2", File.ReadAllText(settings));

        ClaudeStatusLine.Disconnect(_home);
        var text = File.ReadAllText(settings);
        Assert.Contains("\"padding\": 2", text);
        Assert.Contains("echo hi", text);
    }

    [Fact]
    public void ACommandThatIsNotText_IsNotConnected_AndConnectingReplacesIt()
    {
        var settings = Path.Combine(_home, ".claude", "settings.json");
        File.WriteAllText(settings, """{"statusLine":{"type":"command","command":42}}""");

        Assert.False(ClaudeStatusLine.IsConnected(_home)); // Used to throw InvalidOperationException.
        ClaudeStatusLine.Connect(_home);
        Assert.True(ClaudeStatusLine.IsConnected(_home));
        Assert.False(File.Exists(Path.Combine(_home, ".claude", "deskgrid-statusline-original.txt")));
    }

    [Fact]
    public void Disconnect_WithoutClaudesFolder_DoesNothing()
    {
        var empty = Path.Combine(_home, "nobody");
        ClaudeStatusLine.Disconnect(empty); // Used to throw DirectoryNotFoundException.
        Assert.False(Directory.Exists(empty));
    }
}
