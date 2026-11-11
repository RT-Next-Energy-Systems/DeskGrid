using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace DeskGrid.Island;

/// <summary>What is in front: the program (without ".exe", lower case), its window's title, and its package family name (empty for a program that is not a packaged app).</summary>
internal readonly record struct FrontWindow(string Process, string Title, string Package = "")
{
    public static readonly FrontWindow None = new("", "");
}

/// <summary>
/// Whether the window in front is the page of a song that plays (a music app, a video site in a browser tab): NotiLand has
/// nothing to tell about it then, the user is looking at it.
/// </summary>
internal static class PlayerInFront
{
    // The frame Windows draws around a packaged (UWP) app such as Media Player; the app itself is a child window of it.
    private const string FrameHost = "applicationframehost";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>The window in front; <see cref="FrontWindow.None"/> when it cannot be told.</summary>
    public static FrontWindow Foreground()
    {
        try
        {
            var window = PInvoke.GetForegroundWindow();
            if (window.IsNull || PInvoke.GetWindowThreadProcessId(window, out var pid) == 0)
            {
                return FrontWindow.None;
            }

            var length = PInvoke.GetWindowTextLength(window);
            var title = "";
            if (length > 0)
            {
                Span<char> buffer = stackalloc char[length + 1];
                title = new string(buffer[..PInvoke.GetWindowText(window, buffer)]);
            }

            var name = NameOf(pid);
            if (name == FrameHost && PInvoke.FindWindowEx(window, HWND.Null, CoreWindowClass, null) is { IsNull: false } app
                && PInvoke.GetWindowThreadProcessId(app, out var appPid) != 0)
            {
                pid = appPid; // The app in the frame, not the frame: Media Player is told by its own process and package.
                name = NameOf(pid);
            }

            return new FrontWindow(name, title, PackageOf(pid));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return FrontWindow.None; // The program ended meanwhile, or is not ours to look at.
        }
    }

    private static string NameOf(uint pid)
    {
        using var process = Process.GetProcessById((int)pid);
        return process.ProcessName.ToLowerInvariant();
    }

    // The package family name ("Microsoft.ZuneMusic_8wekyb3d8bbwe") of a packaged app's process; empty for any other.
    private static string PackageOf(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
        {
            return "";
        }

        const int Capacity = 128; // PACKAGE_FAMILY_NAME_MAX_LENGTH + 1 is 65.
        var length = (uint)Capacity;
        Span<char> buffer = stackalloc char[Capacity];
        return PInvoke.GetPackageFamilyName(process, ref length, buffer) == WIN32_ERROR.ERROR_SUCCESS && length > 1
            ? new string(buffer[..((int)length - 1)])
            : "";
    }

    /// <summary>
    /// Whether <paramref name="front"/> shows the song <paramref name="songTitle"/> of player <paramref name="source"/> (the
    /// app's user model id). A packaged app: its user model id starts with its package family name. A browser: its window is
    /// the page when the tab in front carries the song's name (a browser's own id does not always say which one it is). Another
    /// music app: told by its program name.
    /// </summary>
    internal static bool Shows(string source, string songTitle, FrontWindow front)
    {
        if (front.Process.Length == 0)
        {
            return false;
        }

        if (front.Package.Length > 0 && source.StartsWith(front.Package + "!", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MediaJumper.IsBrowser(front.Process))
        {
            var needle = MediaJumper.Needle(songTitle);
            return needle.Length >= 2 && front.Title.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        return MediaJumper.Matches(source, front.Process);
    }
}
