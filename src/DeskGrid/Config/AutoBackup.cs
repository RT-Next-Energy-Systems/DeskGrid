using System.Globalization;
using System.IO;

namespace DeskGrid.Config;

/// <summary>One automatic daily backup file in the backup folder.</summary>
internal sealed record BackupEntry(DateOnly Date, string Label, string FilePath);

/// <summary>
/// Writes a daily settings backup at or after the chosen hour when enabled, keeps at most the configured number of backups
/// (up to <see cref="AppConfig.MaxAutoBackupDays"/>), and lists the saved backups for one-click restore.
/// </summary>
internal static class AutoBackup
{
    private const string Prefix = "DeskGrid_";

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskGrid", "Backups");

    /// <summary>The file name for <paramref name="date"/>'s automatic backup: <c>DeskGrid_2026_11_10.json</c>.</summary>
    public static string FileNameFor(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{date:yyyy_MM_dd}{ConfigPorter.Extension}");

    /// <summary>Parses the date from an automatic backup file name, or <see langword="null"/> if it is not one.</summary>
    public static DateOnly? TryParseDate(string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(ConfigPorter.Extension, StringComparison.OrdinalIgnoreCase)
            || name.Length != Prefix.Length + 10 + ConfigPorter.Extension.Length)
        {
            return null;
        }

        var slice = name.AsSpan(Prefix.Length, 10);
        return DateOnly.TryParseExact(slice, "yyyy_MM_dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Lists all automatic backups in <paramref name="directory"/>, newest first.</summary>
    public static IReadOnlyList<BackupEntry> List(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<BackupEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, $"{Prefix}*{ConfigPorter.Extension}"))
        {
            if (TryParseDate(path) is { } date)
            {
                entries.Add(new BackupEntry(date, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), path));
            }
        }

        return [.. entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.FilePath, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Writes today's backup to <paramref name="directory"/> if automatic backup is enabled, <paramref name="now"/> is at or
    /// past the configured hour, and today's file does not exist yet; then prunes older backups beyond the retention limit.
    /// Returns the path written, or <see langword="null"/> when no new backup was needed.
    /// </summary>
    public static string? RunIfDue(AppConfig config, DateTime now, string directory)
    {
        if (!config.AutoBackupEnabled)
        {
            return null;
        }

        var hour = Math.Clamp(config.AutoBackupHour, 0, 23);
        var keepDays = Math.Clamp(config.AutoBackupDays, AppConfig.MinAutoBackupDays, AppConfig.MaxAutoBackupDays);
        if (now.Hour < hour)
        {
            Prune(directory, keepDays);
            return null;
        }

        var today = DateOnly.FromDateTime(now);
        var path = Path.Combine(directory, FileNameFor(today));
        if (File.Exists(path))
        {
            Prune(directory, keepDays);
            return null;
        }

        Directory.CreateDirectory(directory);
        ConfigPorter.Export(config, path);
        Prune(directory, keepDays);
        return path;
    }

    /// <summary>Deletes the oldest automatic backups in <paramref name="directory"/> when more than <paramref name="keepDays"/> exist.</summary>
    public static void Prune(string directory, int keepDays)
    {
        keepDays = Math.Clamp(keepDays, AppConfig.MinAutoBackupDays, AppConfig.MaxAutoBackupDays);
        foreach (var old in List(directory).Skip(keepDays))
        {
            File.Delete(old.FilePath);
        }
    }
}
