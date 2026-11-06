using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using DeskGrid.Diagnostics;
using DeskGrid.Resources;

namespace DeskGrid.Updates;

/// <summary>
/// Downloads a release's exe, checks it against the release's checksum and swaps it in for the running one
/// (a helper script waits for this process to exit, since a running exe can't be overwritten).
/// Only the published single-file exe can do that; a development build opens the release page instead.
/// </summary>
internal static class UpdateInstaller
{
    private const int BufferSize = 81920;

    /// <summary>Whether the running program is the published single-file exe, which an update can replace.</summary>
    public static bool CanReplaceSelf =>
        Environment.ProcessPath is not null && !File.Exists(Path.Combine(AppContext.BaseDirectory, "DeskGrid.dll"));

    private static string WorkFolder { get; } = Path.Combine(Path.GetTempPath(), "DeskGrid-update");

    /// <summary>
    /// Downloads <paramref name="release"/>'s exe to a temporary folder and verifies it.
    /// Returns its path, or why that was not possible.
    /// </summary>
    public static async Task<(string? Path, string? Error)> DownloadAsync(
        ReleaseInfo release, IProgress<UpdateProgress> progress, CancellationToken cancellation = default)
    {
        if (release.ExeUrl is null || release.Sha256 is null)
        {
            return (null, Strings.UpdateNoFile);
        }

        if (!IsWritable(Path.GetDirectoryName(Environment.ProcessPath)!))
        {
            return (null, Strings.UpdateFolderNotWritable);
        }

        var file = Path.Combine(WorkFolder, $"DeskGrid-{release.Version.ToString(3)}.exe");
        try
        {
            using var client = UpdateChecker.CreateClient(Timeout.InfiniteTimeSpan);
            Directory.CreateDirectory(WorkFolder);
            using (var response = await client.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync(cancellation);
                await using var target = File.Create(file);
                var buffer = new byte[BufferSize];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    progress.Report(new UpdateProgress(UpdateStage.Downloading, done, total));
                }
            }

            progress.Report(new UpdateProgress(UpdateStage.Verifying));
            if (!await HasHashAsync(file, release.Sha256, cancellation))
            {
                // Closed by now: an open file can't be deleted.
                File.Delete(file);
                return (null, Strings.UpdateChecksumMismatch);
            }

            return (file, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Log.Error("Downloading the update failed", exception);
            DeleteQuietly(file); // Half a download is of no use.
            return (null, exception.Message);
        }
    }

    /// <summary>
    /// Starts the helper that puts <paramref name="newExe"/> in place of the running exe once this process has
    /// exited, then starts it. The caller must shut the app down right after. Returns why it couldn't start, or <see langword="null"/>.
    /// </summary>
    public static string? StartReplacement(string newExe)
    {
        try
        {
            var script = Path.Combine(WorkFolder, "apply-update.ps1");
            WriteScript(script, Environment.ProcessId, newExe, Environment.ProcessPath!);
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script },
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Starting the update failed", exception);
            return exception.Message;
        }
    }

    // Windows PowerShell reads a script without a byte order mark in the system's ANSI code page, which would
    // garble a path with non-ASCII characters (e.g. a Chinese user name), so the script is written as UTF-8 with one.
    internal static void WriteScript(string scriptPath, int processId, string newExe, string targetExe) =>
        File.WriteAllText(scriptPath, $$"""
            Wait-Process -Id {{processId}} -ErrorAction SilentlyContinue
            for ($i = 0; $i -lt 40; $i++) {
                try { Move-Item -LiteralPath {{Quote(newExe)}} -Destination {{Quote(targetExe)}} -Force -ErrorAction Stop; break }
                catch { Start-Sleep -Milliseconds 500 }
            }
            Start-Process -FilePath {{Quote(targetExe)}}
            """, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    /// <summary>Whether the SHA-256 of <paramref name="file"/> is <paramref name="sha256"/> (lower case hex). The file is closed again when this returns.</summary>
    internal static async Task<bool> HasHashAsync(string file, string sha256, CancellationToken cancellation = default)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)) == sha256;
    }

    private static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Overwritten by the next download.
        }
    }

    // A PowerShell single-quoted string.
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static bool IsWritable(string folder)
    {
        try
        {
            using var probe = File.Create(Path.Combine(folder, $".deskgrid-write-test-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
