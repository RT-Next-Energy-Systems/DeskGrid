using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskGrid.Open;

/// <summary>
/// What a pinned DeskGrid button starts: <c>DeskGridOpen.exe --open "Folder"</c> or <c>--all</c>. It hands the request to the
/// DeskGrid that is running (the pipe <c>InstanceChannel</c> listens on) and ends, or starts DeskGrid with the same arguments
/// when none runs. DeskGrid.exe does the same when it is started with those arguments, but it is a large single-file program
/// that takes about 130 ms only to unpack itself; this one takes a few milliseconds.
/// </summary>
internal static class Program
{
    // The single-instance mutex of DeskGrid (App.SingleInstanceName): it exists while DeskGrid runs.
    private const string RunningMutex = @"Local\DeskGrid.SingleInstance";

    // The path of DeskGrid.exe, which DeskGrid writes here (OpenLauncher) each time it starts.
    private const string TargetFile = "DeskGridOpen.target";

    // What a request for the list of all folders is called on the pipe (OpenRequest.AllFolders).
    private const string AllFolders = "*";

    private const uint AnyProcess = 0xFFFFFFFF;

    private static int Main(string[] args)
    {
        string name;
        if (args.Length == 1 && string.Equals(args[0], "--all", StringComparison.OrdinalIgnoreCase))
        {
            name = AllFolders;
        }
        else if (args.Length == 2 && string.Equals(args[0], "--open", StringComparison.OrdinalIgnoreCase) && args[1].Length > 0)
        {
            name = args[1];
        }
        else
        {
            return 2;
        }

        // First: the cursor is where the button was clicked, not where it is once this has finished.
        GetCursorPos(out var cursor);

        if (IsDeskGridRunning())
        {
            return Send(cursor, name) ? 0 : 1;
        }

        return StartDeskGrid(args) ? 0 : 3;
    }

    private static bool IsDeskGridRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(RunningMutex, out var running))
            {
                running.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // It exists but may not be opened (a DeskGrid run as administrator): it is there all the same.
            return true;
        }
    }

    private static bool Send(Point cursor, string name)
    {
        try
        {
            AllowSetForegroundWindow(AnyProcess);
            using var client = new NamedPipeClientStream(".", "DeskGrid.Open." + Environment.UserName, PipeDirection.Out);
            client.Connect(5000);
            var line = cursor.X + "\t" + cursor.Y + "\t" + name + "\n";
            var bytes = new UTF8Encoding(false).GetBytes(line);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // DeskGrid is not running: it starts and opens the panel once its folders are read (Program.Main of DeskGrid).
    private static bool StartDeskGrid(string[] args)
    {
        try
        {
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TargetFile);
            if (!File.Exists(file))
            {
                return false;
            }

            var program = File.ReadAllText(file).Trim();
            if (!File.Exists(program))
            {
                return false;
            }

            var arguments = args.Length == 1 ? "--all" : "--open \"" + args[1] + "\"";
            Process.Start(new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(program),
            })?.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
