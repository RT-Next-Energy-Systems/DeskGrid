using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeskGrid.Diagnostics;
using DeskGrid.Resources;
using DeskGrid.Shell;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Taskbar;

/// <summary>
/// Makes the taskbar buttons the user has pinned from DeskGrid open their panel at once. A pinned button that has no window
/// starts a program (the launcher), which Windows needs about 35 ms for before DeskGrid hears of the click. A button whose
/// application has a window is not started but activated, and the window is sent the message a minimized window gets when its button
/// is clicked, within 8 ms (measured). So for every pinned DeskGrid button DeskGrid keeps a window of its own, with the button's
/// AppUserModelID, minimized and hidden from Alt+Tab; a click on the button restores it, which this answers by opening the panel
/// and leaving the window minimized. The cost is that Windows marks such a button as running.
/// </summary>
/// <remarks>
/// Without DeskGrid running there is no window and the button starts the launcher as before. Call on the UI thread.
/// </remarks>
internal sealed unsafe class TaskbarAnchors : IDisposable
{
    private const uint WmSysCommand = 0x0112;
    private const uint WmClose = 0x0010;
    private const uint CommandMask = 0xFFF0;
    private const uint ScRestore = 0xF120;
    private const uint ScMaximize = 0xF030;

    // Folders and files that change many times in a row (a pin, an unpin) are looked at once, after they have been quiet.
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(20);

    // Registered once, and kept: the system holds the delegate for as long as the class exists.
    private static readonly WNDPROC Procedure = WindowProcedure;

    private static readonly Lazy<string> WindowClass = new(RegisterWindowClass);

    // The windows by handle, for the window procedure. Touched on the UI thread only.
    private static readonly Dictionary<nint, TaskbarAnchors> Owners = new();

    private readonly Dispatcher _dispatcher;
    private readonly Action<OpenMessage> _onClick;
    private readonly string _folder;
    private readonly Dictionary<string, (HWND Window, string Name)> _anchors = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _retry;
    private bool _enabled;

    /// <param name="onClick">Called on the UI thread, with where the cursor was, for a click on a button.</param>
    /// <param name="folder">Where Windows keeps the shortcuts of the pinned buttons; the user's own by default.</param>
    public TaskbarAnchors(Action<OpenMessage> onClick, string? folder = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _onClick = onClick;
        _folder = folder ?? PinnedFolder;
    }

    public static string PinnedFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>How many windows are kept (one for each pinned DeskGrid button).</summary>
    public int Count => _anchors.Count;

