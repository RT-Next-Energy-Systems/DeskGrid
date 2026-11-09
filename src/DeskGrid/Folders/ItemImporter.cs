using System.IO;
using System.Runtime.InteropServices;
using DeskGrid.Config;
using DeskGrid.Diagnostics;
using DeskGrid.Shell;

namespace DeskGrid.Folders;

internal enum ImportAction
{
    None,
    Move,
    Copy,
    CreateShortcut,
}

/// <summary>
/// Adds files and folders dropped on a widget to its folder. Whatever lies directly on the desktop (shortcut,
/// file, program or folder) is moved in, so it is not seen in both places; with <see cref="DropMode.Copy"/> the
/// desktop keeps it instead (a shortcut is copied, anything else gets a shortcut). From anywhere else, programs,
/// files and folders get a new shortcut (they are never moved or copied from there) and shortcuts are copied.
/// Items taken out of another widget's panel are moved.
/// </summary>
internal static class ItemImporter
{
    /// <summary>What dropping <paramref name="path"/> on the widget for <paramref name="widgetFolder"/> does.</summary>
    public static ImportAction Plan(string path, string widgetFolder, DropMode mode, IEnumerable<string> desktops, string rootFolder)
    {
        if (FolderPaths.IsDirectlyIn(path, widgetFolder) || FolderPaths.IsSameOrInside(widgetFolder, path))
        {
            // Already in this folder, or the folder (or one containing it) dropped on itself.
            return ImportAction.None;
        }

        var isFolder = Directory.Exists(path);
        if (!isFolder && !File.Exists(path))
        {
            return ImportAction.None;
        }

        if (FolderPaths.IsSameOrInside(path, rootFolder))
        {
            // Dragged out of another widget (see PopupPanel): it moves, whatever it is. Another widget's own folder
            // is only linked: moved, that widget would vanish into this one.
            return isFolder && FolderPaths.IsDirectlyIn(path, rootFolder) ? ImportAction.CreateShortcut : ImportAction.Move;
        }

        var isShortcut = !isFolder && IsShortcut(path);
        if (desktops.Any(desktop => FolderPaths.IsDirectlyIn(path, desktop)))
        {
            // What lies on the desktop moves in, whatever it is, so it is not seen in both places; unless the user
            // chose to keep the desktop as it is.
            return mode == DropMode.Move ? ImportAction.Move
                : isShortcut ? ImportAction.Copy
                : ImportAction.CreateShortcut;
        }

        return isShortcut ? ImportAction.Copy : ImportAction.CreateShortcut;
    }

    /// <summary>Carries out <see cref="Plan"/> for each path. Returns how many items were added.</summary>
    public static int Import(
        IEnumerable<string> paths, string widgetFolder, DropMode mode, IEnumerable<string> desktops, string rootFolder) =>
        ImportTo(paths, widgetFolder, mode, desktops, rootFolder).Count;

    /// <summary>Like <see cref="Import"/>, but returns the path of every item that was added.</summary>
    public static List<string> ImportTo(
        IEnumerable<string> paths, string widgetFolder, DropMode mode, IEnumerable<string> desktops, string rootFolder)
    {
        var added = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var action = Plan(path, widgetFolder, mode, desktops, rootFolder);
                if (action != ImportAction.None)
                {
                    added.Add(Apply(action, path, widgetFolder));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException
                or ArgumentException or NotSupportedException)
            {
                Log.Error($"Adding {path} to {widgetFolder} failed", exception);
            }
        }

        return added;
    }

    /// <summary><paramref name="fileName"/> in <paramref name="folder"/>, numbered "name (2).ext" if already taken.</summary>
    internal static string UniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        for (var number = 2; File.Exists(candidate) || Directory.Exists(candidate); number++)
        {
            candidate = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({number}){Path.GetExtension(fileName)}");
        }

        return candidate;
    }

    // Returns the path of the new item.
    private static string Apply(ImportAction action, string path, string widgetFolder)
    {
        if (action == ImportAction.CreateShortcut)
        {
            var shortcut = UniquePath(widgetFolder, ShortcutName(path));
            ShortcutFile.Create(shortcut, path);
            return shortcut;
        }

        var destination = UniquePath(widgetFolder, Path.GetFileName(path));
        if (action == ImportAction.Copy)
        {
            File.Copy(path, destination);
            return destination;
        }

        var isFolder = Directory.Exists(path);
        try
        {
            if (isFolder)
            {
                FolderDeleter.MoveDirectory(path, destination);
            }
            else
            {
                File.Move(path, destination);
            }
        }
        catch (UnauthorizedAccessException) when (!isFolder)
        {
            // The public desktop is writable only by administrators: leave the original there.
            Log.Info($"No permission to move {path}; copied it instead.");
            File.Copy(path, destination);
        }
        catch (UnauthorizedAccessException)
        {
            // A folder is not copied whole behind the user's back: it stays, with a shortcut to it here.
            Log.Info($"No permission to move {path}; linked it instead.");
            return Apply(ImportAction.CreateShortcut, path, widgetFolder);
        }

        return destination;
    }

    // "notepad.exe" -> "notepad.lnk", "report.pdf" -> "report.pdf.lnk", "D:\Photos" -> "Photos.lnk", "D:\" -> "D.lnk".
    // A root keeps its separator ("D:\"), so it has no file name: the name is made of what is left of the path once the
    // characters a file name can't have are taken out. Never a path itself, or the shortcut would land outside the folder.
    internal static string ShortcutName(string target)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(target);
        var name = string.Equals(Path.GetExtension(trimmed), ".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(trimmed)
            : Path.GetFileName(trimmed);
        if (name.Length == 0)
        {
            name = string.Join(' ', trimmed.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        }

        return name + ".lnk";
    }

    private static bool IsShortcut(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url";
}
