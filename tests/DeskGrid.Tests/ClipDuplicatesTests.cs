using BrimDeck.Core;

namespace DeskGrid.Tests;

public sealed class ClipDuplicatesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SamePicture_ShortlyAfter_IsTheSameCopy() =>
        Assert.True(ClipDuplicates.IsSameCopy([1, 2, 3], T0, [1, 2, 3], T0.AddMilliseconds(125)));

    [Fact]
    public void SamePicture_AfterTheWindow_IsANewCopy() =>
        Assert.False(ClipDuplicates.IsSameCopy([1, 2, 3], T0, [1, 2, 3], T0 + ClipDuplicates.Window + TimeSpan.FromMilliseconds(1)));

    [Fact]
    public void OtherPicture_IsANewCopy() =>
        Assert.False(ClipDuplicates.IsSameCopy([1, 2, 3], T0, [1, 2, 4], T0.AddMilliseconds(100)));

    [Theory]
    [InlineData("SnippingTool", "explorer", "SnippingTool")]
    [InlineData("", "SnippingTool", "SnippingTool")]
    [InlineData("SnippingTool", "", "SnippingTool")]
    [InlineData("ScreenClippingHost", "SnippingTool", "SnippingTool")]
    public void Source_KeepsTheFirstUnlessUnknownOrAHelper(string first, string second, string expected) =>
        Assert.Equal(expected, ClipDuplicates.Source(first, second));
}
