using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Desktop;

/// <summary>
/// A per-pixel transparent child window that hosts WPF content inside Explorer's desktop icon layer.
/// <see cref="DesktopHost"/> decides when to (re)attach it.
/// </summary>
internal sealed class DesktopSurface(UIElement content, ScreenPoint position) : IDisposable
{
    // Win32 HWND_TOP; not in CsWin32's metadata.
    private static readonly HWND HwndTop = HWND.Null;

    private HwndSource? _source;

    /// <summary>Top-left corner in physical screen pixels.</summary>
    public ScreenPoint Position { get; private set; } = position;

    /// <summary>Raised when someone else destroys the window, typically Explorer exiting.</summary>
    public event EventHandler? Lost;

    public bool IsAttachedTo(HWND layer) =>
        _source is { IsDisposed: false } source && PInvoke.GetParent(new HWND(source.Handle)) == layer;

    /// <summary>Creates a fresh child window under <paramref name="layer"/>, replacing any previous one.</summary>
    public void AttachTo(HWND layer)
    {
        Release();

        var origin = ToClient(layer, Position);
        var parameters = new HwndSourceParameters(nameof(DesktopSurface))
        {
            ParentWindow = layer,
            WindowStyle = (int)(WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE | WINDOW_STYLE.WS_CLIPSIBLINGS),
            // Explorer's thread must never be able to block ours through parent notifications.
            ExtendedWindowStyle = (int)WINDOW_EX_STYLE.WS_EX_NOPARENTNOTIFY,
            // Layered child window: the only kind that renders under the composition-based Progman
            // of 24H2+, and fully transparent pixels become click-through.
            UsesPerPixelTransparency = true,
            PositionX = origin.X,
            PositionY = origin.Y,
        };
        _source = new HwndSource(parameters) { SizeToContent = SizeToContent.WidthAndHeight, RootVisual = content };
        _source.Disposed += OnSourceDisposed;
        _source.AddHook(KeepDesktopInactive);

        // Above the icon list view, so clicks land on us rather than on the icons.
        BringToFront();
    }

    /// <summary>Puts the window above its siblings: the icons and the other surfaces.</summary>
    public void BringToFront()
    {
        if (_source is { IsDisposed: false } source)
        {
            PInvoke.SetWindowPos(new HWND(source.Handle), HwndTop, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
    }

    public void MoveTo(ScreenPoint position)
    {
        Position = position;
        if (_source is not { IsDisposed: false } source)
        {
            return;
        }

        var window = new HWND(source.Handle);
        var origin = ToClient(PInvoke.GetParent(window), position);
        PInvoke.SetWindowPos(window, HWND.Null, origin.X, origin.Y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Puts the window back at <see cref="Position"/> if it has drifted. A child window keeps its place relative to
    /// its parent, and the desktop spans every monitor: add one on the left of or above the primary and the desktop's
    /// origin moves, taking the window with it.
    /// </summary>
    /// <returns>Whether the window had to be moved.</returns>
    public bool Resync()
    {
        if (_source is not { IsDisposed: false } source
            || !PInvoke.GetWindowRect(new HWND(source.Handle), out var rect)
            || (rect.left == Position.X && rect.top == Position.Y))
        {
            return false;
        }

        MoveTo(Position);
        return true;
    }

    public void Dispose() => Release();

    // Clicking a surface must not activate the desktop: Explorer swallows the click that activates it,
    // so with one of our windows active (e.g. a panel) the surface would never see the press.
    private static nint KeepDesktopInactive(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != (int)PInvoke.WM_MOUSEACTIVATE)
        {
            return 0;
        }

        handled = true;
        return (nint)PInvoke.MA_NOACTIVATE;
    }

    private static System.Drawing.Point ToClient(HWND parent, ScreenPoint position)
    {
        var point = new System.Drawing.Point(position.X, position.Y);
        PInvoke.ScreenToClient(parent, ref point);
        return point;
    }

    private void Release()
    {
        if (_source is null)
        {
            return;
        }

        _source.Disposed -= OnSourceDisposed;
        _source.Dispose();
        _source = null;
    }

    private void OnSourceDisposed(object? sender, EventArgs e)
    {
        _source = null;
        Lost?.Invoke(this, EventArgs.Empty);
    }
}
