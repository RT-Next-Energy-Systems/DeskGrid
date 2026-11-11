using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Panel;

/// <summary>
/// The icon that follows the cursor while an item is dragged out of a panel: lifts off when the drag starts,
/// and flies to its slot or shrinks away when it ends. Never takes input or activation.
/// </summary>
/// <remarks>
/// A plain Win32 layered window that is handed finished pixels, not a WPF window: moving a transparent WPF window
/// waits for WPF to present a frame (measured 15–25 ms a move), which kept the UI thread too busy for the items
/// sliding out of the way. The icon is drawn only when its size or opacity changes. Not an <c>HwndSource</c> either:
/// WPF renders into any window it hosts, and on a layered one that makes <c>UpdateLayeredWindow</c> fail
/// (ERROR_INVALID_PARAMETER) and leaves a black square where the icon should be.
/// </remarks>
internal sealed unsafe class DragGhost
{
    private const double WindowSize = 96;
    private const double IconSize = 48;
    private const double TileCornerRadius = 10.7; // as the tile in PopupPanel.xaml
    private const double TilePadding = 5.8;
    private const double LiftedScale = 1.2;
    private static readonly TimeSpan LiftTime = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan LandTime = TimeSpan.FromMilliseconds(180);

    // How often a resting icon is put back on top, in case a window (a panel opened mid-drag) came up over it.
    private static readonly TimeSpan RaiseInterval = TimeSpan.FromMilliseconds(100);

    private static readonly HWND Topmost = new(-1);

    // The window only ever needs the default handling. A static field, so the delegate outlives every window.
    private static readonly WNDPROC WindowProcedure = PInvoke.DefWindowProc;

    // Registered on first use.
    private static readonly Lazy<string> WindowClass = new(RegisterWindowClass);

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Grid _content = new() { Width = WindowSize, Height = WindowSize };
    private readonly System.Diagnostics.Stopwatch _sinceRaised = new();
    private readonly uint _dpi;
    private readonly int _size;
    private readonly HWND _window;
    private readonly HDC _memory;
    private readonly HBITMAP _bitmap;
    private readonly HGDIOBJ _previousBitmap;
    private readonly byte* _pixels;
    private ScreenPoint _center;
    private ScreenPoint? _following;
    private DispatcherTimer? _animation;
    private byte _opacity = 255;
    private bool _closed;

    /// <param name="center">Where the icon's center first appears, in physical screen pixels.</param>
    /// <param name="tile">The plate under the icon of a folder, or <see langword="null"/> for a file.</param>
    public DragGhost(ImageSource? icon, ScreenPoint center, Brush? tile = null)
    {
        _center = center;
        _dpi = MonitorArea.At(center).Dpi;
        _size = (int)Math.Round(WindowSize * _dpi / 96);

        _content.Children.Add(new Border
        {
            Width = IconSize,
            Height = IconSize,
            // A folder's preview sits on its tile, as in the panel's list.
            Background = tile,
            CornerRadius = new CornerRadius(TileCornerRadius),
            Padding = tile is null ? new Thickness(0) : new Thickness(TilePadding),
            Child = new Image { Source = icon },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _scale,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45 },
        });
        RenderOptions.SetBitmapScalingMode(_content, BitmapScalingMode.HighQuality);
        _content.Measure(new Size(WindowSize, WindowSize));
        _content.Arrange(new Rect(0, 0, WindowSize, WindowSize));

