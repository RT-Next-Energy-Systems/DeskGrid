namespace BrimDeck.Core;

// DeskGrid change: a screenshot taken to the clipboard (Win+Shift+S) is written twice a moment apart by the snipping tool, the same
// picture each time (measured: two updates 125 ms apart, equal PNG bytes). The history keeps it once.
public static class ClipDuplicates
{
    // How long after a picture was recorded the same picture counts as the same copy.
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    // Helper processes of the screenshot tools; a copy they make for the tool is credited to the tool.
    private static readonly string[] Helpers = ["ScreenClippingHost"];

    // recorded, picture: hashes of the two pictures' bytes.
    public static bool IsSameCopy(ReadOnlySpan<byte> recorded, DateTimeOffset recordedAt, ReadOnlySpan<byte> picture, DateTimeOffset now) =>
        now - recordedAt >= TimeSpan.Zero && now - recordedAt <= Window && recorded.SequenceEqual(picture);

    // The source to show for one copy that was seen twice: the first one's, unless it is unknown or only a helper's.
    public static string Source(string first, string second)
    {
        if (second.Length == 0) return first;
        if (first.Length == 0) return second;
        return Helpers.Any(helper => string.Equals(first, helper, StringComparison.OrdinalIgnoreCase)) ? second : first;
    }
}
