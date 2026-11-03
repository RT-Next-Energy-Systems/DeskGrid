using System.Windows.Interop;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Shell;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Tray;

/// <summary>One entry of the tray menu; <see cref="Separator"/> draws a line.</summary>
internal sealed record TrayMenuItem(string Text, Action Invoke, bool Checked = false)
{
    public static TrayMenuItem Separator { get; } = new("", () => { });
}

/// <summary>
/// The notification-area icon. Either click opens a native menu built fresh by <c>menu</c> each time
/// (so check marks are current). Re-adds itself when Explorer restarts.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint NinKeySelect = PInvoke.NIN_SELECT | 1; // NIN_KEYSELECT; not in CsWin32's metadata.

    // Where the SDK puts <ApplicationIcon> inside the exe (the C# compiler's default icon id).
    private const string AppIconResource = "#32512";

    private readonly string _tooltip;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _menu;
    private readonly HwndSource _window;
    private readonly DestroyIconSafeHandle _icon;

    public TrayIcon(string tooltip, Func<IReadOnlyList<TrayMenuItem>> menu)
    {
        _tooltip = tooltip;
        _menu = menu;
        _window = ShellMessageWindow.Create("DeskGrid.Tray", WndProc);
        PInvoke.LoadIconMetric(PInvoke.GetModuleHandle((string?)null), AppIconResource, _LI_METRIC.LIM_SMALL, out _icon)
            .ThrowOnFailure();
        Add();
    }

    public void Dispose()
    {
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        _icon.Dispose();
        _window.Dispose();
    }

    private NOTIFYICONDATAW Data() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = new HWND(_window.Handle),
        uID = IconId,
    };

    private void Add()
    {
        var data = Data();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
            | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = new HICON(_icon.DangerousGetHandle());
        data.szTip = _tooltip;
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            Log.Info("Adding the tray icon failed (taskbar not ready?); waiting for TaskbarCreated.");
            return;
        }

        // Version 4: the callback carries the event in LOWORD(lParam) and sends WM_CONTEXTMENU / NIN_SELECT.
        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == ShellMessageWindow.TaskbarCreatedMessage)
        {
            Add();
        }
        else if (message == CallbackMessage
            && (uint)(lParam & 0xFFFF) is PInvoke.WM_CONTEXTMENU or PInvoke.NIN_SELECT or NinKeySelect)
        {
            ShowMenu();
            handled = true;
        }

        return 0;
    }

    private unsafe void ShowMenu()
    {
        var items = _menu();
        MenuTheme.FollowSystem(); // Before the menu is made, so a change of the system theme since the last time shows.
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == TrayMenuItem.Separator)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
                    continue;
                }

                var flags = MENU_ITEM_FLAGS.MF_STRING | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
                fixed (char* text = item.Text)
                {
                    PInvoke.AppendMenu(menu, flags, (nuint)(i + 1), text);
                }
            }

            // The documented dance: be foreground while the menu is up, so clicking elsewhere closes it.
            var window = new HWND(_window.Handle);
            PInvoke.SetForegroundWindow(window);
            PInvoke.GetCursorPos(out var cursor);
            var chosen = PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
                cursor.X, cursor.Y, window, null).Value;
            PInvoke.PostMessage(window, PInvoke.WM_NULL, 0, 0);

            if (chosen > 0)
            {
                // Run after this message returns: the action may open a modal dialog.
                _window.Dispatcher.BeginInvoke(items[chosen - 1].Invoke, DispatcherPriority.Normal);
            }
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }
}
