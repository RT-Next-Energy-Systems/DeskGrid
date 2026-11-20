using System.Diagnostics;

namespace BrimDeck;

// DeskGrid build (see third_party/BrimDeck/DESKGRID.md): DeskGrid starts this program as "NotiLand" with `--deskgrid <pid>`.
// DeskGrid then owns its life: it starts and stops it, so this program neither registers itself to start with Windows nor
// checks for updates on its own, and it ends when DeskGrid does.
public partial class App
{
    internal const string DeskGridSwitch = "--deskgrid";

    // The DeskGrid process that started this one, or 0 when BrimDeck was started on its own.
    internal static int DeskGridProcess { get; } = ParentFromArguments(Environment.GetCommandLineArgs());

    internal static bool ManagedByDeskGrid => DeskGridProcess != 0;

    internal static int ParentFromArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == DeskGridSwitch && int.TryParse(args[i + 1], out var pid) && pid > 0) return pid;
        }

        return 0;
    }

    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitListener;

    private static string QuitEventName(string user) => @"Local\BrimDeck.Quit." + user;

    // A second start with --quit (what DeskGrid does when NotiLand is turned off) raises this event: the running copy leaves cleanly.
    private void WatchForQuit(string user)
    {
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName(user));
        _quitListener = ThreadPool.RegisterWaitForSingleObject(_quitEvent, (_, _) => Dispatcher.BeginInvoke(ExitApplication), null, Timeout.Infinite, true);
    }

    private void FollowDeskGrid()
    {
        if (!ManagedByDeskGrid) return;
        try
        {
            var parent = Process.GetProcessById(DeskGridProcess);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => Dispatcher.BeginInvoke(ExitApplication);
            if (parent.HasExited) Dispatcher.BeginInvoke(ExitApplication);
        }
        catch (ArgumentException) { Dispatcher.BeginInvoke(ExitApplication); } // DeskGrid is already gone.
    }
}
