using System.IO;
using System.Text;
using Windows.Win32;
using Windows.Win32.System.Console;

namespace DeskGrid.Control;

/// <summary>
/// <c>DeskGrid.exe --ctl &lt;command&gt; [args…]</c>: sends one command to the running DeskGrid over the control pipe,
/// prints its JSON answer on standard output and exits. For scripts and AI agents.
/// </summary>
/// <remarks>Exit codes: 0 done, 1 the command failed, 2 DeskGrid is not running (or did not answer), 3 wrong usage.</remarks>
internal static class ControlCli
{
    public const int Done = 0;
    public const int Failed = 1;
    public const int NotRunning = 2;
    public const int WrongUsage = 3;

    private const string Switch = "--ctl";

    private const uint AnyProcess = 0xFFFFFFFF;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    // Generous: "add" makes a shortcut per path, and the UI thread may be busy for a moment.
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    public static bool IsRequest(IReadOnlyList<string> args) =>
        args is [var first, ..] && string.Equals(first, Switch, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the command in <paramref name="args"/> (which start with <c>--ctl</c>); returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, string? pipeName = null)
    {
        if (ToRequest(args) is not { } request)
        {
            output.Write(HelpText());
            return args.Count == 2 && args[1] is "help" or "--help" or "-h" or "/?" ? Done : WrongUsage;
        }

        ControlResponse response;
        try
        {
            // An opened panel has to come to the front, which only a process in the foreground may grant.
            PInvoke.AllowSetForegroundWindow(AnyProcess);
            response = ControlChannel.Send(request, ConnectTimeout, ReplyTimeout, pipeName);
        }
        catch (TimeoutException)
        {
            output.WriteLine(ControlResponse.Failure("DeskGrid is not running, or did not answer.").Format());
            return NotRunning;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            output.WriteLine(ControlResponse.Failure($"Could not reach DeskGrid: {exception.Message}").Format());
            return NotRunning;
        }

        output.WriteLine(response.Format());
        return response.Ok ? Done : Failed;
    }

    /// <summary>
    /// The request in <paramref name="args"/>, or <see langword="null"/> for help or no command. Relative paths given to
    /// <c>add</c> are made full here, against the directory the command was run in.
    /// </summary>
    internal static ControlRequest? ToRequest(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || args[1] is "help" or "--help" or "-h" or "/?")
        {
            return null;
        }

        var command = args[1].ToLowerInvariant();
        var rest = args.Skip(2).ToList();
        if (command == "add")
        {
            rest = [.. rest.Select((arg, i) => i == 0 ? arg : Path.GetFullPath(arg))];
        }

        return new ControlRequest(command, rest);
    }

    internal static string HelpText()
    {
        var text = new StringBuilder();
        text.AppendLine("Controls the running DeskGrid. Turn on \"Allow control from scripts and AI agents\" in Settings first.");
        text.AppendLine();
        text.AppendLine("Usage: DeskGrid.exe --ctl <command> [arguments]");
        text.AppendLine();
        var width = ControlCommands.Usage.Max(u => u.Usage.Length);
        foreach (var (usage, description) in ControlCommands.Usage)
        {
            text.AppendLine($"  {usage.PadRight(width)}  {description}");
        }

        text.AppendLine();
        text.AppendLine("Prints one line of JSON: {\"ok\":true,\"result\":...} or {\"ok\":false,\"error\":\"...\"}.");
        text.AppendLine("Folders and items are named as \"list\" shows them (items by file name, e.g. \"Notepad.lnk\").");
        text.AppendLine("Exit code: 0 done, 1 the command failed, 2 DeskGrid is not running, 3 wrong usage.");
        return text.ToString();
    }

    /// <summary>
    /// Standard output of this (windowed) process: the pipe or file it was redirected to, or else the console of the
    /// command prompt it was started from. A windowed program gets no console of its own.
    /// </summary>
    public static TextWriter OpenOutput()
    {
        var handle = PInvoke.GetStdHandle(STD_HANDLE.STD_OUTPUT_HANDLE);
        // Null when nothing was handed down; -1 (INVALID_HANDLE_VALUE) when it couldn't be.
        if (handle.IsNull || (nint)(IntPtr)handle == -1)
        {
            PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS);
        }

        return new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }
}
