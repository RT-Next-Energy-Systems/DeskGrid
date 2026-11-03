using DeskGrid.Taskbar;

namespace DeskGrid.Tests;

public sealed class OpenMessageTests
{
    [Theory]
    [InlineData("Dev tools", 120, 1040)]
    [InlineData("*", -2360, -5)] // A monitor left of / above the primary one has negative coordinates.
    public void Format_RoundTripsThroughTryParse(string name, int x, int y) =>
        Assert.Equal(new OpenMessage(name, x, y), OpenMessage.TryParse(new OpenMessage(name, x, y).Format()));

    [Fact]
    public void TryParse_KeepsSpacesInTheName() =>
        Assert.Equal("a b  c", OpenMessage.TryParse("1\t2\ta b  c")!.Value.Name);

    [Theory]
    [InlineData("")]
    [InlineData("Dev tools")]
    [InlineData("1\t2")]
    [InlineData("1\t2\t")]
    [InlineData("x\t2\tDev")]
    [InlineData("1\ty\tDev")]
    public void TryParse_ReturnsNull_ForAMalformedLine(string line) =>
        Assert.Null(OpenMessage.TryParse(line));
}
