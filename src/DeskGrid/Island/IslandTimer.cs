namespace DeskGrid.Island;

/// <summary>A countdown the user starts with a shortcut. Pure: callers pass the time.</summary>
internal sealed class IslandTimer
{
    private TimeSpan _end;
    private TimeSpan _total;

    public bool Running { get; private set; }

    public void Start(TimeSpan length, TimeSpan now)
    {
        _total = length;
        _end = now + length;
        Running = true;
    }

    public void Cancel() => Running = false;

    /// <summary>Whether the time is up now (and, the first time it is asked, stops the timer).</summary>
    public bool TakeDone(TimeSpan now)
    {
        if (!Running || now < _end)
        {
            return false;
        }

        Running = false;
        return true;
    }

    public TimeSpan Left(TimeSpan now) => Running ? TimeSpan.FromTicks(Math.Max(0, (_end - now).Ticks)) : TimeSpan.Zero;

    /// <summary>How much of the time has gone, 0 to 1.</summary>
    public double Elapsed(TimeSpan now) => !Running || _total <= TimeSpan.Zero ? 0 : Math.Clamp(1 - (_end - now) / _total, 0, 1);

    /// <summary>"24:59", or "1:00:00" from an hour.</summary>
    public static string Format(TimeSpan left)
    {
        var seconds = (long)Math.Ceiling(left.TotalSeconds);
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }
}
