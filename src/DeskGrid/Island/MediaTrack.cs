using System.Windows.Media;

namespace DeskGrid.Island;

/// <summary>
/// What a media player (a music app, or video in a browser) has told Windows about: what it plays, by whom, whether it is
/// playing, its cover, and which player it is (<see cref="Source"/>, so the buttons control that one).
/// </summary>
internal sealed record MediaTrack(string Title, string Artist, bool Playing, ImageSource? Cover, string Source = "", MediaTimeline? Timeline = null);

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

    /// <summary>"1:05" or "1:02:03".</summary>
    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
}

/// <summary>What the buttons of a music pill do; each is told which player (<see cref="MediaTrack.Source"/>) to act on.</summary>
internal sealed record MusicActions(Action<string> Previous, Action<string> PlayPause, Action<string> Next);
