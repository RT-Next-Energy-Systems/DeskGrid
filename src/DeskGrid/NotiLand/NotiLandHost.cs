using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DeskGrid.Diagnostics;

namespace DeskGrid.NotiLand;

/// <summary>
/// NotiLand is BrimDeck (Apache-2.0, <c>third_party/BrimDeck</c>) running beside DeskGrid as a program of its own: DeskGrid starts it
/// when "Show NotiLand" is on, tells it which process to follow (it ends when DeskGrid does) and stops it when the setting is
/// turned off. A release has BrimDeck.exe inside DeskGrid.exe and unpacks it on first use; a development build finds it in
/// <c>NotiLand\</c> beside DeskGrid.exe.
/// </summary>
internal sealed class NotiLandHost : IDisposable
{
    private const string ResourceName = "DeskGrid.NotiLand.exe";
    private const string ExecutableName = "BrimDeck.exe";
    private const string ProcessName = "BrimDeck";
    private const string QuitSwitch = "--quit";

    private bool _enabled;
    private bool _disposed;
    private string? _unpacked;

    /// <summary>Where BrimDeck.exe is, or <see langword="null"/> when this build has none.</summary>
    public string? Executable => FindBesideExe() ?? Unpack();

    /// <summary>Whether BrimDeck is running now (whoever started it).</summary>
    public bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>Starts or stops NotiLand to match the setting.</summary>
    public void Apply(bool enabled)
    {
        if (_disposed || (enabled == _enabled && enabled == IsRunning))
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            Start(null);
        }
        else
        {
            Stop();
        }
    }

    /// <summary>Opens NotiLand's own settings (BrimDeck's settings window).</summary>
    public void OpenSettings()
    {
        if (_enabled)
        {
            Start("--settings");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    // BrimDeck keeps one instance; a second start with "--settings" tells the first to open its settings window.
    private void Start(string? extra)
    {
        if (Executable is not { } exe)
        {
            Log.Info("NotiLand is not part of this build.");
            return;
        }

        if (extra is null && IsRunning)
        {
            return;
        }

        try
        {
            var arguments = $"--deskgrid {Environment.ProcessId}" + (extra is null ? "" : " " + extra);
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
            Log.Info("NotiLand started.");
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("NotiLand could not be started", exception);
        }
    }

    // Asks BrimDeck to leave (it saves its settings first); one that does not answer in time is ended.
    private void Stop()
    {
        var running = Process.GetProcessesByName(ProcessName).Where(IsOurs).ToList();
        if (running.Count == 0)
        {
            return;
        }

        try
        {
            if (Executable is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe, QuitSwitch) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            }

            foreach (var process in running)
            {
                if (!process.WaitForExit(4000))
                {
                    process.Kill();
                }
            }

            Log.Info("NotiLand stopped.");
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("NotiLand could not be stopped", exception);
        }
        finally
        {
            foreach (var process in running)
            {
                process.Dispose();
            }
        }
    }

    // Only a BrimDeck from the places DeskGrid starts it from; one the user installed themselves is theirs.
    private static bool IsOurs(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path is not null && (string.Equals(path, FindBesideExe(), StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(UnpackRoot, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string UnpackRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskGrid", "NotiLand");

    private static string? FindBesideExe()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "NotiLand", ExecutableName);
        return File.Exists(path) ? path : null;
    }

    // A release carries BrimDeck.exe as a resource; it is unpacked once per version (by its hash) and checked by size afterwards.
    private string? Unpack()
    {
        if (_unpacked is not null && File.Exists(_unpacked))
        {
            return _unpacked;
        }

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            return null;
        }

        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(resource))[..16];
            var folder = Path.Combine(UnpackRoot, hash);
            var target = Path.Combine(folder, ExecutableName);
            if (!File.Exists(target) || new FileInfo(target).Length != resource.Length)
            {
                Directory.CreateDirectory(folder);
                resource.Position = 0;
                var temporary = target + ".tmp";
                using (var file = File.Create(temporary))
                {
                    resource.CopyTo(file);
                }

                File.Move(temporary, target, overwrite: true);
                RemoveOtherVersions(folder);
            }

            return _unpacked = target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("NotiLand could not be unpacked", exception);
            return null;
        }
    }

    private static void RemoveOtherVersions(string current)
    {
        foreach (var folder in Directory.GetDirectories(UnpackRoot).Where(f => !string.Equals(f, current, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still in use by an older run; the next unpacking tries again.
            }
        }
    }
}
