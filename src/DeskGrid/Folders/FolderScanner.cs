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

    private readonly string _rootPath;
    private readonly SynchronizationContext _context;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private bool _disposed;

    public FolderScanner(string rootPath)
    {
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _context = SynchronizationContext.Current
            ?? throw new InvalidOperationException($"{nameof(FolderScanner)} must be created on a thread with a SynchronizationContext.");
        _debounceTimer = new Timer(_ => Post(() => Changed?.Invoke(this, EventArgs.Empty)));

        _watcher = new FileSystemWatcher(_rootPath)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
        };
        _watcher.Created += OnEntryChanged;
        _watcher.Deleted += OnEntryChanged;
        _watcher.Changed += OnEntryChanged;
        _watcher.Renamed += OnEntryRenamed;
        _watcher.Error += OnWatcherError;
    }

    /// <summary>Raised once changes have settled for <see cref="DebounceDelay"/>; call <see cref="Scan"/> to see them.</summary>
    public event EventHandler? Changed;

    public event EventHandler<WidgetRenamedEventArgs>? WidgetRenamed;

    public event EventHandler<ItemRenamedEventArgs>? ItemRenamed;

    public void Start() => _watcher.EnableRaisingEvents = true;

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
        foreach (var folder in folders.OrderBy(f => f.Name, NaturalStringComparer.Instance))
        {
            try
            {
                var items = folder.GetFiles("*", VisibleEntries)
                    .OrderBy(f => f.Name, NaturalStringComparer.Instance)
                    .Select(f => new FolderItem(f.Name, f.FullName, f.LastWriteTimeUtc, TargetWriteTime(f)))
                    .ToList();
                widgets.Add(new WidgetFolder(folder.Name, folder.FullName, items));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Usually deleted mid-scan; the deletion's own event triggers another scan.
                Log.Error($"Cannot read widget folder {folder.FullName}", exception);
            }
        }

        return widgets;
    }

    // A shortcut shows its target's icon, so it has to be redrawn when the target is replaced (an app update).
    private static DateTime TargetWriteTime(FileInfo file)
    {
        if (!file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ShortcutFile.TryGetTarget(file.FullName) is not { } target)
        {
            return default;
        }

        var written = File.GetLastWriteTimeUtc(target);
        return written.Year == 1601 ? default : written; // 1601: the target doesn't exist
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        _debounceTimer.Dispose();
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
