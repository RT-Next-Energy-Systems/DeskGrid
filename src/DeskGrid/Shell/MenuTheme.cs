using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DeskGrid.Shell;

/// <summary>
/// Lets the native popup menus (the tray menu, the menu Explorer shows for a panel item) follow the system's dark
/// mode. Windows has no public API for it: <c>uxtheme.dll</c> exports <c>SetPreferredAppMode</c> and
/// <c>FlushMenuThemes</c> by ordinal only. If they are missing the menus stay light, as before.
/// </summary>
internal static unsafe class MenuTheme
{
    private const ushort SetPreferredAppModeOrdinal = 135;
    private const ushort FlushMenuThemesOrdinal = 136;
    private const int AllowDark = 1; // PreferredAppMode: dark when the system's app mode is dark.

    private static delegate* unmanaged<int, int> _setPreferredAppMode;
    private static delegate* unmanaged<void> _flushMenuThemes;

    /// <summary>Makes this process's native menus follow the system's light or dark mode. Safe to call again before showing a menu.</summary>
    /// <returns>Whether Windows took it; <see langword="false"/> where it has no such function.</returns>
    public static bool FollowSystem()
    {
        try
        {
            // Ordinal 135 is something else before Windows 10 1903 (build 18362).
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            {
                return false;
            }

            if (_setPreferredAppMode is null && !Load())
            {
                return false;
            }

            _setPreferredAppMode(AllowDark);
            _flushMenuThemes();
            return true;
        }
        catch (Exception exception)
        {
            // Cosmetic only: never let it keep a menu from opening.
            Log.Error("Making native menus follow the system theme failed", exception);
            return false;
        }
    }

    private static bool Load()
    {
        var library = PInvoke.LoadLibrary("uxtheme.dll");
        if (library.IsInvalid)
        {
            return false;
        }

        // The library stays loaded for the life of the process (uxtheme is loaded anyway).
        var module = new HMODULE(library.DangerousGetHandle());
        var setMode = PInvoke.GetProcAddress(module, new PCSTR((byte*)SetPreferredAppModeOrdinal));
        var flush = PInvoke.GetProcAddress(module, new PCSTR((byte*)FlushMenuThemesOrdinal));
        if (setMode.IsNull || flush.IsNull)
        {
            Log.Info("uxtheme does not export the dark menu functions; native menus stay light.");
            return false;
        }

        _setPreferredAppMode = (delegate* unmanaged<int, int>)setMode.Value;
        _flushMenuThemes = (delegate* unmanaged<void>)flush.Value;
        return true;
    }
}
