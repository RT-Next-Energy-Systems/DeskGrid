using System.IO;
using DeskGrid.Diagnostics;
using DeskGrid.Shell;

namespace DeskGrid.Folders;

/// <summary>
/// Reads the root folder (each subfolder is a widget, each visible file in one is an item) and watches
/// it. Renames are reported right away; everything else is folded into one change after a quiet
/// period. Events are raised on the <see cref="SynchronizationContext"/> the scanner was created on,
/// in the order they happened, so a rename is always seen before the change that follows it.
/// </summary>
internal sealed class FolderScanner : IDisposable
{
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    // Skips hidden and system entries (desktop.ini, Office lock files) and unreadable ones.
    private static readonly EnumerationOptions VisibleEntries = new();

    // How often to look for a root folder that was deleted to come back.
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(1);

    private readonly string _rootPath;
    private readonly SynchronizationContext _context;
    private readonly Timer _debounceTimer;
    private readonly Timer _recoveryTimer;
    private readonly ShortcutTargetWatcher _targetWatcher;
    private FileSystemWatcher _watcher;
    private Dictionary<string, (DateTime LinkWritten, string? Target)> _shortcutTargets = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;
    private bool _disposed;

    public FolderScanner(string rootPath)
    {
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _context = SynchronizationContext.Current
            ?? throw new InvalidOperationException($"{nameof(FolderScanner)} must be created on a thread with a SynchronizationContext.");
        _debounceTimer = new Timer(_ => OnQuiet());
        _recoveryTimer = new Timer(_ => TryRecover());
        _targetWatcher = new ShortcutTargetWatcher(RestartDebounce);
        _watcher = CreateWatcher();
    }

    /// <summary>Raised once changes have settled for <see cref="DebounceDelay"/>; call <see cref="Scan"/> to see them.</summary>
    public event EventHandler? Changed;

    public event EventHandler<WidgetRenamedEventArgs>? WidgetRenamed;

    public event EventHandler<ItemRenamedEventArgs>? ItemRenamed;

