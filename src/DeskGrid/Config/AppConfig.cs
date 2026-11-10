using System.IO;
using System.Text.Json.Serialization;
using DeskGrid.Desktop;
using DeskGrid.Hotkeys;

namespace DeskGrid.Config;

/// <summary>
/// Everything persisted in config.json. Folder contents are never stored here: the folders on disk
/// are the source of truth, this only adds what the file system can't hold.
/// </summary>
internal sealed class AppConfig
{
    public const int DefaultPanelColumns = 3;
    public const int MinPanelColumns = 1;
    public const int MaxPanelColumns = 8;
    public const int DefaultAutoBackupHour = 12;
    public const int DefaultAutoBackupDays = 7;
    public const int MinAutoBackupDays = 1;
    public const int MaxAutoBackupDays = 30;

    public static string DefaultRootPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DeskGrid");

    public string RootPath { get; set; } = DefaultRootPath;

    public int PanelColumns { get; set; } = DefaultPanelColumns;

    public DropMode DropMode { get; set; } = DropMode.Move;

    public DeleteMode DeleteMode { get; set; } = DeleteMode.MoveToDesktop;

    public PanelBackground PanelBackground { get; set; } = new();

    public PanelOpenMode PanelOpenMode { get; set; } = PanelOpenMode.Nearby;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Whether DeskGrid automatically saves a daily backup of the settings.</summary>
    public bool AutoBackupEnabled { get; set; }

    /// <summary>The hour of the day (0 to 23) at or after which the daily automatic backup is written.</summary>
    public int AutoBackupHour { get; set; } = DefaultAutoBackupHour;

    /// <summary>How many automatic daily backups to keep (1 to 30); older ones are deleted automatically.</summary>
    public int AutoBackupDays { get; set; } = DefaultAutoBackupDays;

    /// <summary>Whether DeskGrid asks GitHub for a newer release a few seconds after it starts. The only network use that isn't the user's own request.</summary>
    public bool CheckForUpdatesAtStartup { get; set; } = true;

    /// <summary>
    /// Whether other programs of this user (scripts, AI agents) may control DeskGrid through <c>DeskGrid.exe --ctl</c>
    /// and the control pipe. Off until the user turns it on.
    /// </summary>
    public bool AllowControl { get; set; }

    /// <summary>Whether the island (a small pill at the top of the screen that shows short messages) exists. Off until the user turns it on.</summary>
    public bool IslandEnabled { get; set; }

    /// <summary>What the island shows of the music that is playing (read from Windows' system media controls, on this computer only).</summary>
    public IslandMusicMode IslandMusic { get; set; } = IslandMusicMode.WhilePlaying;

    /// <summary>Whether the island says when the charger is plugged in or out and when the battery runs low.</summary>
    public bool IslandBattery { get; set; }

    /// <summary>Where the user dragged the island, on any screen: the middle of its top edge in physical screen pixels; null = the top centre of the primary screen.</summary>
    public int? IslandX { get; set; }

    public int? IslandY { get; set; }

    /// <summary>How many seconds the "show the island" shortcut keeps it up; 0 = until it is pressed again or the hide shortcut.</summary>
    public int IslandShowSeconds { get; set; } = 5;

    /// <summary>Whether the island watches the AI usage read from Claude Code's logs on this computer and warns (see the other <c>Usage</c> settings).</summary>
    public bool IslandUsage { get; set; }

    /// <summary>The usage limit per session block in millions of tokens; 0 = the highest earlier block (as ccusage's "max").</summary>
    public double UsageLimitMillions { get; set; }

    /// <summary>The length of a session block in hours (Claude's is 5).</summary>
    public int UsageHours { get; set; } = 5;

    /// <summary>The percentage of the limit at which the island warns.</summary>
    public int UsageWarnPercent { get; set; } = 80;

    /// <summary>Whether the island says when a program starts using the microphone or camera.</summary>
    public bool IslandPrivacy { get; set; }

    /// <summary>Whether the island says when a Bluetooth device connects.</summary>
    public bool IslandBluetooth { get; set; }

    /// <summary>Whether the island says when a download to the Downloads folder is finished.</summary>
    public bool IslandDownloads { get; set; }

    /// <summary>Whether the state of the system (processor, memory, network) is part of the island's info and card.</summary>
    public bool IslandSystem { get; set; }

    /// <summary>The minutes the island's timer runs.</summary>
    public int IslandTimerMinutes { get; set; } = 25;

