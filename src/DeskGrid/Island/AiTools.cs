using System.IO;
using System.Text;
using System.Text.Json;

namespace DeskGrid.Island;

/// <summary>An AI tool found on this computer, and whether DeskGrid can read how much of its plan is used.</summary>
internal sealed record AiTool(string Id, string Name, bool ReportsUsage);

/// <summary>One tool's own usage numbers (5-hour style window and, if it has one, the weekly window).</summary>
internal sealed record ToolUsage(AiTool Tool, PlanUsage Plan);

/// <summary>
/// Finds which AI tools this computer has, by their data folders (nothing is run and nothing is sent anywhere), and reads usage
/// where the tool keeps it on disk: Claude Code (through its status line, see <see cref="ClaudeStatusLine"/>) and Codex CLI (its
/// session logs carry the plan's rate limits). The others are listed as found; they keep no usage that can be read here.
/// </summary>
internal static class AiTools
{
    public const string ClaudeId = "claude";
    public const string CodexId = "codex";

    /// <summary>The tools found under these folders: user profile, roaming and local application data.</summary>
    public static IReadOnlyList<AiTool> Detect(string home, string roaming, string local)
    {
        var found = new List<AiTool>();

        void Check(string id, string name, bool reports, params string[] folders)
        {
            if (folders.Any(Directory.Exists))
            {
                found.Add(new AiTool(id, name, reports));
            }
        }

        Check(ClaudeId, "Claude Code", true, Path.Combine(home, ".claude"));
        Check(CodexId, "Codex", true, Path.Combine(home, ".codex"));
        Check("antigravity", "Antigravity", false, Path.Combine(roaming, "Antigravity"), Path.Combine(home, ".gemini", "antigravity"));
        Check("gemini", "Gemini CLI", false, Path.Combine(home, ".gemini", "tmp"), Path.Combine(home, ".gemini", "settings.json"));
        Check("cursor", "Cursor", false, Path.Combine(roaming, "Cursor"));
        Check("copilot", "GitHub Copilot", false, Path.Combine(home, ".config", "github-copilot"));
        return found;
    }

    public static IReadOnlyList<AiTool> Detect() => Detect(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}

/// <summary>
/// Codex CLI writes <c>token_count</c> events into its session logs (<c>~/.codex/sessions/yyyy/mm/dd/rollout-*.jsonl</c>); each
/// carries <c>rate_limits.primary</c> (the 5-hour window) and <c>rate_limits.secondary</c> (the weekly one) with
/// <c>used_percent</c> and <c>resets_at</c> (Unix seconds) or <c>resets_in_seconds</c>. The newest such event wins.
/// </summary>
internal static class CodexLimits
{
    private const int TailBytes = 1 << 20;

    public static PlanUsage? Read(string home, DateTimeOffset now)
    {
        var sessions = Path.Combine(home, ".codex", "sessions");
        if (!Directory.Exists(sessions))
        {
            return null;
        }

        try
        {
            // The newest logs first: the limits are in the last token_count event of the latest session that has one.
            var files = new DirectoryInfo(sessions)
                .EnumerateFiles("*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(5);
            foreach (var file in files)
            {
                if (ParseTail(file.FullName, now) is { } plan)
                {
                    return plan;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logs that are busy or gone: no reading this time.
        }

        return null;
    }

    private static PlanUsage? ParseTail(string path, DateTimeOffset now)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - TailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        if (start > 0)
        {
            reader.ReadLine(); // Cut off at the start: not a whole line.
        }

        PlanUsage? latest = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("\"rate_limits\"", StringComparison.Ordinal) && ParseLine(line, now) is { } plan)
            {
                latest = plan;
            }
        }

        return latest;
    }

    internal static PlanUsage? ParseLine(string line, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var stamp = document.RootElement.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(t.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : now;
            var limits = Find(document.RootElement);
            if (limits is not { } found || Window(found, "primary", stamp, now) is not { } primary)
            {
                return null;
            }

            var week = Window(found, "secondary", stamp, now);
            return new PlanUsage(primary.Percent, primary.Resets, week?.Percent, week?.Resets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The rate_limits object: on the event's payload (or, defensively, anywhere one level down).
    private static JsonElement? Find(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("rate_limits", out var direct) && direct.ValueKind == JsonValueKind.Object)
        {
            return direct;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("rate_limits", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                return nested;
            }
        }

        return null;
    }

    private static (double Percent, DateTimeOffset Resets)? Window(JsonElement limits, string name, DateTimeOffset stamp, DateTimeOffset now)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percent", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used))
        {
            return null;
        }

        DateTimeOffset end;
        if (window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out var seconds))
        {
            end = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        else if (window.TryGetProperty("resets_in_seconds", out var inSeconds) && inSeconds.ValueKind == JsonValueKind.Number && inSeconds.TryGetInt64(out var left))
        {
            end = stamp.AddSeconds(left); // Older logs: the time left when the event was written.
        }
        else
        {
            return null; // A window without a known end cannot be told to have ended.
        }

        return end > now ? (Math.Clamp(used, 0, 100), end) : null;
    }
}
