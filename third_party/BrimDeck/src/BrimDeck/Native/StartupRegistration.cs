using System.IO;
using Microsoft.Win32;
using BrimDeck.Core;

namespace BrimDeck.Native;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        if (App.ManagedByDeskGrid) return false;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key is not null && IsEnabled(key);
    }
    internal static bool IsEnabled(RegistryKey key) => key.GetValue("BrimDeck") is string value && value.Length > 0;
    public static void SetEnabled(bool enabled)
    {
        if (App.ManagedByDeskGrid) return; // DeskGrid starts it.
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        SetEnabled(key, enabled, Environment.ProcessPath);
    }
    internal static void SetEnabled(RegistryKey key, bool enabled, string? executable)
    {
        if (!enabled) { key.DeleteValue("BrimDeck", false); return; }
        if (executable is null) throw new IOException(Loc.T("無法確定應用位置。", "The application location could not be determined."));
        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "BrimDeck", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Loc.T("請從 BrimDeck.exe 啟動應用後設定自啟動。", "Start the app from BrimDeck.exe before setting it to start with Windows."));
        var command = '"' + executable + '"';
        if (command.Length > 260) throw new IOException(Loc.T("應用路徑過長，無法註冊自啟動。", "The application path is too long to register for startup."));
        key.SetValue("BrimDeck", command, RegistryValueKind.String);
    }
}
