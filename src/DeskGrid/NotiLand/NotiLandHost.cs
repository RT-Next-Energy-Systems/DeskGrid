using DeskGrid.Diagnostics;

namespace DeskGrid.NotiLand;

/// <summary>
/// NotiLand is BrimDeck (Apache-2.0, <c>third_party/BrimDeck</c>), which runs inside DeskGrid's own process: this starts it when
/// "Show NotiLand" is on and stops it when the setting is turned off or DeskGrid ends. It brings its own windows, tray icon and
/// settings (kept in <c>%LOCALAPPDATA%\BrimDeck</c>); the host only decides whether it exists. Everything must be called on the UI thread.
/// </summary>
internal sealed class NotiLandHost : IDisposable
{
    private global::BrimDeck.App? _app;
    private bool _disposed;

    /// <summary>Whether NotiLand runs now.</summary>
    public bool IsRunning => _app is not null;

    /// <summary>Starts or stops NotiLand to match the setting.</summary>
    public void Apply(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        if (enabled)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>Opens NotiLand's own settings window.</summary>
    public void OpenSettings() => _app?.OpenSettings();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_app is { } app)
        {
            _app = null;
            Guard("stopped", app.StopNow);
        }
    }

    private void Start()
    {
        if (_app is not null)
        {
            return;
        }

        Guard("started", () =>
        {
            if (global::BrimDeck.App.StartHosted() is not { } app)
            {
                Log.Info("NotiLand did not start: a BrimDeck of its own is already running.");
                return;
            }

            app.Stopped += () => _app = _app == app ? null : _app;
            _app = app;
            Log.Info("NotiLand started.");
        });
    }

    // The panels power off (a short animation), then BrimDeck lets go of everything and reports Stopped.
    private void Stop()
    {
        if (_app is { } app)
        {
            Guard("stopped", app.ExitApplication);
        }
    }

    // NotiLand is someone else's code: whatever it throws must not take DeskGrid down.
    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Error($"NotiLand could not be {what}", exception);
        }
    }
}
