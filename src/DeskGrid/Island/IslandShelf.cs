using System.IO;
using DeskGrid.Diagnostics;
using DeskGrid.Folders;

namespace DeskGrid.Island;

/// <summary>
/// Files the user parked on the island to drop somewhere later. Every file is copied into a folder of DeskGrid's own
/// (<see cref="DefaultFolder"/>, one subfolder per file), wherever it came from, so a file from a USB stick or a network share
/// is still there after the source is gone. A copy is deleted when it is used (dragged out and taken, or removed from the
/// shelf); what is left at the end (an exit, a crash) is deleted by <see cref="PurgeLeftoversAsync"/> at the next start. The list
/// itself is in memory.
/// </summary>
internal sealed class IslandShelf(string? folder = null)
{
    /// <summary>More than this and the oldest goes: a shelf is for a few things at hand.</summary>
    public const int MaxItems = 12;

    /// <summary>Where the copies are kept: <c>%LOCALAPPDATA%\DeskGrid\shelf</c>.</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskGrid", "shelf");

    // Source = where the file was taken from (to tell the same file brought twice), Staged = the copy the shelf shows.
    private sealed record Entry(string Source, string Staged);

    private readonly string _folder = folder ?? DefaultFolder;
    private readonly List<Entry> _items = [];

    /// <summary>The copies on the shelf, oldest first: what the card shows and drags out.</summary>
    public IReadOnlyList<string> Paths => [.. _items.Select(item => item.Staged)];

    /// <summary>Raised when the shelf changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Copies the files and folders that exist and are not on the shelf yet; returns how many were added. The copying is off
    /// the calling thread (a file from a USB stick can be big); the shelf changes back on the caller's context.
    /// </summary>
    public async Task<int> AddAsync(IEnumerable<string> paths)
    {
        var wanted = paths
            .Where(path => (File.Exists(path) || Directory.Exists(path)) && !IsOnShelf(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var staged = await Task.Run(() => wanted.Select(Stage).OfType<Entry>().ToList());

        var added = 0;
        foreach (var entry in staged)
        {
            if (_items.Any(item => string.Equals(item.Source, entry.Source, StringComparison.OrdinalIgnoreCase)))
            {
                Delete(entry); // Brought twice while the first copy was still being made.
                continue;
            }

            if (_items.Count == MaxItems)
            {
                Delete(_items[0]);
                _items.RemoveAt(0);
            }

            _items.Add(entry);
            added++;
        }

        if (added > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    /// <summary>Takes a copy off the shelf and deletes it.</summary>
    public void Remove(string path)
    {
        var gone = _items.Where(item => string.Equals(item.Staged, path, StringComparison.OrdinalIgnoreCase)).ToList();
        RemoveAll(gone);
    }

    /// <summary>Empties the shelf and deletes every copy.</summary>
    public void Clear() => RemoveAll([.. _items]);

    /// <summary>Drops what no longer exists (the copy was moved away or deleted meanwhile).</summary>
    public void Prune() => RemoveAll([.. _items.Where(item => !File.Exists(item.Staged) && !Directory.Exists(item.Staged))]);

    /// <summary>
    /// Deletes everything in the folder of copies that the shelf does not hold: what a previous run left when it ended
    /// before the shelf was emptied. Call it once, at start.
    /// </summary>
    public Task PurgeLeftoversAsync()
    {
        var held = _items.Select(ItemFolder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.Run(() =>
        {
            if (!Directory.Exists(_folder))
            {
                return;
            }

            foreach (var leftover in Directory.EnumerateDirectories(_folder).Where(dir => !held.Contains(dir)))
            {
                Delete(leftover);
            }
        });
    }

    private bool IsOnShelf(string path) =>
        FolderPaths.IsSameOrInside(path, _folder)
        || _items.Any(item => string.Equals(item.Source, path, StringComparison.OrdinalIgnoreCase));

    private void RemoveAll(List<Entry> gone)
    {
        if (gone.Count == 0)
        {
            return;
        }

        foreach (var entry in gone)
        {
            _items.Remove(entry);
            Delete(entry);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // The copy is <folder>\<guid>\<name>: the name stays as it was, and two files of the same name don't meet.
    private Entry? Stage(string source)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        if (name.Length == 0)
        {
            return null; // A drive's root.
        }

        var directory = Path.Combine(_folder, Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(directory, name);
        try
        {
            Directory.CreateDirectory(directory);
            if (Directory.Exists(source))
            {
                FolderDeleter.CopyDirectory(source, staged);
            }
            else
            {
                File.Copy(source, staged);
            }

            return new Entry(source, staged);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Copying {source} to the shelf failed", exception);
            Delete(directory);
            return null;
        }
    }

    private static string ItemFolder(Entry entry) => Path.GetDirectoryName(entry.Staged)!;

    private void Delete(Entry entry) => Delete(ItemFolder(entry));

    private static void Delete(string directory)
    {
        try
        {
            FolderDeleter.DeleteDirectoryRecursively(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // In use by the program it was given to: it is purged at the next start.
            Log.Error($"Deleting {directory} from the shelf failed", exception);
        }
    }
}
