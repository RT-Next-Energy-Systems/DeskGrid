// The refraction falloff curve (f(x) below) and the directional rim glow are adapted from
//   OverShifted/LiquidGlass, Copyright (c) 2026 Sepehr Kalanaki, MIT License
//   https://github.com/OverShifted/LiquidGlass
// and the idea of a rounded-rectangle distance field that bends the backdrop near its edge, with a slight
// contrast / saturation lift, from
//   shuding/liquid-glass, Copyright (c) 2025 Shu Ding, MIT License
//   https://github.com/shuding/liquid-glass
// Both are re-written here for the CPU in C#. The licenses are in THIRD-PARTY-NOTICES.md.

using DeskGrid.Desktop;

namespace DeskGrid.Glass;

/// <summary>A picture of the screen, as the pixels the glass looks through.</summary>
/// <param name="Area">Where on the screen the pixels are.</param>
/// <param name="Bgra">Top-down 32-bit pixels of <paramref name="Area"/>.</param>
internal sealed record ScreenImage(ScreenRect Area, byte[] Bgra);

/// <summary>How a piece of glass looks.</summary>
/// <param name="Scale">Pixels per device-independent unit of the monitor (DPI / 96).</param>
/// <param name="CornerRadius">In device-independent units.</param>
/// <param name="Tint">0 to 1: how much of <paramref name="Surface"/> is mixed over the glass. Lower is clearer.</param>
/// <param name="Surface">The color the glass is frosted with (red, green, blue).</param>
internal readonly record struct GlassStyle(
    double Scale, double CornerRadius, double Tint, (byte R, byte G, byte B) Surface, double DarkSide = 0.5);

/// <summary>
/// "Liquid glass": a rounded sheet that shows the screen behind it, lightly blurred, bent near its edge as through a
/// curved lens, with a bright rim. Drawn once from a picture of the screen (the sheet is a window of its own, so
/// the screen behind it can't be read live without capturing the sheet too).
/// </summary>
internal static class LiquidGlass
{
    private const double Blur = 3;           // Gaussian sigma, in device-independent units.
    private const double Refraction = 1.8;   // How far the rim bends the backdrop, as a share of the rim's width.
    private const double Chromatic = 0.12;   // Red and blue are bent a little differently at the rim.
    private const double Saturation = 1.2;
    private const double Contrast = 1.0;
    private const double Brightness = 0.03;
    private const double GlowWeight = 0.28;

    // OverShifted's falloff: 1 - b * (c * e)^(-d * x - a), 1 deep inside the glass and ~0.64 at its edge.
    private const double A = 0.7, B = 2.3, C = 5.2, D = 6.9;
    private const double Reach = 0.3;
    private const int Steps = 256;
    private static readonly double[] Bend = BuildBend();

