using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using DeskGrid.Config;
using DeskGrid.Folders;
using DeskGrid.Island;
using DeskGrid.Updates;

namespace DeskGrid.Control;

/// <summary>What <see cref="ControlCommands"/> needs of the running app. All of it is used on the UI thread.</summary>
internal interface IControlHost
{
    AppConfig Config { get; }

    /// <summary>The widget folders in their order, or <see langword="null"/> while the widgets are not up yet.</summary>
    IReadOnlyList<WidgetFolder>? Widgets { get; }

    /// <summary>The folder whose panel is open, <c>*</c> for the list of all folders, or <see langword="null"/>.</summary>
    string? OpenPanel { get; }

    /// <summary>Reads the folders again now, instead of after the file watcher's quiet period.</summary>
    void Rescan();

    /// <summary>Renames a widget folder on disk; returns why not, or <see langword="null"/>.</summary>
    string? RenameFolder(string oldName, string newName);

    void MoveItem(string folderName, string itemName, int index);

    void OpenFolder(WidgetFolder folder);

    void OpenList();

    void ClosePanel();

    /// <summary>Shows a line on the island for a while, after the ones already waiting.</summary>
    void ShowOnIsland(string text, TimeSpan duration, IslandLook? look = null);

    /// <summary>Takes away what the island shows and what waits.</summary>
    void HideIsland();

    /// <summary>Runs an island action as its shortcut does: <c>timer</c>, <c>card</c> or <c>info</c>.</summary>
    void IslandAction(string action);

    /// <summary>What NotiLand is doing now (for status), or <see langword="null"/> when there is none.</summary>
    JsonObject? IslandState { get; }
}

/// <summary>
/// Carries out the commands of the control pipe (see <see cref="ControlChannel"/>). Only what can be undone:
/// nothing here deletes a folder or an item.
/// </summary>
internal sealed class ControlCommands(IControlHost host, IEnumerable<string> desktops)
{
    /// <summary>What <c>open</c> reports and <see cref="IControlHost.OpenPanel"/> returns for the list of all folders.</summary>
    public const string AllFolders = "*";

    /// <summary>The commands, how to call them and what they do; shown by <c>DeskGrid.exe --ctl help</c>.</summary>
    public static IReadOnlyList<(string Usage, string Description)> Usage { get; } =
    [
        ("status", "Version, root folder, number of folders and the open panel."),
        ("list [folder]", "All folders with their items in display order, or one folder."),
        ("new <name>", "Creates a folder (a new widget on the desktop)."),
        ("rename <folder> <new name>", "Renames a folder."),
        ("add <folder> <path>...", "Adds files, programs or folders, as a drop on the widget would (programs get a shortcut)."),
        ("move <folder> <item> <index>", "Puts an item at a position (0 = first) in its folder's order."),
        ("open [folder]", "Opens a folder's panel next to its widget, or the list of all folders."),
        ("close", "Closes the open panel."),
        ("island show <text> [seconds] [--icon name] [--color #RRGGBB] [--progress 0-100] [--open folder:NAME|URL|path]",
            "Shows a line of text on NotiLand for a few seconds (default 5, at most 60). --icon: a symbol (check, warning, error, info, download, folder, mic, camera, bluetooth, timer, battery, system) or one character; --color: the symbol and bar; --progress: a bar under the text; --open: what a click does. Needs the island turned on in Settings."),
        ("island hide", "Takes away what NotiLand shows."),
        ("island timer", "Starts NotiLand's timer (the minutes set in Settings), or stops it if it runs."),
        ("island card", "Opens NotiLand's card (players, usage, folders, shelf), or closes it."),
        ("island open", "Brings the player of the song that plays now (its app, or its browser tab) to the front."),
        ("island info", "Shows NotiLand's info (usage, system, players) for the seconds set in Settings."),
        ("island place <default|desktop|window|maximized|fullScreen>", "Lets you set where NotiLand is in that situation: every window goes down, drag NotiLand, Enter keeps the place, Esc keeps the old one."),
    ];

    public ControlResponse Execute(ControlRequest request)
    {
        if (!host.Config.AllowControl)
        {
            return ControlResponse.Failure(
                "Control from scripts and AI agents is turned off. Turn on \"Allow control from scripts and AI agents\" in DeskGrid's Settings.");
        }

        var args = request.Args;
        // "notiland" is the name of the island now; "island" stays, for the scripts that use it.
        return (request.Command == "notiland" ? "island" : request.Command) switch
        {
            "status" => Status(),
            "list" when args.Count <= 1 => List(args.Count == 1 ? args[0] : null),
            "new" when args.Count == 1 => New(args[0]),
            "rename" when args.Count == 2 => Rename(args[0], args[1]),
            "add" when args.Count >= 2 => Add(args[0], args.Skip(1).ToList()),
            "move" when args.Count == 3 => Move(args[0], args[1], args[2]),
            "open" when args.Count <= 1 => Open(args.Count == 1 ? args[0] : null),
            "close" when args.Count == 0 => Close(),
            "island" when args is ["show", _, ..] => IslandShow(args.Skip(1).ToList()),
            "island" when args is ["hide"] => IslandHide(),
            "island" when args is ["timer" or "card" or "info" or "open"] => IslandRun(args[0]),
            "island" when args is ["place", var scene] && IslandScene.All.Contains(scene) => IslandRun("place:" + scene),
            var command when Usage.Where(u => NameOf(u) == command).ToList() is { Count: > 0 } own =>
                ControlResponse.Failure($"Wrong arguments. Usage: {string.Join(" or ", own.Select(u => u.Usage))}"),
            var command => ControlResponse.Failure($"Unknown command \"{command}\". Commands: {string.Join(", ", Usage.Select(NameOf).Distinct())}."),
        };
    }

