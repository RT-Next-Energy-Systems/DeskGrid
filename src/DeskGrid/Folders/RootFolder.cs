using System.IO;
using DeskGrid.Resources;

namespace DeskGrid.Folders;

/// <summary>Rules for the folder whose subfolders become widgets.</summary>
internal static class RootFolder
{
    /// <summary>
    /// Creates <paramref name="path"/> if needed. Returns <see langword="null"/> if it is usable as the
    /// root folder, otherwise a user-facing reason why not.
    /// </summary>
    public static string? Prepare(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return Strings.RootFolderNotAbsolute;
        }

        try
        {
            if (IsOnDesktop(path))
            {
                return Strings.RootFolderOnDesktop;
            }

            Directory.CreateDirectory(path);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return exception.Message;
        }
    }

    // Explorer already shows these folders' contents as desktop icons.
    internal static bool IsOnDesktop(string path) =>
        FolderPaths.Desktops.Any(desktop => FolderPaths.IsSameOrInside(path, desktop));
}
