using BrimDeck.Core;

namespace DeskGrid.Tests;

public sealed class StandInDisplayTests
{
    private static readonly string[] Monitors = ["a", "b", "c"];

    [Fact]
    public void NoFreeMonitor_GivesNone() =>
        Assert.Null(DisplayLayout.StandIn(["a"], new HashSet<string?> { "a" }, null, "a"));

    [Fact]
    public void NeverTheMonitorAPanelOwns() =>
        Assert.Equal("b", DisplayLayout.StandIn(Monitors, new HashSet<string?> { "a" }, null, "a"));

    [Fact]
    public void PrefersTheMonitorUnderThePointer() =>
        Assert.Equal("c", DisplayLayout.StandIn(Monitors, new HashSet<string?> { "a" }, null, "c"));

    [Fact]
    public void StaysWhereItIs_WhileThatMonitorIsFree() =>
        Assert.Equal("b", DisplayLayout.StandIn(Monitors, new HashSet<string?> { "a" }, "b", "c"));

    [Fact]
    public void MovesOn_WhenItsMonitorIsGone() =>
        Assert.Equal("c", DisplayLayout.StandIn(["a", "c"], new HashSet<string?> { "a" }, "b", null));
}