        // Layered, so it can't be hit; not activatable, so the panel keeps its place in the focus order.
        _window = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST,
            WindowClass.Value, null, WINDOW_STYLE.WS_POPUP, 0, 0, _size, _size, HWND.Null, null, null, null);
        if (_window.IsNull)
        {
            throw new Win32Exception();
        }

        // A top-down 32-bit DIB: premultiplied BGRA, the format both WPF's renderer and UpdateLayeredWindow use.
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = _size,
                biHeight = -_size,
                biPlanes = 1,
                biBitCount = 32,
            },
        };
        void* bits;
        _memory = PInvoke.CreateCompatibleDC(HDC.Null);
        _bitmap = PInvoke.CreateDIBSection(_memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (_bitmap.IsNull || bits is null)
        {
            // Out of GDI resources: drawing into a null bitmap would bring the whole process down, so give up here.
            var error = new Win32Exception();
            if (!_bitmap.IsNull)
            {
                PInvoke.DeleteObject(_bitmap);
            }

            PInvoke.DeleteDC(_memory);
            PInvoke.DestroyWindow(_window);
            throw error;
        }

        _pixels = (byte*)bits;
        _previousBitmap = PInvoke.SelectObject(_memory, _bitmap);
    }

    /// <summary>Shows the icon where it was created, without taking activation.</summary>
    public void Show()
    {
        Redraw();
        PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    /// <summary>The icon's size grows a little, as if picked up.</summary>
    public void Lift()
    {
        var from = _scale.ScaleX;
        Animate(LiftTime, t =>
        {
            var scale = from + (LiftedScale - from) * (1 - (1 - t) * (1 - t));
            (_scale.ScaleX, _scale.ScaleY) = (scale, scale);
            Redraw();
        });
    }

    /// <summary>
    /// Moves the icon's center to <paramref name="center"/> (physical screen pixels) with the next frame: a mouse
    /// reports hundreds of moves a second, more than the screen can show.
    /// </summary>
    public void Follow(ScreenPoint center)
    {
        if (_following is null)
        {
            CompositionTarget.Rendering += OnRendering;
            _sinceRaised.Restart();
        }

        _following = center;
    }

    /// <summary>Puts the icon's center at <paramref name="center"/> (physical screen pixels), above everything else.</summary>
    public void PlaceCenter(ScreenPoint center)
    {
        _center = center;
        _sinceRaised.Restart();
        PInvoke.SetWindowPos(Handle, Topmost, center.X - _size / 2, center.Y - _size / 2, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Ends the drag: flies to <paramref name="target"/> and settles to normal size, or, with
    /// <paramref name="vanish"/>, shrinks and fades where it is. Closes itself, then calls <paramref name="done"/>.
    /// </summary>
    public void Land(ScreenPoint target, bool vanish, Action? done = null)
    {
        StopFollowing();
        var from = _center;
        var startScale = _scale.ScaleX;
        var endScale = vanish ? 0.4 : 1.0;
        Animate(LandTime, t =>
        {
            var eased = 1 - (1 - t) * (1 - t) * (1 - t);
            if (!vanish)
            {
                PlaceCenter(new ScreenPoint(
                    (int)Math.Round(from.X + (target.X - from.X) * eased), (int)Math.Round(from.Y + (target.Y - from.Y) * eased)));
            }

            var scale = startScale + (endScale - startScale) * eased;
            (_scale.ScaleX, _scale.ScaleY) = (scale, scale);
            _opacity = (byte)Math.Round(255 * (vanish ? 1 - eased : 1));
            Redraw();
            if (t >= 1)
            {
                Close();
                done?.Invoke();
            }
        });
    }

    /// <summary>How many times <c>UpdateLayeredWindow</c> refused to take the drawn icon.</summary>
    internal int FailedDraws { get; private set; }

    private HWND Handle => _window;

    private static string RegisterWindowClass()
    {
        const string name = "DeskGrid.DragGhost";
        fixed (char* className = name)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = WindowProcedure,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception();
            }
        }

        return name;
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        StopFollowing();
        _animation?.Stop();
        PInvoke.DestroyWindow(_window);
        PInvoke.SelectObject(_memory, _previousBitmap);
        PInvoke.DeleteObject(_bitmap);
        PInvoke.DeleteDC(_memory);
    }

    // Draws the icon at its current size into the DIB and hands it to the window, at the window's current place.
    private void Redraw()
    {
        if (_closed)
        {
            return;
        }

        var image = new RenderTargetBitmap(_size, _size, _dpi, _dpi, PixelFormats.Pbgra32);
        image.Render(_content);
        var stride = _size * 4;
        image.CopyPixels(new Int32Rect(0, 0, _size, _size), (nint)_pixels, stride * _size, stride);

        var position = new System.Drawing.Point(_center.X - _size / 2, _center.Y - _size / 2);
        var size = new SIZE(_size, _size);
        var source = new System.Drawing.Point(0, 0);
        var blend = new BLENDFUNCTION
        {
            BlendOp = (byte)PInvoke.AC_SRC_OVER,
            SourceConstantAlpha = _opacity,
            AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA,
        };
        if (!PInvoke.UpdateLayeredWindow(Handle, HDC.Null, &position, &size, _memory, &source, default, &blend,
            UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
        {
            FailedDraws++;
            Log.Error("Drawing the drag ghost failed", new Win32Exception());
        }
    }

    // Once per frame: where the cursor took the icon since the last frame.
    private void OnRendering(object? sender, EventArgs e)
    {
        if (_following is { } target && (target != _center || _sinceRaised.Elapsed >= RaiseInterval))
        {
            PlaceCenter(target);
        }
    }

    private void StopFollowing()
    {
        if (_following is not null)
        {
            CompositionTarget.Rendering -= OnRendering;
            _following = null;
        }
    }

    // Runs step(t) for t going from 0 to 1 over the given time. A new animation replaces the running one, so a drag
    // that ends before the lift is over doesn't have both fighting over the icon's size.
    private void Animate(TimeSpan time, Action<double> step)
    {
        _animation?.Stop();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = _animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / time);
            if (t >= 1)
            {
                timer.Stop();
            }

            step(t);
        };
        timer.Start();
    }
}
