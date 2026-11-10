using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace DeskGrid.Shell;

/// <summary>Asks the Shell for a file's icon, the same one Explorer shows. Call on an STA thread.</summary>
internal static class ShellImage
{
    private const int SmallestFullSize = 48;
    private const int FrameProbeInset = 12;
    private const byte FrameAlpha = 20;

    /// <summary>
    /// Returns a frozen <paramref name="size"/>×<paramref name="size"/> icon (a shortcut's target icon, without the arrow
    /// overlay), or <see langword="null"/> if the Shell can't produce one. Never a content thumbnail.
    /// </summary>
    public static BitmapSource? GetIcon(string path, int size)
    {
        try
        {
            // A shortcut to an app without a file path is asked for through the app itself, not the .lnk.
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && TryGetLinkTargetIcon(path, size) is { } appIcon)
            {
                return appIcon;
            }

            PInvoke.SHCreateItemFromParsingName(path, null, out IShellItemImageFactory factory).ThrowOnFailure();
            try
            {
                return TryGetIcon(factory, size) ?? throw new COMException("The Shell has no image for this item.");
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"No shell icon for {path}: {exception.Message}");
            return null;
        }
    }

    // Null if the factory can't give an image.
    private static BitmapSource? TryGetIcon(IShellItemImageFactory factory, int size)
    {
        try
        {
            var icon = GetImage(factory, size);

            // An icon that has no large image comes back as a small one centered in a framed box. Ask for the
            // small size instead, which fills the whole bitmap (the preview scales it up).
            return LooksFramed(icon) ? GetImage(factory, SmallestFullSize) : icon;
        }
        catch (COMException exception)
        {
            Log.Info($"Shell image failed: {exception.Message} (0x{exception.HResult:X8})");
            return null;
        }
    }

    // A shortcut to a Store app (Slack, WhatsApp...) has no file path, only a shell item ID list
    // (shell:AppsFolder\<AppUserModelID>); asking for the .lnk's own icon gives a blank document.
    private static unsafe BitmapSource? TryGetLinkTargetIcon(string path, int size)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, STGM.STGM_READ);
            Span<char> filePath = stackalloc char[260];
            fixed (char* buffer = filePath)
            {
                link.GetPath(buffer, filePath.Length, null, 0);
            }

            if (filePath[0] != '\0')
            {
                return null;
            }

            link.GetIDList(out var idList);
            if (idList is null)
            {
                return null;
            }

            try
            {
                PInvoke.SHCreateItemFromIDList(*idList, out IShellItem item).ThrowOnFailure();
                try
                {
                    // A Store app's item built straight from the list answers with a generic icon; the same item
                    // looked up again by its parsing name (shell:AppsFolder\<AppUserModelID>) has the real one.
                    item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out var name);
                    string parsingName;
                    try
                    {
                        parsingName = name.ToString();
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem((nint)name.Value);
                    }

                    if (TryGetStoreAppIcon(parsingName, size) is { } appIcon)
                    {
                        return appIcon;
                    }

                    // Other apps without a file path (a known folder in their parsing name) answer from the item itself.
                    return TryGetIcon((IShellItemImageFactory)item, size);
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                PInvoke.ILFree(idList);
            }
        }
        catch (COMException exception)
        {
            Log.Info($"No link target for {path}: {exception.Message}");
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    // The icon of an entry in the Apps folder (Slack, WhatsApp...), or null if there is none.
    private static BitmapSource? TryGetStoreAppIcon(string parsingName, int size)
    {
        if (!PInvoke.SHCreateItemFromParsingName(@"shell:AppsFolder\" + parsingName, null, out IShellItemImageFactory app).Succeeded)
        {
            return null;
        }

        try
        {
            // Some packages (measured: WhatsApp) have no logo of the size icon-only asks for and get a generic
            // document; the same request without icon-only finds a smaller logo (88 px).
            foreach (var flags in new[] { SIIGBF.SIIGBF_ICONONLY, SIIGBF.SIIGBF_RESIZETOFIT })
            {
                var icon = GetImage(app, size, flags);
                if (!LooksFramed(icon))
                {
                    return icon;
                }
            }

            return null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(app);
        }
    }

    private static BitmapSource GetImage(IShellItemImageFactory factory, int size, SIIGBF flags = SIIGBF.SIIGBF_ICONONLY)
    {
        factory.GetImage(new SIZE(size, size), flags, out var bitmap);
        using (bitmap)
        {
            return ToBitmapSource(new HBITMAP(bitmap.DangerousGetHandle()));
        }
    }

    // The frame is a faint line at the edge (alpha 38, 77, 51, 26 over the first pixels) with a transparent
    // gap inside it, measured on a 256 px image.
    private static bool LooksFramed(BitmapSource image)
    {
        if (image.PixelWidth < FrameProbeInset * 4)
        {
            return false;
        }

        var middle = image.PixelHeight / 2;
        return AlphaAt(image, 1, middle) > FrameAlpha && AlphaAt(image, FrameProbeInset, middle) == 0
            && AlphaAt(image, middle, 1) > FrameAlpha && AlphaAt(image, middle, FrameProbeInset) == 0;
    }

    private static byte AlphaAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    // CreateBitmapSourceFromHBitmap drops the alpha channel, so copy the DIB section's pixels ourselves.
    private static unsafe BitmapSource ToBitmapSource(HBITMAP bitmap)
    {
        DIBSECTION dib;
        if (PInvoke.GetObject(bitmap, sizeof(DIBSECTION), &dib) == 0 || dib.dsBm.bmBitsPixel != 32 || dib.dsBm.bmBits == null)
        {
            throw new COMException("Shell image is not a 32-bit DIB section.");
        }

        int width = dib.dsBm.bmWidth, height = dib.dsBm.bmHeight, stride = dib.dsBm.bmWidthBytes;
        var bottomUp = dib.dsBmih.biHeight > 0;
        var pixels = new byte[stride * height];
        var source = (byte*)dib.dsBm.bmBits;
        for (var row = 0; row < height; row++)
        {
            var sourceRow = bottomUp ? height - 1 - row : row;
            new ReadOnlySpan<byte>(source + sourceRow * stride, stride).CopyTo(pixels.AsSpan(row * stride));
        }

        // Straight (not premultiplied) alpha: measured, many edge pixels have color above alpha.
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
