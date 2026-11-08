using System.Windows.Interop;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace DeskGrid.Hotkeys;

/// <summary>
/// Registers shortcuts with Windows (<c>RegisterHotKey</c>), so they work whichever program has the focus, and says which
/// one was pressed. Used on the UI thread only: the shortcuts are delivered to a hidden window of that thread.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _window;

    // Registration id (what Windows reports) -> the action it triggers. Ids count up from 1.
    private readonly Dictionary<int, string> _registered = [];
    private int _lastId;
    private IReadOnlyList<HotkeyBinding> _applied = [];
    private IReadOnlyList<HotkeyBinding> _failed = [];

    public HotkeyService() => _window = ShellMessageWindow.Create("DeskGrid.Hotkeys", OnMessage);

    /// <summary>The hidden window the shortcuts are delivered to (for tests).</summary>
    internal IntPtr Handle => _window.Handle;

    /// <summary>Raised on the UI thread with the id of the action whose shortcut was pressed.</summary>
    public event EventHandler<string>? Pressed;

    /// <summary>
    /// Makes <paramref name="bindings"/> the shortcuts in force and returns the ones Windows refused, because another program
    /// (or an earlier binding in the list) already has that combination. Nothing happens when the list is the one already applied.
    /// </summary>
    public IReadOnlyList<HotkeyBinding> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        if (bindings.SequenceEqual(_applied))
        {
            return _failed;
        }

        Unregister();
        var failed = new List<HotkeyBinding>();
        foreach (var binding in bindings)
        {
            if (Register(binding))
            {
                continue;
            }

            Log.Info($"Shortcut {binding.Key} ({binding.Id}) is already used by another program.");
            failed.Add(binding);
        }

        (_applied, _failed) = (bindings, failed);
        return failed;
    }

    /// <summary>Gives every shortcut back to the system, e.g. while the user is recording a new one (which must reach the window).</summary>
    public void Suspend() => Apply([]);

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    private bool Register(HotkeyBinding binding)
    {
        var id = ++_lastId;
        var modifiers = (HOT_KEY_MODIFIERS)(uint)binding.Key.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (!PInvoke.RegisterHotKey(new HWND(_window.Handle), id, modifiers, (uint)binding.Key.VirtualKey))
        {
            return false;
        }

        _registered[id] = binding.Id;
        return true;
    }

    private void Unregister()
    {
        foreach (var id in _registered.Keys)
        {
            PInvoke.UnregisterHotKey(new HWND(_window.Handle), id);
        }

        _registered.Clear();
        (_applied, _failed) = ([], []);
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == PInvoke.WM_HOTKEY && _registered.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            Pressed?.Invoke(this, action);
        }

        return IntPtr.Zero;
    }
}
