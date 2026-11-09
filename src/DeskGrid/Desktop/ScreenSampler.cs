using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Desktop;

/// <summary>Reads what the desktop looks like (the wallpaper) behind a screen rectangle.</summary>
internal static class ScreenSampler
{
    /// <summary>
    /// How bright the wallpaper is in <paramref name="area"/> (0 = black, 1 = white): the level that a quarter
    /// of the pixels exceed, so the white text and dark halo drawn on top don't sway it. <see langword="null"/>
    /// if the desktop isn't what's showing there (a window or the taskbar covers it) or the screen can't be read.
    /// </summary>
    public static double? DesktopBrightness(ScreenRect area) =>
        area.Width <= 0 || area.Height <= 0 || !IsDesktopVisible(area) || Capture(area) is not { } bgra ? null : UpperQuartile(bgra);

    /// <summary>
    /// What is on screen in <paramref name="area"/> as top-down 32-bit BGRA pixels (windows on top included), or
    /// <see langword="null"/> if it can't be read.
    /// </summary>
    public static unsafe byte[]? Capture(ScreenRect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = area.Width,
                biHeight = -area.Height, // Top-down.
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };

        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        void* bits = null;
        var bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        try
        {
            if (bitmap.IsNull || bits == null)
            {
                return null;
            }

            var previous = PInvoke.SelectObject(memory, bitmap);
            var copied = PInvoke.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, ROP_CODE.SRCCOPY);
            PInvoke.SelectObject(memory, previous);
            return copied ? new ReadOnlySpan<byte>(bits, area.Width * area.Height * 4).ToArray() : null;
        }
        finally
        {
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memory);
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    // Bgra pixels in, Rec. 709 luma at the 75th percentile out.
    private static double UpperQuartile(byte[] bgra)
    {
        Span<int> histogram = stackalloc int[256];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            histogram[(int)Math.Round(0.0722 * bgra[i] + 0.7152 * bgra[i + 1] + 0.2126 * bgra[i + 2])]++;
        }

        var threePerFour = bgra.Length / 4 * 3 / 4;
        var seen = 0;
        for (var level = 0; level < 256; level++)
        {
            seen += histogram[level];
            if (seen > threePerFour)
            {
                return level / 255.0;
            }
        }

        return 1;
    }

    // Reading the screen also reads whatever is on top, so only trust spots where the desktop shows:
    // the topmost window there belongs to Explorer's desktop (Progman, or the WorkerW of older Windows).
    private static bool IsDesktopVisible(ScreenRect area)
    {
        var xs = new[] { area.Left, (area.Left + area.Right) / 2, area.Right - 1 };
        var ys = new[] { area.Top, area.Bottom - 1 };
        return xs.SelectMany(_ => ys, (x, y) => new System.Drawing.Point(x, y)).All(IsDesktopAt);
    }

    private static unsafe bool IsDesktopAt(System.Drawing.Point point)
    {
        var root = PInvoke.GetAncestor(PInvoke.WindowFromPoint(point), GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root.IsNull)
        {
            return false;
        }

        var name = stackalloc char[16];
        var length = PInvoke.GetClassName(root, name, 16);
        return new string(name, 0, length) is "Progman" or "WorkerW";
    }
}
