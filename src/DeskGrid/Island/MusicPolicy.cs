using DeskGrid.Config;

namespace DeskGrid.Island;

/// <summary>When the island shows the music that is playing, for each <see cref="IslandMusicMode"/>. Pure, so it can be tested.</summary>
internal static class MusicPolicy
{
    /// <summary>The island stays up with the song for as long as it plays.</summary>
    public static bool ShowsWhilePlaying(IslandMusicMode mode, MediaTrack? now) =>
        mode == IslandMusicMode.WhilePlaying && now is { Playing: true };

    /// <summary>
    /// The island says what is playing for a few seconds: when a song that has not been announced yet plays. Pausing and
    /// resuming the same song says nothing again; the song counts as announced until the player has none (it was closed).
    /// </summary>
    public static bool Announces(IslandMusicMode mode, MediaTrack? announced, MediaTrack? now) =>
        mode == IslandMusicMode.OnChange && now is { Playing: true } && !SameSong(announced, now);

    /// <summary>Every player that plays now (music or video), when the island is to stay up for as long as something plays.</summary>
    public static IReadOnlyList<MediaTrack> PlayingToShow(IslandMusicMode mode, IReadOnlyList<MediaTrack> tracks) =>
        mode == IslandMusicMode.WhilePlaying ? [.. tracks.Where(t => t.Playing)] : [];

    /// <summary>
    /// The one of several that is up now: they take turns, each for <paramref name="turn"/>, by the clock, so nothing has to
    /// remember whose turn it is. <see langword="null"/> when there are none.
    /// </summary>
    public static T? Rotate<T>(IReadOnlyList<T> playing, TimeSpan clock, TimeSpan turn) where T : class =>
        playing.Count == 0 ? null : playing[(int)(clock.Ticks / turn.Ticks % playing.Count)];

    public static bool SameSong(MediaTrack? a, MediaTrack? b) =>
        a is not null && b is not null && a.Title == b.Title && a.Artist == b.Artist;

    /// <summary>"Title — Artist", or the title alone when the player gives no artist.</summary>
    public static string Label(MediaTrack track) => track.Artist.Length == 0 ? track.Title : $"{track.Title} — {track.Artist}";
}