    public void Start()
    {
        _started = true;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Reads the current folders, in natural name order. Returns <see langword="null"/> when the root
    /// itself can't be read, so callers can tell "nothing there" from "couldn't look".
    /// </summary>
    public IReadOnlyList<WidgetFolder>? Scan()
    {
        IEnumerable<DirectoryInfo> folders;
        try
        {
            folders = new DirectoryInfo(_rootPath).GetDirectories("*", VisibleEntries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Cannot read root folder {_rootPath}", exception);
            return null;
        }

        var widgets = new List<WidgetFolder>();
        var seen = new Dictionary<string, (DateTime LinkWritten, string? Target)>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.OrderBy(f => f.Name, NaturalStringComparer.Instance))
        {
            try
            {
                var items = folder.GetFiles("*", VisibleEntries)
                    .OrderBy(f => f.Name, NaturalStringComparer.Instance)
                    .Select(f => new FolderItem(f.Name, f.FullName, f.LastWriteTimeUtc, TargetWriteTime(f, seen)))
                    .ToList();
                widgets.Add(new WidgetFolder(folder.Name, folder.FullName, items));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Usually deleted mid-scan; the deletion's own event triggers another scan.
                Log.Error($"Cannot read widget folder {folder.FullName}", exception);
            }
        }

        _shortcutTargets = seen; // Shortcuts that are gone drop out of the cache.

        // A target that is replaced (an app update) changes no widget folder, so it is watched where it lives.
        _targetWatcher.Watch(seen.Values.Select(v => v.Target).OfType<string>().Where(t => !IsOnNetwork(t)));
        return widgets;
    }

    // A shortcut shows its target's icon, so it has to be redrawn when the target is replaced (an app update).
    // Where a shortcut points is read from the .lnk only when the .lnk changed; every scan still looks at the target.
    private DateTime TargetWriteTime(FileInfo file, Dictionary<string, (DateTime LinkWritten, string? Target)> seen)
    {
        if (!file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        if (!_shortcutTargets.TryGetValue(file.FullName, out var known) || known.LinkWritten != file.LastWriteTimeUtc)
        {
            known = (file.LastWriteTimeUtc, ShortcutFile.TryGetTarget(file.FullName));
        }

        seen[file.FullName] = known;

        // A target on a network share that is down would hold this (UI) thread for the length of the connection timeout.
        if (known.Target is not { } target || IsOnNetwork(target))
        {
            return default;
        }

        var written = File.GetLastWriteTimeUtc(target);
        return written.Year == 1601 ? default : written; // 1601: the target doesn't exist
    }

    private static bool IsOnNetwork(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        try
        {
            return root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        _debounceTimer.Dispose();
        _recoveryTimer.Dispose();
        _targetWatcher.Dispose();
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(_rootPath)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
        };
        watcher.Created += OnEntryChanged;
        watcher.Deleted += OnEntryChanged;
        watcher.Changed += OnEntryChanged;
        watcher.Renamed += OnEntryRenamed;
        watcher.Error += OnWatcherError;
        return watcher;
    }

    // Changes have settled. A watcher dies with the folder it watches, so if the root is gone it has to be rebuilt
    // once the root is back; the rescan meanwhile reports it as unreadable.
    private void OnQuiet()
    {
        if (!Directory.Exists(_rootPath))
        {
            BeginRecovery();
        }

        Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private void BeginRecovery()
    {
        try
        {
            _recoveryTimer.Change(RecoveryInterval, RecoveryInterval);
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose.
        }
    }

    // Runs on a timer thread, every RecoveryInterval until the root exists again.
    private void TryRecover()
    {
        if (!Directory.Exists(_rootPath))
        {
            return;
        }

        try
        {
            _recoveryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        Post(RebuildWatcher);
    }

    private void RebuildWatcher()
    {
        FileSystemWatcher fresh;
        try
        {
            fresh = CreateWatcher();
            fresh.EnableRaisingEvents = _started;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Gone again before the watcher could attach.
            Log.Error($"Cannot watch root folder {_rootPath}; will retry", exception);
            BeginRecovery();
            return;
        }

        _watcher.Dispose();
        _watcher = fresh;
        Log.Info($"Watching root folder {_rootPath} again.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntryChanged(object sender, FileSystemEventArgs e)
    {
        if (RelativeSegments(e.FullPath).Length <= 2)
        {
            RestartDebounce();
        }
    }

    private void OnEntryRenamed(object sender, RenamedEventArgs e)
    {
        var oldSegments = RelativeSegments(e.OldFullPath);
        var newSegments = RelativeSegments(e.FullPath);
        if (oldSegments.Length == 1 && newSegments.Length == 1)
        {
            Post(() => WidgetRenamed?.Invoke(this, new WidgetRenamedEventArgs(oldSegments[0], newSegments[0])));
        }
        else if (oldSegments.Length == 2 && newSegments.Length == 2
            && string.Equals(oldSegments[0], newSegments[0], StringComparison.OrdinalIgnoreCase))
        {
            Post(() => ItemRenamed?.Invoke(this, new ItemRenamedEventArgs(newSegments[0], oldSegments[1], newSegments[1])));
        }

        // Anything else (an item moved to another folder or level) is no rename of a widget or item;
        // the rescan below sees its effect.
        if (newSegments.Length <= 2 || oldSegments.Length <= 2)
        {
            RestartDebounce();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Typically an overflowing buffer: individual events were lost, so rescan everything.
        Log.Error("Folder watcher error; rescanning", e.GetException());
        RestartDebounce();

        // Or the watcher died with its folder: rebuild it as soon as there is a folder to watch.
        BeginRecovery();
    }

    // "Root\Widget" -> ["Widget"], "Root\Widget\Item.lnk" -> ["Widget", "Item.lnk"].
    private string[] RelativeSegments(string path) =>
        Path.GetRelativePath(_rootPath, path).Split(Path.DirectorySeparatorChar);

    private void RestartDebounce()
    {
        try
        {
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Event raced with Dispose.
        }
    }

    private void Post(Action action) => _context.Post(_ =>
    {
        if (!_disposed)
        {
            action();
        }
    }, null);
}
