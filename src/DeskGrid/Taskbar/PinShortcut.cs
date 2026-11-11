using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskGrid.Diagnostics;
using DeskGrid.Shell;

namespace DeskGrid.Taskbar;

/// <summary>
/// Creates the shortcuts that, once pinned to the taskbar, open a widget folder's panel or the list of all
/// folders. Windows lets only the user pin, so the shortcut is created in a folder of ours and shown to them.
/// </summary>
internal static class PinShortcut
{
    private const int IconSize = 256;
    private const double TileCornerRadius = 57;

    // Each shortcut is an app of its own to the taskbar; otherwise pins of the same program merge into one.
    private const string AppIdPrefix = "DeskGrid.Pin.";

    // Not the "DeskGrid.Pin.All" of the shortcut up to 0.2.2: the taskbar treats a shortcut with the ID of a pinned
    // one as already pinned, so the renamed shortcut couldn't be pinned while (or after, its entry lingering hidden)
    // the old one was.
    private const string AllFoldersAppId = AppIdPrefix + "AllFolders";

    // Frozen: an unfrozen static brush belongs to the first thread that used it, and the tile may be drawn on another.
    private static readonly Brush TileBrush = (Brush)new SolidColorBrush(Color.FromArgb(0xB0, 0x40, 0x40, 0x40)).GetAsFrozen();

    public static string FolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskGrid", "Pins");

    // A folder of its own, so no widget folder's shortcut can have the same name.
    private static string AllFoldersPath { get; } = Path.Combine(FolderPath, "All");

    /// <summary>
    /// Writes <paramref name="folderName"/>.lnk (and its icon, drawn from <paramref name="preview"/>) and returns its path.
    /// Call on the UI thread.
    /// </summary>
    public static string Create(string folderName, BitmapSource preview)
    {
        Directory.CreateDirectory(FolderPath);
        var iconPath = Path.Combine(FolderPath, folderName + ".ico");
        var shortcutPath = Path.Combine(FolderPath, folderName + ".lnk");

        File.WriteAllBytes(iconPath, ToIcon(preview));
        ShortcutFile.Create(shortcutPath, Environment.ProcessPath!, OpenRequest.Arguments(folderName), iconPath, AppId(folderName));
        return shortcutPath;
    }

    /// <summary>
    /// Deletes what <see cref="Create"/> wrote for <paramref name="folderName"/>, once the folder is gone. A button
    /// the user already pinned is a copy of its own and stays.
    /// </summary>
    public static void Remove(string folderName)
    {
        foreach (var extension in new[] { ".lnk", ".ico" })
        {
            try
            {
                File.Delete(Path.Combine(FolderPath, folderName + extension));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Removing the taskbar shortcut file of \"{folderName}\" failed", exception);
            }
        }
    }

    /// <summary>
    /// Writes the shortcut to the list of all folders, named <paramref name="displayName"/> and showing DeskGrid's own
    /// icon, and returns its path. Call on the UI thread.
    /// </summary>
    public static string CreateForAllFolders(string displayName)
    {
        Directory.CreateDirectory(AllFoldersPath);
        var shortcutPath = Path.Combine(AllFoldersPath, displayName + ".lnk");

        // Drop shortcuts left under an earlier name (up to 0.2.2 it had a localized name), so only this one is shown.
        foreach (var stale in Directory.EnumerateFiles(AllFoldersPath, "*.lnk"))
        {
            if (!string.Equals(stale, shortcutPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(stale);
            }
        }

        var program = Environment.ProcessPath!;
        ShortcutFile.Create(shortcutPath, program, OpenRequest.AllFoldersArguments, program, AllFoldersAppId);
        return shortcutPath;
    }

    // At most 128 characters and no spaces, so the folder name goes in as a hash.
    internal static string AppId(string folderName) =>
        AppIdPrefix + "Folder." + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folderName.ToUpperInvariant())), 0, 8);

    // The preview on the same kind of tile the widget has, as a single-image (PNG) .ico.
    private static byte[] ToIcon(BitmapSource preview)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(TileBrush, null, new Rect(0, 0, IconSize, IconSize), TileCornerRadius, TileCornerRadius);
            context.DrawImage(preview, new Rect(0, 0, IconSize, IconSize));
        }

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        using var icon = new MemoryStream();
        using (var writer = new BinaryWriter(icon, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);      // reserved
            writer.Write((ushort)1);      // type: icon
            writer.Write((ushort)1);      // image count
            writer.Write((byte)0);        // width: 0 means 256
            writer.Write((byte)0);        // height: 0 means 256
            writer.Write((byte)0);        // palette size
            writer.Write((byte)0);        // reserved
            writer.Write((ushort)1);      // color planes
            writer.Write((ushort)32);     // bits per pixel
            writer.Write((uint)png.Length);
            writer.Write((uint)(6 + 16)); // image offset: after the header and its one entry
            writer.Write(png.ToArray());
        }

        return icon.ToArray();
    }
}