    /// <summary>Quiet hours as "HH:mm" (they may span midnight): the island stays away. Empty = none.</summary>
    public string? IslandQuietFrom { get; set; }

    public string? IslandQuietTo { get; set; }

    /// <summary>Programs (names, comma separated) that hide the island while they are in front.</summary>
    public string? IslandHideInApps { get; set; }

    /// <summary>Programs the island shows over even when they are full screen.</summary>
    public string? IslandShowOverApps { get; set; }

    /// <summary>Whether the island, at its default place, appears on the screen the pointer is on.</summary>
    public bool IslandFollowPointer { get; set; }

    /// <summary>The size of Notiland, 1 = 100 %.</summary>
    public double IslandScale { get; set; } = 1;

    /// <summary>What Notiland does under the pointer: open at all, show a whole line, the music buttons, the song's time.</summary>
    public bool IslandHoverExpand { get; set; } = true;

    public bool IslandHoverFullText { get; set; } = true;

    public bool IslandHoverButtons { get; set; } = true;

    public bool IslandHoverTime { get; set; } = true;

    /// <summary>
    /// Notiland's place in each situation (desktop, window, maximized, full screen), keyed by its name; the default place is
    /// <see cref="IslandX"/>, <see cref="IslandY"/>. A situation without one uses the default.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, IslandPlace> IslandPlaces { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether holding the pointer at the top centre of the screen for a moment opens the list of all folders.</summary>
    public bool IslandFolders { get; set; } = true;

    /// <summary>
    /// Keyboard shortcuts of the general actions (see <see cref="HotkeyBindings"/>), written like <c>Ctrl+M</c>. Starts with the
    /// defaults; an entry in the file replaces its default, and an empty text means the user cleared it.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> Hotkeys { get; } = new()
    {
        [HotkeyBindings.AllFolders] = HotkeyBindings.DefaultAllFolders.ToString(),
    };

    /// <summary>The release the user chose to keep the current version over ("Keep"); not asked about again unless a newer one appears.</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Whether the user was asked, once, if DeskGrid should start with Windows.</summary>
    public bool AutoStartAsked { get; set; }

    /// <summary>The version whose "what's new" window was last shown (three parts, like <c>0.5.0</c>); <see langword="null"/> before the first one.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Folder names in the order the list of all folders shows them.</summary>
    public List<string> FolderOrder { get; set; } = [];

    /// <summary>Keyed by folder name under <see cref="RootPath"/>; case-insensitive like the file system.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, WidgetConfig> Widgets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes over every setting of <paramref name="other"/> in place (others hold this object), except what describes this
    /// computer's own history: whether it asked about starting with Windows, the version last shown, the update kept.
    /// </summary>
    public void ReplaceWith(AppConfig other)
    {
        RootPath = other.RootPath;
        PanelColumns = other.PanelColumns;
        DropMode = other.DropMode;
        DeleteMode = other.DeleteMode;
        PanelBackground = other.PanelBackground;
        PanelOpenMode = other.PanelOpenMode;
        Language = other.Language;
        AutoBackupEnabled = other.AutoBackupEnabled;
        AutoBackupHour = other.AutoBackupHour;
        AutoBackupDays = other.AutoBackupDays;
        CheckForUpdatesAtStartup = other.CheckForUpdatesAtStartup;
        AllowControl = other.AllowControl;
        IslandEnabled = other.IslandEnabled;
        IslandMusic = other.IslandMusic;
        IslandFolders = other.IslandFolders;
        IslandBattery = other.IslandBattery;
        IslandX = other.IslandX;
        IslandY = other.IslandY;
        IslandShowSeconds = other.IslandShowSeconds;
        IslandUsage = other.IslandUsage;
        UsageLimitMillions = other.UsageLimitMillions;
        UsageHours = other.UsageHours;
        UsageWarnPercent = other.UsageWarnPercent;
        IslandPrivacy = other.IslandPrivacy;
        IslandBluetooth = other.IslandBluetooth;
        IslandDownloads = other.IslandDownloads;
        IslandSystem = other.IslandSystem;
        IslandTimerMinutes = other.IslandTimerMinutes;
        IslandQuietFrom = other.IslandQuietFrom;
        IslandQuietTo = other.IslandQuietTo;
        IslandHideInApps = other.IslandHideInApps;
        IslandShowOverApps = other.IslandShowOverApps;
        IslandFollowPointer = other.IslandFollowPointer;
        IslandScale = other.IslandScale;
        IslandHoverExpand = other.IslandHoverExpand;
        IslandHoverFullText = other.IslandHoverFullText;
        IslandHoverButtons = other.IslandHoverButtons;
        IslandHoverTime = other.IslandHoverTime;
        IslandPlaces.Clear();
        foreach (var (scene, place) in other.IslandPlaces)
        {
            IslandPlaces[scene] = place;
        }

        FolderOrder = other.FolderOrder;
        Hotkeys.Clear();
        foreach (var (action, key) in other.Hotkeys)
        {
            Hotkeys[action] = key;
        }

        Widgets.Clear();
        foreach (var (name, widget) in other.Widgets)
        {
            Widgets[name] = widget;
        }
    }
}

