using System.Diagnostics;
using Windows.Win32;

namespace DeskGrid.Island;

/// <summary>
/// Whether the window in front is the page of a song that plays (a music app, a video site in a browser tab): NotiLand has
/// nothing to tell about it then, the user is looking at it.
/// </summary>
internal static class PlayerInFront
{
    /// <summary>The program (without ".exe", lower case) and the title of the window in front; empty when they cannot be told.</summary>
    public static (string Process, string Title) Foreground()
    {
        try
        {
            var window = PInvoke.GetForegroundWindow();
            if (window.IsNull || PInvoke.GetWindowThreadProcessId(window, out var pid) == 0)
            {
                return ("", "");
            }

            var length = PInvoke.GetWindowTextLength(window);
            var title = "";
            if (length > 0)
            {
                Span<char> buffer = stackalloc char[length + 1];
                title = new string(buffer[..PInvoke.GetWindowText(window, buffer)]);
            }

            using var process = Process.GetProcessById((int)pid);
            return (process.ProcessName.ToLowerInvariant(), title);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ("", ""); // The program ended meanwhile, or is not ours to look at.
        }
    }

    /// <summary>
    /// Whether the window of program <paramref name="process"/> titled <paramref name="windowTitle"/> shows the song
    /// <paramref name="songTitle"/> of player <paramref name="source"/>. A music app: its window is the page. A browser: its
    /// window is the page when the tab in front carries the song's name (a browser's own id does not always say which one it is).
    /// </summary>
    internal static bool Shows(string source, string songTitle, string process, string windowTitle)
    {
        if (process.Length == 0)
        {
            return false;
        }

        if (MediaJumper.IsBrowser(process))
        {
            var needle = MediaJumper.Needle(songTitle);
            return needle.Length >= 2 && windowTitle.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        return MediaJumper.Matches(source, process);
    }
}
