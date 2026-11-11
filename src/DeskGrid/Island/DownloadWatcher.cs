using System.IO;
using System.Windows.Threading;

namespace DeskGrid.Island;

/// <summary>
/// Tells when a file has finished downloading to the user's Downloads folder: a new file whose size stops changing (browsers
/// write a temporary name first and rename it, or keep writing for a while). Only on while the option is on.
/// </summary>
internal sealed class DownloadWatcher : IDisposable
{
    private static readonly string[] Partial = [".crdownload", ".part", ".tmp", ".download", ".opdownload"];
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, long> _pending = new(StringComparer.OrdinalIgnoreCase); // Path to the size seen last.
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public DownloadWatcher(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        _timer.Tick += (_, _) => CheckPending();
        if (!Directory.Exists(folder))
        {
            return;
        }

        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        _watcher.Created += (_, e) => Note(e.FullPath);
        _watcher.Renamed += (_, e) => Note(e.FullPath);
    }

    /// <summary>Raised on the dispatcher thread with the path of a file that has finished downloading.</summary>
    public event Action<string>? Finished;

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _watcher?.Dispose();
        _pending.Clear();
    }

    private bool _disposed;

    // A file kept open for writing this long (a log, a database) is not a download that finishes: it is no longer watched,
    // or the 2-second check would run for as long as DeskGrid does.
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, DateTime> _noted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is a name a browser gives a download that is not finished.</summary>
    internal static bool IsPartial(string path) => Partial.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void Note(string path)
    {
        if (IsPartial(path) || Directory.Exists(path))
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return; // An event that was on its way when the option was turned off.
            }

            _pending[path] = -1;
            _noted.TryAdd(path, DateTime.UtcNow);
            _timer.Start();
        });
    }

    private void Forget(string path)
    {
        _pending.Remove(path);
        _noted.Remove(path);
    }

    private void CheckPending()
    {
        foreach (var (path, before) in _pending.ToList())
        {
            if (_noted.TryGetValue(path, out var since) && DateTime.UtcNow - since > GiveUpAfter)
            {
                Forget(path);
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); // Still being written: it cannot be opened exclusively.
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                Forget(path); // Gone (or its folder is): a directory-not-found used to count as "still busy" forever.
                continue;
            }
            catch (IOException)
            {
                _pending[path] = -1;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                Forget(path);
                continue;
            }

            if (size == before && size > 0)
            {
                Forget(path);
                Finished?.Invoke(path);
            }
            else
            {
                _pending[path] = size;
            }
        }

        if (_pending.Count == 0)
        {
            _timer.Stop();
        }
    }
}
