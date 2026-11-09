using System.Windows.Media.Imaging;
using DeskGrid.Folders;
using DeskGrid.Shell;
using DeskGrid.Threading;

namespace DeskGrid.Icons;

/// <summary>
/// Item icons and the widget previews built from them. Shell calls run on a dedicated STA thread so a
/// slow handler never stalls the UI. Results are frozen bitmaps cached until the file changes (keyed by
/// path and last-write time, plus the target's for shortcuts). Not thread-safe: call from the UI thread.
/// </summary>
internal sealed class IconService : IDisposable
{
    public const int IconSize = 256;

    private readonly DispatcherThread _shellThread = new("DeskGrid.Shell");
    private readonly Dictionary<IconKey, Task<BitmapSource?>> _icons = [];
    private readonly Dictionary<string, (string Key, Task<BitmapSource> Image)> _previews = new(StringComparer.OrdinalIgnoreCase);

    public Task<BitmapSource?> GetIconAsync(FolderItem item)
    {
        var key = IconKey.Of(item);
        if (!_icons.TryGetValue(key, out var icon))
        {
            var started = _shellThread.InvokeAsync(() => ShellImage.GetIcon(item.FullPath, IconSize));
            _icons[key] = icon = EvictIfFailed(started, () => _icons.Remove(key));
        }

        return icon;
    }

    /// <summary>The 2×2 preview of the widget's first items; rebuilt only when one of those changes.</summary>
    public Task<BitmapSource> GetPreviewAsync(WidgetFolder widget)
    {
        var shown = widget.Items.Take(PreviewComposer.Slots).ToList();
        var key = string.Join('\n', shown.Select(i => $"{i.FullPath}|{i.LastWriteTimeUtc.Ticks}|{i.TargetWriteTimeUtc.Ticks}"));
        if (_previews.TryGetValue(widget.FullPath, out var cached) && cached.Key == key)
        {
            return cached.Image;
        }

        var path = widget.FullPath;
        var preview = EvictIfFailed(ComposeAsync(shown), () =>
        {
            // Only this attempt: a newer one for the same folder may already be cached.
            if (_previews.TryGetValue(path, out var current) && current.Key == key)
            {
                _previews.Remove(path);
            }
        });
        _previews[path] = (key, preview);
        return preview;
    }

    /// <summary>Drops cached images of folders and files that are gone or have changed.</summary>
    public void Retain(IReadOnlyList<WidgetFolder> widgets)
    {
        var liveIcons = widgets.SelectMany(w => w.Items).Select(IconKey.Of).ToHashSet();
        foreach (var key in _icons.Keys.Where(k => !liveIcons.Contains(k)).ToList())
        {
            _icons.Remove(key);
        }

        var liveWidgets = widgets.Select(w => w.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _previews.Keys.Where(p => !liveWidgets.Contains(p)).ToList())
        {
            _previews.Remove(path);
        }
    }

    public void Dispose() => _shellThread.Dispose();

    // A task that failed must not be handed out again for as long as the file stays as it is: the next request tries anew.
    // The continuation runs on the caller's context (the UI thread), where the caches live.
    private static async Task<T> EvictIfFailed<T>(Task<T> task, Action evict)
    {
        try
        {
            return await task;
        }
        catch
        {
            evict();
            throw;
        }
    }

    private async Task<BitmapSource> ComposeAsync(List<FolderItem> items)
    {
        var icons = await Task.WhenAll(items.Select(GetIconAsync));
        return await _shellThread.InvokeAsync(() => PreviewComposer.Compose(icons));
    }

    private readonly record struct IconKey(string Path, DateTime LastWriteTimeUtc, DateTime TargetWriteTimeUtc)
    {
        public static IconKey Of(FolderItem item) => new(item.FullPath, item.LastWriteTimeUtc, item.TargetWriteTimeUtc);
    }
}
