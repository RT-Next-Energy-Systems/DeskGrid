using System.Diagnostics;
using Windows.Win32;

namespace DeskGrid.Island;

/// <summary>
/// When the island stays out of the way, beyond a full-screen app: quiet hours, programs that hide it while they are in
/// front, programs it shows over even when they are full screen, and which screen it appears on.
/// </summary>
internal sealed record IslandRules(
    string? QuietFrom = null, string? QuietTo = null,
    IReadOnlyList<string>? HideApps = null, IReadOnlyList<string>? ShowApps = null, bool FollowPointer = false)
{
    /// <summary>"chrome, zoom.exe; Teams" as a list of program names (without ".exe"), lower case.</summary>
    public static IReadOnlyList<string> SplitApps(string? text) =>
        [.. (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .Where(name => name.Length > 0)
            .Select(name => name.ToLowerInvariant())
            .Distinct()];

    /// <summary>Whether <paramref name="now"/> lies in the quiet hours <paramref name="from"/> to <paramref name="to"/> (they may span midnight).</summary>
    public static bool InQuietHours(TimeOnly now, TimeOnly? from, TimeOnly? to)
    {
        if (from is not { } start || to is not { } end || start == end)
        {
            return false;
        }

        return start < end ? now >= start && now < end : now >= start || now < end;
    }

    /// <summary>Whether the quiet hours of these rules are on at <paramref name="now"/>.</summary>
    public bool Quiet(DateTime now) =>
        InQuietHours(TimeOnly.FromDateTime(now), Parse(QuietFrom), Parse(QuietTo));

    /// <summary>"HH:mm" as a time, or <see langword="null"/>.</summary>
    public static TimeOnly? Parse(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "H:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time) ? time : null;

    /// <summary>The program that has the foreground, without ".exe", lower case; empty when it cannot be told.</summary>
    public static string ForegroundApp()
    {
        try
        {
            var window = PInvoke.GetForegroundWindow();
            if (window.IsNull || PInvoke.GetWindowThreadProcessId(window, out var pid) == 0)
            {
                return "";
            }

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.ToLowerInvariant();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ""; // The program ended meanwhile, or is not ours to look at.
        }
    }
}
