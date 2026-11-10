namespace DeskGrid.Island;

/// <summary>
/// What the island shows now and what waits behind it. Pure state: callers pass the time, so the countdown can be tested.
/// The current activity counts down to its end, except while held (the pointer is over it, or a full-screen app hides the
/// island); a hold keeps the time that was left.
/// </summary>
internal sealed class ActivityQueue
{
    /// <summary>More than this waiting and the oldest is dropped: a message that old is not worth showing any more.</summary>
    public const int MaxWaiting = 10;

    private readonly Queue<IslandActivity> _waiting = new();
    private TimeSpan _left;
    private TimeSpan _counted; // The time up to which _left has been counted down.
    private bool _held;

    public IslandActivity? Current { get; private set; }

    public int Waiting => _waiting.Count;

    /// <summary>Raised when <see cref="Current"/> is another activity, or none.</summary>
    public event EventHandler? CurrentChanged;

    public void Show(IslandActivity activity, TimeSpan now)
    {
        if (activity.IsMusic)
        {
            // A song replaces the one before it, shown or waiting: skipping through songs must not queue up one notice per song.
            var others = _waiting.Where(waiting => !waiting.IsMusic).ToList();
            _waiting.Clear();
            others.ForEach(_waiting.Enqueue);
            if (Current is { IsMusic: true })
            {
                Start(activity, now);
                return;
            }
        }

        if (Current is null)
        {
            Start(activity, now);
            return;
        }

        if (_waiting.Count == MaxWaiting)
        {
            _waiting.Dequeue();
        }

        _waiting.Enqueue(activity);
    }

    /// <summary>Stops or resumes the countdown of the current activity.</summary>
    public void Hold(bool held, TimeSpan now)
    {
        if (held == _held)
        {
            return;
        }

        CountDown(now);
        _held = held;
    }

    /// <summary>Ends the current activity if its time is up; returns whether it did.</summary>
    public bool Tick(TimeSpan now)
    {
        if (Current is null)
        {
            return false;
        }

        CountDown(now);
        if (_left > TimeSpan.Zero)
        {
            return false;
        }

        Next(now);
        return true;
    }

    /// <summary>Ends the current activity at once (the user clicked it); the next one, if any, starts.</summary>
    public void Dismiss(TimeSpan now)
    {
        if (Current is not null)
        {
            Next(now);
        }
    }

    /// <summary>Ends the current activity and drops all that wait.</summary>
    public void Clear()
    {
        _waiting.Clear();
        if (Current is not null)
        {
            Current = null;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CountDown(TimeSpan now)
    {
        if (!_held)
        {
            _left -= now - _counted;
        }

        _counted = now;
    }

    private void Next(TimeSpan now)
    {
        if (_waiting.Count > 0)
        {
            Start(_waiting.Dequeue(), now);
            return;
        }

        Current = null;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Start(IslandActivity activity, TimeSpan now)
    {
        Current = activity;
        _left = activity.Duration;
        _counted = now;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
