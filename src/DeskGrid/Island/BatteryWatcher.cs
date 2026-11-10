using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace DeskGrid.Island;

/// <summary>What the battery reading was: the charge in percent and whether the charger is plugged in.</summary>
internal readonly record struct BatteryReading(int Percent, bool Charging);

/// <summary>What changed between two readings that is worth a line on the island.</summary>
internal enum BatteryEvent
{
    None,
    ChargerConnected,
    ChargerRemoved,
    Low,
    Critical,
}

/// <summary>Which change between two battery readings the island tells of. Pure, so it can be tested.</summary>
internal static class BatteryRules
{
    internal const int LowPercent = 20;
    internal const int CriticalPercent = 10;

    public static BatteryEvent Between(BatteryReading before, BatteryReading now)
    {
        if (!before.Charging && now.Charging)
        {
            return BatteryEvent.ChargerConnected;
        }

        if (before.Charging && !now.Charging)
        {
            return BatteryEvent.ChargerRemoved;
        }

        if (!now.Charging && now.Percent <= CriticalPercent && before.Percent > CriticalPercent)
        {
            return BatteryEvent.Critical;
        }

        if (!now.Charging && now.Percent <= LowPercent && before.Percent > LowPercent)
        {
            return BatteryEvent.Low;
        }

        return BatteryEvent.None;
    }
}

/// <summary>
/// Reads the battery every half minute while it is on and raises <see cref="Happened"/> for a change worth telling. The first
/// reading says nothing. A computer without a battery never raises anything.
/// </summary>
internal sealed class BatteryWatcher : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private BatteryReading? _last;

    public BatteryWatcher()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        _last = Read();
    }

    /// <summary>Raised on the dispatcher thread with what happened and the charge.</summary>
    public event Action<BatteryEvent, int>? Happened;

    public void Dispose() => _timer.Stop();

    /// <summary>The battery now, or <see langword="null"/> without one (or when Windows does not say).</summary>
    internal static BatteryReading? Read()
    {
        if (!PInvoke.GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            return null;
        }

        const byte NoBattery = 128;
        const byte Unknown = 255;
        if ((status.BatteryFlag & NoBattery) != 0 || status.BatteryLifePercent == Unknown)
        {
            return null;
        }

        return new BatteryReading(status.BatteryLifePercent, status.ACLineStatus == 1);
    }

    private void Check()
    {
        var now = Read();
        if (now is { } reading && _last is { } before && BatteryRules.Between(before, reading) is var change and not BatteryEvent.None)
        {
            Happened?.Invoke(change, reading.Percent);
        }

        _last = now;
    }
}
