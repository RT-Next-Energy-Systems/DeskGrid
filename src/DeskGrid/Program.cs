using DeskGrid.Control;
using DeskGrid.Diagnostics;
using DeskGrid.Taskbar;

namespace DeskGrid;

/// <summary>
/// The program's entry point. A taskbar button starts a second copy of this exe only to hand its request to the DeskGrid
/// that is already running, and a script's <c>--ctl</c> command does the same; neither needs WPF, whose application and
/// theme take most of a start, so they are answered here before an <see cref="App"/> is made.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ControlCli.IsRequest(args))
        {
            using var output = ControlCli.OpenOutput();
            return ControlCli.Run(args, output);
        }

        if (OpenRequest.Parse(args) is { } folder)
        {
            // Read first: the cursor is where the button was clicked, not where it is once this start is done.
            var request = App.OpenMessageAtCursor(folder);
            using (var probe = new Mutex(initiallyOwned: true, App.SingleInstanceName, out var isFirst))
            {
                if (!isFirst)
                {
                    // Another copy holds it: it is the running DeskGrid.
                    Log.Info("DeskGrid is already running; this instance exits.");
                    InstanceChannel.Send(request);
                    return 0;
                }

                // Only a probe: App takes the mutex for good.
                probe.ReleaseMutex();
            }

            // Nobody runs yet: this start becomes DeskGrid and opens the panel once its folders are read.
            App.EarlyOpen = request;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
