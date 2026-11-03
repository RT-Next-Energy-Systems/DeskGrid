using System.IO;
using DeskGrid.Diagnostics;

namespace DeskGrid.Folders;

/// <summary>
/// Notices when a file that a shortcut points to is written, replaced or renamed into place (an app update), so the
/// shortcut's icon can be redrawn without waiting for something in the widget folders to change. One watcher per
/// folder that holds targets, not recursive; it reports every change to a target through one callback.
/// </summary>
/// <remarks>Call <see cref="Watch"/> from one thread. The callback runs on thread-pool threads.</remarks>
internal sealed class ShortcutTargetWatcher(Action changed) : IDisposable
{
    private readonly Dictionary<string, FolderWatch> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Watches exactly the folders of <paramref name="targets"/>, for changes to those files.</summary>
    public void Watch(IEnumerable<string> targets)
    {
        var wanted = targets
            .Select(path => (Folder: Path.GetDirectoryName(path), Name: Path.GetFileName(path)))
            .Where(t => !string.IsNullOrEmpty(t.Folder) && t.Name.Length > 0)
            .GroupBy(t => t.Folder!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        foreach (var folder in _folders.Keys.Where(f => !wanted.ContainsKey(f)).ToList())
        {
            _folders.Remove(folder, out var gone);
            gone!.Dispose();
        }

        foreach (var (folder, names) in wanted)
        {
            if (_folders.TryGetValue(folder, out var existing))
            {
                existing.Names = names;
            }
            else if (FolderWatch.TryStart(folder, names, changed) is { } started)
            {
                _folders[folder] = started;
            }
        }
    }

    public void Dispose()
    {
        foreach (var watch in _folders.Values)
        {
            watch.Dispose();
        }

        _folders.Clear();
    }

    private sealed class FolderWatch : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private volatile HashSet<string> _names;

        private FolderWatch(FileSystemWatcher watcher, HashSet<string> names, Action changed)
        {
            (_watcher, _names) = (watcher, names);
            void Notify(string? name)
            {
                if (name is not null && _names.Contains(name))
                {
                    changed();
                }
            }

            _watcher.Changed += (_, e) => Notify(e.Name);
            _watcher.Created += (_, e) => Notify(e.Name);
            _watcher.Renamed += (_, e) => Notify(e.Name); // An installer's temp file renamed over the old one.
        }

        public HashSet<string> Names
        {
            set => _names = value;
        }

        // Null when the folder can't be watched (gone, no access): its shortcuts just refresh with the next scan.
        public static FolderWatch? TryStart(string folder, HashSet<string> names, Action changed)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                var watch = new FolderWatch(watcher, names, changed);
                watcher.EnableRaisingEvents = true;
                return watch;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Log.Info($"Not watching shortcut targets in \"{folder}\": {exception.Message}");
                return null;
            }
        }

        public void Dispose() => _watcher.Dispose();
    }
}
