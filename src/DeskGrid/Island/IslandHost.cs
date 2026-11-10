using System.Windows.Threading;
using DeskGrid.Config;
using DeskGrid.Resources;

namespace DeskGrid.Island;

/// <summary>What the island is set to do.</summary>
internal sealed record IslandOptions(
    bool Enabled, IslandMusicMode Music, bool OpenFolders, bool Battery = false,
    UsageOptions? Usage = null, int? X = null, int? Y = null, int ShowSeconds = 5)
{
    public static IslandOptions From(AppConfig config) => new(
        config.IslandEnabled, config.IslandMusic, config.IslandFolders, config.IslandBattery,
        config.IslandUsage ? new UsageOptions(config.UsageLimitMillions, config.UsageHours, config.UsageWarnPercent) : null,
        config.IslandX, config.IslandY, config.IslandShowSeconds);
}

/// <summary>
/// The island as the app uses it. Lines are shown with <see cref="Show"/>, one after another, each for its time; the
/// song that plays is shown as its mode says; the pointer held at the top centre of the screen asks for the folders
/// (<see cref="OpenFoldersRequested"/>). Off by default; then nothing exists, not even a window. While something is up a
/// half-second timer holds the countdown under the pointer and hides the island under a full-screen app; with nothing up
/// there is no timer.
/// </summary>
internal sealed class IslandHost : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(500);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ActivityQueue _queue = new();
    private readonly DispatcherTimer _timer;
    private readonly MusicActions _musicActions;
    private IslandOptions _options = new(false, IslandMusicMode.Off, false);
    private IslandWindow? _window;
    private IslandHotZone? _hotZone;
    private NowPlaying? _music;
    private BatteryWatcher? _battery;
    private IReadOnlyList<MediaTrack> _tracks = []; // What every player reports now.
    private readonly Dictionary<string, MediaTrack> _announced = new(StringComparer.Ordinal); // The song last announced, by player.
    private object? _shown; // What the window shows now: an activity, or a song while it plays.

    // Players that play together take turns on the pill, this long each.
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(4);

    public IslandHost()
    {
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => Refresh();
        _queue.CurrentChanged += (_, _) => Refresh();
        _musicActions = new MusicActions(source => _music?.Previous(source), source => _music?.PlayPause(source), source => _music?.Next(source));
    }

    /// <summary>Raised when the pointer has been held at the top centre of the screen: the list of all folders is wanted.</summary>
    public event EventHandler? OpenFoldersRequested;

    /// <summary>Whether the window has been made (it is, the first time something is to be shown); for the tests.</summary>
    internal bool HasWindow => _window is not null;

    /// <summary>Whether the strip that notices the pointer at the top edge exists; for the tests.</summary>
    internal bool HasHotZone => _hotZone is not null;

    /// <summary>Whether the system media controls are being read; for the tests.</summary>
    internal bool ReadsMusic => _music is not null;

    /// <summary>Sets what the island does. Turning it off takes away what it shows and what waits.</summary>
    public void Apply(IslandOptions options)
    {
        _options = options;
        if (!options.Enabled)
        {
            Stop();
            return;
        }

        ApplyMusic();
        ApplyBattery();
        ApplyUsage();
        ApplyHotZone();
        _window?.SetAnchor(options.X, options.Y);
        Refresh();
    }

    /// <summary>Raised when the user dropped the island somewhere: its offset in DIPs from the top centre.</summary>
    public event Action<int, int>? PositionChanged;

    /// <summary>
    /// Shows a line for <paramref name="duration"/>, after the ones before it (longer if it takes longer to scroll through
    /// it). Nothing happens while the island is off.
    /// </summary>
    public void Show(string text, TimeSpan duration)
    {
        if (_options.Enabled)
        {
            _queue.Show(new IslandActivity(text, Marquee.ToRead(text, duration)), Now);
        }
    }

    /// <summary>Takes away what the island shows and what waits (a song that plays on is not touched), and the info of a shortcut.</summary>
    public void Hide()
    {
        _info = null;
        _queue.Clear();
        Refresh();
    }

    // ---- Info (the shortcuts) ----

    // What a shortcut puts up: the usage line and every player, taking turns. Null = not up; otherwise when it ends, or
    // TimeSpan.MaxValue to stay until it is pressed again.
    private TimeSpan? _info;

    /// <summary>Whether the info of a shortcut is up.</summary>
    internal bool InfoUp => _info is not null;

    /// <summary>The "show" shortcut: the info for the set number of seconds (0 = until it is hidden).</summary>
    public void ShowInfo()
    {
        if (_options.Enabled)
        {
            _info = _options.ShowSeconds > 0 ? Now + TimeSpan.FromSeconds(_options.ShowSeconds) : TimeSpan.MaxValue;
            Refresh();
        }
    }

    /// <summary>The "toggle" shortcut: the info stays up until the shortcut is pressed again.</summary>
    public void ToggleInfo()
    {
        if (!_options.Enabled)
        {
            return;
        }

        _info = _info is null ? TimeSpan.MaxValue : null;
        Refresh();
    }

    private List<object> InfoItems()
    {
        var items = new List<object>();
        if (_usage?.Tokens is { } tokens)
        {
            items.Add(new IslandActivity(UsageLine(tokens), TimeSpan.Zero));
        }

        items.AddRange(_tracks);
        if (items.Count == 0)
        {
            items.Add(new IslandActivity(Strings.IslandNothing, TimeSpan.Zero));
        }

        return items;
    }

    // ---- AI usage ----

    private UsageMonitor? _usage;

    private string UsageLine(long tokens)
    {
        var options = _usage!.Options;
        return string.Format(Strings.IslandUsageLine, Math.Round(UsageRules.Percent(tokens, options)), UsageRules.Short(tokens),
            UsageRules.Short(options.LimitTokens), options.Hours);
    }

    private void ApplyUsage()
    {
        if (_options.Usage is not { } options)
        {
            _usage?.Dispose();
            _usage = null;
        }
        else if (_usage is null)
        {
            _usage = new UsageMonitor(options);
            _usage.Alert += (level, tokens) =>
            {
                var percent = Math.Round(UsageRules.Percent(tokens, _usage!.Options));
                Show(string.Format(level == UsageLevel.Full ? Strings.IslandUsageFull : Strings.IslandUsageWarn, percent), TimeSpan.FromSeconds(8));
            };
        }
        else
        {
            _usage.Configure(options);
        }
    }

    /// <summary>The screens changed: the windows go back to the top centre of the primary one.</summary>
    public void OnDisplayChanged()
    {
        _window?.Reposition();
        _hotZone?.Place();
    }

    public void Dispose() => Stop();

    // A clock that does not jump when the user changes the time.
    private static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private void Stop()
    {
        _timer.Stop();
        _queue.Clear();
        StopMusic();
        StopBattery();
        _usage?.Dispose();
        _usage = null;
        _info = null;
        _hotZone?.Close();
        _hotZone = null;
        _window?.Close();
        _window = null;
        _shown = null;
    }

    // ---- Music ----

    private void ApplyMusic()
    {
        if (_options.Music == IslandMusicMode.Off)
        {
            StopMusic();
        }
        else if (_music is null)
        {
            var music = new NowPlaying(_dispatcher);
            _music = music;
            music.Changed += OnMusicChanged;
            _ = music.StartAsync(); // Logs its own failure.
        }
        else
        {
            AnnounceIfNew(); // Switched to "on change" while a song plays: say what it is once.
        }
    }

    // Says what each player plays that has not been said yet (when the mode is "on change").
    private void AnnounceIfNew()
    {
        foreach (var track in _tracks)
        {
            _announced.TryGetValue(track.Source, out var before);
            if (MusicPolicy.Announces(_options.Music, before, track))
            {
                _announced[track.Source] = track;
                var label = MusicPolicy.Label(track);
                _queue.Show(new IslandActivity(label, Marquee.ToRead(label, IslandActivity.DefaultDuration), track.Cover, IsMusic: true, track.Source), Now);
            }
        }
    }

    private void StopMusic()
    {
        _music?.Dispose();
        _music = null;
        _tracks = [];
        _announced.Clear();
    }

    private void OnMusicChanged(object? sender, EventArgs e)
    {
        _tracks = _music?.Tracks ?? [];
        foreach (var gone in _announced.Keys.Where(source => _tracks.All(t => t.Source != source)).ToList())
        {
            _announced.Remove(gone); // The player went away: its next song is news again.
        }

        AnnounceIfNew();
        Refresh();
    }

    // ---- Battery ----

    private void ApplyBattery()
    {
        if (!_options.Battery)
        {
            StopBattery();
        }
        else if (_battery is null)
        {
            _battery = new BatteryWatcher();
            _battery.Happened += (change, percent) =>
            {
                var text = change switch
                {
                    BatteryEvent.ChargerConnected => Strings.IslandBatteryCharging,
                    BatteryEvent.ChargerRemoved => Strings.IslandBatteryUnplugged,
                    BatteryEvent.Low => Strings.IslandBatteryLow,
                    _ => Strings.IslandBatteryCritical,
                };
                Show(string.Format(text, percent), IslandActivity.DefaultDuration);
            };
        }
    }

    private void StopBattery()
    {
        _battery?.Dispose();
        _battery = null;
    }

    // ---- The top edge ----

    private void ApplyHotZone()
    {
        if (!_options.OpenFolders)
        {
            _hotZone?.Close();
            _hotZone = null;
        }
        else if (_hotZone is null)
        {
            _hotZone = new IslandHotZone();
            _hotZone.Dwelled += (_, _) =>
            {
                if (!FullscreenGuard.IsActive()) // Not over a game.
                {
                    OpenFoldersRequested?.Invoke(this, EventArgs.Empty);
                }
            };
            _hotZone.Place();
        }
    }

    // ---- What is on screen ----

    // Brings the window in line with what should be up: the pointer and full-screen apps hold the countdown of a line, a
    // finished line ends, and the window shows the current line, or else the song when it is to stay while it plays,
    // unless a full-screen app is up.
    private void Refresh()
    {
        var now = Now;
        var concealed = FullscreenGuard.IsActive();
        _queue.Hold(concealed || _window?.IsHovered == true, now);
        if (_queue.Tick(now))
        {
            return; // CurrentChanged has come back here for the next one.
        }

        if (_info is { } until && until != TimeSpan.MaxValue && now >= until)
        {
            _info = null;
        }

        object? wanted = _queue.Current;
        if (wanted is null && _info is not null)
        {
            // The info of a shortcut: its lines take turns; under the pointer the pill stays with the one it shows.
            var items = InfoItems();
            wanted = _window?.IsHovered == true && _shown is not null && items.FirstOrDefault(i => SameInfo(i, _shown)) is { } kept
                ? kept
                : MusicPolicy.Rotate(items, now, Turn);
        }

        if (wanted is null)
        {
            // Everything that plays, music or video, takes turns on the pill.
            var playing = MusicPolicy.PlayingToShow(_options.Music, _tracks);
            // Under the pointer the pill stays with its player, so the buttons act on what the user is looking at.
            wanted = _window?.IsHovered == true && _shown is MediaTrack current && playing.FirstOrDefault(t => t.Source == current.Source) is { } held
                ? held
                : MusicPolicy.Rotate(playing, now, Turn);
        }

        if (wanted is null)
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
        else if (!Equals(_shown, wanted))
        {
            _shown = wanted;
            Present(wanted);
        }

        if (!concealed)
        {
            _window?.RaiseAboveTaskbar(); // Over the taskbar, which jumps back up when it is clicked.
        }
    }

    // The same line of the info (a player is the same player when it has a new song).
    private static bool SameInfo(object a, object b) =>
        a is MediaTrack x && b is MediaTrack y ? x.Source == y.Source : Equals(a, b);

    private void Present(object what)
    {
        var window = Window();
        switch (what)
        {
            case IslandActivity activity:
                window.Display(activity.Text, activity.Cover, activity.IsMusic ? _musicActions : null, activity.Source);
                window.SetPlaying(activity.IsMusic && _tracks.Any(t => t.Source == activity.Source && t.Playing));
                break;
            case MediaTrack track:
                window.Display(MusicPolicy.Label(track), track.Cover, _musicActions, track.Source);
                window.SetPlaying(track.Playing);
                break;
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
            _window.SetAnchor(_options.X, _options.Y);
            _window.PositionChanged += (x, y) => PositionChanged?.Invoke(x, y);
            _window.HoverChanged += (_, _) => Refresh();
            _window.Clicked += (_, _) => _queue.Dismiss(Now);
        }

        return _window;
    }
}