    private static string NameOf((string Usage, string Description) usage) => usage.Usage.Split(' ')[0];

    private static JsonObject Describe(WidgetFolder folder) => new()
    {
        ["name"] = folder.Name,
        ["path"] = folder.FullPath,
        ["items"] = new JsonArray([.. folder.Items.Select(item => (JsonNode)new JsonObject { ["name"] = item.Name, ["path"] = item.FullPath })]),
    };

    private ControlResponse Status() => ControlResponse.Success(new JsonObject
    {
        ["version"] = UpdateChecker.CurrentVersion.ToString(3),
        ["rootPath"] = host.Config.RootPath,
        ["ready"] = host.Widgets is not null,
        ["folders"] = host.Widgets?.Count,
        ["openPanel"] = host.OpenPanel,
        ["notiland"] = host.IslandState,
    });

    private ControlResponse List(string? name)
    {
        if (!TryGetWidgets(out var widgets, out var notReady))
        {
            return notReady;
        }

        if (name is null)
        {
            return ControlResponse.Success(new JsonArray([.. widgets.Select(w => (JsonNode)Describe(w))]));
        }

        return Find(widgets, name, out var folder, out var missing) ? ControlResponse.Success(Describe(folder)) : missing;
    }

    private ControlResponse New(string name)
    {
        var root = host.Config.RootPath;
        name = name.Trim();
        if (WidgetFolderName.Validate(root, name) is { } problem)
        {
            return ControlResponse.Failure(problem);
        }

        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        host.Rescan();
        return ControlResponse.Success(new JsonObject { ["name"] = name, ["path"] = path });
    }

    private ControlResponse Rename(string oldName, string newName)
    {
        if (!TryGetWidgets(out var widgets, out var notReady))
        {
            return notReady;
        }

        if (!Find(widgets, oldName, out var folder, out var missing))
        {
            return missing;
        }

        newName = newName.Trim();
        if (host.RenameFolder(folder.Name, newName) is { } problem)
        {
            return ControlResponse.Failure(problem);
        }

        host.Rescan();
        return ControlResponse.Success(new JsonObject { ["name"] = newName, ["path"] = Path.Combine(host.Config.RootPath, newName) });
    }

    private ControlResponse Add(string name, IReadOnlyList<string> paths)
    {
        if (!TryGetWidgets(out var widgets, out var notReady))
        {
            return notReady;
        }

        if (!Find(widgets, name, out var folder, out var missing))
        {
            return missing;
        }

        if (paths.FirstOrDefault(p => !Path.IsPathFullyQualified(p)) is { } relative)
        {
            return ControlResponse.Failure($"\"{relative}\" is not a full path.");
        }

        var config = host.Config;
        var added = new JsonArray();
        var skipped = new JsonArray();
        foreach (var path in paths)
        {
            // One at a time, so each path can be reported as added or not.
            if (ItemImporter.ImportTo([path], folder.FullPath, config.DropMode, desktops, config.RootPath) is [var item])
            {
                added.Add(Path.GetFileName(item));
            }
            else
            {
                skipped.Add(path);
            }
        }

        host.Rescan();
        return ControlResponse.Success(new JsonObject { ["folder"] = folder.Name, ["added"] = added, ["skipped"] = skipped });
    }

    private ControlResponse Move(string name, string itemName, string position)
    {
        if (!TryGetWidgets(out var widgets, out var notReady))
        {
            return notReady;
        }

        if (!Find(widgets, name, out var folder, out var missing))
        {
            return missing;
        }

        if (!int.TryParse(position, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
        {
            return ControlResponse.Failure($"\"{position}\" is not a position (0 = first).");
        }

        if (folder.Items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase)) is not { } item)
        {
            return ControlResponse.Failure($"\"{folder.Name}\" has no item \"{itemName}\". Items are named by file name, e.g. \"Notepad.lnk\".");
        }

        host.MoveItem(folder.Name, item.Name, index);
        return Find(host.Widgets ?? [], folder.Name, out var moved, out _)
            ? ControlResponse.Success(new JsonObject
            {
                ["folder"] = moved.Name,
                ["items"] = new JsonArray([.. moved.Items.Select(i => (JsonNode)JsonValue.Create(i.Name))]),
            })
            : ControlResponse.Success();
    }

