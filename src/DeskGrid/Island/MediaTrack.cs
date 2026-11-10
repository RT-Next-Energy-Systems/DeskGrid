using System.Windows.Media;

namespace DeskGrid.Island;

/// <summary>
/// What a media player (a music app, or video in a browser) has told Windows about: what it plays, by whom, whether it is
/// playing, its cover, and which player it is (<see cref="Source"/>, so the buttons control that one).
/// </summary>
internal sealed record MediaTrack(string Title, string Artist, bool Playing, ImageSource? Cover, string Source = "");

/// <summary>What the buttons of a music pill do; each is told which player (<see cref="MediaTrack.Source"/>) to act on.</summary>
internal sealed record MusicActions(Action<string> Previous, Action<string> PlayPause, Action<string> Next);
