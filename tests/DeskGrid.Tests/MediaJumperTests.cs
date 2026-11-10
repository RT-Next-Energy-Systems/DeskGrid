using DeskGrid.Island;

namespace DeskGrid.Tests;

public class MediaJumperTests
{
    [Theory]
    [InlineData("Chrome", "chrome", true)]
    [InlineData("MSEdge", "msedge", true)]
    [InlineData("Spotify.exe", "Spotify", true)]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "AppleMusic", true)]
    [InlineData("Chrome", "notepad", false)]
    [InlineData("", "chrome", false)]
    [InlineData("Chrome", "", false)]
    public void Matches_ComparesThePlayerIdWithTheProcessName(string source, string process, bool expected) =>
        Assert.Equal(expected, MediaJumper.Matches(source, process));

    [Theory]
    [InlineData("Song - Artist", "Song")]
    [InlineData("Song", "Song")]
    [InlineData("  Song  ", "Song")]
    public void Needle_IsTheSongPartOfTheTitle(string title, string expected) =>
        Assert.Equal(expected, MediaJumper.Needle(title));
}
