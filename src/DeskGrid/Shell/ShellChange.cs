using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace DeskGrid.Shell;

/// <summary>Tells Explorer about file changes we made, for the cases where it misses them.</summary>
internal static class ShellChange
{
    /// <summary>
    /// Says that something in <paramref name="folder"/> was added or removed, so every view of it, the desktop
    /// included, reads it again. Explorer can keep the icon of an item that was moved off the desktop; this removes it.
    /// Does not wait for Explorer, so a busy Explorer can't hold the caller up.
    /// </summary>
    public static unsafe void FolderChanged(string folder)
    {
        fixed (char* path = folder)
        {
            PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_UPDATEDIR, SHCNF_FLAGS.SHCNF_PATHW | SHCNF_FLAGS.SHCNF_FLUSHNOWAIT, path, null);
        }
    }
}
