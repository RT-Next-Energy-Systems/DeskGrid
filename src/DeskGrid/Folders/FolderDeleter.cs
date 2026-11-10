using System.IO;
using DeskGrid.Config;
using DeskGrid.Diagnostics;
using DeskGrid.Resources;

namespace DeskGrid.Folders;

/// <summary>
/// Deletes a widget folder: with <see cref="DeleteMode.MoveToDesktop"/> its items are put on the desktop
/// first, then the folder itself goes to the Recycle Bin. If something can't be moved, the folder stays.
/// </summary>
internal static class FolderDeleter
{
    // ERROR_NOT_SAME_DEVICE, as an HRESULT: the same drive letter, but another volume mounted in a folder.
    private const int NotSameDevice = unchecked((int)0x80070011);

    // Skips hidden and system entries (desktop.ini), like the scanner does.
    private static readonly EnumerationOptions VisibleEntries = new();

    // Everything, for copying a folder whole.
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0 };

    /// <summary>The entries of <paramref name="folder"/> the user sees: files and subfolders, not hidden or system ones.</summary>
    public static List<string> Entries(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(folder, "*", VisibleEntries)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Cannot read {folder}", exception);
            return [];
        }
    }

    /// <summary>Deletes <paramref name="folder"/> as <paramref name="mode"/> says.</summary>
    /// <param name="recycle">Sends a path to the Recycle Bin; returns <see langword="false"/> if that was called off.</param>
    /// <returns><see langword="null"/> when the folder is gone, else why not.</returns>
    public static string? Delete(string folder, DeleteMode mode, string desktop, Func<string, bool> recycle)
    {
        if (mode == DeleteMode.MoveToDesktop && Entries(folder).Count > 0)
        {
            if (!Directory.Exists(desktop))
            {
                return Strings.DeleteNoDesktop;
            }

            if (MoveEntries(folder, desktop) is { Count: > 0 } stuck)
            {
                return string.Format(Strings.DeleteMoveFailed, string.Join(", ", stuck.Take(3).Select(Path.GetFileName)));
            }
        }

        try
        {
            return recycle(folder) ? null : Strings.DeleteStopped;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Deleting \"{folder}\" failed", exception);
            return string.Format(Strings.DeleteFailed, exception.Message);
        }
    }

    // Returns the entries that could not be moved.
    private static List<string> MoveEntries(string folder, string destination)
    {
        var stuck = new List<string>();
        foreach (var entry in Entries(folder))
        {
            try
            {
                var target = ItemImporter.UniquePath(destination, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    MoveDirectory(entry, target);
                }
                else
                {
                    File.Move(entry, target);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Moving \"{entry}\" to the desktop failed", exception);
                stuck.Add(entry);
            }
        }

        return stuck;
    }

    // Directory.Move can't go to another drive (the root folder on D:, the desktop on C:), where File.Move just copies:
    // there the folder is copied, then the original deleted.
    internal static void MoveDirectory(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException exception) when (!SameRoot(source, target) || exception.HResult == NotSameDevice)
        {
            CopyThenDelete(source, target);
        }
    }

    private static bool SameRoot(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Moves a folder the slow way, with everything in it (hidden files too). If the copy fails, the original stays.</summary>
    internal static void CopyThenDelete(string source, string target)
    {
        try
        {
            CopyDirectory(source, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                DeleteDirectoryRecursively(target);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Removing the partial copy \"{target}\" failed", cleanup);
            }

            throw;
        }

        DeleteDirectoryRecursively(source);
    }

    private static void DeleteDirectoryRecursively(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", AllEntries))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(path, "*", AllEntries))
        {
            DeleteDirectoryRecursively(directory);
        }

        File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(path, recursive: false);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", AllEntries))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var folder in Directory.EnumerateDirectories(source, "*", AllEntries))
        {
            CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
    }
}
