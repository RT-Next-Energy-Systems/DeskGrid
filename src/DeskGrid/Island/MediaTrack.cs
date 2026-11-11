using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskGrid.Island;

/// <summary>
/// What a media player (a music app, or video in a browser) has told Windows about: what it plays, by whom, whether it is
/// playing, its cover, and which player it is (<see cref="Source"/>, so the buttons control that one).
/// </summary>
internal sealed record MediaTrack(string Title, string Artist, bool Playing, ImageSource? Cover, string Source = "", MediaTimeline? Timeline = null, Color? Accent = null)
{
    /// <summary>
    /// Picks a lively accent color from <paramref name="cover"/> by averaging pixels that are neither near-black nor near-white
    /// and boosting their saturation a little so the progress bar stays readable on a dark pill; <see langword="null"/> when
    /// the cover is grey or cannot be read.
    /// </summary>
    public static Color? DominantColor(BitmapSource? cover)
    {
        if (cover is null || cover.PixelWidth <= 0 || cover.PixelHeight <= 0)
        {
            return null;
        }

        try
        {
            var bgra = cover.Format == PixelFormats.Bgra32 || cover.Format == PixelFormats.Pbgra32
                ? cover
                : new FormatConvertedBitmap(cover, PixelFormats.Bgra32, null, 0);
            var (w, h) = (bgra.PixelWidth, bgra.PixelHeight);
            var stride = w * 4;
            var pixels = new byte[h * stride];
            bgra.CopyPixels(pixels, stride, 0);

            long sumR = 0, sumG = 0, sumB = 0, weightSum = 0;
            var step = Math.Max(1, Math.Min(w, h) / 16);
            for (var y = 0; y < h; y += step)
            {
                var row = y * stride;
                for (var x = 0; x < w; x += step)
                {
                    var i = row + x * 4;
                    int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                    if (a < 128)
                    {
                        continue;
                    }

                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));
                    var chroma = max - min;
                    if (max < 35 || min > 235 || chroma < 20)
                    {
                        continue; // Too dark, too bright, or nearly grey.
                    }

                    var weight = chroma;
                    sumR += r * weight;
                    sumG += g * weight;
                    sumB += b * weight;
                    weightSum += weight;
                }
            }

            if (weightSum == 0)
            {
                return null;
            }

            return Readable(
                (byte)(sumR / weightSum),
                (byte)(sumG / weightSum),
                (byte)(sumB / weightSum));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Lifts a dark color so it stands out on the black pill and card.
    internal static Color Readable(byte r, byte g, byte b)
    {
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        if (luminance >= 115)
        {
            return Color.FromRgb(r, g, b);
        }

        var lift = (115 - luminance) / 255.0;
        byte Boost(byte c) => (byte)Math.Clamp(Math.Round(c + (255 - c) * lift), 0, 255);
        return Color.FromRgb(Boost(r), Boost(g), Boost(b));
    }
}

/// <summary>
/// Where a player was in what it plays when it last said so: the length, the position, and the time of that report. Between
/// reports the position is worked out from the clock while the player plays.
/// </summary>
internal sealed record MediaTimeline(TimeSpan Duration, TimeSpan Position, DateTimeOffset At)
{
    public TimeSpan PositionAt(DateTimeOffset now, bool playing)
    {
        var position = playing ? Position + (now - At) : Position;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
    }

    /// <summary>How far the song is, 0 to 1; <see langword="null"/> when the player gives no length (a live stream).</summary>
    public double? Fraction(DateTimeOffset now, bool playing) =>
        Duration <= TimeSpan.Zero ? null : PositionAt(now, playing) / Duration;

    /// <summary>The position at <paramref name="fraction"/> (0 to 1) of the song's length.</summary>
    public TimeSpan SeekTarget(double fraction) =>
        Duration <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks((long)Math.Round(Duration.Ticks * Math.Clamp(fraction, 0, 1)));

    /// <summary>
    /// The timeline to go by after the user moved the song to <paramref name="seeked"/>: that position from the moment it was
    /// moved, unless the player's own <paramref name="reported"/> one is newer (some players move and report nothing).
    /// </summary>
    public static MediaTimeline? AfterSeek(MediaTimeline? reported, (TimeSpan Position, DateTimeOffset At)? seeked) =>
        reported is not null && seeked is { } moved && reported.At <= moved.At ? reported with { Position = moved.Position, At = moved.At } : reported;

    /// <summary>"1:05" or "1:02:03".</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }
}

/// <summary>What the buttons and progress bar of a music pill or card do; each is told which player (<see cref="MediaTrack.Source"/>) to act on.</summary>
internal sealed record MusicActions(Action<string> Previous, Action<string> PlayPause, Action<string> Next, Action<string, TimeSpan>? Seek = null, Action<string>? Open = null);

