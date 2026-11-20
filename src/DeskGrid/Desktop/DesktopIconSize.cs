using System.IO;
using Microsoft.Win32;

namespace DeskGrid.Desktop;

/// <summary>The size of the desktop's icon pictures that the user chose (Ctrl + mouse wheel on the desktop), which Explorer keeps in the registry.</summary>
internal static class DesktopIconSize
{
    private const string BagKey = @"Software\Microsoft\Windows\Shell\Bags\1\Desktop";
    private const int Smallest = 16;
    private const int Largest = 256;

    /// <summary>The size in device-independent units (Explorer scales it with the display), or <see langword="null"/> when it isn't set.</summary>
    public static int? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(BagKey);
            return key?.GetValue("IconSize") is int size && size >= Smallest && size <= Largest ? size : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