    /// <summary>Whether pinned buttons get a window. Turning it off takes the windows away, and the buttons start the launcher again.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (value)
            {
                Watch();
                Sync();

                // The folder of the pinned buttons is made by Windows with the first pin: until it is there, look for it now and then.
                _retry ??= new DispatcherTimer(RetryEvery, DispatcherPriority.Background, (_, _) =>
                {
                    if (_watcher is null && Directory.Exists(_folder))
                    {
                        Watch();
                        Sync();
                    }
                }, _dispatcher);
                _retry.Start();
            }
            else
            {
                _retry?.Stop();
                _watcher?.Dispose();
                _watcher = null;
                _timer?.Stop();
                foreach (var id in _anchors.Keys.ToList())
                {
                    Remove(id);
                }
            }
        }
    }

    /// <summary>
    /// The pinned DeskGrid buttons in <paramref name="folder"/>: the AppUserModelID of each and what it opens (a folder's name, or
    /// <see cref="OpenRequest.AllFolders"/>).
    /// </summary>
    public static Dictionary<string, string> ReadPinned(string folder)
    {
        var pinned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder))
        {
            return pinned;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
        {
            if (ShortcutFile.TryReadLaunch(file) is ({ } arguments, { } appId)
                && appId.StartsWith(PinShortcut.AppIdPrefix, StringComparison.Ordinal)
                && OpenRequest.ParseArguments(arguments) is { } name)
            {
                pinned[appId] = name;
            }
        }

        return pinned;
    }

    /// <summary>Gives every pinned button a window and takes the windows of unpinned ones away.</summary>
    public void Sync()
    {
        if (!_enabled)
        {
            return;
        }

        Dictionary<string, string> pinned;
        try
        {
            pinned = ReadPinned(_folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Reading the pinned taskbar buttons failed", exception);
            return;
        }

        foreach (var id in _anchors.Keys.Where(id => !pinned.ContainsKey(id)).ToList())
        {
            Remove(id);
        }

        foreach (var (id, name) in pinned)
        {
            if (_anchors.TryGetValue(id, out var existing) && existing.Name == name)
            {
                continue;
            }

            Remove(id);
            try
            {
                _anchors[id] = (Create(id, name), name);
            }
            catch (Win32Exception exception)
            {
                Log.Error($"Making a window for the taskbar button \"{name}\" failed", exception);
            }
        }
    }

    public void Dispose() => Enabled = false;

    private void Watch()
    {
        if (_watcher is not null || !Directory.Exists(_folder))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_folder, "*.lnk") { EnableRaisingEvents = true };
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error("Watching the pinned taskbar buttons failed", exception);
            return;
        }

        _timer ??= new DispatcherTimer(Quiet, DispatcherPriority.Background, (_, _) =>
        {
            _timer!.Stop();
            Sync();
        }, _dispatcher);
        void Changed(object? sender, FileSystemEventArgs e) => _dispatcher.BeginInvoke(() =>
        {
            _timer!.Stop();
            _timer.Start();
        });
        _watcher.Created += Changed;
        _watcher.Deleted += Changed;
        _watcher.Changed += Changed;
        _watcher.Renamed += Changed;
    }

    private HWND Create(string appId, string name)
    {
        // Minimized and out of the way, but a top-level window of its own, so that the taskbar lists it under the button.
        var window = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_APPWINDOW, WindowClass.Value, name == OpenRequest.AllFolders ? Strings.AppName : name,
            WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 200, 100, HWND.Null, null, null, null);
        if (window.IsNull)
        {
            throw new Win32Exception();
        }

        Owners[window] = this;
        SetAppId(window, appId);
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE);

        // Hidden from Alt+Tab and the task view (the taskbar still lists it), and shown there as the button's icon, not a picture.
        int on = 1;
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAK, &on, sizeof(int));
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_FORCE_ICONIC_REPRESENTATION, &on, sizeof(int));
        PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_DISALLOW_PEEK, &on, sizeof(int));
        return window;
    }

    private void Remove(string appId)
    {
        if (_anchors.Remove(appId, out var anchor))
        {
            Owners.Remove(anchor.Window);
            PInvoke.DestroyWindow(anchor.Window);
        }
    }

    // The taskbar groups a window under the pinned shortcut that has the same AppUserModelID.
    private static void SetAppId(HWND window, string appId)
    {
        PInvoke.SHGetPropertyStoreForWindow(window, out IPropertyStore properties).ThrowOnFailure();
        try
        {
            fixed (char* text = appId)
            {
                var value = new PROPVARIANT();
                value.Anonymous.Anonymous.vt = VARENUM.VT_LPWSTR;
                value.Anonymous.Anonymous.Anonymous.pwszVal = text;
                properties.SetValue(PInvoke.PKEY_AppUserModel_ID, value);
                properties.Commit();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(properties);
        }
    }

    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (Owners.TryGetValue(window, out var owner))
        {
            if (message == WmSysCommand && ((uint)wParam.Value & CommandMask) is ScRestore or ScMaximize)
            {
                // The click on the button. The window stays as it is, minimized: the button was only a way to ask for the panel.
                owner.Clicked(window);
                return new LRESULT(0);
            }

            if (message == WmClose)
            {
                // "Close window" in the button's menu: DeskGrid is not closed by it.
                return new LRESULT(0);
            }
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void Clicked(HWND window)
    {
        var found = _anchors.Values.FirstOrDefault(anchor => anchor.Window == window);
        if (found.Window.IsNull)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        var message = new OpenMessage(found.Name, cursor.X, cursor.Y);

        // Not now: the button's click is a message that Explorer waits for an answer to.
        _dispatcher.BeginInvoke(() => _onClick(message), DispatcherPriority.Send);
    }

    private static string RegisterWindowClass()
    {
        const string name = "DeskGrid.TaskbarAnchor";
        fixed (char* className = name)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Procedure,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception();
            }
        }

        return name;
    }
}
