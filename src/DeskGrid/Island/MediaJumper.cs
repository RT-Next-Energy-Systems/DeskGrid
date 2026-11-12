using System.Diagnostics;
using System.Windows.Automation;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Island;

/// <summary>
/// Brings the player of a song to the front: the window of the app (a music app), or the browser window, and in it the tab
/// that plays (a video site). The system media controls only name the player (<c>SourceAppUserModelId</c>) and the song,
/// so the window is found by its process and by the song's title in the window's title or tab strip.
/// </summary>
internal static class MediaJumper
{
    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "zen"];

    /// <summary>Brings the player to the front; slow (window and tab search), so it runs on a pool thread. Failures are logged.</summary>
    public static void Jump(string source, string title) => _ = Task.Run(() =>
    {
        try
        {
            if (!JumpCore(source, title))
            {
                OpenPackagedApp(source);
            }
        }
        catch (Exception exception)
        {
            Log.Error("Bringing the player to the front failed", exception);
        }
    });

    /// <summary>Whether a process called <paramref name="processName"/> is the player named <paramref name="source"/> (the app's user model id).</summary>
    internal static bool Matches(string source, string processName)
    {
        if (processName.Length < 3 || source.Length == 0)
        {
            return false;
        }

        // "Spotify.exe" → "spotify"; "AppleInc.AppleMusicWin_xyz!App" contains "applemusic"; "Chrome" is chrome.exe.
        var id = source.ToLowerInvariant();
        var name = processName.ToLowerInvariant();
        return id.Contains(name, StringComparison.Ordinal) || (name.Length >= 4 && name.StartsWith(id.Replace(".exe", ""), StringComparison.Ordinal) && id.Length >= 4);
    }

    /// <summary>Whether a program called <paramref name="processName"/> is a web browser.</summary>
    internal static bool IsBrowser(string processName) =>
        Browsers.Any(b => processName.Contains(b, StringComparison.OrdinalIgnoreCase));

    /// <summary>The part of a title worth looking for in a window or tab name (the song, not the artist or a suffix).</summary>
    internal static string Needle(string title)
    {
        var parts = title.Split([" - ", " – ", " — ", " · ", " | "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : title.Trim();
    }

    private static bool JumpCore(string source, string title)
    {
        var needle = Needle(title);
        var candidates = TopLevelWindows().Where(w => Matches(source, w.Process)).ToList();
        Log.Info($"Player '{source}': {candidates.Count} window(s) found ({string.Join(", ", candidates.Select(c => $"{c.Process} {((IntPtr)c.Handle).ToInt64()} {c.Title}"))}).");
        if (candidates.Count == 0)
        {
            return false;
        }

        // 1. A window whose title carries the song (a music app's window, or a browser whose active tab plays).
        var at = needle.Length > 0 ? candidates.FindIndex(w => w.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)) : -1;
        if (at >= 0)
        {
            return Raise(candidates[at].Handle);
        }

        // 2. A browser: the tab that plays may be in the background of any of its windows.
        if (needle.Length > 0 && Browsers.Any(b => candidates[0].Process.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var window in candidates)
            {
                if (SelectTab(window.Handle, needle))
                {
                    return Raise(window.Handle);
                }
            }
        }

        // 3. At least the player itself: the biggest-titled window of it (the main one), not a helper.
        var main = candidates.OrderByDescending(c => c.Title.Length).First();
        return Raise(main.Handle);
    }

    // A packaged app that has no window of its own to find: asking the shell to open it brings it forward.
    private static void OpenPackagedApp(string source)
    {
        if (source.Contains('!'))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + source) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>
    /// Moves the song to <paramref name="position"/> through the player's own progress slider (UI Automation), for players that
    /// do not take a new position from the system media controls (Apple Music says so and ignores the request). The slider is
    /// the one whose range is the song's length. Slow, so it runs on a pool thread; returns at once.
    /// </summary>
    public static void SeekThroughSlider(string source, TimeSpan duration, TimeSpan position) => _ = Task.Run(() =>
    {
        try
        {
            foreach (var window in TopLevelWindows().Where(w => Matches(source, w.Process)))
            {
                var sliders = AutomationElement.FromHandle(window.Handle)
                    .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Slider));
                foreach (AutomationElement slider in sliders)
                {
                    if (slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern) && pattern is RangeValuePattern range
                        && !range.Current.IsReadOnly && Math.Abs(range.Current.Maximum - duration.TotalSeconds) <= 3)
                    {
                        range.SetValue(Math.Clamp(position.TotalSeconds, range.Current.Minimum, range.Current.Maximum));
                        Log.Info($"Moved '{source}' to {position} through its progress slider.");
                        return;
                    }
                }
            }

            Log.Info($"No progress slider found for '{source}'.");
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Moving the song through the player's slider failed", exception);
        }
    });

    private static readonly string[] NextNames = ["next", "skip forward", "下一首", "下一個", "下一曲"];
    private static readonly string[] PreviousNames = ["previous", "skip back", "上一首", "上一個", "上一曲"];

    /// <summary>
    /// Presses the player's own next / previous button through UI Automation, for players whose system media controls refuse
    /// it. Not tried in browsers (their page tree is huge, and a page that offers no next song has no button either).
    /// Slow, so call it off the UI thread. Returns whether a button was pressed.
    /// </summary>
    public static bool PressTransportButton(string source, bool next)
    {
        try
        {
            var names = next ? NextNames : PreviousNames;
            foreach (var window in TopLevelWindows().Where(w => Matches(source, w.Process) && !IsBrowser(w.Process)))
            {
                var buttons = AutomationElement.FromHandle(window.Handle)
                    .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement button in buttons)
                {
                    var label = button.Current.Name;
                    if (label.Length > 0 && button.Current.IsEnabled && names.Any(n => label.Contains(n, StringComparison.OrdinalIgnoreCase))
                        && button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
                    {
                        ((InvokePattern)pattern).Invoke();
                        Log.Info($"Pressed '{label}' of '{source}' through UI Automation.");
                        return true;
                    }
                }
            }

            Log.Info($"No {(next ? "next" : "previous")} button found for '{source}'.");
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Pressing the player's button failed", exception);
        }

        return false;
    }

    /// <summary>Sends the keyboard's next / previous track key; Windows gives it to the player it considers current.</summary>
    public static void SendMediaKey(bool next)
    {
        const byte nextTrack = 0xB0, previousTrack = 0xB1;
        var key = next ? nextTrack : previousTrack;
        var extended = Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        PInvoke.keybd_event(key, 0, extended, 0);
        PInvoke.keybd_event(key, 0, extended | Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
        Log.Info($"Sent the media key for the {(next ? "next" : "previous")} song.");
    }

    private static bool SelectTab(HWND window, string needle)
    {
        try
        {
            var root = AutomationElement.FromHandle(window);
            var tabs = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            foreach (AutomationElement tab in tabs)
            {
                if (tab.Current.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) && tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
                {
                    ((SelectionItemPattern)pattern).Select();
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("The tab of the player could not be selected", exception);
        }

        return false;
    }

    // Windows refuses SetForegroundWindow from a program that does not own the foreground; joining the input queue of the
    // window in front (and tapping Alt) lifts that. Returns whether the window is in front afterwards.
    private static bool Raise(HWND window)
    {
        var wasMinimized = PInvoke.IsIconic(window);
        if (wasMinimized)
        {
            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
            Thread.Sleep(150); // The window's own program restores it; wait for that before asking for the front.
        }

        var asked = PInvoke.SetForegroundWindow(window);
        Log.Info($"Raise: minimized={wasMinimized}, now minimized={PInvoke.IsIconic(window)}, SetForegroundWindow={asked}, in front={PInvoke.GetForegroundWindow() == window}.");
        if (asked && PInvoke.GetForegroundWindow() == window)
        {
            return true;
        }

        var front = PInvoke.GetForegroundWindow();
        var frontThread = PInvoke.GetWindowThreadProcessId(front, out _);
        var thisThread = PInvoke.GetCurrentThreadId();
        var attached = frontThread != 0 && frontThread != thisThread && PInvoke.AttachThreadInput(thisThread, frontThread, true);
        try
        {
            PInvoke.keybd_event(0x12, 0, 0, 0); // Alt down and up.
            PInvoke.keybd_event(0x12, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
            PInvoke.BringWindowToTop(window);
            PInvoke.SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(thisThread, frontThread, false);
            }
        }

        // Last resort, and the one Alt-Tab itself uses: it also restores a minimized window. The other program answers
        // asynchronously, so the result is waited for a moment.
        for (var attempt = 0; attempt < 10 && PInvoke.GetForegroundWindow() != window; attempt++)
        {
            if (attempt % 3 == 0)
            {
                PInvoke.SwitchToThisWindow(window, true);
            }

            Thread.Sleep(100);
        }

        return PInvoke.GetForegroundWindow() == window;
    }

    private static List<(HWND Handle, string Title, string Process)> TopLevelWindows()
    {
        var found = new List<(HWND, string, string)>();
        var names = new Dictionary<uint, string>();
        PInvoke.EnumWindows((handle, _) =>
        {
            if (!PInvoke.IsWindowVisible(handle))
            {
                return true;
            }

            var length = PInvoke.GetWindowTextLength(handle);
            if (length == 0)
            {
                return true;
            }

            Span<char> buffer = stackalloc char[length + 1];
            var read = PInvoke.GetWindowText(handle, buffer);
            PInvoke.GetWindowThreadProcessId(handle, out var processId);

            if (!names.TryGetValue(processId, out var name))
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    name = process.ProcessName;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    name = ""; // Ended meanwhile (or not ours to look at); nothing may escape into the native callback.
                }

                names[processId] = name;
            }

            found.Add((handle, new string(buffer[..read]), name));
            return true;
        }, default);
        return found;
    }
}
