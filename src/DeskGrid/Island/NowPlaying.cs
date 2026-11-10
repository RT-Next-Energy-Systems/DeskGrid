using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskGrid.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DeskGrid.Island;

/// <summary>
/// What every player that uses Windows' system media controls (the ones behind the volume flyout and the lock screen)
/// reports: music apps, and video in a browser or a video player. One entry per player, and the buttons to control each.
/// Whatever a player shares there is all there is: players that do not use the system media controls do not show up.
/// Nothing leaves this computer. Used on one dispatcher thread; the events of the system arrive on other threads and are
/// moved onto it.
/// </summary>
internal sealed class NowPlaying(Dispatcher dispatcher) : IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _disposed;

    /// <summary>What every player has to say now (those with a title), in a fixed order, playing or not.</summary>
    public IReadOnlyList<MediaTrack> Tracks { get; private set; } = [];

    /// <summary>Raised on the dispatcher thread when <see cref="Tracks"/> is another list.</summary>
    public event EventHandler? Changed;

    /// <summary>Connects to the system media controls; returns when the players have been read. A failure is logged and leaves no tracks.</summary>
    public async Task StartAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
            {
                return;
            }

            _manager = manager;
            manager.SessionsChanged += OnSessionsChanged;
            await SyncAsync();
        }
        catch (Exception exception)
        {
            Log.Error("The system media controls could not be read; the island shows no music or video", exception);
        }
    }

    public void Previous(string source) => Control(source, session => session.TrySkipPreviousAsync());

    public void PlayPause(string source) => Control(source, session => session.TryTogglePlayPauseAsync());

    public void Next(string source) => Control(source, session => session.TrySkipNextAsync());

    public void Seek(string source, TimeSpan position)
    {
        if (!_entries.TryGetValue(source, out var entry))
        {
            return;
        }

        var start = entry.Session.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
        var target = (start + (position < TimeSpan.Zero ? TimeSpan.Zero : position)).Ticks;
        Control(source, session => session.TryChangePlaybackPositionAsync(target));
    }

    public void Dispose()
    {
        _disposed = true;
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        foreach (var entry in _entries.Values)
        {
            Detach(entry);
        }

        _entries.Clear();
        _manager = null;
    }

    private void Control(string source, Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        if (!_entries.TryGetValue(source, out var entry))
        {
            return;
        }

        var session = entry.Session;

        // The player answers when it has done it; nothing here waits for that, and a refusal is not an error.
        _ = Task.Run(async () =>
        {
            try
            {
                await command(session);
            }
            catch (Exception exception)
            {
                Log.Error("A media control command failed", exception);
            }
        });
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        dispatcher.BeginInvoke(async () =>
        {
            if (!_disposed)
            {
                await SyncAsync();
            }
        });

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) =>
        dispatcher.BeginInvoke(async () =>
        {
            if (!_disposed && _entries.Values.FirstOrDefault(e => ReferenceEquals(e.Session, sender)) is { } entry)
            {
                await ReadAsync(entry);
                Publish();
            }
        });

    // Follows every player the system knows: new ones are attached, gone ones let go.
    private async Task SyncAsync()
    {
        var sessions = _manager?.GetSessions() ?? [];
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            var id = session.SourceAppUserModelId;
            if (!alive.Add(id))
            {
                continue; // Two sessions of one app: the first is the one shown.
            }

            if (!_entries.ContainsKey(id))
            {
                var entry = new Entry(id, session);
                _entries[id] = entry;
                session.MediaPropertiesChanged += OnSessionChanged;
                session.PlaybackInfoChanged += OnSessionChanged;
                session.TimelinePropertiesChanged += OnSessionChanged;
            }
        }

        foreach (var gone in _entries.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            Detach(_entries[gone]);
            _entries.Remove(gone);
        }

        foreach (var entry in _entries.Values.ToList())
        {
            await ReadAsync(entry);
        }

        Publish();
    }

    private async Task ReadAsync(Entry entry)
    {
        var generation = ++entry.Generation;
        MediaTrack? track = null;
        try
        {
            var properties = await entry.Session.TryGetMediaPropertiesAsync();
            var playing = entry.Session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var line = entry.Session.GetTimelineProperties();
            var duration = line is null ? TimeSpan.Zero : line.EndTime - line.StartTime;
            var timeline = line is null || duration <= TimeSpan.Zero ? null : new MediaTimeline(duration, line.Position - line.StartTime, line.LastUpdatedTime);
            if (properties is not null && (properties.Title.Length > 0 || properties.Artist.Length > 0))
            {
                (System.Windows.Media.ImageSource? cover, System.Windows.Media.Color? accent) = entry.Cover is { } known && known.Title == properties.Title && known.Artist == properties.Artist
                    ? (known.Image, known.Accent)
                    : await ReadCoverAsync(properties.Thumbnail);
                entry.Cover = (properties.Title, properties.Artist, cover, accent);
                track = new MediaTrack(properties.Title, properties.Artist, playing, cover, entry.Id, timeline, accent);
            }
        }
        catch (Exception exception)
        {
            // A player that closes while it is read: no track.
            Log.Error("Reading a media player failed", exception);
        }

        if (generation == entry.Generation && !_disposed)
        {
            entry.Track = track;
        }
    }

    private void Publish()
    {
        if (_disposed)
        {
            return;
        }

        var tracks = _entries.Values.Select(e => e.Track).OfType<MediaTrack>().OrderBy(t => t.Source, StringComparer.Ordinal).ToList();
        if (!tracks.SequenceEqual(Tracks))
        {
            Tracks = tracks;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task<(System.Windows.Media.ImageSource? Image, System.Windows.Media.Color? Accent)> ReadCoverAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return (null, null);
        }

        try
        {
            using var source = await thumbnail.OpenReadAsync();
            using var stream = source.AsStreamForRead();
            var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            bytes.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // Reads it all now, so the stream can go.
            image.DecodePixelWidth = 96; // The cover is drawn at 22 DIP: this is enough up to 400 % scaling.
            image.StreamSource = bytes;
            image.EndInit();
            image.Freeze();
            return (image, MediaTrack.DominantColor(image));
        }
        catch (Exception exception)
        {
            Log.Error("The cover of a song could not be read; it is shown without", exception);
            return (null, null);
        }
    }

    // One player: its session, what it said last, and the cover of that (playing and pausing do not fetch it again).
    private sealed class Entry(string id, GlobalSystemMediaTransportControlsSession session)
    {
        public string Id { get; } = id;

        public GlobalSystemMediaTransportControlsSession Session { get; } = session;

        public MediaTrack? Track { get; set; }

        public int Generation { get; set; } // Counts the readings, so the answer of an older one is dropped.

        public (string Title, string Artist, System.Windows.Media.ImageSource? Image, System.Windows.Media.Color? Accent)? Cover { get; set; }
    }

    private void Detach(Entry entry)
    {
        entry.Session.MediaPropertiesChanged -= OnSessionChanged;
        entry.Session.PlaybackInfoChanged -= OnSessionChanged;
        entry.Session.TimelinePropertiesChanged -= OnSessionChanged;
    }
}
