using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace DeskGrid.Panel;

/// <summary>
/// The Windows screen capture overlay (Win+Shift+S, Print Screen): it takes the foreground, which would close an open
/// panel before the capture could show it.
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    // WinEvents (winuser.h).
    private const uint SystemForeground = 0x0003;
    private const uint OutOfContext = 0; // WINEVENT_OUTOFCONTEXT

    // Snipping Tool on Windows 11 (measured: its overlay is a XamlWindow of SnippingTool.exe); Snip & Sketch's
    // overlay on Windows 10.
    private static readonly string[] Tools = ["SnippingTool", "ScreenClippingHost"];

    private readonly Action<HWND> _done;
    private readonly WINEVENTPROC _callback; // Kept alive for as long as the hook is.
    private HWINEVENTHOOK _hook;

    private ScreenCapture(Action<HWND> done)
    {
        _done = done;
        _callback = OnEvent;
        // Out of context: the events are posted to this (UI) thread's message loop.
        _hook = PInvoke.SetWinEventHook(SystemForeground, SystemForeground, HMODULE.Null, _callback, 0, 0, OutOfContext);
    }

    /// <summary>Whether a screen capture tool has the foreground.</summary>
    public static bool HasForeground() => IsTool(PInvoke.GetForegroundWindow());

    /// <summary>
    /// Calls <paramref name="done"/> with the new foreground window once the foreground moves on from the capture tool.
    /// Dispose to stop waiting.
    /// </summary>
    public static ScreenCapture WaitUntilDone(Action<HWND> done) => new(done);

    public void Dispose()
    {
        if (!_hook.IsNull)
        {
            PInvoke.UnhookWinEvent(_hook);
            _hook = default;
        }
    }

    private static bool IsTool(HWND window)
    {
        if (window.IsNull || PInvoke.GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return Tools.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false; // Already exited, or not ours to look at.
        }
    }

    private void OnEvent(HWINEVENTHOOK hook, uint @event, HWND window, int objectId, int childId, uint thread, uint time)
    {
        // The tool may move between its own windows while capturing.
        if (_hook.IsNull || IsTool(window))
        {
            return;
        }

        Dispose();
        _done(window);
    }
}
