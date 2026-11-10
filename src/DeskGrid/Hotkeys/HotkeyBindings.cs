using DeskGrid.Config;

namespace DeskGrid.Hotkeys;

/// <summary>A shortcut and what it does, named by an id from <see cref="HotkeyBindings"/>.</summary>
internal readonly record struct HotkeyBinding(string Id, Hotkey Key);

/// <summary>
/// Which shortcut triggers which action. The general actions are stored in <see cref="AppConfig.Hotkeys"/>; a folder's own
/// shortcut lives in its <see cref="WidgetConfig"/>, so it follows the folder when that is renamed and goes when it is deleted.
/// </summary>
internal static class HotkeyBindings
{
    public const string AllFolders = "allFolders";
    public const string NewFolder = "newFolder";
    public const string Settings = "settings";
    public const string Island = "island";
    public const string IslandShow = "islandShow";
    public const string IslandToggle = "islandToggle";
    public const string IslandHide = "islandHide";

    // A folder's action is this plus the folder's name.
    private const string FolderPrefix = "folder:";

    /// <summary>
    /// Ctrl+M, as the user asked for. While DeskGrid runs, other programs lose it (Enter in terminals, indent in editors);
    /// the shortcuts window lets the user pick another.
    /// </summary>
    public static Hotkey DefaultAllFolders { get; } = new(HotkeyModifiers.Ctrl, 'M');

    /// <summary>The actions that don't belong to a folder, in the order the shortcut window lists them.</summary>
    public static IReadOnlyList<string> GeneralIds { get; } = [AllFolders, Island, IslandShow, IslandToggle, IslandHide, NewFolder, Settings];

    public static string FolderId(string folderName) => FolderPrefix + folderName;

    public static string? FolderNameOf(string id) => id.StartsWith(FolderPrefix, StringComparison.Ordinal) ? id[FolderPrefix.Length..] : null;

    /// <summary>The shortcut a fresh install has for <paramref name="id"/>, if any.</summary>
    public static Hotkey? DefaultOf(string id) => id == AllFolders ? DefaultAllFolders : null;

    /// <summary>The shortcut set for <paramref name="id"/>; <see langword="null"/> when none is, or when the text in the config isn't a usable shortcut.</summary>
    public static Hotkey? Get(AppConfig config, string id)
    {
        if (FolderNameOf(id) is { } folder)
        {
            return config.Widgets.TryGetValue(folder, out var widget) ? Hotkey.Parse(widget.Hotkey) : null;
        }

        return config.Hotkeys.TryGetValue(id, out var text) ? Hotkey.Parse(text) : null;
    }

    /// <summary>Sets (or, with <see langword="null"/>, clears) the shortcut for <paramref name="id"/>. A folder that isn't known is ignored.</summary>
    public static void Set(AppConfig config, string id, Hotkey? key)
    {
        if (FolderNameOf(id) is { } folder)
        {
            if (config.Widgets.TryGetValue(folder, out var widget))
            {
                widget.Hotkey = key?.ToString();
            }

            return;
        }

        // An empty text, not a missing entry: a missing one means "the default".
        config.Hotkeys[id] = key?.ToString() ?? "";
    }

    /// <summary>Every shortcut that is set, general ones first.</summary>
    public static IReadOnlyList<HotkeyBinding> All(AppConfig config)
    {
        var bindings = new List<HotkeyBinding>();
        foreach (var id in GeneralIds)
        {
            if (Get(config, id) is { } key)
            {
                bindings.Add(new HotkeyBinding(id, key));
            }
        }

        foreach (var (name, widget) in config.Widgets)
        {
            if (Hotkey.Parse(widget.Hotkey) is { } key)
            {
                bindings.Add(new HotkeyBinding(FolderId(name), key));
            }
        }

        return bindings;
    }
}