internal sealed class WidgetConfig
{
    /// <summary>Top-left corner in physical screen pixels; <see langword="null"/> until the widget is placed.</summary>
    public ScreenPoint? Position { get; set; }

    /// <summary>Item file names in display order.</summary>
    public List<string> ItemOrder { get; set; } = [];

    /// <summary>Keyboard shortcut that opens this folder's panel, written like <c>Ctrl+Alt+1</c>; <see langword="null"/> for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] // Most folders have none: keep config.json free of "hotkey": null.
    public string? Hotkey { get; set; }
}

/// <summary>The popup panel's background.</summary>
internal sealed class PanelBackground
{
    public PanelBackgroundStyle Style { get; set; } = PanelBackgroundStyle.System;

    /// <summary>"#RRGGBB", used with <see cref="PanelBackgroundStyle.Custom"/>.</summary>
    public string CustomColor { get; set; } = "#2B2B2B";

    /// <summary>0 to 1, used with <see cref="PanelBackgroundStyle.Custom"/>.</summary>
    public double CustomOpacity { get; set; } = 0.85;

    /// <summary>
    /// 0 to 1, how opaque the other styles are; <see langword="null"/> keeps the style's own default (the system style then
    /// stays the system acrylic, while a value turns it into a plain translucent color, since acrylic has no opacity).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Opacity { get; set; }

    /// <summary>
    /// 0 to 1, used with <see cref="PanelBackgroundStyle.LiquidGlass"/>: how dark the glass rim is on the side away from
    /// the light, relative to its bright side (0 = no dark side, 1 = as strong as the bright one); <see langword="null"/>
    /// keeps <see cref="DefaultGlassShade"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GlassShade { get; set; }

    public const double DefaultGlassShade = 0.5;
}

internal enum PanelBackgroundStyle
{
    /// <summary>System acrylic in the system's light/dark theme (Windows 11); opaque theme color elsewhere.</summary>
    System,
    Dark,
    Light,
    Custom,

    /// <summary>The screen behind the panel, blurred and bent at the rim like curved glass.</summary>
    LiquidGlass,
}

/// <summary>Where a folder's panel opens.</summary>
internal enum PanelOpenMode
{
    /// <summary>Next to the widget, as big as its items need.</summary>
    Nearby,

    /// <summary>In the middle of the widget's screen, always the same size (set by the column count), whatever is in the folder.</summary>
    Centered,
}

/// <summary>What the island shows of the music that is playing.</summary>
/// <summary>Where Notiland is in one situation: the middle of its top edge, in screen pixels.</summary>
internal sealed class IslandPlace
{
    public int X { get; set; }

    public int Y { get; set; }
}

internal enum IslandMusicMode
{
    /// <summary>Nothing; the system media controls are not even read.</summary>
    Off,

    /// <summary>The song and its cover for a few seconds when a new one starts.</summary>
    OnChange,

    /// <summary>The song and its cover for as long as music plays.</summary>
    WhilePlaying,
}

/// <summary>The language of DeskGrid's own texts.</summary>
internal enum AppLanguage
{
    /// <summary>Traditional Chinese if Windows is set to it, English otherwise.</summary>
    System,
    English,
    TraditionalChinese,
}

/// <summary>What dropping a desktop shortcut onto a widget does. Dropped .exe files always get a new shortcut.</summary>
internal enum DropMode
{
    Move,
    Copy,
}

/// <summary>What happens to the items inside a widget folder when the folder is deleted.</summary>
internal enum DeleteMode
{
    /// <summary>The items are moved to the user's desktop first.</summary>
    MoveToDesktop,

    /// <summary>The items go with the folder.</summary>
    DeleteItems,
}
