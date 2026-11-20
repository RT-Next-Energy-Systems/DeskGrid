using System.Globalization;
using DeskGrid.Diagnostics;

namespace DeskGrid.NotiLand;

/// <summary>
/// NotiLand is BrimDeck (Apache-2.0, <c>third_party/BrimDeck</c>), which runs inside DeskGrid's own process: this starts it when
/// "Show NotiLand" is on and stops it when the setting is turned off or DeskGrid ends. It brings its own windows and tray icon; its
/// settings pages are shown in DeskGrid's settings window (<see cref="OpenSettingsPages"/>) and are kept in
/// <c>%LOCALAPPDATA%\BrimDeck</c>. It speaks DeskGrid's language. Everything must be called on the UI thread.
/// </summary>
internal sealed class NotiLandHost : IDisposable
{
    private global::BrimDeck.App? _app;
    private NotiLandSettings? _settings;
    private bool _stopping;
    private bool _disposed;

    /// <summary>Whether NotiLand runs now.</summary>
    public bool IsRunning => _app is not null;

    /// <summary>Raised after NotiLand started or stopped: what shows its settings pages must look again (<see cref="OpenSettingsPages"/>).</summary>
    public event Action? StateChanged;

    /// <summary>Raised when something in NotiLand asks for its settings (its gear, its tray menu, a link on one of its pages); the page, or -1 for the one shown last.</summary>
    public event Action<int>? SettingsRequested;

    /// <summary>Starts or stops NotiLand to match the setting, and has it speak DeskGrid's language.</summary>
    public void Apply(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        if (enabled)
        {
            Start();
            _app?.FollowLanguage(English);
        }
        else
        {
            Stop();
        }
    }

    // DeskGrid's language in force (UiLanguage.Apply sets it): English, or Traditional Chinese.
    private static bool English => !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>NotiLand's settings pages for DeskGrid's settings window, or <see langword="null"/> while NotiLand does not run. The same pages until <see cref="CloseSettingsPages"/>.</summary>
    public NotiLandSettings? OpenSettingsPages() => _app is null || _stopping ? null : _settings ??= new NotiLandSettings(_app.CreateSettingsPages());

    /// <summary>The settings window is done with the pages: what they hold is let go.</summary>
    public void CloseSettingsPages()
    {
        if (_settings is { } settings)
        {
            _settings = null;
            Guard("released", settings.Dispose);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseSettingsPages();
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
            if (global::BrimDeck.App.StartHosted(English) is not { } app)
            {
                Log.Info("NotiLand did not start: a BrimDeck of its own is already running.");
                return;
            }

            app.Stopped += () => OnStopped(app);
            app.SettingsRequested += page => SettingsRequested?.Invoke(page);
            _app = app;
            Log.Info("NotiLand started.");
            StateChanged?.Invoke();
        });
    }

    // The panels power off (a short animation), then BrimDeck lets go of everything and reports Stopped. Its settings pages go at once.
    private void Stop()
    {
        if (_app is { } app && !_stopping)
        {
            _stopping = true;
            CloseSettingsPages();
            StateChanged?.Invoke();
            Guard("stopped", app.ExitApplication);
        }
    }

    private void OnStopped(global::BrimDeck.App app)
    {
        if (_app != app)
        {
            return;
        }

        CloseSettingsPages();
        _app = null;
        _stopping = false;
        StateChanged?.Invoke();
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
