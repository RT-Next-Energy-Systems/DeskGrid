using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskGrid.Island;

/// <summary>Which program uses the microphone ("microphone") or the camera ("webcam"). Windows notes it in the registry (the "consent store").</summary>
internal readonly record struct SensorUse(string Kind, string App);

/// <summary>
/// Watches who is using the microphone or camera, every two seconds while on, and raises <see cref="Started"/> when a
/// program begins. Reads only the names Windows itself keeps for its privacy page; nothing is recorded or sent.
/// </summary>
internal sealed class PrivacyWatcher : IDisposable
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private HashSet<SensorUse> _inUse;

    public PrivacyWatcher()
    {
        _inUse = [.. Read()]; // What is already in use when it starts is not news.
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    /// <summary>Raised on the dispatcher thread when a program starts using the microphone or camera.</summary>
    public event Action<SensorUse>? Started;

    /// <summary>Everything in use now.</summary>
    public IReadOnlyCollection<SensorUse> InUse => _inUse;

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private bool _disposed;

    internal static IEnumerable<SensorUse> Read()
    {
        foreach (var kind in new[] { "microphone", "webcam" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{kind}");
            if (key is null)
            {
                continue;
            }

            foreach (var use in Scan(key, kind))
            {
                yield return use;
            }
        }
    }

    // Packaged apps are keys of their own, desktop programs sit one level down under NonPackaged (named by their path with
    // a hash sign for each backslash). A key whose "LastUsedTimeStop" is 0 and "LastUsedTimeStart" is not: in use now.
    private static IEnumerable<SensorUse> Scan(RegistryKey key, string kind)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub is null)
            {
                continue;
            }

            if (name == "NonPackaged")
            {
                foreach (var use in Scan(sub, kind))
                {
                    yield return use;
                }

                continue;
            }

            if (sub.GetValue("LastUsedTimeStart") is long start && sub.GetValue("LastUsedTimeStop") is long stop && start != 0 && stop == 0)
            {
                yield return new SensorUse(kind, NameOf(name));
            }
        }
    }

    /// <summary>The program in a consent-store key: <c>C:#Program Files#Zoom#Zoom.exe</c> is "Zoom"; a package name is its first part.</summary>
    internal static string NameOf(string key)
    {
        var last = key.Split('#', '\\').Last();
        if (last.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return last[..^4];
        }

        return last.Split('_')[0].Split('.').Last();
    }

    // The registry is read on a pool thread (many keys), and the result is applied here; a read still running is not joined by another.
    private async void Check()
    {
        if (_reading)
        {
            return;
        }

        _reading = true;
        try
        {
            var now = await Task.Run(() => Read().ToHashSet());
            if (_disposed)
            {
                return; // Turned off while the registry was read: nothing more to tell.
            }

            foreach (var use in now.Where(u => !_inUse.Contains(u)))
            {
                Started?.Invoke(use);
            }

            _inUse = now;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An async void method: whatever escapes here would reach the dispatcher's unhandled-exception handler.
            Diagnostics.Log.Error("Reading who uses the microphone or camera failed", exception);
        }
        finally
        {
            _reading = false;
        }
    }

    private bool _reading;
}
