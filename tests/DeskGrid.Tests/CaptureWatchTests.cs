using BrimDeck.Core;

namespace DeskGrid.Tests;

public sealed class CaptureWatchTests
{
    [Fact]
    public void SilentCapture_WhileTheMeterHearsSound_IsDeafOnlyAfterTheGrace()
    {
        var watch = new CaptureWatch();
        watch.Update(0, captureLoud: false, sessionPeak: .2f);
        Assert.False(watch.Deaf);
        watch.Update(CaptureWatch.Grace - .01, false, .2f);
        Assert.False(watch.Deaf);
        Assert.True(watch.Update(CaptureWatch.Grace + .01, false, .2f));
        Assert.True(watch.Deaf);
    }

    [Fact]
    public void QuietMeter_IsNeverDeaf()
    {
        var watch = new CaptureWatch();
        for (double t = 0; t < 5; t += .1) watch.Update(t, false, .001f);
        Assert.False(watch.Deaf);
    }

    [Fact]
    public void ACaptureThatHearsAgain_IsNotDeafAnyMore()
    {
        var watch = new CaptureWatch();
        for (double t = 0; t < 2; t += .1) watch.Update(t, false, .3f);
        Assert.True(watch.Deaf);
        watch.Update(2.1, captureLoud: true, .3f);
        Assert.False(watch.Deaf);
    }

    [Fact]
    public void ABriefQuietMoment_RestartsTheGrace()
    {
        var watch = new CaptureWatch();
        watch.Update(0, false, .3f);
        watch.Update(.5, false, .005f);   // the meter drops below the sound level: the wait starts over
        watch.Update(.9, false, .3f);
        Assert.False(watch.Deaf);
    }

    [Fact]
    public void Reset_ClearsTheState()
    {
        var watch = new CaptureWatch();
        for (double t = 0; t < 2; t += .1) watch.Update(t, false, .3f);
        watch.Reset();
        Assert.False(watch.Deaf);
    }
}
