using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck;

// DeskGrid build (see third_party/BrimDeck/DESKGRID.md): BrimDeck runs inside DeskGrid's process as NotiLand. DeskGrid starts it
// (StartHosted), asks it to leave (ExitApplication, with the panels' power-off, or StopNow) and is told when it has (Stopped).
// It neither registers itself to start with Windows (StartupRegistration) nor checks for updates on its own
// (StartBackgroundUpdates): DeskGrid does both.
public sealed partial class App
{
    internal static bool ManagedByDeskGrid => true;

    /// <summary>Where the audio level meter says what it found (which sessions, which capture); the host writes it to its log. May be called on any thread.</summary>
    public static Action<string>? Trace { get; set; }

    /// <summary>Whether someone is using NotiLand now (a panel is open or being resized, or its settings are shown): the host leaves its memory alone then.</summary>
    public bool InUse => !Exiting && (Decks.Any(deck => deck.IsExpanded || deck.IsResizing) || _settingsPages is { IsVisible: true });

    private EventWaitHandle? _quitEvent;

    private string? _hostLanguage;

    private static string? s_testDataFolder;
    private static string? s_testInstanceId;

    /// <summary>
    /// For tests of the host: gives BrimDeck a throwaway data folder and instance name so tests neither collide with a running
    /// DeskGrid nor touch <c>%LOCALAPPDATA%\BrimDeck</c>.
    /// </summary>
    public static IDisposable IsolationForTest(string dataFolder, string instanceId)
    {
        (s_testDataFolder, s_testInstanceId) = (dataFolder, instanceId);
        return new TestIsolationScope();
    }

    private sealed class TestIsolationScope : IDisposable
    {
        public void Dispose() => (s_testDataFolder, s_testInstanceId) = (null, null);
    }

    /// <summary>
    /// Starts BrimDeck on the calling (UI) thread of the host, in the host's language (<paramref name="english"/>: English, else Traditional
    /// Chinese); <see langword="null"/> when a BrimDeck of its own already runs.
    /// </summary>
    public static App? StartHosted(bool english, params string[] args)
    {
        var app = new App { _hostLanguage = english ? Loc.English : Loc.Chinese };
        return app.Start(args) ? app : null;
    }

    /// <summary>The host's language changed (<paramref name="english"/>: English, else Traditional Chinese): everything of BrimDeck follows.</summary>
    public void FollowLanguage(bool english)
    {
        string language = english ? Loc.English : Loc.Chinese;
        if (Exiting || Loc.Normalize(Settings.Language) == language) return;
        var settings = Settings.Copy(); settings.Language = language; UpdateSettings(settings);
    }

    /// <summary>Stops at once, without the panels' power-off animation (the host is ending).</summary>
    public void StopNow()
    {
        if (Exiting) { Shutdown(); return; }
        Exiting = true; Lifetime.Cancel();
        try { FlushSettings(); FinishWriting(); } catch (Exception ex) when (ex is not OutOfMemoryException) { _recordCrash?.Invoke(ex); }
        Shutdown();
    }

    /// <summary>
    /// The two helpers BrimDeck starts as a copy of its own program (a portable update, and the one that needs administrator
    /// rights to let the user read frame rates). The host calls this first thing; true means the helper ran and the host must exit.
    /// </summary>
    public static bool TryRunHelper(string[] args, out int exitCode)
    {
        if (Core.PortableUpdate.TryRun(args, out exitCode)) return true;
        if (args is [FramePermission.ElevatedSwitch, var user]) { exitCode = FramePermission.RunElevated(user); return true; }
        exitCode = 0;
        return false;
    }
}
