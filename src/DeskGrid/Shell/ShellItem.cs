using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace DeskGrid.Shell;

/// <summary>Shell operations on a folder item. Call on an STA thread (the UI thread is one).</summary>
internal static class ShellItem
{
    /// <summary>
    /// Opens the item exactly like a double-click in Explorer: ShellExecuteEx with the item's folder as the
    /// working directory (a shortcut's own working directory still wins). Windows shows its own error
    /// dialog if the item can't be opened, e.g. a shortcut whose target is gone.
    /// </summary>
    public static void Open(string path)
    {
        // A web address has no folder to start in.
        var folder = Path.GetDirectoryName(path);
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            ErrorDialog = true,
            WorkingDirectory = Directory.Exists(folder) ? folder : null,
        };

        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            // Also thrown when the user cancels a UAC prompt.
            Log.Info($"Opening {path} failed: {exception.Message}");
        }
    }

    /// <summary>Shows <paramref name="path"/> selected in an Explorer window.</summary>
    /// <remarks>
    /// Through the shell API rather than <c>explorer /select</c>, which can answer a file that was just written
    /// to a new folder with "Location is not available".
    /// </remarks>
    public static unsafe void Reveal(string path)
    {
        PInvoke.SHParseDisplayName(path, null, out var item, 0, out _).ThrowOnFailure();
        try
        {
            PInvoke.SHOpenFolderAndSelectItems(item, 0, null, 0).ThrowOnFailure();
        }
        finally
        {
            PInvoke.ILFree(item);
        }
    }

    /// <summary>
    /// The name of a Shell item given as an absolute identifier list, and whether the item is a file or folder
    /// (<see langword="false"/> for a virtual one, such as an app of the Start menu).
    /// </summary>
    public static unsafe (string Name, bool IsFile) Describe(byte[] idList)
    {
        fixed (byte* pidl = idList)
        {
            PInvoke.SHCreateItemFromIDList(*(ITEMIDLIST*)pidl, out IShellItem item).ThrowOnFailure();
            try
            {
                return (ReadName(item, SIGDN.SIGDN_NORMALDISPLAY) ?? "", ReadName(item, SIGDN.SIGDN_FILESYSPATH) is not null);
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    // The name in the given form, or null if the item has none (a virtual item has no file system path).
    private static unsafe string? ReadName(IShellItem item, SIGDN form)
    {
        try
        {
            item.GetDisplayName(form, out var name);
            try
            {
                return name.ToString();
            }
            finally
            {
                Marshal.FreeCoTaskMem((nint)name.Value);
            }
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or IOException or NotSupportedException)
        {
            // The marshaller turns the HRESULT into whichever of these fits it; E_INVALIDARG, for a form the item doesn't have.
            return null;
        }
    }

    /// <summary>The name Explorer shows (e.g. "Google Chrome" for "Google Chrome.lnk").</summary>
    public static unsafe string GetDisplayName(string path)
    {
        try
        {
            PInvoke.SHCreateItemFromParsingName(path, null, out IShellItem item).ThrowOnFailure();
            try
            {
                item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out var name);
                try
                {
                    return name.ToString();
                }
                finally
                {
                    Marshal.FreeCoTaskMem((nint)name.Value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
