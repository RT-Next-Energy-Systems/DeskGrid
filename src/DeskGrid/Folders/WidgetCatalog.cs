using DeskGrid.Config;
using DeskGrid.Diagnostics;

namespace DeskGrid.Folders;

/// <summary>
/// The widgets to show: what <see cref="FolderScanner"/> sees on disk, with items in the saved order.
/// Keeps config.json in step with the folders: settings follow a renamed folder or item, and settings
/// of folders and items that no longer exist are dropped.
/// </summary>
internal sealed class WidgetCatalog : IDisposable
{
    private readonly ConfigStore _store;
    private readonly FolderScanner _scanner;

    public WidgetCatalog(ConfigStore store)
    {
        _store = store;
        _scanner = new FolderScanner(store.Config.RootPath);
        _scanner.Changed += (_, _) => Refresh();
        _scanner.WidgetRenamed += OnWidgetRenamed;
        _scanner.ItemRenamed += OnItemRenamed;
    }

    public IReadOnlyList<WidgetFolder> Widgets { get; private set; } = [];

    /// <summary>Raised on the creating thread after <see cref="Widgets"/> was refreshed.</summary>
    public event EventHandler? Changed;

    public void Start()
    {
        // Watch first, so nothing that happens during the initial scan is missed.
        _scanner.Start();
        Refresh();
    }

    public void Dispose() => _scanner.Dispose();

    /// <summary>Reads the folders now rather than after the watcher's quiet period (e.g. right after adding to one).</summary>
    public void Rescan() => Refresh();

    /// <summary>
    /// Orders <paramref name="scanned"/> by the saved item order and updates <paramref name="settings"/>
    /// to match it: new folders get settings, new items go last, vanished folders and items are dropped.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="settings"/> changed.</returns>
    internal static bool Reconcile(
        Dictionary<string, WidgetConfig> settings, IReadOnlyList<WidgetFolder> scanned, out List<WidgetFolder> ordered)
    {
        var changed = false;
        var present = scanned.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in settings.Keys.Where(n => !present.Contains(n)).ToList())
        {
            settings.Remove(name);
            changed = true;
        }

        ordered = new List<WidgetFolder>(scanned.Count);
        foreach (var widget in scanned)
        {
            if (!settings.TryGetValue(widget.Name, out var widgetConfig))
            {
                settings[widget.Name] = widgetConfig = new WidgetConfig();
                changed = true;
            }

            changed |= SyncItemOrder(widgetConfig.ItemOrder, widget.Items);
            var rank = widgetConfig.ItemOrder
                .Select((name, index) => (name, index))
                .ToDictionary(e => e.name, e => e.index, StringComparer.OrdinalIgnoreCase);
            ordered.Add(widget with { Items = [.. widget.Items.OrderBy(i => rank[i.Name])] });
        }

        return changed;
    }

    /// <summary>Puts folder <paramref name="name"/> at <paramref name="index"/> of the saved folder order and rescans.</summary>
    public void MoveFolder(string name, int index)
    {
        if (MoveInOrder(_store.Config.FolderOrder, name, index))
        {
            _store.Save();
            Refresh();
        }
    }

    /// <summary>Puts <paramref name="itemName"/> at <paramref name="index"/> of the widget's saved order and rescans.</summary>
    public void MoveItem(string widgetName, string itemName, int index)
    {
        if (_store.Config.Widgets.TryGetValue(widgetName, out var widgetConfig) && MoveInOrder(widgetConfig.ItemOrder, itemName, index))
        {
            _store.Save();
            Refresh();
        }
    }

    /// <summary>
    /// Like <see cref="MoveItem"/> for an item that was just put into the widget's folder: rescans first, so
    /// the item is known by the time it is moved.
    /// </summary>
    public void PlaceNewItem(string widgetName, string itemName, int index)
    {
        Refresh();
        MoveItem(widgetName, itemName, index);
    }

    /// <summary>Moves <paramref name="name"/> to <paramref name="index"/> (clamped) in <paramref name="order"/>.</summary>
    /// <returns><see langword="true"/> if the order changed.</returns>
    internal static bool MoveInOrder(List<string> order, string name, int index)
    {
        var from = order.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (from < 0)
        {
            return false;
        }

        var to = Math.Clamp(index, 0, order.Count - 1);
        if (from == to)
        {
            return false;
        }

        var moved = order[from];
        order.RemoveAt(from);
        order.Insert(to, moved);
        return true;
    }

    /// <summary>Sorts <paramref name="widgets"/> by the saved folder order (new folders last) and updates <paramref name="order"/> to match.</summary>
    /// <returns><see langword="true"/> if <paramref name="order"/> changed.</returns>
    internal static bool OrderFolders(List<string> order, ref List<WidgetFolder> widgets)
    {
        var changed = SyncOrder(order, widgets.Select(w => w.Name).ToList());
        var rank = order.Select((name, index) => (name, index)).ToDictionary(e => e.name, e => e.index, StringComparer.OrdinalIgnoreCase);
        widgets = [.. widgets.OrderBy(w => rank[w.Name])];
        return changed;
    }

    private static bool SyncItemOrder(List<string> order, IReadOnlyList<FolderItem> items) =>
        SyncOrder(order, items.Select(i => i.Name).ToList());

    // Keeps saved names that still exist (once each), then appends new ones in scan order.
    private static bool SyncOrder(List<string> order, List<string> names)
    {
        var present = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var synced = order.Where(present.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = synced.ToHashSet(StringComparer.OrdinalIgnoreCase);
        synced.AddRange(names.Where(known.Add));

        if (synced.SequenceEqual(order))
        {
            return false;
        }

        order.Clear();
        order.AddRange(synced);
        return true;
    }

    private void Refresh()
    {
        // Root unreadable (e.g. a drive went away): keep what we have rather than dropping all settings.
        if (_scanner.Scan() is not { } scanned)
        {
            return;
        }

        var changed = Reconcile(_store.Config.Widgets, scanned, out var ordered);
        changed |= OrderFolders(_store.Config.FolderOrder, ref ordered);
        if (changed)
        {
            _store.Save();
        }

        Widgets = ordered;
        Log.Info($"Scanned {ordered.Count} widget folder(s).");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWidgetRenamed(object? sender, WidgetRenamedEventArgs e) => MoveSettings(e.OldName, e.NewName);

    /// <summary>
    /// Makes the settings of folder <paramref name="oldName"/> (position, item order, shortcut, place in the folder order) those of
    /// <paramref name="newName"/>. Done by whoever renames a folder, <b>before</b> anything rescans: a rescan that finds the new
    /// name and not the old one drops the old settings as those of a vanished folder. The file watcher reports the rename later
    /// as well; by then there is nothing left to move.
    /// </summary>
    public void MoveSettings(string oldName, string newName)
    {
        var widgets = _store.Config.Widgets;
        if (!widgets.Remove(oldName, out var widgetConfig))
        {
            return;
        }

        widgets[newName] = widgetConfig;
        var order = _store.Config.FolderOrder;
        if (order.FindIndex(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase)) is >= 0 and var at)
        {
            order[at] = newName;
        }

        _store.Save();
        Log.Info($"Widget folder renamed \"{oldName}\" -> \"{newName}\"; settings moved.");
    }
    private void OnItemRenamed(object? sender, ItemRenamedEventArgs e)
    {
        if (!_store.Config.Widgets.TryGetValue(e.WidgetName, out var widgetConfig))
        {
            return;
        }

        var index = widgetConfig.ItemOrder.FindIndex(n => string.Equals(n, e.OldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        widgetConfig.ItemOrder[index] = e.NewName;
        _store.Save();
    }
}