    /// <summary>
    /// The glass for <paramref name="window"/> (screen pixels) as premultiplied BGRA, top-down, outside the rounded
    /// shape fully transparent. Parts of the window beyond <paramref name="screen"/> repeat the picture's edge.
    /// </summary>
    public static byte[] Render(ScreenImage screen, ScreenRect window, GlassStyle style)
    {
        int width = window.Width, height = window.Height;
        var output = new byte[Math.Max(width, 0) * Math.Max(height, 0) * 4];
        if (output.Length == 0)
        {
            return output;
        }

        var scale = style.Scale;
        var radius = Math.Min(style.CornerRadius * scale, Math.Min(width, height) / 2.0);
        var rim = Math.Min(Math.Clamp(Math.Min(width, height) * 0.14, 14 * scale, 30 * scale), Math.Min(width, height) / 2.0);
        var depth = rim * Refraction;
        var sigma = Blur * scale;
        var pad = (int)Math.Ceiling(depth + 3 * sigma) + 2;
        var backdrop = Blurred(screen, window, pad, sigma);

        double half = width / 2.0, halfY = height / 2.0;
        var glowWidth = rim * 0.7;
        var line = 1.5 * scale;
        var surface = (R: style.Surface.R / 255.0, G: style.Surface.G / 255.0, B: style.Surface.B / 255.0);
        var tint = Math.Clamp(style.Tint, 0, 1);
        var darkSide = Math.Clamp(style.DarkSide, 0, 1);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double px = x + 0.5 - half, py = y + 0.5 - halfY;
                var inside = -Sdf(px, py, half, halfY, radius);
                var coverage = Math.Clamp(inside + 0.5, 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                var depthIn = Math.Max(inside, 0);
                var (nx, ny) = InwardNormal(px, py, half, halfY, radius);
                var bend = depth * BendAt(depthIn / rim);

                // Where the backdrop is looked up: further in than the pixel itself, so the rim shows what lies deeper.
                double sx = x + 0.5 + pad, sy = y + 0.5 + pad;
                var red = backdrop.At(sx + nx * bend * (1 + Chromatic), sy + ny * bend * (1 + Chromatic)).R;
                var (_, green, _) = backdrop.At(sx + nx * bend, sy + ny * bend);
                var blue = backdrop.At(sx + nx * bend * (1 - Chromatic), sy + ny * bend * (1 - Chromatic)).B;

                var luma = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
                red = Lift(red, luma);
                green = Lift(green, luma);
                blue = Lift(blue, luma);

                red = red * (1 - tint) + surface.R * tint;
                green = green * (1 - tint) + surface.G * tint;
                blue = blue * (1 - tint) + surface.B * tint;

                if (depthIn < glowWidth)
                {
                    // The bright side of the rim faces one corner, the dark side the other; the style says how strong
                    // the dark side is (the user's choice: some like depth, some read it as a dirty shadow).
                    var glow = Math.Sin(Math.Atan2(py, px) - 0.5);
                    var near = 1 - Smooth(0, glowWidth, depthIn);
                    var factor = 1 + (glow > 0 ? glow : glow * darkSide) * GlowWeight * near;
                    var edge = (1 - Smooth(0, line, depthIn)) * (0.16 + 0.2 * Math.Max(glow, 0));
                    red = red * factor + edge;
                    green = green * factor + edge;
                    blue = blue * factor + edge;
                }

                var i = (y * width + x) * 4;
                output[i] = Byte(blue * coverage);
                output[i + 1] = Byte(green * coverage);
                output[i + 2] = Byte(red * coverage);
                output[i + 3] = Byte(coverage);
            }
        }

