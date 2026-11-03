using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Desktop;

/// <summary>Hidden windows that hear Explorer's broadcasts and Shell callbacks.</summary>
internal static class ShellMessageWindow
{
    /// <summary>Broadcast by Explorer after (re)creating the taskbar, e.g. when explorer.exe restarts.</summary>
    public static uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    /// <summary>
    /// Creates a hidden, unowned top-level window: message-only windows don't receive broadcasts.
    /// <see cref="TaskbarCreatedMessage"/> is let through UIPI in case we run elevated.
    /// </summary>
    public static unsafe HwndSource Create(string name, HwndSourceHook hook)
    {
        var source = new HwndSource(new HwndSourceParameters(name)
        {
            WindowStyle = unchecked((int)WINDOW_STYLE.WS_POPUP),
            Width = 0,
            Height = 0,
        });
        source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        PInvoke.ChangeWindowMessageFilterEx(new HWND(source.Handle), TaskbarCreatedMessage, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW, null);
        source.AddHook(hook);
        return source;
    }
}
