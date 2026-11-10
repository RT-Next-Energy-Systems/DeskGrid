using System.IO;
using System.Windows.Threading;
using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Resources;
using DeskGrid.Shell;
using Windows.Win32;

namespace DeskGrid.Island;

/// <summary>What else the island watches and how it behaves, beyond the song, the battery and the usage.</summary>
internal sealed record IslandExtras(
    bool Privacy = false, bool Bluetooth = false, bool Downloads = false, bool System = false, int TimerMinutes = 25, IslandRules? Rules = null,
    double Scale = 1, IslandHover? Hover = null, IReadOnlyDictionary<string, ScreenPoint>? Places = null,
    IReadOnlyList<string>? CardSections = null)
{
    public IslandRules RulesOrDefault => Rules ?? new IslandRules();

    public IslandHover HoverOrDefault => Hover ?? new IslandHover();

    /// <summary>The place set for <paramref name="scene"/>, else the default place, else none (the top centre).</summary>
    public ScreenPoint? PlaceFor(string scene) =>
        Places is null ? null : Places.TryGetValue(scene, out var place) ? place : Places.TryGetValue(IslandScene.Default, out var home) ? home : null;
}

/// <summary>What the island is set to do.</summary>
internal sealed record IslandOptions(
    bool Enabled, IslandMusicMode Music, bool OpenFolders, bool Battery = false,
    UsageOptions? Usage = null, int? X = null, int? Y = null, int ShowSeconds = 5, IslandExtras? More = null)
{
    public IslandExtras Extras => More ?? new IslandExtras();

    public static IslandOptions From(AppConfig config) => new(
        config.IslandEnabled, config.IslandMusic, config.IslandFolders, config.IslandBattery,
        config.IslandUsage ? new UsageOptions(config.UsageLimitMillions, config.UsageHours, config.UsageWarnPercent) : null,
        config.IslandX, config.IslandY, config.IslandShowSeconds,
        new IslandExtras(
            config.IslandPrivacy, config.IslandBluetooth, config.IslandDownloads, config.IslandSystem, config.IslandTimerMinutes,
            new IslandRules(
                config.IslandQuietFrom, config.IslandQuietTo, IslandRules.SplitApps(config.IslandHideInApps),
                IslandRules.SplitApps(config.IslandShowOverApps), config.IslandFollowPointer),
            config.IslandScale,
            new IslandHover(config.IslandHoverExpand, config.IslandHoverFullText, config.IslandHoverButtons, config.IslandHoverTime),
            PlacesOf(config),
            Island.CardSections.Parse(config.IslandCardSections)));

    /// <summary>Every place the user set: the default one and each situation's.</summary>
    public static Dictionary<string, ScreenPoint> PlacesOf(AppConfig config)
    {
        var places = config.IslandPlaces.ToDictionary(p => p.Key, p => new ScreenPoint(p.Value.X, p.Value.Y), StringComparer.OrdinalIgnoreCase);
        places.Remove(IslandScene.Default);
        if (config.IslandX is { } x && config.IslandY is { } y)
        {
            places[IslandScene.Default] = new ScreenPoint(x, y);
        }

        return places;
    }
}

