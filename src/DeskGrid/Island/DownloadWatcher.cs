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
        _timer.Stop();
        _watcher?.Dispose();
    }

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
            _pending[path] = -1;
            _timer.Start();
        });
    }

    private void CheckPending()
    {
        foreach (var (path, before) in _pending.ToList())
        {
            long size;
            try
            {
                size = new FileInfo(path).Length;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); // Still being written: it cannot be opened exclusively.
            }
            catch (FileNotFoundException)
            {
                _pending.Remove(path);
                continue;
            }
            catch (IOException)
            {
                _pending[path] = -1;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                _pending.Remove(path);
                continue;
            }

            if (size == before && size > 0)
            {
                _pending.Remove(path);
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
