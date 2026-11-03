using DeskGrid.Desktop;

namespace DeskGrid.Tests;

public sealed class DesktopHostTests
{
    [Theory]
    [InlineData(0x000Du)] // SPI_ICONHORIZONTALSPACING
    [InlineData(0x0018u)] // SPI_ICONVERTICALSPACING
    [InlineData(0x002Eu)] // SPI_SETICONMETRICS
    [InlineData(0x002Fu)] // SPI_SETWORKAREA: the taskbar moved or was resized
    [InlineData(0x009Fu)] // SPI_SETLOGICALDPIOVERRIDE
    public void ChangesDesktopLayout_IsTrue_ForSettingsThatMoveTheCells(uint action) =>
        Assert.True(DesktopHost.ChangesDesktopLayout(action));

    [Theory]
    [InlineData(0u)] // Theme and colour changes broadcast with no SPI value.
    [InlineData(0x0014u)] // SPI_SETDESKWALLPAPER
    [InlineData(0x0057u)] // SPI_SETMOUSEHOVERTIME
    public void ChangesDesktopLayout_IsFalse_ForOtherSettings(uint action) =>
        Assert.False(DesktopHost.ChangesDesktopLayout(action));
}
