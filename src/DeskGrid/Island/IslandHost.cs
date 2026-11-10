using System.Windows.Threading;

namespace DeskGrid.Island;

/// <summary>
/// The island as the app uses it: lines are shown with <see cref="Show"/>, one after another, each for its time. Off by
/// default (<see cref="Enabled"/>); then nothing exists, not even the window. While a line is up a half-second timer
/// holds its countdown under the pointer and hides the island under a full-screen app; with nothing up there is no timer.
/// </summary>
internal sealed class IslandHost : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(500);

    private readonly ActivityQueue _queue = new();
    private readonly DispatcherTimer _timer;
    private IslandWindow? _window;
    private IslandActivity? _shown; // The activity the window shows now.
    private bool _enabled;

    public IslandHost()
    {
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => Refresh();
        _queue.CurrentChanged += (_, _) => Refresh();
    }

    /// <summary>Whether the island exists. Turning it off takes away what it shows and what waits.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
            {
                return;
            }

            _enabled = value;
            if (!value)
            {
                Stop();
            }
        }
    }

    /// <summary>Whether the window has been made (it is, the first time a line is to be shown); for the tests.</summary>
    internal bool HasWindow => _window is not null;

    /// <summary>Shows a line for <paramref name="duration"/>, after the ones before it. Nothing happens while the island is off.</summary>
    public void Show(string text, TimeSpan duration)
    {
        if (_enabled)
        {
            _queue.Show(new IslandActivity(text, duration), Now);
        }
    }

    /// <summary>Takes away what the island shows and what waits.</summary>
    public void Hide() => _queue.Clear();

    public void Dispose() => Stop();

    // A clock that does not jump when the user changes the time.
    private static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private void Stop()
    {
        _timer.Stop();
        _queue.Clear();
        _window?.Close();
        _window = null;
        _shown = null;
    }

    // Brings the window in line with the queue: the pointer and full-screen apps hold the countdown, a finished line ends,
    // and the window shows the current line unless a full-screen app is up.
    private void Refresh()
    {
        var now = Now;
        var concealed = FullscreenGuard.IsActive();
        _queue.Hold(concealed || _window?.IsHovered == true, now);
        if (_queue.Tick(now))
        {
            return; // CurrentChanged has come back here for the next one.
        }

        if (_queue.Current is not { } current)
        {
            _timer.Stop();
            Withdraw(animated: true);
            return;
        }

        _timer.Start(); // Also while concealed: the timer is what notices the full-screen app ending.
        if (concealed)
        {
            Withdraw(animated: false); // Nobody to watch an animation.
        }
        else if (!ReferenceEquals(_shown, current))
        {
            _shown = current;
            Window().Display(current.Text);
        }
    }

    private void Withdraw(bool animated)
    {
        if (_shown is not null)
        {
            _shown = null;
            _window?.Withdraw(animated);
        }
    }

    private IslandWindow Window()
    {
        if (_window is null)
        {
            _window = new IslandWindow();
            _window.HoverChanged += (_, _) => Refresh();
            _window.Clicked += (_, _) => _queue.Dismiss(Now);
        }

        return _window;
    }
}
