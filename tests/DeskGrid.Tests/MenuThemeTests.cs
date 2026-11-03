using DeskGrid.Shell;

namespace DeskGrid.Tests;

public sealed class MenuThemeTests
{
    // The functions it calls are undocumented, found by ordinal: this notices if a Windows version drops them.
    [Fact]
    public void FollowSystem_FindsItsFunctions_AndMayBeCalledAgain()
    {
        Assert.True(MenuTheme.FollowSystem());
        Assert.True(MenuTheme.FollowSystem());
    }
}
