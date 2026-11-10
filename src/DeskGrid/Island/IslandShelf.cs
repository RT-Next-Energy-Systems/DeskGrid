using System.IO;

namespace DeskGrid.Island;

/// <summary>
/// Files the user parked on the island to drop somewhere later. Only the paths are kept (nothing is copied or moved), in
/// memory: a restart empties the shelf.
/// </summary>
internal sealed class IslandShelf
{
    /// <summary>More than this and the oldest goes: a shelf is for a few things at hand.</summary>
    public const int MaxItems = 12;

    private readonly List<string> _paths = [];

    public IReadOnlyList<string> Paths => _paths;

    /// <summary>Raised when the shelf changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Adds the paths that exist and are not on it yet; returns how many were added.</summary>
    public int Add(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && !_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                if (_paths.Count == MaxItems)
                {
                    _paths.RemoveAt(0);
                }

                _paths.Add(path);
                added++;
            }
        }

        if (added > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    public void Remove(string path)
    {
        if (_paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (_paths.Count > 0)
        {
            _paths.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drops what no longer exists (the file was moved or deleted meanwhile).</summary>
    public void Prune()
    {
        if (_paths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
