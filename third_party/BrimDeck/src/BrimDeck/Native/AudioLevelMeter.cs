using System.Diagnostics;
using BrimDeck.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BrimDeck.Native;

// Audio is a visualization input, never a discoverable media source. Every COM
// object is created, read, and released on this single worker thread.
// The level bars prefer the player's captured audio; when process capture is not
// available they fall back to the session peak meter. Bars shown at all times read the
// whole system's sound (every process but BrimDeck) and fall back to the devices' peak meters.
internal sealed class AudioLevelMeter : IDisposable
{
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private readonly List<AudioSessionControl> _sessions = [];
    private readonly List<MMDevice> _devices = [];
    private Thread? _thread;
    private MediaAppOwner? _requested;
    private int _revision;
    private bool _disposed;
    private float _peak;
    private volatile ProcessLoopbackCapture? _capture;
    // DeskGrid change: notices a capture that cannot hear the player, and the processes already tried for it.
    private readonly CaptureWatch _watch = new();
    private readonly HashSet<uint> _tried = [];
    // DeskGrid change: no session of the player could be told from the others (its executable, package or id is not the one
    // looked for: another Apple Music version, another Windows), so the capture hears everything but this program instead of
    // leaving the bars flat. Only touched on the meter's thread.
    private bool _systemFallback;
    private string _lastNote = "";
    // The target that stands for the whole system; no application has this id or process name.
    private static readonly MediaAppOwner Everything = new(null, "\0", "\0");
    public float Peak => Volatile.Read(ref _peak);
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    // How long a capture keeps running after nothing asks for it (MemoryPlan.MediaLinger): a song paused and played again, or bars
    // hidden and shown again, within it find the capture running instead of starting it again, which waits up to two seconds for
    // the system and shows flat bars meanwhile.
    public TimeSpan Linger { get; set; }
    private Timer? _release;
    public void SetSource(MediaAppOwner? owner, bool enabled, bool system = false)
    {
        var target = enabled ? owner : system ? Everything : null;
        lock (_gate)
        {
            if (_disposed) return;
            if (target is not null || Linger <= TimeSpan.Zero) { _release?.Dispose(); _release = null; }
            else if (_requested is not null)
            {
                _release ??= new Timer(_ => Release(), null, Linger, Timeout.InfiniteTimeSpan);
                return;
            }
            if (_requested == target) return;
            _requested = target; _revision++;
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "BrimDeck audio level" };
                _thread.SetApartmentState(ApartmentState.MTA); _thread.Start();
            }
            _wake.Set();
        }
    }
    // The linger ran out with nothing asking again: the capture stops.
    private void Release()
    {
        lock (_gate)
        {
            if (_disposed || _release is null) return;
            _release.Dispose(); _release = null;
            if (_requested is null) return;
            _requested = null; _revision++; _wake.Set();
        }
    }
    // Fills the bars' levels from the captured audio, in the given way: flowing along the bars, or one band of pitch for each of
    // the levels.Length bars.
    public bool TryFill(Span<double> levels, EqualizerMode mode)
    {
        // DeskGrid change: a capture that hears nothing while the session meters do leaves the bars to the peak meter (CaptureWatch).
        if (_capture is not { } capture || _watch.Deaf) return false;
        capture.Signal.Fill(levels, Now, mode);
        return true;
    }
    private void Run()
    {
        int revision = -1;
        MediaAppOwner? target = null;
        bool retried = false;
        double nextPeak = 0, nextWatch = 0, nextAlive = 0, nextLook = 0, nextSearch = 0, lastTry = double.NegativeInfinity;
        bool refreshed = false;
        // A player may create its audio session after it reports playback; look again with growing pauses.
        int searchDelay = 1000;
        WaitHandle[]? waits = null;
        try
        {
            while (true)
            {
                bool changed;
                lock (_gate)
                {
                    if (_disposed) break;
                    changed = revision != _revision;
                    if (changed) { revision = _revision; target = _requested; }
                }
                // Starting a capture waits for the system; do it outside the lock SetSource takes on the UI thread.
                if (changed) { _tried.Clear(); _watch.Reset(); refreshed = false; Clear(); if (target is not null) Enumerate(target); retried = false; searchDelay = 1000; nextSearch = Now + searchDelay; }
                var capture = _capture;
                double now = Now;
                if (capture is not null && !capture.Drain(now)) { StopCapture(); capture = null; }
                // DeskGrid change: a capture of a process that has ended (Apple Music restarts its AMPLibraryAgent) goes on without an
                // error and without sound, and the sessions read before are gone with it: the player's sessions are looked for
                // again, and the pauses of that search are kept short, as the player usually has a new process within moments.
                if (capture is not null && target is not null && target != Everything && now >= nextAlive)
                {
                    nextAlive = now + .5;
                    if (!Alive(capture.ProcessId)) { Clear(); Enumerate(target); capture = _capture; _watch.Reset(); retried = false; searchDelay = 1000; }
                    // Silent for a while although the player plays: it may have moved its sound to another process. A capture of the
                    // whole system (no session of the player known) keeps looking, as the player's session may only appear later.
                    else if ((_systemFallback || now - capture.LastLoud > 2) && now >= nextLook)
                    {
                        nextLook = now + (_systemFallback ? 3 : 2);
                        if (FindPlayingPid(target) is { } playing && playing != capture.ProcessId) { Clear(); Enumerate(target); capture = _capture; _watch.Reset(); }
                    }
                }
                // A capture that hears nothing while the player's session meters do: capture another process of the player, or
                // look for its sessions again; failing that the bars follow the peak meter (TryFill).
                if (capture is not null && target is not null && target != Everything && now >= nextWatch)
                {
                    nextWatch = now + .1;
                    float heard = SessionPeak();
                    Volatile.Write(ref _peak, heard);
                    _watch.Update(now, now - capture.LastLoud < .3, heard);
                    if (_watch.Deaf && now - lastTry > 1)
                    {
                        lastTry = now;
                        // The sessions are looked for again once; after that the bars stay with the peak meter.
                        if (!Retarget(capture.ProcessId) && !refreshed) { refreshed = true; Clear(); Enumerate(target); _watch.Reset(); }
                    }
                    capture = _capture;
                }
                // Captured audio drives the bars whenever a capture runs, so session peaks are only read without one.
                if (capture is null && now >= nextPeak)
                {
                    nextPeak = now + .033;
                    float peak = 0;
                    try
                    {
                        // DeskGrid change: a player none of whose sessions was found follows the devices' meters, like the whole system.
                        if (target == Everything || _sessions.Count == 0) foreach (var device in _devices) peak = Math.Max(peak, device.AudioMeterInformation.MasterPeakValue);
                        else foreach (var session in _sessions) peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue);
                    }
                    catch
                    {
                        Clear();
                        if (!retried && target is not null) { Enumerate(target); retried = true; }
                    }
                    Volatile.Write(ref _peak, peak);
                }
                if (capture is not null)
                {
                    if (waits?[1] != capture.Ready) waits = [_wake, capture.Ready];
                    WaitHandle.WaitAny(waits, 20);
                }
                else if (_sessions.Count > 0 || _devices.Count > 0)
                {
                    _wake.WaitOne(33);
                    // DeskGrid change: no session of the player yet (only the devices' meters are read): look again with growing pauses.
                    if (target is not null && target != Everything && _sessions.Count == 0 && Now >= nextSearch)
                    {
                        searchDelay = Math.Min(searchDelay * 2, 5_000); nextSearch = Now + searchDelay;
                        Clear(); Enumerate(target);
                    }
                }
                else if (target is null) _wake.WaitOne();
                else if (!_wake.WaitOne(searchDelay))
                {
                    // DeskGrid change: 5 seconds at most (was 30) for a player, whose process may only be restarting.
                    searchDelay = Math.Min(searchDelay * 2, target == Everything ? 30_000 : 5_000);
                    Clear(); Enumerate(target);
                }
            }
        }
        finally { Clear(); Volatile.Write(ref _peak, 0); _wake.Dispose(); }
    }
    private void Enumerate(MediaAppOwner target)
    {
        if (target == Everything) { EnumerateEverything(); return; }
        var others = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int d = 0; d < devices.Count; d++)
            {
                var device = devices[d]; _devices.Add(device);
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i]; bool keep = false;
                    try
                    {
                        var process = AppIdentity.Describe((int)session.GetProcessID);
                        if (MediaAppTarget.Plays(target, process)) { _sessions.Add(session); keep = true; }
                        else if (others.Count < 8) others.Add($"{process.ProcessName}[{process.PackageFamily}|{process.AppId}]");
                    }
                    catch { }
                    finally { if (!keep) session.Dispose(); }
                }
            }
        }
        catch (Exception ex) { Note($"listing the audio sessions failed: {ex.GetType().Name} {ex.Message}"); Clear(); return; }
        // Capture the process that owns the playing session; its child processes are included.
        var playing = _sessions.FirstOrDefault(s => s.State == AudioSessionState.AudioSessionStateActive) ?? _sessions.FirstOrDefault();
        if (playing is not null) _capture = ProcessLoopbackCapture.TryStart(playing.GetProcessID);
        // DeskGrid change: none of the audio sessions is the player's (another version of it, another way Windows names its
        // processes): hear everything but this program rather than show flat bars.
        else if (_devices.Count > 0)
        {
            _capture = ProcessLoopbackCapture.TryStart((uint)Environment.ProcessId, exclude: true);
            _systemFallback = _capture is not null;
        }
        Note($"player id '{target.AppId}' name '{target.ProcessName}': {_sessions.Count} session(s) of it, capture "
            + (_capture is null ? "none" : _systemFallback ? "of the whole system" : $"of process {_capture.ProcessId}")
            + $", Windows build {Environment.OSVersion.Version.Build}"
            + (_sessions.Count == 0 ? $", other sessions: {(others.Count == 0 ? "none" : string.Join(", ", others))}" : ""));
    }
    // Says what the meter found, to the host's log (DeskGrid's), once for each different thing.
    private void Note(string message)
    {
        if (message == _lastNote) return;
        _lastNote = message;
        try { App.Trace?.Invoke("NotiLand audio: " + message); } catch { }
    }
    // Every render device, for their peak meters, and a capture of every process but this one.
    private void EnumerateEverything()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int d = 0; d < devices.Count; d++) _devices.Add(devices[d]);
        }
        catch { Clear(); return; }
        _capture = ProcessLoopbackCapture.TryStart((uint)Environment.ProcessId, exclude: true);
    }
    private static bool Alive(uint processId)
    {
        try { using var process = Process.GetProcessById((int)processId); return !process.HasExited; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    // The process of a session of the player that is playing now; null when there is none. Read afresh, touching nothing held.
    private static uint? FindPlayingPid(MediaAppOwner target)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        try
                        {
                            if (session.State == AudioSessionState.AudioSessionStateActive && MediaAppTarget.Plays(target, AppIdentity.Describe((int)session.GetProcessID)))
                                return session.GetProcessID;
                        }
                        catch { }
                        finally { session.Dispose(); }
                    }
                }
        }
        catch { }
        return null;
    }
    // The loudest of the player's session meters; 0 when they cannot be read.
    private float SessionPeak()
    {
        float peak = 0;
        foreach (var session in _sessions)
            try { peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue); } catch { }
        return peak;
    }
    // Captures the player's loudest session of another process than the one that hears nothing; false when there is none to try.
    private bool Retarget(uint deaf)
    {
        uint best = 0; float loudest = 0;
        foreach (var session in _sessions)
        {
            try
            {
                uint pid = session.GetProcessID;
                if (pid == deaf || _tried.Contains(pid)) continue;
                float peak = session.AudioMeterInformation.MasterPeakValue;
                if (peak >= CaptureWatch.Heard && peak > loudest) { best = pid; loudest = peak; }
            }
            catch { }
        }
        if (best == 0) return false;
        _tried.Add(deaf); _tried.Add(best);
        StopCapture();
        _capture = ProcessLoopbackCapture.TryStart(best);
        _watch.Reset();
        return _capture is not null;
    }
    private void StopCapture()
    {
        var capture = _capture; _capture = null; _systemFallback = false;
        capture?.Dispose();
    }
    private void Clear()
    {
        StopCapture();
        foreach (var session in _sessions) { try { session.Dispose(); } catch { } }
        _sessions.Clear();
        foreach (var device in _devices) { try { device.AudioSessionManager.Dispose(); device.Dispose(); } catch { } }
        _devices.Clear();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _release?.Dispose(); _release = null; if (_thread is null) _wake.Dispose(); else _wake.Set(); }
    }
}
