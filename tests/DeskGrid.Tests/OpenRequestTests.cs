using DeskGrid.Taskbar;

namespace DeskGrid.Tests;

public sealed class OpenRequestTests
{
    [Fact]
    public void Arguments_RoundTripThroughParse() =>
        Assert.Equal("Dev tools", OpenRequest.Parse(["--open", "Dev tools"]));

    [Fact]
    public void Arguments_QuoteTheFolderName() =>
        Assert.Equal("--open \"Dev tools\"", OpenRequest.Arguments("Dev tools"));

    [Theory]
    [InlineData]
    [InlineData("--open")]
    [InlineData("--other", "Dev")]
    [InlineData("--open", "")]
    public void Parse_ReturnsNull_WhenNotAnOpenRequest(params string[] args) =>
        Assert.Null(OpenRequest.Parse(args));

    [Fact]
    public void Parse_IgnoresTheCaseOfTheSwitch() =>
        Assert.Equal("Dev", OpenRequest.Parse(["--OPEN", "Dev"]));

    [Fact]
    public void AllFoldersArguments_ParseToAllFolders() =>
        Assert.Equal(OpenRequest.AllFolders, OpenRequest.Parse([OpenRequest.AllFoldersArguments]));

    [Theory]
    [InlineData("--all", "*")]
    [InlineData("  --all ", "*")]
    [InlineData("--open \"Dev tools\"", "Dev tools")]
    [InlineData("--OPEN \"開發工具\"", "開發工具")]
    public void ParseArguments_ReadsWhatAShortcutStores(string arguments, string expected) =>
        Assert.Equal(expected, OpenRequest.ParseArguments(arguments));

    [Theory]
    [InlineData("")]
    [InlineData("--open")]
    [InlineData("--open Dev")]
    [InlineData("--open \"\"")]
    [InlineData("--other \"Dev\"")]
    public void ParseArguments_ReturnsNull_WhenNotAnOpenRequest(string arguments) =>
        Assert.Null(OpenRequest.ParseArguments(arguments));

    [Theory]
    [InlineData("Dev tools")]
    [InlineData("開發工具")]
    public void ParseArguments_UndoesArguments(string folder) =>
        Assert.Equal(folder, OpenRequest.ParseArguments(OpenRequest.Arguments(folder)));

    [Fact]
    public void AllFolders_IsNoValidFolderName() =>
        Assert.Contains(OpenRequest.AllFolders, new string(System.IO.Path.GetInvalidFileNameChars()));
}

public sealed class PinShortcutTests
{
    [Theory]
    [InlineData("Dev tools")]
    [InlineData("開發工具")]
    [InlineData("a very long folder name that goes on and on and on, well past what an app id may hold, and then some more")]
    public void AppId_IsAValidAppUserModelId(string folderName)
    {
        var id = DeskGrid.Taskbar.PinShortcut.AppId(folderName);

        Assert.InRange(id.Length, 1, 128);
        Assert.DoesNotContain(' ', id);
        Assert.All(id, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c == '.'));
    }

    [Fact]
    public void AppId_DependsOnTheFolder_ButNotOnItsCase()
    {
        Assert.Equal(DeskGrid.Taskbar.PinShortcut.AppId("Dev"), DeskGrid.Taskbar.PinShortcut.AppId("DEV"));
        Assert.NotEqual(DeskGrid.Taskbar.PinShortcut.AppId("Dev"), DeskGrid.Taskbar.PinShortcut.AppId("Games"));
    }
}
