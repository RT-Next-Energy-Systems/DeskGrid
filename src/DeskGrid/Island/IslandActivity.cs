namespace DeskGrid.Island;

/// <summary>One thing the island shows for a while: a line of text.</summary>
internal sealed record IslandActivity(string Text, TimeSpan Duration)
{
    /// <summary>How long a line stays when the sender names no time.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    /// <summary>Longer text is not a message for a pill; senders are told so instead of the text being cut.</summary>
    public const int MaxTextLength = 200;
}
