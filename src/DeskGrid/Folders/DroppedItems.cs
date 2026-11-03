using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using DeskGrid.Diagnostics;
using DeskGrid.Shell;

namespace DeskGrid.Folders;

/// <summary>
/// What a drag carries that can be added to a folder: files and folders (the <c>FileDrop</c> format), or, from the
/// Start menu, apps. An app has no file path, only a Shell identifier; it comes as a shortcut made for the occasion in a
/// temporary folder, which goes away with this object. Everything after that treats it like a dropped shortcut.
/// </summary>
internal sealed class DroppedItems : IDisposable
{
    private string? _stagingFolder;

    private DroppedItems(IReadOnlyList<string> paths, string? stagingFolder) =>
        (Paths, _stagingFolder) = (paths, stagingFolder);

    /// <summary>The files, folders and shortcuts to add.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Whether <paramref name="data"/> has anything to add. Cheap enough to ask for every mouse move of a drag.</summary>
    public static bool Offers(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) || OffersApps(data);

    /// <summary>
    /// Whether the drag carries Shell items but no files: the apps of the Start menu. (Files dragged from Explorer carry
    /// the same Shell format next to their paths, and are handled as files.)
    /// </summary>
    public static bool OffersApps(IDataObject data) =>
        !data.GetDataPresent(DataFormats.FileDrop) && data.GetDataPresent(ShellIdList.Format);

    /// <summary>Reads what was dropped; dispose it once the items have been added.</summary>
    public static DroppedItems Read(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            return new DroppedItems(paths, null);
        }

        return OffersApps(data) && data.GetData(ShellIdList.Format) is MemoryStream stream
            ? StageApps(ShellIdList.Parse(stream.ToArray()))
            : new DroppedItems([], null);
    }

    public void Dispose()
    {
        if (_stagingFolder is not { } folder)
        {
            return;
        }

        _stagingFolder = null;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Removing the temporary folder {folder} failed", exception);
        }
    }

    // A shortcut for each item that is no file or folder (those are the ones the Start menu gives), named after the item.
    private static DroppedItems StageApps(IReadOnlyList<byte[]> items)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeskGrid", "drops", Guid.NewGuid().ToString("N"));
        var shortcuts = new List<string>();
        foreach (var item in items)
        {
            try
            {
                var (name, isFile) = ShellItem.Describe(item);
                if (isFile || name.Length == 0)
                {
                    continue;
                }

                Directory.CreateDirectory(folder);
                var shortcut = ItemImporter.UniquePath(folder, FileName(name) + ".lnk");
                ShortcutFile.CreateFromIdList(shortcut, item);
                shortcuts.Add(shortcut);
            }
            catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
            {
                Log.Error("Making a shortcut for a dropped Start menu item failed", exception);
            }
        }

        return new DroppedItems(shortcuts, shortcuts.Count > 0 ? folder : null);
    }

    private static string FileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)).Trim().TrimEnd('.');
    }
}
