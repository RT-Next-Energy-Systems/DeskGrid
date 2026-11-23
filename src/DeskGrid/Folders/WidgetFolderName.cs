using System.IO;
using System.Text.RegularExpressions;
using DeskGrid.Resources;

namespace DeskGrid.Folders;

/// <summary>Rules for naming a new widget folder.</summary>
internal static partial class WidgetFolderName
{
    /// <summary>"New folder", or "New folder (2)" and so on if taken.</summary>
    public static string Suggest(string rootPath) =>
        Path.GetFileName(ItemImporter.UniquePath(rootPath, Strings.NewFolderDefaultName));

    /// <summary>Returns <see langword="null"/> if <paramref name="name"/> can be created in <paramref name="rootPath"/>, else why not.</summary>
    public static string? Validate(string rootPath, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedName().IsMatch(name))
        {
            return Strings.NewFolderInvalidName;
        }

        var path = Path.Combine(rootPath, name);
        // Too long for Windows (a name of 255, or a path of 259 with room for the items inside): it would fail with the system's own message.
        if (name.Length > 100 || path.Length > 200)
        {
            return Strings.NewFolderInvalidName;
        }

        return Directory.Exists(path) || File.Exists(path) ? Strings.NewFolderExists : null;
    }

    // Device names Windows won't create as folders, with or without an extension.
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedName();
}