        return output;
    }

    private static double Lift(double channel, double luma) =>
        (luma + (channel - luma) * Saturation - 0.5) * Contrast + 0.5 + Brightness;

    private static byte Byte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);

    // 1 at the edge of the glass, falling to 0 at one rim's width inside it.
    private static double BendAt(double t)
    {
        if (t >= 1)
        {
            return 0;
        }

        var position = Math.Max(t, 0) * (Steps - 1);
        var index = (int)position;
        return Bend[index] + (Bend[Math.Min(index + 1, Steps - 1)] - Bend[index]) * (position - index);
    }

    private static double[] BuildBend()
    {
        // x runs 0 to Reach over the rim. The curve is steep: most of the bend is in the first fifth of the rim,
        // which makes the bright, lens-like band at the very edge.
        static double Fall(double x) => 1 - B * Math.Pow(C * Math.E, -D * x - A);
        var bend = new double[Steps];
        var edge = 1 - Fall(0);
        for (var i = 0; i < Steps; i++)
        {
            bend[i] = (1 - Fall(Reach * i / (Steps - 1))) / edge;
        }

        return bend;
    }

    // Signed distance to a rounded rectangle centered on the origin: negative inside.
    private static double Sdf(double px, double py, double halfX, double halfY, double radius)
    {
        double qx = Math.Abs(px) - halfX + radius, qy = Math.Abs(py) - halfY + radius;
        return Math.Min(Math.Max(qx, qy), 0) + Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) - radius;
    }

    // The unit vector pointing from the edge towards the middle (the way down the distance field).
    private static (double X, double Y) InwardNormal(double px, double py, double halfX, double halfY, double radius)
    {
        double qx = Math.Abs(px) - halfX + radius, qy = Math.Abs(py) - halfY + radius;
        double gx, gy;
        if (qx > 0 && qy > 0)
        {
            var length = Math.Sqrt(qx * qx + qy * qy);
            (gx, gy) = (qx / length, qy / length);
        }
        else if (qx > qy)
        {
            (gx, gy) = (1, 0);
        }
        else
        {
            (gx, gy) = (0, 1);
        }

        return (-gx * Math.Sign(px), -gy * Math.Sign(py));
    }

    private static double Smooth(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // The screen under the window plus a margin, blurred, as something to look up bilinearly.
    private static Pixels Blurred(ScreenImage screen, ScreenRect window, int pad, double sigma)
    {
        int width = window.Width + 2 * pad, height = window.Height + 2 * pad;
        var data = new float[width * height * 3];
        var area = screen.Area;
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Clamp(window.Top - pad + y - area.Top, 0, area.Height - 1);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp(window.Left - pad + x - area.Left, 0, area.Width - 1);
                var from = (sy * area.Width + sx) * 4;
                var to = (y * width + x) * 3;
                data[to] = screen.Bgra[from + 2] / 255f;     // red
                data[to + 1] = screen.Bgra[from + 1] / 255f; // green
                data[to + 2] = screen.Bgra[from] / 255f;     // blue
            }
        }

        // Three box blurs come close to a Gaussian.
        var box = (int)Math.Round((Math.Sqrt(12 * sigma * sigma / 3 + 1) - 1) / 2);
        if (box > 0)
        {
            var scratch = new float[data.Length];
            for (var pass = 0; pass < 3; pass++)
            {
                BoxBlur(data, scratch, width, height, box, horizontal: true);
                BoxBlur(scratch, data, width, height, box, horizontal: false);
            }
        }

        return new Pixels(data, width, height);
    }

    // One box blur of the given radius along rows or columns, the picture's edge repeating.
    private static void BoxBlur(float[] source, float[] target, int width, int height, int radius, bool horizontal)
    {
        int lines = horizontal ? height : width, length = horizontal ? width : height;
        int step = horizontal ? 3 : width * 3, lineStep = horizontal ? width * 3 : 3;
        var window = 2 * radius + 1f;
        for (var line = 0; line < lines; line++)
        {
            var origin = line * lineStep;
            for (var channel = 0; channel < 3; channel++)
            {
                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    sum += source[origin + Math.Clamp(k, 0, length - 1) * step + channel];
                }

                for (var i = 0; i < length; i++)
                {
                    target[origin + i * step + channel] = sum / window;
                    sum += source[origin + Math.Min(i + radius + 1, length - 1) * step + channel]
                        - source[origin + Math.Max(i - radius, 0) * step + channel];
                }
            }
        }
    }

    private sealed record Pixels(float[] Data, int Width, int Height)
    {
        // Bilinear lookup at a position in pixel units (the center of the first pixel is 0.5).
        public (double R, double G, double B) At(double x, double y)
        {
            double fx = Math.Clamp(x - 0.5, 0, Width - 1), fy = Math.Clamp(y - 0.5, 0, Height - 1);
            int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
            double tx = fx - x0, ty = fy - y0;
            double Mix(int channel) =>
                (Data[(y0 * Width + x0) * 3 + channel] * (1 - tx) + Data[(y0 * Width + x1) * 3 + channel] * tx) * (1 - ty)
                + (Data[(y1 * Width + x0) * 3 + channel] * (1 - tx) + Data[(y1 * Width + x1) * 3 + channel] * tx) * ty;
            return (Mix(0), Mix(1), Mix(2));
        }
    }
}