/// <summary>
/// The island as the app uses it. Lines are shown with <see cref="Show"/>, one after another, each for its time; a timer, the
/// info of a shortcut and the song that plays are shown as their settings say; the pointer held at the top centre of the
/// screen asks for the folders (<see cref="OpenFoldersRequested"/>), a click on a song opens the card. Off by default; then
/// nothing exists, not even a window. While something is up a half-second timer holds the countdown under the pointer and
/// hides the island where the rules say; with nothing up there is no timer.
/// </summary>
internal sealed class IslandHost : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(500);

    // Players and the lines of the info take turns on the pill, this long each.
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(4);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ActivityQueue _queue = new();
    private readonly DispatcherTimer _timer;
    private readonly MusicActions _musicActions;
    private readonly IslandTimer _countdown = new();
    private readonly IslandShelf _shelf = new();
    private readonly SystemStatus _system = new();
    private readonly Dictionary<string, MediaTrack> _announced = new(StringComparer.Ordinal); // The song last announced, by player.
    private IslandOptions _options = new(false, IslandMusicMode.Off, false);
    private IslandWindow? _window;
    private IslandHotZone? _hotZone;
    private IslandCard? _card;
    private NowPlaying? _music;
    private BatteryWatcher? _battery;
    private UsageMonitor? _usage;
    private PrivacyWatcher? _privacy;
    private BluetoothWatcher? _bluetooth;
    private DownloadWatcher? _downloads;
    private IReadOnlyList<MediaTrack> _tracks = []; // What every player reports now.
    private object? _shown; // What the window shows now: an activity, a song, or a live line.

    // What a shortcut puts up: the usage, the system, every player, taking turns. Null = not up; otherwise when it ends, or
    // TimeSpan.MaxValue to stay until it is pressed again.
    private TimeSpan? _info;

    // Esc closes the expanded NotiLand (or cancels setting a place). The island never takes the focus, so the key is watched,
    // not received: a quick timer that only runs while needed.
    private readonly DispatcherTimer _escape = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _escapeWasDown;

    public IslandHost()
    {
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => Refresh();
        _queue.CurrentChanged += (_, _) => Refresh();
        _musicActions = new MusicActions(
            source => _music?.Previous(source),
            source => _music?.PlayPause(source),
            source => _music?.Next(source),
            (source, pos) => _music?.Seek(source, pos));
        _shelf.Changed += (_, _) => _card?.Refresh();
        _escape.Tick += (_, _) => OnEscapeTick();
    }

    /// <summary>Raised when the pointer has been held at the top centre of the screen: the list of all folders is wanted.</summary>
    public event EventHandler? OpenFoldersRequested;

    /// <summary>Raised when the user dropped the island somewhere: the middle of its top edge, in screen pixels.</summary>
    /// <summary>Raised when a place changed (dragged, or set in Settings): the situation, and the place (null = reset).</summary>
    public event Action<string, ScreenPoint?>? PlaceChanged;

    /// <summary>Raised with the target of a clicked line (<c>folder:Name</c>, an address or a path), or the folder chosen on the card.</summary>
    public event Action<string>? ActionRequested;

    /// <summary>The names of the folders, for the card; set by the app.</summary>
    public Func<IReadOnlyList<string>>? FolderNames { get; set; }

    /// <summary>Whether the window has been made (it is, the first time something is to be shown); for the tests.</summary>
    internal bool HasWindow => _window is not null;

    /// <summary>Whether the strip that notices the pointer at the top edge exists; for the tests.</summary>
    internal bool HasHotZone => _hotZone is not null;

    /// <summary>Whether the system media controls are being read; for the tests.</summary>
    internal bool ReadsMusic => _music is not null;

    /// <summary>Whether the info of a shortcut is up; for the tests.</summary>
    internal bool InfoUp => _info is not null;

    /// <summary>The text of the line that is up, if any; for the tests.</summary>
    internal string? CurrentLine => _queue.Current?.Text;

    /// <summary>Whether the timer runs; for the tests.</summary>
    internal bool TimerRunning => _countdown.Running;

    /// <summary>The files on the shelf.</summary>
    internal IslandShelf Shelf => _shelf;

    /// <summary>Whether the card is open; for the tests.</summary>
    internal bool CardOpen => _card is { IsVisible: true, IsClosing: false };

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
        ApplyWatchers();
        ApplyHotZone();
        if (_window is not null)
        {
            Configure(_window);
        }

        Refresh();
    }

    // ---- Places ----

    private string _scene = IslandScene.Default; // The situation in front, as last seen.
    private string? _editing; // The situation whose place is being set in Settings, if one is.
    private ScreenPoint? _editPlace; // Where it was dropped while being set.
    private Action? _editEnded;

    /// <summary>What NotiLand does now, for `--ctl status`: the situation, whether a place is being set, the line that is up, the places.</summary>
    public System.Text.Json.Nodes.JsonObject State()
    {
        var places = new System.Text.Json.Nodes.JsonObject();
        foreach (var (scene, place) in _options.Extras.Places ?? new Dictionary<string, ScreenPoint>())
        {
            places[scene] = new System.Text.Json.Nodes.JsonArray(place.X, place.Y);
        }

        return new System.Text.Json.Nodes.JsonObject
        {
            ["enabled"] = _options.Enabled,
            ["scene"] = _scene,
            ["editing"] = _editing,
            ["overlay"] = _overlayUp,
            ["line"] = _shown switch
            {
                IslandActivity activity => activity.Text,
                LiveLine live => live.Text,
                MediaTrack track => MusicPolicy.Label(track),
                _ => null,
            },
            ["waiting"] = _queue.Waiting,
            ["places"] = places,
            ["pill"] = _window?.PillRect() is { } pill ? new System.Text.Json.Nodes.JsonArray(pill.Left, pill.Top, pill.Width, pill.Height) : null,
        };
    }

    /// <summary>The situation in front, as last seen; for the tests.</summary>
    internal string Scene => _scene;

    private void Configure(IslandWindow window)
    {
        var extras = _options.Extras;
        window.FollowPointer = extras.RulesOrDefault.FollowPointer;
        window.PillScale = extras.Scale;
        window.Hover = extras.HoverOrDefault;
        ApplyPlace();
    }

    // Puts the window in the place of the situation in front (or of the one being set), and gives it the other places to snap to.
    private void ApplyPlace()
    {
        if (_window is null)
        {
            return;
        }

        var scene = _editing ?? _scene;
        var places = _options.Extras.Places ?? new Dictionary<string, ScreenPoint>();
        _window.SnapTargets = [.. places.Where(p => p.Key != scene).Select(p => p.Value)];
        _window.SetAnchor(_editing is not null && _editPlace is { } dropped ? dropped : _options.Extras.PlaceFor(scene));
    }

    // Dragged by hand: while a place is being set it is that place; otherwise it is the place of the situation in front if
    // that has one, else the default place (the one the user sees).
    private void OnDragged(ScreenPoint point)
    {
        if (_editing is not null)
        {
            _editPlace = point;
            return;
        }

        var places = _options.Extras.Places;
        var scene = places is not null && places.ContainsKey(_scene) ? _scene : IslandScene.Default;
        PlaceChanged?.Invoke(scene, point);
    }

    /// <summary>The name a situation has in Settings.</summary>
    public static string SceneName(string scene) => scene switch
    {
        IslandScene.Desktop => Strings.SceneDesktop,
        IslandScene.Window => Strings.SceneWindow,
        IslandScene.Maximized => Strings.SceneMaximized,
        IslandScene.FullScreen => Strings.SceneFullScreen,
        _ => Strings.SceneDefault,
    };

    /// <summary>
    /// Lets the user set the place of <paramref name="scene"/>: NotiLand shows up there (or where it would be now) saying what
    /// to do, ready to be dragged; Enter or a double click keeps the place, Esc keeps the old one. <paramref name="ended"/> is
    /// called either way.
    /// </summary>
    public void BeginPlaceEdit(string scene, Action ended)
    {
        if (!_options.Enabled)
        {
            ended();
            return;
        }

        EndPlaceEdit(save: false);
        _card?.Close();
        _editing = scene;
        _editPlace = null;
        _editEnded = ended;
        ApplyPlace();
        Refresh();
    }

    /// <summary>Whether a place is being set; for the tests.</summary>
    internal bool EditingPlace => _editing is not null;

    /// <summary>Ends the setting of a place, keeping where it was dropped (<paramref name="save"/>) or not.</summary>
    internal void EndPlaceEdit(bool save)
    {
        if (_editing is not { } scene)
        {
            return;
        }

        var dropped = _editPlace;
        var ended = _editEnded;
        (_editing, _editPlace, _editEnded) = (null, null, null);
        if (save && dropped is { } place)
        {
            PlaceChanged?.Invoke(scene, place); // The app saves it and applies the options again.
        }

        ApplyPlace();
        Refresh();
        ended?.Invoke();
    }

    /// <summary>
    /// Shows a line for <paramref name="duration"/>, after the ones before it (longer if it takes longer to scroll through
    /// it). Nothing happens while the island is off.
    /// </summary>
    public void Show(string text, TimeSpan duration, IslandLook? look = null)
    {
        if (_options.Enabled)
        {
            _queue.Show(new IslandActivity(text, Marquee.ToRead(text, duration), Look: look), Now);
        }
    }

    private static bool EscapeDown() => (PInvoke.GetAsyncKeyState(0x1B) & 0x8000) != 0;

    private void OnEscapeTick()
    {
        var down = EscapeDown();
        var pressed = down && !_escapeWasDown;
        _escapeWasDown = down;
        var enter = (PInvoke.GetAsyncKeyState(0x0D) & 0x8000) != 0;
        var entered = enter && !_enterWasDown;
        _enterWasDown = enter;
        if (entered && _editing is not null)
        {
            EndPlaceEdit(save: true); // Enter keeps the place being set.
        }
        else if (pressed && !OverlayJustUp())
        {
            Dismiss();
        }
    }

    private bool _enterWasDown;

    // Esc that closes a screen snip or Start is theirs, not a request to close what NotiLand shows (the overlay may be gone
    // by the time the key is seen, so a moment after it counts too).
    private bool OverlayJustUp() => IslandScene.SystemOverlayInFront() || Environment.TickCount64 - _overlaySeen < 1000;
    private bool _overlayUp;
    private long _overlaySeen = long.MinValue / 2; // When a system overlay was last in front (TickCount64).

    // Runs the watch while the island is expanded, the card is open, or a place is being edited.
    private void UpdateEscapeWatch()
    {
        var needed = _editing is not null || CardOpen || _window is { IsExpanded: true };
        if (needed && !_escape.IsEnabled)
        {
            _escapeWasDown = EscapeDown();
            _enterWasDown = (PInvoke.GetAsyncKeyState(0x0D) & 0x8000) != 0;
            _escape.Start();
        }
        else if (!needed)
        {
            _escape.Stop();
        }
    }

    /// <summary>Esc: cancels setting a place, or closes the expanded NotiLand (the card or the expanded pill); does nothing while collapsed.</summary>
    internal void Dismiss()
    {
        if (_editing is not null)
        {
            EndPlaceEdit(save: false); // Esc leaves the place as it was.
            return;
        }

        if (CardOpen)
        {
            _card!.CloseSmoothly();
            return;
        }

        if (_window is { IsExpanded: true })
        {
            _window.Collapse();
        }
    }

    /// <summary>Takes away what the island shows and what waits (a song that plays on is not touched), the timer and the info of a shortcut.</summary>
    public void Hide()
    {
        _info = null;
        _countdown.Cancel();
        _queue.Clear();
        _card?.Close();
        Refresh();
    }

    // ---- Shortcuts ----

    /// <summary>The "show" shortcut: the info for the set number of seconds (0 = until it is hidden).</summary>
    public void ShowInfo()
    {
        if (_options.Enabled)
        {
            _info = _options.ShowSeconds > 0 ? Now + TimeSpan.FromSeconds(_options.ShowSeconds) : TimeSpan.MaxValue;
            Refresh();
        }
    }

    /// <summary>The "show or hide" shortcut: the info for the set seconds (0 = until pressed again), or away if it is up.</summary>
    public void ToggleInfo()
    {
        if (!_options.Enabled)
        {
            return;
        }

        // Shown for the set seconds (0 = until pressed again); pressed while up, it hides.
        _info = _info is not null ? null : _options.ShowSeconds > 0 ? Now + TimeSpan.FromSeconds(_options.ShowSeconds) : TimeSpan.MaxValue;
        Refresh();
    }

    /// <summary>The "timer" shortcut: starts the countdown (the set number of minutes), or stops it if it runs.</summary>
    public void ToggleTimer()
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (_countdown.Running)
        {
            _countdown.Cancel();
        }
        else
        {
            _countdown.Start(TimeSpan.FromMinutes(Math.Max(1, _options.Extras.TimerMinutes)), Now);
        }

        Refresh();
    }

    /// <summary>The "card" shortcut (and a click on a song, or files dragged to the island): opens the card, or closes it.</summary>
    public void ToggleCard()
    {
        if (CardOpen)
        {
            _card!.CloseSmoothly();
        }
        else
        {
            OpenCard();
        }
    }

    /// <summary>Opens the card under the island.</summary>
    public void OpenCard()
    {
        if (!_options.Enabled || CardOpen)
        {
            return;
        }

        _shelf.Prune();
        var card = new IslandCard(_musicActions);
        _card = card;
        card.Closed += (_, _) =>
        {
            if (ReferenceEquals(_card, card))
            {
                _card = null; // Not a newer card, opened while this one was still leaving.
            }

            UpdateEscapeWatch();
        };
        card.FolderChosen += name => ActionRequested?.Invoke(IslandAction.FolderPrefix + name);
        card.ShelfDropped += paths => _shelf.Add(paths);
        card.ShelfRemoved += _shelf.Remove;
        card.ShelfCleared += _shelf.Clear;
        card.ShelfOpened += path =>
        {
            try
            {
                ShellItem.Open(path);
            }
            catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
            {
                Diagnostics.Log.Error("A file on the shelf could not be opened", exception);
            }
        };
        var (home, pillHeight) = Window().PillOnScreen();
        card.Open(CardModelNow, home, pillHeight, Window().PillRect()?.Width ?? 0);
        UpdateEscapeWatch();
    }

    private CardModel CardModelNow()
    {
        var reading = _usage?.Reading;
        var percent = reading is not null ? UsageRules.Percent(reading, _usage!.Options) : (double?)null;
        return new CardModel(
            _tracks, reading is not null ? UsageLine(reading) : null, percent,
            _options.Extras.System ? _system.Read(Now) : null,
            FolderNames?.Invoke() ?? [], _shelf.Paths, _options.Extras.CardSections,
            [.. (reading?.Others ?? []).Select(o => (ToolLine(o), o.Plan.FivePercent))]);
    }

    // ---- Info ----

    private List<object> InfoItems()
    {
        var items = new List<object>();
        if (_usage?.Reading is { } reading)
        {
            items.Add(new IslandActivity(UsageLine(reading), TimeSpan.Zero, Look: UsageLook(reading)));
            foreach (var other in reading.Others ?? [])
            {
                items.Add(new IslandActivity(ToolLine(other), TimeSpan.Zero, Look: new IslandLook("usage", other.Plan.FivePercent >= 100 ? "#E84D4D" : other.Plan.FivePercent >= 80 ? "#F0A030" : "#4CC38A", Math.Min(other.Plan.FivePercent, 100) / 100)));
            }
        }

        if (_options.Extras.System)
        {
            items.Add(new LiveLine("system", _system.Read(Now), new IslandLook("system")));
        }

        foreach (var use in _privacy?.InUse ?? [])
        {
            items.Add(new IslandActivity(SensorText(use), TimeSpan.Zero, Look: SensorLook(use)));
        }

        items.AddRange(_tracks);
        if (items.Count == 0)
        {
            items.Add(new IslandActivity(Strings.IslandNothing, TimeSpan.Zero));
        }

        return items;
    }

    // ---- AI usage ----

    // "AI usage 42% · 1.2M of 3M tokens · resets in 2h 05m", or, with no block running, that nothing is counted now.
    private string UsageLine(UsageReading reading)
    {
        var options = _usage!.Options;
        if (reading.Resets is not { } resets)
        {
            return Strings.IslandUsageIdle;
        }

        if (reading.Plan is { } plan)
        {
            var left = UsageRules.Left(plan.FiveResets, DateTimeOffset.UtcNow);
            return plan.WeekPercent is { } week
                ? string.Format(Strings.IslandUsagePlanWeek, Math.Round(plan.FivePercent), left, Math.Round(week))
                : string.Format(Strings.IslandUsagePlan, Math.Round(plan.FivePercent), left);
        }

        var limit = reading.LimitFor(options);
        return string.Format(Strings.IslandUsageLine, Math.Round(UsageRules.Percent(reading, options)), UsageRules.Short(reading.Tokens),
            limit > 0 ? UsageRules.Short(limit) : "?", UsageRules.Left(resets, DateTimeOffset.UtcNow));
    }

    // "Codex 12% · resets in 3h 10m · week 40%".
    private static string ToolLine(ToolUsage use)
    {
        var left = UsageRules.Left(use.Plan.FiveResets, DateTimeOffset.UtcNow);
        return use.Plan.WeekPercent is { } week
            ? string.Format(Strings.IslandToolUsageWeek, use.Tool.Name, Math.Round(use.Plan.FivePercent), left, Math.Round(week))
            : string.Format(Strings.IslandToolUsage, use.Tool.Name, Math.Round(use.Plan.FivePercent), left);
    }

    private IslandLook UsageLook(UsageReading reading)
    {
        var percent = UsageRules.Percent(reading, _usage!.Options);
        var accent = percent >= 100 ? "#E84D4D" : percent >= _usage.Options.WarnPercent ? "#F0A030" : "#4CC38A";
        return new IslandLook("usage", accent, Math.Min(percent, 100) / 100);
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
            _usage.Alert += (level, reading) =>
            {
                var percent = Math.Round(UsageRules.Percent(reading, _usage!.Options));
                Show(string.Format(level == UsageLevel.Full ? Strings.IslandUsageFull : Strings.IslandUsageWarn, percent), TimeSpan.FromSeconds(8), UsageLook(reading));
            };
        }
        else
        {
            _usage.Configure(options);
        }
    }

    /// <summary>The screens changed: the windows go back to where they belong.</summary>
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
        (_editing, _editPlace, _editEnded) = (null, null, null);
        _timer.Stop();
        _queue.Clear();
        StopMusic();
        StopBattery();
        _usage?.Dispose();
        _usage = null;
        StopWatchers();
        _countdown.Cancel();
        _info = null;
        _card?.Close();
        _hotZone?.Close();
        _hotZone = null;
        _window?.Close();
        _window = null;
        _escape.Stop();
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
                var look = track.Accent is { } c ? new IslandLook(Accent: $"#{c.R:X2}{c.G:X2}{c.B:X2}") : null;
                _queue.Show(new IslandActivity(label, Marquee.ToRead(label, IslandActivity.DefaultDuration), track.Cover, IsMusic: true, track.Source, look), Now);
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
                var accent = change is BatteryEvent.Low or BatteryEvent.Critical ? "#F0A030" : null;
                Show(string.Format(text, percent), IslandActivity.DefaultDuration, new IslandLook("battery", accent, percent / 100.0));
            };
        }
    }

    private void StopBattery()
    {
        _battery?.Dispose();
        _battery = null;
    }

    // ---- Microphone and camera, Bluetooth devices, downloads ----

    private static string SensorText(SensorUse use) =>
        string.Format(use.Kind == "webcam" ? Strings.IslandCameraInUse : Strings.IslandMicInUse, use.App);

    private static IslandLook SensorLook(SensorUse use) => new(use.Kind == "webcam" ? "camera" : "mic", "#F0A030");

    private void ApplyWatchers()
    {
        var extras = _options.Extras;
        if (!extras.Privacy)
        {
            _privacy?.Dispose();
            _privacy = null;
        }
        else if (_privacy is null)
        {
            _privacy = new PrivacyWatcher();
            _privacy.Started += use => Show(SensorText(use), IslandActivity.DefaultDuration, SensorLook(use));
        }

        if (!extras.Bluetooth)
        {
            _bluetooth?.Dispose();
            _bluetooth = null;
        }
        else if (_bluetooth is null)
        {
            _bluetooth = new BluetoothWatcher();
            _bluetooth.Connected += (name, battery) => Show(
                battery is { } percent ? string.Format(Strings.IslandBluetoothBattery, name, percent) : string.Format(Strings.IslandBluetooth, name),
                IslandActivity.DefaultDuration, new IslandLook("bluetooth", null, battery / 100.0));
        }

        if (!extras.Downloads)
        {
            _downloads?.Dispose();
            _downloads = null;
        }
        else if (_downloads is null)
        {
            _downloads = new DownloadWatcher();
            _downloads.Finished += path => Show(
                string.Format(Strings.IslandDownloaded, Path.GetFileName(path)), IslandActivity.DefaultDuration, new IslandLook("download", "#4CC38A", null, path));
        }
    }

    private void StopWatchers()
    {
        _privacy?.Dispose();
        _privacy = null;
        _bluetooth?.Dispose();
        _bluetooth = null;
        _downloads?.Dispose();
        _downloads = null;
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
            _hotZone.FilesHovered += (_, _) => OpenCard(); // Files dragged to the top edge: the shelf is where they go.
            _hotZone.Place();
        }
    }

    // ---- What is on screen ----

    // Whether the island has to stay out of the way now: quiet hours, a program that hides it, or a full-screen app (unless
    // the program is one it shows over).
    private bool Concealed()
    {
        var rules = _options.Extras.RulesOrDefault;
        if (rules.Quiet(DateTime.Now))
        {
            return true;
        }

        var fullscreen = FullscreenGuard.IsActive();
        var hide = rules.HideApps is { Count: > 0 };
        if (!fullscreen && !hide)
        {
            return false;
        }

        var app = IslandRules.ForegroundApp();
        return hide && rules.HideApps!.Contains(app) || fullscreen && !(rules.ShowApps?.Contains(app) ?? false);
    }

    // Brings the window in line with what should be up: the pointer and what hides the island hold the countdown of a line,
    // a finished line ends, and the window shows the current line, or else the timer, the info, or the song when it is to
    // stay while it plays.
    private void Refresh()
    {
        var now = Now;
        // The situation in front decides where NotiLand is (while it is placed in Settings, that place is shown instead).
        var scene = _editing ?? IslandScene.Detect(_scene);
        if (scene != _scene)
        {
            _scene = scene;
            ApplyPlace();
        }

        // A system overlay (a screen snip, Start) in front: NotiLand stays under it like any window, and its lines wait.
        var overlay = IslandScene.SystemOverlayInFront();
        _overlayUp = overlay;
        if (overlay)
        {
            _overlaySeen = Environment.TickCount64;
        }

        var concealed = _editing is null && Concealed();
        _queue.Hold(concealed || overlay || _window?.IsHovered == true, now);
        if (_queue.Tick(now))
        {
            return; // CurrentChanged has come back here for the next one.
        }

        if (_countdown.TakeDone(now))
        {
            _queue.Show(new IslandActivity(Strings.IslandTimerDone, TimeSpan.FromSeconds(10), Look: new IslandLook("timer", "#4CC38A")), now);
            return;
        }

        if (_info is { } until && until != TimeSpan.MaxValue && now >= until)
        {
            _info = null;
        }

        object? wanted = _editing is { } editing ? new LiveLine("place", string.Format(Strings.IslandPlaceEditing, SceneName(editing)), new IslandLook("move", "#4CC3FF")) : _queue.Current;
        if (wanted is null && _countdown.Running)
        {
            wanted = new LiveLine("timer", string.Format(Strings.IslandTimerLeft, IslandTimer.Format(_countdown.Left(now))),
                new IslandLook("timer", null, _countdown.Elapsed(now)));
        }

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
            UpdateEscapeWatch();
            return;
        }

        _timer.Start(); // Also while concealed: the timer is what notices the full-screen app ending.
        if (concealed)
        {
            Withdraw(animated: false); // Nobody to watch an animation.
            UpdateEscapeWatch();
            return;
        }

        if (!Equals(_shown, wanted))
        {
            var previous = _shown;
            _shown = wanted;
            Present(wanted, previous);
        }

        UpdateProgress();
        UpdateEscapeWatch();
        if (!overlay)
        {
            _window?.RaiseAboveTaskbar(); // Over the taskbar, which jumps back up when it is clicked.
        }
    }

    // The bar of a song under the text follows its position between the reports of the player.
    private void UpdateProgress()
    {
        var source = _shown switch
        {
            MediaTrack track => track.Source,
            IslandActivity { IsMusic: true } activity => activity.Source,
            _ => null,
        };
        if (source is null || _window is null)
        {
            return;
        }

        var current = _tracks.FirstOrDefault(t => t.Source == source);
        var now = DateTimeOffset.UtcNow;
        _window.SetProgress(current?.Timeline?.Fraction(now, current.Playing));
        _window.SetTime(current?.Timeline is { } line
            ? MediaTimeline.Format(line.PositionAt(now, current.Playing)) + " / " + MediaTimeline.Format(line.Duration)
            : null);
    }

    // The same line of the info (a player is the same player when it has a new song; a live line when it has a new text).
    private static bool SameInfo(object a, object b) => (a, b) switch
    {
        (MediaTrack x, MediaTrack y) => x.Source == y.Source,
        (LiveLine x, LiveLine y) => x.Kind == y.Kind,
        _ => Equals(a, b),
    };

    private void Present(object what, object? previous)
    {
        var window = Window();
        switch (what)
        {
            case IslandActivity activity:
                window.Display(activity.Text, activity.Cover, activity.IsMusic ? _musicActions : null, activity.Source, activity.Look);
                window.SetPlaying(activity.IsMusic && _tracks.Any(t => t.Source == activity.Source && t.Playing));
                break;
            case MediaTrack track when previous is MediaTrack before && before.Source == track.Source && MusicPolicy.SameSong(before, track):
                // The same song with a new position or state: only what changed, so the text keeps scrolling and the bar stays.
                window.SetPlaying(track.Playing);
                break;
            case MediaTrack track:
                var trackLook = track.Accent is { } c ? new IslandLook(Accent: $"#{c.R:X2}{c.G:X2}{c.B:X2}") : null;
                window.Display(MusicPolicy.Label(track), track.Cover, _musicActions, track.Source, trackLook);
                window.SetPlaying(track.Playing);
                break;
            case LiveLine live when previous is LiveLine before && before.Kind == live.Kind:
                window.Update(live.Text, live.Look?.Progress);
                break;
            case LiveLine live:
                window.Display(live.Text, look: live.Look);
                window.MarkLive(true);
                break;
        }
    }

    // A change of hover starts the pill's animation; the refresh (queries of the foreground window and the like) waits until
    // that animation's first frames are drawn, one refresh for any number of changes.
    private bool _refreshQueued;

    private void RefreshSoon()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            if (_window is not null)
            {
                Refresh();
            }
        });
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
            Configure(_window);
            _window.PositionChanged += (x, y) => OnDragged(new ScreenPoint(x, y));
            _window.DoubleClicked += (_, _) => EndPlaceEdit(save: true);
            _window.HoverChanged += (_, _) => RefreshSoon();
            _window.Clicked += (_, _) => _queue.Dismiss(Now);
            _window.ActionRequested += target => ActionRequested?.Invoke(target);
            _window.ExpandRequested += (_, _) => ToggleCard();
            _window.FilesHovered += (_, _) => OpenCard();
        }

        return _window;
    }
}
