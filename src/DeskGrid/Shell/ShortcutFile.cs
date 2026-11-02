using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace DeskGrid.Shell;

/// <summary>Reads and writes .lnk files. Call on an STA thread (the UI thread is one).</summary>
internal static class ShortcutFile
{
    /// <summary>
    /// The file or folder path <paramref name="shortcutPath"/> points to, or <see langword="null"/> if it has none
    /// (a Store app, a virtual folder) or can't be read.
    /// </summary>
    public static unsafe string? TryGetTarget(string shortcutPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, STGM.STGM_READ);
            Span<char> target = stackalloc char[260];
            fixed (char* buffer = target)
            {
                link.GetPath(buffer, target.Length, null, 0);
            }

            var length = target.IndexOf('\0');
            return length > 0 ? new string(target[..length]) : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// Creates <paramref name="shortcutPath"/>, a shortcut that opens <paramref name="target"/> (a file or folder),
    /// or starts it with <paramref name="arguments"/> if it is a program, showing <paramref name="iconPath"/> if given.
    /// <paramref name="appUserModelId"/> makes the taskbar treat the shortcut as an app of its own, so several
    /// shortcuts to the same program can be pinned side by side.
    /// </summary>
    public static unsafe void Create(
        string shortcutPath, string target, string? arguments = null, string? iconPath = null, string? appUserModelId = null)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            // Start in the target's own folder, as Explorer's "Create shortcut" does.
            fixed (char* targetPath = target, workingDirectory = Path.GetDirectoryName(target) ?? "", file = shortcutPath)
            fixed (char* argumentText = arguments, iconFile = iconPath, appId = appUserModelId)
            {
                link.SetPath(targetPath);
                link.SetWorkingDirectory(workingDirectory);
                if (arguments is not null)
                {
                    link.SetArguments(argumentText);
                }

                if (iconPath is not null)
                {
                    link.SetIconLocation(iconFile, 0);
                }

                if (appUserModelId is not null)
                {
                    // The store copies the value, so pointing it at the pinned string is enough.
                    var value = new PROPVARIANT();
                    value.Anonymous.Anonymous.vt = VARENUM.VT_LPWSTR;
                    value.Anonymous.Anonymous.Anonymous.pwszVal = appId;
                    var properties = (IPropertyStore)link;
                    properties.SetValue(PInvoke.PKEY_AppUserModel_ID, value);
                    properties.Commit();
                }

                ((IPersistFile)link).Save(file, true);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
