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
/// Adds files and folders dropped on a widget to its folder. Programs, ordinary files and folders get a
/// new shortcut (a program itself is never moved or copied); shortcuts lying on the desktop are moved or
/// copied per <see cref="DropMode"/>; shortcuts from anywhere else are copied; items taken out of another
/// widget's panel are moved.
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

        if (Directory.Exists(path))
        {
            return ImportAction.CreateShortcut;
        }

        if (!File.Exists(path))
        {
            return ImportAction.None;
        }

        if (FolderPaths.IsSameOrInside(path, rootFolder))
        {
            // Dragged out of another widget (see PopupPanel): it moves, whatever it is.
            return ImportAction.Move;
        }

        if (!IsShortcut(path))
        {
            return ImportAction.CreateShortcut;
        }

        var onDesktop = desktops.Any(desktop => FolderPaths.IsDirectlyIn(path, desktop));
        return onDesktop && mode == DropMode.Move ? ImportAction.Move : ImportAction.Copy;
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

        try
        {
            File.Move(path, destination);
        }
        catch (UnauthorizedAccessException)
        {
            // The public desktop is writable only by administrators: leave the original there.
            Log.Info($"No permission to move {path}; copied it instead.");
            File.Copy(path, destination);
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
