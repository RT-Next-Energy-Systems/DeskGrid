using BrimDeck.Native;

namespace BrimDeck;

// DeskGrid build (see third_party/BrimDeck/DESKGRID.md): BrimDeck runs inside DeskGrid's process as NotiLand. DeskGrid starts it
// (StartHosted), asks it to leave (ExitApplication, with the panels' power-off, or StopNow) and is told when it has (Stopped).
// It neither registers itself to start with Windows (StartupRegistration) nor checks for updates on its own
// (StartBackgroundUpdates): DeskGrid does both.
public sealed partial class App
{
    internal static bool ManagedByDeskGrid => true;

    private EventWaitHandle? _quitEvent;

    /// <summary>Starts BrimDeck on the calling (UI) thread of the host; <see langword="null"/> when a BrimDeck of its own already runs.</summary>
    public static App? StartHosted(params string[] args)
    {
        var app = new App();
        return app.Start(args) ? app : null;
    }

    /// <summary>Stops at once, without the panels' power-off animation (the host is ending).</summary>
    public void StopNow()
    {
        if (Exiting) { Shutdown(); return; }
        Exiting = true; Lifetime.Cancel();
        try { _settingsWindow?.RememberSize(); FlushSettings(); FinishWriting(); } catch (Exception ex) when (ex is not OutOfMemoryException) { _recordCrash?.Invoke(ex); }
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
