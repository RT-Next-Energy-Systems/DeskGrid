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

    public static string DefaultRootPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DeskGrid");

    public string RootPath { get; set; } = DefaultRootPath;

    public int PanelColumns { get; set; } = DefaultPanelColumns;

    public DropMode DropMode { get; set; } = DropMode.Move;

    public DeleteMode DeleteMode { get; set; } = DeleteMode.MoveToDesktop;

    public PanelBackground PanelBackground { get; set; } = new();

    public PanelOpenMode PanelOpenMode { get; set; } = PanelOpenMode.Nearby;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Whether DeskGrid asks GitHub for a newer release a few seconds after it starts. The only network use that isn't the user's own request.</summary>
    public bool CheckForUpdatesAtStartup { get; set; } = true;

    /// <summary>
    /// Whether other programs of this user (scripts, AI agents) may control DeskGrid through <c>DeskGrid.exe --ctl</c>
    /// and the control pipe. Off until the user turns it on.
    /// </summary>
    public bool AllowControl { get; set; }

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
