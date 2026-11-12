using System.Diagnostics;
using DeskGrid.Desktop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Island;

/// <summary>
/// The situations NotiLand can have a place of its own for. <see cref="Default"/> is the place it has when the situation
/// has none (and when none is known).
/// </summary>
internal static class IslandScene
{
    public const string Default = "default";
    public const string Desktop = "desktop";
    public const string Window = "window";
    public const string Maximized = "maximized";
    public const string FullScreen = "fullScreen";

    /// <summary>The situations, in the order Settings lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Desktop, Window, Maximized, FullScreen];

    /// <summary>
    /// What is in front now: the desktop (nothing, or the desktop itself), a full-screen window (as big as its screen), a
    /// maximized one, or an ordinary one. DeskGrid's own windows and the taskbar keep whatever was there before
    /// (<paramref name="before"/>), so opening a panel or clicking the taskbar does not move NotiLand.
    /// </summary>
    public static string Detect(string before)
    {
        var window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            return Desktop;
        }

        // The desktop: its classic windows, a minimized window left in front (all windows went down), or, on newer Windows
        // (seen on build 26300), Explorer's XAML host that covers the screen while the desktop is shown.
        var kind = ClassOf(window);
        if (kind is "Progman" or "WorkerW" || PInvoke.IsIconic(window))
        {
            return Desktop;
        }

        // The taskbar, and the short-lived windows Windows puts in front while switching (Alt+Tab, the task view, the staging window
        // a window passes through when it is activated): none of them is a situation of its own.
        if (kind is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow"
            or "ForegroundStaging" or "MultitaskingViewFrame" or "TaskSwitcherWnd" or "TaskListThumbnailWnd" or "TaskListOverlayWnd"
            || IsOurs(window))
        {
            return before;
        }

        if (!PInvoke.GetWindowRect(window, out var rect))
        {
            return before;
        }

        var bounds = TopEdge.BoundsAt(new ScreenPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2));
        if (rect.left <= bounds.Left && rect.top <= bounds.Top && rect.right >= bounds.Right && rect.bottom >= bounds.Bottom)
        {
            return kind == "XamlExplorerHostIslandWindow" ? Desktop : FullScreen;
        }

        return PInvoke.IsZoomed(window) ? Maximized : Window;
    }

    private static unsafe string ClassOf(HWND window)
    {
        var buffer = stackalloc char[64];
        var length = PInvoke.GetClassName(window, buffer, 64);
        return new string(buffer, 0, length);
    }

    private static bool IsOurs(HWND window)
    {
        _ = PInvoke.GetWindowThreadProcessId(window, out var process);
        return process == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Whether a system overlay that NotiLand must not cover is in front: the screen-snip tool (Win+Shift+S, Print Screen),
    /// Start, search, the notification centre. Then it neither lifts itself above other windows nor counts down.
    /// </summary>
    public static bool SystemOverlayInFront() => IsOverlay(IslandRules.ForegroundApp());

    internal static bool IsOverlay(string app) => app is
        "screenclippinghost" or "snippingtool" or "screensketch"
        or "startmenuexperiencehost" or "searchhost" or "searchapp" or "shellexperiencehost";
}
