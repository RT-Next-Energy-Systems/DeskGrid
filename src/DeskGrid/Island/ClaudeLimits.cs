using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskGrid.Island;

/// <summary>
/// What Claude Code itself reports about the plan's limits: how much of the 5-hour window (and the weekly one) is used and when
/// each resets. This is the number Claude shows in its own usage page; it cannot be worked out from token counts.
/// </summary>
internal sealed record PlanUsage(double FivePercent, DateTimeOffset FiveResets, double? WeekPercent, DateTimeOffset? WeekResets);

/// <summary>
/// Reads the limits Claude Code hands to its status line (<c>rate_limits.five_hour / seven_day</c>: <c>used_percentage</c> 0–100 and
/// <c>resets_at</c> in Unix seconds), which a small status-line script saved to <see cref="FilePath"/>.
/// </summary>
internal static class ClaudeLimits
{
    public static string FilePath(string home) => Path.Combine(home, ".claude", "deskgrid-limits.json");

    /// <summary>The saved limits that still apply at <paramref name="now"/>, or <see langword="null"/> (no file, unreadable, or the 5-hour window has ended).</summary>
    public static PlanUsage? Read(string file, DateTimeOffset now)
    {
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file, Encoding.UTF8), now) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static PlanUsage? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Claude Code drops a window once it has ended: with no 5-hour window nothing is counted now.
            if (Window(limits, "five_hour", now) is not { } five)
            {
                return null;
            }

            var week = Window(limits, "seven_day", now);
            return new PlanUsage(five.Percent, five.Resets, week?.Percent, week?.Resets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (double Percent, DateTimeOffset Resets)? Window(JsonElement limits, string name, DateTimeOffset now)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percentage", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used)
            || !window.TryGetProperty("resets_at", out var resets) || UnixTime(resets) is not { } end)
        {
            return null;
        }

        return end > now ? (Math.Clamp(used, 0, 100), end) : null;
    }

    /// <summary>
    /// A time given as Unix seconds (a whole or a fractional number), or <see langword="null"/> when it is not a number or lies
    /// outside what a date can hold: one broken value must not throw and take the rest of the reading with it.
    /// </summary>
    internal static DateTimeOffset? UnixTime(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var seconds) || !double.IsFinite(seconds))
        {
            return null;
        }

        return seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            : null;
    }
}

/// <summary>
/// Connects DeskGrid to Claude Code's status line: Claude Code runs the status line command often and feeds it the limits. The
/// command is replaced by a small PowerShell script that saves the limits for DeskGrid and then runs the user's old command
/// (kept in a file next to it), so the status line looks as before. Disconnecting puts the old command back.
/// </summary>
internal static class ClaudeStatusLine
{
    private const string ScriptName = "deskgrid-statusline.ps1";
    private const string OriginalName = "deskgrid-statusline-original.txt";
    private const string LimitsName = "deskgrid-limits.json";

    private const string Script = """
        # Written by DeskGrid: saves the plan limits Claude Code reports, then runs the status line command you had before.
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [Console]::InputEncoding = $utf8
        [Console]::OutputEncoding = $utf8
        $OutputEncoding = $utf8
        $dir = Split-Path -Parent $MyInvocation.MyCommand.Path
        $in = [Console]::In.ReadToEnd()
        try {
            $data = $in | ConvertFrom-Json
            if ($data.rate_limits) {
                $text = @{ rate_limits = $data.rate_limits } | ConvertTo-Json -Depth 6 -Compress
                $target = Join-Path $dir 'deskgrid-limits.json'
                [IO.File]::WriteAllText($target + '.tmp', $text, $utf8)
                Move-Item -Force ($target + '.tmp') $target
            }
        } catch { }
        $originalFile = Join-Path $dir 'deskgrid-statusline-original.txt'
        if (Test-Path $originalFile) {
            $original = (Get-Content $originalFile -Raw -Encoding UTF8).Trim()
            if ($original) { $in | cmd.exe /c $original }
        }
        """;

    private static string SettingsFile(string home) => Path.Combine(home, ".claude", "settings.json");

    /// <summary>Whether Claude Code's status line runs DeskGrid's script.</summary>
    public static bool IsConnected(string home)
    {
        try
        {
            var file = SettingsFile(home);
            return File.Exists(file) && CommandOf(File.ReadAllText(file)) is { } command && command.Contains(ScriptName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Switches the status line to DeskGrid's script, keeping the old command; the old settings file is copied to <c>settings.json.deskgrid-backup</c> first.</summary>
    public static void Connect(string home)
    {
        var folder = Path.Combine(home, ".claude");
        Directory.CreateDirectory(folder);
        var file = SettingsFile(home);
        var root = File.Exists(file)
            ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new InvalidDataException("settings.json is not a JSON object")
            : new JsonObject();
        if (File.Exists(file))
        {
            File.Copy(file, file + ".deskgrid-backup", overwrite: true);
        }

        // The user's own status line keeps its other settings (padding and the like); only its command is swapped.
        var line = StatusLineOf(root);
        var old = TextOf(line["command"]);
        if (old is not null && !old.Contains(ScriptName, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(Path.Combine(folder, OriginalName), old, new UTF8Encoding(false));
        }

        File.WriteAllText(Path.Combine(folder, ScriptName), Script, new UTF8Encoding(true)); // A BOM: Windows PowerShell reads the file as UTF-8.
        line["type"] = "command";
        line["command"] = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(folder, ScriptName)}\"";
        WriteJson(file, root);
    }

    /// <summary>Puts the old status line command back (or removes the status line if there was none) and deletes DeskGrid's files.</summary>
    public static void Disconnect(string home)
    {
        var folder = Path.Combine(home, ".claude");
        var file = SettingsFile(home);
        if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject root)
        {
            var original = Path.Combine(folder, OriginalName);
            if (File.Exists(original) && File.ReadAllText(original).Trim() is { Length: > 0 } command)
            {
                var line = StatusLineOf(root);
                line["type"] = "command";
                line["command"] = command;
            }
            else
            {
                root.Remove("statusLine");
            }

            WriteJson(file, root);
        }

        if (!Directory.Exists(folder))
        {
            return; // Nothing of DeskGrid's is left to delete (File.Delete throws for a missing folder).
        }

        foreach (var name in new[] { ScriptName, OriginalName, LimitsName })
        {
            File.Delete(Path.Combine(folder, name));
        }
    }

    private static string? CommandOf(string json) =>
        (JsonNode.Parse(json) as JsonObject)?["statusLine"] is JsonObject line ? TextOf(line["command"]) : null;

    // The settings' status line object, made (and attached) when there is none or it is not an object.
    private static JsonObject StatusLineOf(JsonObject root)
    {
        if (root["statusLine"] is JsonObject line)
        {
            return line;
        }

        line = [];
        root["statusLine"] = line;
        return line;
    }

    // A JSON string's text; null for anything else (a hand-edited settings file may hold a number or an object there).
    private static string? TextOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static void WriteJson(string file, JsonObject root)
    {
        var temporary = file + ".tmp";
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(temporary, root.ToJsonString(options), new UTF8Encoding(false));
        File.Move(temporary, file, overwrite: true);
    }
}
