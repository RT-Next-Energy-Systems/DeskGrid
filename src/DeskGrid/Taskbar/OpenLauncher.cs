using System.IO;
using System.Reflection;
using DeskGrid.Diagnostics;

namespace DeskGrid.Taskbar;

/// <summary>
/// The small program a pinned taskbar button starts (<c>src/DeskGrid.Open</c>), kept in DeskGrid's own folder. DeskGrid.exe, the
/// program a button used to start, is a large single-file exe that unpacks itself on every start, which is most of the wait
/// between a click and the panel; this one only passes the request on to the DeskGrid that is running.
/// </summary>
internal static class OpenLauncher
{
    private const string ResourceName = "DeskGridOpen.exe";

    // Read by the launcher from its own folder: where DeskGrid.exe is, for when no DeskGrid is running.
    private const string TargetFileName = "DeskGridOpen.target";

    private static readonly Lock Gate = new();

    public static string FolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskGrid");

    /// <summary>
    /// Puts the launcher into <paramref name="folder"/> and tells it where DeskGrid.exe is (<paramref name="program"/>), unless
    /// both are as they should be, and returns the launcher's path, or <see langword="null"/> if it can't be had (the shortcut
    /// then starts DeskGrid.exe itself, only more slowly). Cheap when nothing changed; safe from any thread.
    /// </summary>
    public static string? Install(string? folder = null, string? program = null)
    {
        folder ??= FolderPath;
        program ??= Environment.ProcessPath!;
        var launcher = Path.Combine(folder, ResourceName);
        lock (Gate)
        {
            try
            {
                using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (resource is null)
                {
                    Log.Info("The taskbar launcher is not part of this build.");
                    return null;
                }

                Directory.CreateDirectory(folder);
                var bytes = new byte[resource.Length];
                resource.ReadExactly(bytes);
                if (!File.Exists(launcher) || !File.ReadAllBytes(launcher).AsSpan().SequenceEqual(bytes))
                {
                    WriteReplacing(launcher, bytes);
                }

                var targetFile = Path.Combine(folder, TargetFileName);
                if (!File.Exists(targetFile) || File.ReadAllText(targetFile) != program)
                {
                    File.WriteAllText(targetFile, program);
                }

                return launcher;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error("Putting the taskbar launcher in place failed", exception);

                // One written earlier still works: only a newer one could not replace it.
                return File.Exists(launcher) ? launcher : null;
            }
        }
    }

    // A launcher that is running (a click just now) can't be overwritten but can be renamed, so the new one goes in beside it.
    private static void WriteReplacing(string path, byte[] bytes)
    {
        var temporary = path + ".new";
        File.WriteAllBytes(temporary, bytes);
        var old = path + ".old";
        try
        {
            File.Delete(old);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still running from an earlier replacement; the move below says if that matters.
        }

        if (File.Exists(path))
        {
            File.Move(path, old);
        }

        File.Move(temporary, path);
        try
        {
            File.Delete(old);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Deleted by the next replacement.
        }
    }
}