    private ControlResponse Open(string? name)
    {
        if (!TryGetWidgets(out var widgets, out var notReady))
        {
            return notReady;
        }

        if (name is null)
        {
            host.OpenList();
            return ControlResponse.Success(new JsonObject { ["openPanel"] = AllFolders });
        }

        if (!Find(widgets, name, out var folder, out var missing))
        {
            return missing;
        }

        host.OpenFolder(folder);
        return ControlResponse.Success(new JsonObject { ["openPanel"] = folder.Name });
    }

    private ControlResponse Close()
    {
        host.ClosePanel();
        return ControlResponse.Success();
    }

    // rest: the text, then optionally the seconds, then options (--icon, --color, --progress, --open) in any order.
    private ControlResponse IslandShow(IReadOnlyList<string> rest)
    {
        if (!host.Config.IslandEnabled)
        {
            return ControlResponse.Failure("NotiLand is turned off. Turn on \"Show NotiLand\" in DeskGrid's Settings.");
        }

        var text = rest[0].Trim();
        if (text.Length == 0 || text.Length > IslandActivity.MaxTextLength)
        {
            return ControlResponse.Failure($"The text must be 1 to {IslandActivity.MaxTextLength} characters.");
        }

        string? seconds = null, icon = null, color = null, progress = null, open = null;
        for (var i = 1; i < rest.Count; i++)
        {
            switch (rest[i])
            {
                case "--icon" or "--color" or "--progress" or "--open" when i + 1 < rest.Count:
                    var value = rest[++i];
                    switch (rest[i - 1])
                    {
                        case "--icon": icon = value; break;
                        case "--color": color = value; break;
                        case "--progress": progress = value; break;
                        default: open = value; break;
                    }

                    break;

                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    return ControlResponse.Failure($"\"{option}\" needs a value (or is not an option of island show).");

                case var number when seconds is null:
                    seconds = number;
                    break;

                default:
                    return ControlResponse.Failure($"Unexpected \"{rest[i]}\". Usage: {Usage.First(u => u.Usage.StartsWith("island show", StringComparison.Ordinal)).Usage}");
            }
        }

        var duration = IslandActivity.DefaultDuration;
        if (seconds is not null)
        {
            if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || value < 1 || value > IslandActivity.MaxDuration.TotalSeconds)
            {
                return ControlResponse.Failure($"\"{seconds}\" is not a number of seconds from 1 to {IslandActivity.MaxDuration.TotalSeconds:0}.");
            }

            duration = TimeSpan.FromSeconds(value);
        }

        if (icon is not null && IslandLook.ResolveGlyph(icon) is null)
        {
            return ControlResponse.Failure($"\"{icon}\" is not an icon. Use one of {string.Join(", ", IslandLook.GlyphNames.Distinct())}, or a single character.");
        }

        if (color is not null && IslandLook.ResolveColor(color) is null)
        {
            return ControlResponse.Failure($"\"{color}\" is not a color. Use #RRGGBB or a color name.");
        }

        double? fraction = null;
        if (progress is not null)
        {
            if (!double.TryParse(progress, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || percent < 0 || percent > 100)
            {
                return ControlResponse.Failure($"\"{progress}\" is not a progress from 0 to 100.");
            }

            fraction = percent / 100;
        }

        if (open is not null && !IslandAction.IsAllowed(open))
        {
            return ControlResponse.Failure($"\"{open}\" cannot be opened by a click. Use folder:NAME, an http(s) address, or the path of a file or folder that exists.");
        }

        var look = icon is null && color is null && fraction is null && open is null ? null : new IslandLook(icon, color, fraction, open);
        host.ShowOnIsland(text, duration, look);
        return ControlResponse.Success(new JsonObject { ["seconds"] = duration.TotalSeconds });
    }

    private ControlResponse IslandRun(string action)
    {
        if (!host.Config.IslandEnabled)
        {
            return ControlResponse.Failure("NotiLand is turned off. Turn on \"Show NotiLand\" in DeskGrid's Settings.");
        }

        host.IslandAction(action);
        return ControlResponse.Success();
    }

    private ControlResponse IslandHide()
    {
        host.HideIsland();
        return ControlResponse.Success();
    }

    private bool TryGetWidgets(out IReadOnlyList<WidgetFolder> widgets, out ControlResponse notReady)
    {
        widgets = host.Widgets ?? [];
        notReady = ControlResponse.Failure("DeskGrid is still starting; try again in a moment.");
        return host.Widgets is not null;
    }

    private static bool Find(IReadOnlyList<WidgetFolder> widgets, string name, out WidgetFolder folder, out ControlResponse missing)
    {
        folder = widgets.FirstOrDefault(w => string.Equals(w.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))!;
        missing = ControlResponse.Failure($"There is no folder \"{name}\". \"list\" shows the folders.");
        return folder is not null;
    }
}
