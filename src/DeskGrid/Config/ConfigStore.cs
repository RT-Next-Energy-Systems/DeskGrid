using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskGrid.Diagnostics;

namespace DeskGrid.Config;

/// <summary>Owns the loaded <see cref="AppConfig"/> and writes it back to disk.</summary>
internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const int ReadAttempts = 4;

    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(250);

    private ConfigStore(string filePath, AppConfig config)
    {
        FilePath = filePath;
        Config = config;
    }

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskGrid", "config.json");

    public string FilePath { get; }

    public AppConfig Config { get; }

    /// <summary>
    /// Loads the config, or defaults if the file doesn't exist. An unreadable file is moved aside rather
    /// than overwritten, so hand edits are never silently lost.
    /// </summary>
    public static ConfigStore Open(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ConfigStore(filePath, new AppConfig());
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                return new ConfigStore(filePath, JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions) ?? new AppConfig());
            }
            catch (FileNotFoundException)
            {
                return new ConfigStore(filePath, new AppConfig());
            }
            catch (JsonException exception)
            {
                var backupPath = $"{filePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bad";
                try
                {
                    File.Move(filePath, backupPath);
                    Log.Error($"Invalid config moved to {backupPath}; using defaults", exception);
                }
                catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
                {
                    // Locked or read-only: the defaults still have to start the app (the next save replaces the file).
                    Log.Error($"Invalid config could not be moved aside ({moveFailure.Message}); using defaults", exception);
                }

                return new ConfigStore(filePath, new AppConfig());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < ReadAttempts)
            {
                // Briefly locked by a virus scanner or sync client, e.g. at sign-in. Defaults would overwrite the real settings.
                Log.Error($"Reading {filePath} failed (attempt {attempt}); retrying", exception);
                Thread.Sleep(ReadRetryDelay);
            }
        }
    }

    /// <summary>
    /// Writes the config. A file that can't be written right now (locked by a sync client or virus scanner,
    /// disk full) is logged, not thrown: the next save writes everything again.
    /// </summary>
    public void Save()
    {
        var tempPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Write-then-rename: a crash mid-write never leaves a truncated config behind.
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Config, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Saving {FilePath} failed", exception);
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Leftover temp file; overwritten by the next save.
            }
        }
    }
}
