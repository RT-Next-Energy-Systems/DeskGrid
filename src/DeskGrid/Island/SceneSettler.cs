namespace DeskGrid.Island;

/// <summary>
/// Decides when a change of the situation in front (desktop, window, maximized, full screen) is real. The foreground flickers
/// (Alt+Tab, the task view, a window being closed or minimized), and NotiLand must not jump to another place and back with it:
/// a new situation counts only after it has been the same for <see cref="Delay"/>. Pure: the time is passed in.
/// </summary>
internal sealed class SceneSettler(TimeSpan delay)
{
    private string? _pending;
    private TimeSpan _since;

    public TimeSpan Delay { get; } = delay;

    /// <summary>
    /// The situation to use now. <paramref name="immediate"/>: nobody sees the island move (it is hidden), so the new one counts
    /// at once. <paramref name="held"/>: the user is handling it (pointer on it, its card open, a drag), so it stays where it is.
    /// </summary>
    public string Settle(string detected, string current, bool immediate, bool held, TimeSpan now)
    {
        if (detected == current)
        {
            _pending = null;
            return current;
        }

        if (immediate)
        {
            _pending = null;
            return detected;
        }

        if (held)
        {
            _pending = null;
            return current;
        }

        if (_pending != detected)
        {
            (_pending, _since) = (detected, now);
            return current;
        }

        if (now - _since < Delay)
        {
            return current;
        }

        _pending = null;
        return detected;
    }
}
