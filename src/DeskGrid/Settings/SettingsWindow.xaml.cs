using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Folders;
using DeskGrid.Hotkeys;
using DeskGrid.Island;
using DeskGrid.Panel;
using DeskGrid.Resources;
using DeskGrid.Shell;
using DeskGrid.Updates;
using Microsoft.Win32;

namespace DeskGrid.Settings;

/// <summary>What the user chose in <see cref="SettingsWindow"/>.</summary>
internal sealed record SettingsChoice(
    string RootPath,
    int PanelColumns,
    DropMode DropMode,
    DeleteMode DeleteMode,
    PanelBackground PanelBackground,
    PanelOpenMode PanelOpenMode,
    AppLanguage Language,
    bool CheckForUpdatesAtStartup,
    bool AllowControl,
    bool IslandEnabled,
    IslandMusicMode IslandMusic,
    bool IslandFolders,
    bool IslandBattery = false,
    int IslandShowSeconds = 5,
    bool IslandUsage = false,
    double UsageLimitMillions = 5,
    int UsageHours = 5,
    int UsageWarnPercent = 80,
    bool IslandPrivacy = false,
    bool IslandBluetooth = false,
    bool IslandDownloads = false,
    bool IslandSystem = false,
    int IslandTimerMinutes = 25,
    bool IslandFollowPointer = false,
    string IslandQuietFrom = "",
    string IslandQuietTo = "",
    string IslandHideInApps = "",
    string IslandShowOverApps = "",
    double IslandScale = 1,
    bool HoverExpand = true,
    bool HoverFullText = true,
    bool HoverButtons = true,
    bool HoverTime = true,
    string? IslandCardSections = null,
    bool AutoBackupEnabled = false,
    int AutoBackupHour = AppConfig.DefaultAutoBackupHour,
    int AutoBackupDays = AppConfig.DefaultAutoBackupDays,
    bool UsageOnline = false);

/// <summary>One line of the language list: the language and the name it is shown under.</summary>
internal sealed record LanguageOption(AppLanguage Language, string Label);

/// <summary>One hour in the daily automatic backup dropdown.</summary>
internal sealed record BackupHourOption(int Hour, string Label);

/// <summary>One entry in the scrollable quiet-hours dropdown (<c>""</c> = off, or <c>"HH:mm"</c>).</summary>
internal sealed record QuietTimeOption(string Value, string Label);

/// <summary>
/// Edits a copy of the settings; <see cref="Applied"/> fires on Apply (and on Done, when the user wants the changes
/// kept) with a validated choice. The window stays open after Apply, so a change can be looked at on a real panel.
/// </summary>
internal partial class SettingsWindow : Window
{
    private const int MaxColumns = 8;

    private readonly AppConfig _config;
    private readonly PanelPreview _preview = new();
    private Color _customColor;
    private PanelBackgroundStyle _shownStyle; // The style whose opacity the slider is showing.
    private string _appliedKey = ""; // What was last applied (or shown at the start): the window is "changed" when it differs.
    private bool _closeWithoutAsking;

    public SettingsWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        LocalizeTexts();
        FluentWindow.UseOpaqueBackground(this);
        PreviewHost.Child = _preview;

        // On a small screen the cards scroll instead of the window running off it.
        MaxHeight = SystemParameters.WorkArea.Height;

        RootBox.Text = config.RootPath;
        ColumnsBox.ItemsSource = Enumerable.Range(1, MaxColumns).ToList();
        ColumnsBox.SelectedItem = Math.Clamp(config.PanelColumns, 1, MaxColumns);
        (config.PanelOpenMode == PanelOpenMode.Centered ? OpenCentered : OpenNearby).IsChecked = true;
        LanguageBox.ItemsSource = LanguageOptions();
        LanguageBox.SelectedValue = config.Language;
        (config.DropMode == DropMode.Copy ? DropCopy : DropMove).IsChecked = true;
        (config.DeleteMode == DeleteMode.DeleteItems ? DeleteItems : DeleteMove).IsChecked = true;
        AutoBackupBox.IsChecked = config.AutoBackupEnabled;
        AutoBackupHourBox.ItemsSource = BackupHourOptions();
        AutoBackupHourBox.SelectedValue = Math.Clamp(config.AutoBackupHour, 0, 23);
        AutoBackupDaysBox.ItemsSource = Enumerable.Range(AppConfig.MinAutoBackupDays, AppConfig.MaxAutoBackupDays - AppConfig.MinAutoBackupDays + 1).ToList();
        AutoBackupDaysBox.SelectedItem = Math.Clamp(config.AutoBackupDays, AppConfig.MinAutoBackupDays, AppConfig.MaxAutoBackupDays);
        UpdateAutoBackupEnabled();

        var background = config.PanelBackground;
        BackgroundRadioFor(background.Style).IsChecked = true;
        _shownStyle = background.Style;
        _customColor = PanelTheme.ParseColor(background.CustomColor) ?? PanelTheme.ParseColor(new PanelBackground().CustomColor)!.Value;
        ColorSwatch.Background = new SolidColorBrush(_customColor);
        OpacitySlider.Value = Math.Clamp(OpacityOf(background, background.Style), OpacitySlider.Minimum, OpacitySlider.Maximum);
        ShadeSlider.Value = Math.Clamp(background.GlassShade ?? PanelBackground.DefaultGlassShade, ShadeSlider.Minimum, ShadeSlider.Maximum);
        foreach (var style in Enum.GetValues<PanelBackgroundStyle>())
        {
            BackgroundRadioFor(style).Checked += OnBackgroundChanged;
        }

        CheckAtStartupBox.IsChecked = config.CheckForUpdatesAtStartup;
        AllowControlBox.IsChecked = config.AllowControl;
        IslandBox.IsChecked = config.IslandEnabled;
        IslandFoldersBox.IsChecked = config.IslandFolders;
        IslandBatteryBox.IsChecked = config.IslandBattery;
        IslandUsageBox.IsChecked = config.IslandUsage;
        UsageLimitBox.Text = config.UsageLimitMillions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UsageOnlineBox.IsChecked = config.UsageOnline;
        UsageHoursBox.Text = config.UsageHours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UsageWarnBox.Text = config.UsageWarnPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IslandSecondsBox.Text = config.IslandShowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IslandPrivacyBox.IsChecked = config.IslandPrivacy;
        IslandBluetoothBox.IsChecked = config.IslandBluetooth;
        IslandDownloadsBox.IsChecked = config.IslandDownloads;
        IslandSystemBox.IsChecked = config.IslandSystem;
        IslandTimerBox.Text = config.IslandTimerMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IslandFollowBox.IsChecked = config.IslandFollowPointer;
        PopulateQuietTime(IslandQuietFromBox, config.IslandQuietFrom);
        PopulateQuietTime(IslandQuietToBox, config.IslandQuietTo);
        IslandHideAppsBox.Text = config.IslandHideInApps ?? "";
        IslandShowOverBox.Text = config.IslandShowOverApps ?? "";
        IslandSizeSlider.Value = Math.Clamp(Math.Round(config.IslandScale * 20) * 5, IslandSizeSlider.Minimum, IslandSizeSlider.Maximum);
        HoverExpandBox.IsChecked = config.IslandHoverExpand;
        HoverFullTextBox.IsChecked = config.IslandHoverFullText;
        HoverButtonsBox.IsChecked = config.IslandHoverButtons;
        HoverTimeBox.IsChecked = config.IslandHoverTime;
        _cardParts = [.. CardSections.ForEditing(config.IslandCardSections)];
        BuildCardParts();
        UpdateIslandEnabled();
        MusicRadioFor(config.IslandMusic).IsChecked = true;
        Pages.SelectedItem = PageGeneralItem;

        // Wired after the values are set, so filling the controls does not draw the preview again and again.
        ColumnsBox.SelectionChanged += (_, _) => RefreshPreview();
        OpacitySlider.ValueChanged += (_, _) => RefreshPreview();
        ShadeSlider.ValueChanged += (_, _) => RefreshPreview();
        Loaded += (_, _) => RefreshPreview(); // The preview needs the window's DPI.
        _appliedKey = Key(ReadChoice());
    }

    // The sample panel for what the controls show now.
    private void RefreshPreview() => _preview.Update(BuildBackground(), ColumnsBox.SelectedItem as int? ?? _config.PanelColumns);

    /// <summary>Whether the controls differ from what was last applied.</summary>
    internal bool HasUnsavedChanges => Key(ReadChoice()) != _appliedKey;

    // PanelBackground has no value equality, so what the window holds is compared as text.
    private static string Key(SettingsChoice choice) => string.Join('|',
        choice.RootPath, choice.PanelColumns, choice.DropMode, choice.DeleteMode, choice.PanelOpenMode, choice.Language,
        choice.CheckForUpdatesAtStartup, choice.AllowControl, choice.IslandEnabled, choice.IslandMusic, choice.IslandFolders, choice.IslandBattery,
        choice.IslandShowSeconds, choice.IslandUsage, choice.UsageLimitMillions, choice.UsageHours, choice.UsageWarnPercent,
        choice.IslandPrivacy, choice.IslandBluetooth, choice.IslandDownloads, choice.IslandSystem, choice.IslandTimerMinutes, choice.IslandFollowPointer,
        choice.IslandQuietFrom, choice.IslandQuietTo, choice.IslandHideInApps, choice.IslandShowOverApps,
        choice.IslandScale, choice.HoverExpand, choice.HoverFullText, choice.HoverButtons, choice.HoverTime, choice.IslandCardSections,
        choice.AutoBackupEnabled, choice.AutoBackupHour, choice.AutoBackupDays, choice.UsageOnline,
        choice.PanelBackground.Style, choice.PanelBackground.CustomColor, choice.PanelBackground.CustomOpacity,
        choice.PanelBackground.Opacity, choice.PanelBackground.GlassShade);

    /// <summary>The page shown on the right, in the order of the list on the left.</summary>
    internal enum Page
    {
        General,
        Panel,
        Shortcuts,
        Island,
        About,
    }

    /// <summary>The page the list on the left has chosen.</summary>
    internal Page CurrentPage => PageItems.FindIndex(pair => pair.Item == Pages.SelectedItem) is var i and >= 0 ? (Page)i : Page.General;

    internal void ShowPage(Page page) => Pages.SelectedItem = PageItems[(int)page].Item;

    // Each entry of the list and the page it stands for, in <see cref="Page"/> order.
    private List<(ListBoxItem Item, FrameworkElement Content)> PageItems =>
    [
        (PageGeneralItem, PageGeneral),
        (PagePanelItem, PagePanel),
        (PageShortcutsItem, PageShortcuts),
        (PageIslandItem, PageIsland),
        (PageAboutItem, PageAbout),
    ];

    private void OnPageChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var (item, content) in PageItems)
        {
            content.Visibility = item.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public event EventHandler<SettingsChoice>? Applied;

    /// <summary>Checks for a newer release (showing the update dialog if there is one); returns what to tell the user, if anything.</summary>
    public Func<Task<string>>? CheckForUpdates { get; set; }

    /// <summary>Creates the taskbar shortcut that lists all folders and shows it to the user, who pins it.</summary>
    public Action? PinAllFolders { get; set; }

    /// <summary>Lets the user change the keyboard shortcuts (and applies them); the window then shows the new one.</summary>
    public Action? EditHotkeys { get; set; }

    /// <summary>Asks the user where to save the settings and writes them there.</summary>
    public Action? ExportSettings { get; set; }

    /// <summary>Asks the user for a settings file and takes it over (the caller closes this window when it did).</summary>
    public Action? ImportSettings { get; set; }

    /// <summary>Lists the available automatic daily backups (newest first) for the one-click restore menu.</summary>
    public Func<IReadOnlyList<BackupEntry>>? ListBackups { get; set; } = () => AutoBackup.List(AutoBackup.DefaultDirectory);

    /// <summary>Restores the settings from the chosen automatic backup file path in one click.</summary>
    public Action<string>? RestoreBackup { get; set; }

    /// <summary>Puts the island back at the top centre of the screen.</summary>
    public Action? ResetIslandPosition { get; set; }

    private void OnAutoBackupSwitched(object sender, RoutedEventArgs e) => UpdateAutoBackupEnabled();

    private void UpdateAutoBackupEnabled() => AutoBackupOptions.IsEnabled = AutoBackupBox.IsChecked == true;

    private void OnResetIsland(object sender, RoutedEventArgs e)
    {
        ResetIslandPosition?.Invoke();
        RefreshPlaces();
    }

    // What the island does is dimmed while it is off, the usage numbers while usage is off.
    private void OnIslandSwitched(object sender, RoutedEventArgs e) => UpdateIslandEnabled();

    private void OnUsageSwitched(object sender, RoutedEventArgs e) => UpdateIslandEnabled();

    private void UpdateIslandEnabled()
    {
        IslandOptions.IsEnabled = IslandBox.IsChecked == true;
        UsageFields.IsEnabled = IslandUsageBox.IsChecked == true;
        UsageOnlineBox.IsEnabled = IslandUsageBox.IsChecked == true;
        HoverOptions.IsEnabled = HoverExpandBox.IsChecked == true;
    }

    // ---- What the card shows ----

    private List<(string Id, bool Shown)> _cardParts = [];

    internal static string CardPartName(string id) => id switch
    {
        CardSections.Players => Strings.CardPartPlayers,
        CardSections.Usage => Strings.CardPartUsage,
        CardSections.System => Strings.CardPartSystem,
        CardSections.Folders => Strings.CardPartFolders,
        _ => Strings.CardPartShelf,
    };

    // One row per part: a tick to show it, and arrows to move it. The shown parts keep the order they have here.
    private void BuildCardParts()
    {
        CardSectionsPanel.Children.Clear();
        for (var i = 0; i < _cardParts.Count; i++)
        {
            var index = i;
            var (id, shown) = _cardParts[i];
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new CheckBox { Content = CardPartName(id), IsChecked = shown, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) => _cardParts[index] = (id, box.IsChecked == true);
            row.Children.Add(box);
            var up = CardMoveButton("", Strings.SettingsMoveUp, index > 0, () => MoveCardPart(index, -1));
            var down = CardMoveButton("", Strings.SettingsMoveDown, index < _cardParts.Count - 1, () => MoveCardPart(index, 1));
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);
            row.Children.Add(up);
            row.Children.Add(down);
            CardSectionsPanel.Children.Add(row);
        }
    }

    private static Button CardMoveButton(string glyph, string tip, bool enabled, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11 },
            Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0), ToolTip = tip, IsEnabled = enabled,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void MoveCardPart(int index, int by)
    {
        var other = index + by;
        if (other < 0 || other >= _cardParts.Count)
        {
            return;
        }

        (_cardParts[index], _cardParts[other]) = (_cardParts[other], _cardParts[index]);
        BuildCardParts();
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Whether Claude Code's status line feeds DeskGrid: then the usage is Claude's own number.
    private void RefreshUsageConnection()
    {
        var connected = ClaudeStatusLine.IsConnected(Home);
        UsageConnectButton.Content = connected ? Strings.SettingsUsageDisconnect : Strings.SettingsUsageConnect;
        UsageConnectStatus.Text = connected ? Strings.SettingsUsageConnected : Strings.SettingsUsageNotConnected;
        var tools = AiTools.Detect();
        UsageFoundText.Text = tools.Count == 0
            ? Strings.SettingsUsageFoundNone
            : string.Format(Strings.SettingsUsageFound, string.Join(", ", tools.Select(t => t.ReportsUsage ? t.Name : string.Format(Strings.SettingsUsageNoData, t.Name))));
    }

    private void OnUsageConnect(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ClaudeStatusLine.IsConnected(Home))
            {
                ClaudeStatusLine.Disconnect(Home);
            }
            else if (MessageBox.Show(this, Strings.SettingsUsageConnectAsk, Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes)
            {
                ClaudeStatusLine.Connect(Home);
                MessageBox.Show(this, Strings.SettingsUsageConnectDone, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, string.Format(Strings.SettingsUsageFailed, exception.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshUsageConnection();
    }

    private void OnHoverSwitched(object sender, RoutedEventArgs e) => UpdateIslandEnabled();

    private void OnIslandSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IslandSizeValue is not null)
        {
            IslandSizeValue.Text = $"{IslandSizeSlider.Value:0}%";
        }
    }

    /// <summary>Sets the place of a situation (the app shows the desktop and lets the user drag NotiLand).</summary>
    public Action<string>? EditIslandPlace { get; set; }

    /// <summary>Gives a situation back the default place.</summary>
    public Action<string>? ResetIslandPlace { get; set; }

    /// <summary>Shows each situation with whether it has a place of its own, an Edit and a Reset button.</summary>
    internal void RefreshPlaces()
    {
        IslandPlacesPanel.Children.Clear();
        foreach (var scene in IslandScene.All)
        {
            var set = scene == IslandScene.Default ? _config.IslandX is not null : _config.IslandPlaces.ContainsKey(scene);
            var status = set ? Strings.SettingsPlaceSet : scene == IslandScene.Default ? Strings.SettingsPlaceTop : Strings.SettingsPlaceUnset;
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = IslandHost.SceneName(scene), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var state = new TextBlock { Text = status, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            var edit = new Button { Content = Strings.SettingsPlaceEdit, Padding = new Thickness(12, 4, 12, 4) };
            var reset = new Button { Content = Strings.SettingsPlaceReset, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), IsEnabled = set };
            System.Windows.Automation.AutomationProperties.SetName(edit, $"{Strings.SettingsPlaceEdit}: {name.Text}");
            System.Windows.Automation.AutomationProperties.SetName(reset, $"{Strings.SettingsPlaceReset}: {name.Text}");
            edit.Click += (_, _) => EditIslandPlace?.Invoke(scene);
            reset.Click += (_, _) =>
            {
                ResetIslandPlace?.Invoke(scene);
                RefreshPlaces();
            };
            Grid.SetColumn(state, 1);
            Grid.SetColumn(edit, 2);
            Grid.SetColumn(reset, 3);
            row.Children.Add(name);
            row.Children.Add(state);
            row.Children.Add(edit);
            row.Children.Add(reset);
            IslandPlacesPanel.Children.Add(row);
        }
    }

    private void OnIslandShortcuts(object sender, RoutedEventArgs e)
    {
        EditHotkeys?.Invoke();
        ShowHotkeysHint();
    }

    private void OnExport(object sender, RoutedEventArgs e) => ExportSettings?.Invoke();

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var menu = BuildRestoreMenu();
        menu.PlacementTarget = ImportButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Builds the one-click restore menu listing each daily automatic backup (newest first) and a Browse option.</summary>
    internal ContextMenu BuildRestoreMenu()
    {
        var menu = new ContextMenu();
        var backups = ListBackups?.Invoke() ?? [];
        if (backups.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Strings.SettingsBackupNone, IsEnabled = false });
        }
        else
        {
            foreach (var entry in backups)
            {
                var item = new MenuItem { Header = entry.Label, Tag = entry.FilePath };
                item.Click += (_, _) => RestoreBackup?.Invoke(entry.FilePath);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var browse = new MenuItem { Header = Strings.SettingsBrowse };
        browse.Click += (_, _) => ImportSettings?.Invoke();
        menu.Items.Add(browse);
        return menu;
    }

    private void OnPinAll(object sender, RoutedEventArgs e) => PinAllFolders?.Invoke();

    private void OnHotkeys(object sender, RoutedEventArgs e)
    {
        EditHotkeys?.Invoke();
        ShowHotkeysHint();
    }

    // The shortcut people most want to know about: the one that opens all folders.
    private void ShowHotkeysHint() => HotkeysHint.Text = HotkeyBindings.Get(_config, HotkeyBindings.AllFolders) is { } key
        ? string.Format(Strings.SettingsHotkeysHint, key)
        : Strings.SettingsHotkeysHintNone;

    private void OnOpenRoot(object sender, RoutedEventArgs e)
    {
        // What is in the box, which may not be saved yet.
        var path = RootBox.Text.Trim();
        if (Directory.Exists(path))
        {
            Error.Visibility = Visibility.Collapsed;
            ShellItem.Open(path);
        }
        else
        {
            Error.Text = Strings.SettingsRootMissing;
            Error.Visibility = Visibility.Visible;
        }
    }

    private void OnGitHub(object sender, RoutedEventArgs e) => ShellItem.Open(UpdateChecker.RepositoryUrl);

    private void OnPrivacy(object sender, RoutedEventArgs e) => ShellItem.Open(Strings.SettingsPrivacyUrl);

    private void OnNotices(object sender, RoutedEventArgs e) => ShellItem.Open(Strings.SettingsNoticesUrl);

    private void OnChangeLog(object sender, RoutedEventArgs e) => new ChangeLogWindow { Owner = this }.ShowDialog();

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (CheckForUpdates is not { } check)
        {
            return;
        }

        CheckUpdatesButton.IsEnabled = false;
        UpdateStatus.Text = Strings.SettingsChecking;
        try
        {
            UpdateStatus.Text = await check();
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    // The opacity the settings hold for a style: the user's own, or the style's default.
    private static double OpacityOf(PanelBackground background, PanelBackgroundStyle style) =>
        style == PanelBackgroundStyle.Custom ? background.CustomOpacity : background.Opacity ?? PanelTheme.DefaultOpacity(style);

    // The slider snaps to 5 %, so a style's default (82 %) can only be shown as the nearest step.
    private static bool SameOpacity(double a, double b) => Math.Abs(a - b) <= 0.025;

    private void OnBackgroundChanged(object sender, RoutedEventArgs e)
    {
        var style = SelectedBackground;

        // A slider the user has not moved follows to the new style's own opacity; one they have moved keeps their value.
        if (SameOpacity(OpacitySlider.Value, OpacityOf(_config.PanelBackground, _shownStyle)))
        {
            OpacitySlider.Value = OpacityOf(_config.PanelBackground, style);
        }

        _shownStyle = style;
        RefreshPreview();
    }

    private PanelBackgroundStyle SelectedBackground =>
        Enum.GetValues<PanelBackgroundStyle>().First(style => BackgroundRadioFor(style).IsChecked == true);

    internal System.Windows.Controls.RadioButton BackgroundRadioFor(PanelBackgroundStyle style) => style switch
    {
        PanelBackgroundStyle.Dark => BackgroundDark,
        PanelBackgroundStyle.Light => BackgroundLight,
        PanelBackgroundStyle.LiquidGlass => BackgroundGlass,
        PanelBackgroundStyle.Custom => BackgroundCustom,
        _ => BackgroundSystem,
    };

    private static LanguageOption[] LanguageOptions() =>
    [
        new(AppLanguage.System, Strings.SettingsLanguageSystem),
        new(AppLanguage.English, Strings.LanguageNameEnglish),
        new(AppLanguage.TraditionalChinese, Strings.LanguageNameChinese),
    ];

    private static BackupHourOption[] BackupHourOptions() =>
        [.. Enumerable.Range(0, 24).Select(hour => new BackupHourOption(hour, $"{hour:00}:00"))];

    private static string NormalizedTime(string? text) =>
        IslandRules.Parse(text) is { } time ? time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "";

    private static List<QuietTimeOption> QuietTimeOptions(string? current)
    {
        var options = new List<QuietTimeOption> { new("", Strings.SettingsQuietNone) };
        var times = new SortedSet<string>(StringComparer.Ordinal);
        for (var hour = 0; hour < 24; hour++)
        {
            times.Add($"{hour:00}:00");
            times.Add($"{hour:00}:30");
        }

        var normalized = NormalizedTime(current);
        if (normalized.Length > 0)
        {
            times.Add(normalized);
        }

        foreach (var time in times)
        {
            options.Add(new QuietTimeOption(time, time));
        }

        return options;
    }

    private static void PopulateQuietTime(ComboBox box, string? value)
    {
        var normalized = NormalizedTime(value);
        box.ItemsSource = QuietTimeOptions(normalized);
        box.SelectedValue = normalized;
    }

    private void LocalizeTexts()
    {
        Title = Strings.SettingsTitle;
        LanguageLabel.Text = Strings.SettingsLanguage;
        OpenModeLabel.Text = Strings.SettingsOpenMode;
        OpenNearby.Content = Strings.SettingsOpenNearby;
        OpenCentered.Content = Strings.SettingsOpenCentered;
        RootLabel.Text = Strings.SettingsRootFolder;
        BrowseButton.Content = Strings.SettingsBrowse;
        OpenRootButton.Content = Strings.SettingsOpen;
        BackupLabel.Text = Strings.SettingsBackup;
        ExportButton.Content = Strings.SettingsExport;
        ImportButton.Content = Strings.SettingsImport;
        AutoBackupBox.Content = Strings.SettingsAutoBackup;
        AutoBackupHourLabel.Text = Strings.SettingsAutoBackupHour;
        AutoBackupDaysLabel.Text = Strings.SettingsAutoBackupDays;
        TaskbarLabel.Text = Strings.SettingsTaskbar;
        PinAllButton.Content = Strings.SettingsPinAll;
        HotkeysLabel.Text = Strings.SettingsHotkeys;
        HotkeysButton.Content = Strings.SettingsHotkeysEdit;
        ShowHotkeysHint();
        AllowControlBox.Content = Strings.SettingsAllowControl;
        IslandBox.Content = Strings.SettingsIsland;
        IslandCardShow.Text = Strings.SettingsIslandCardShow;
        IslandCardShortcuts.Text = Strings.SettingsIslandCardShortcuts;
        IslandCardAway.Text = Strings.SettingsIslandCardAway;
        IslandCardPlace.Text = Strings.SettingsIslandCardPlace;
        IslandShortcutsButton.Content = Strings.SettingsIslandShortcuts;
        IslandCardPlaces.Text = Strings.SettingsIslandCardPlaces;
        IslandCardLook.Text = Strings.SettingsIslandCardLook;
        IslandSizeLabel.Text = Strings.SettingsIslandSize;
        HoverExpandBox.Content = Strings.SettingsHoverExpand;
        HoverFullTextBox.Content = Strings.SettingsHoverFullText;
        HoverButtonsBox.Content = Strings.SettingsHoverButtons;
        HoverTimeBox.Content = Strings.SettingsHoverTime;
        IslandCardContents.Text = Strings.SettingsIslandCardContents;
        BuildCardParts();
        RefreshPlaces();
        IslandMusicLabel.Text = Strings.SettingsIslandMusic;
        MusicOff.Content = Strings.SettingsIslandMusicOff;
        MusicOnChange.Content = Strings.SettingsIslandMusicChange;
        MusicWhilePlaying.Content = Strings.SettingsIslandMusicWhile;
        IslandFoldersBox.Content = Strings.SettingsIslandFolders;
        IslandBatteryBox.Content = Strings.SettingsIslandBattery;
        IslandUsageBox.Content = Strings.SettingsIslandUsage;
        UsageOnlineBox.Content = Strings.SettingsUsageOnline;
        RefreshUsageConnection();
        UsageLimitLabel.Text = Strings.SettingsUsageLimit;
        UsageHoursLabel.Text = Strings.SettingsUsageHours;
        UsageWarnLabel.Text = Strings.SettingsUsageWarn;
        IslandSecondsLabel.Text = Strings.SettingsIslandSeconds;
        IslandResetButton.Content = Strings.SettingsIslandReset;
        IslandPrivacyBox.Content = Strings.SettingsIslandPrivacy;
        IslandBluetoothBox.Content = Strings.SettingsIslandBluetooth;
        IslandDownloadsBox.Content = Strings.SettingsIslandDownloads;
        IslandSystemBox.Content = Strings.SettingsIslandSystem;
        IslandTimerLabel.Text = Strings.SettingsIslandTimer;
        IslandFollowBox.Content = Strings.SettingsIslandFollow;
        IslandQuietFromLabel.Text = Strings.SettingsIslandQuietFrom;
        IslandQuietToLabel.Text = Strings.SettingsIslandQuietTo;
        IslandHideAppsLabel.Text = Strings.SettingsIslandHideApps;
        IslandShowOverLabel.Text = Strings.SettingsIslandShowOver;
        GitHubLabel.Text = Strings.SettingsGitHub;

        // Only owner/name: the label already says GitHub, and the whole address would not fit beside the button.
        // Cleared first: this runs again when the language is changed with the window open.
        GitHubLink.Inlines.Clear();
        GitHubLink.Inlines.Add(new Uri(UpdateChecker.RepositoryUrl).AbsolutePath.Trim('/'));
        GitHubLink.ToolTip = UpdateChecker.RepositoryUrl;
        PrivacyLink.Inlines.Clear();
        PrivacyLink.Inlines.Add(Strings.SettingsPrivacy);
        NoticesLink.Inlines.Clear();
        NoticesLink.Inlines.Add(Strings.SettingsNotices);
        ChangeLogButton.Content = Strings.SettingsChangeLog;
        ColumnsLabel.Text = Strings.SettingsColumns;
        DropModeLabel.Text = Strings.SettingsDropMode;
        DropMove.Content = Strings.SettingsDropMove;
        DropCopy.Content = Strings.SettingsDropCopy;
        DeleteModeLabel.Text = Strings.SettingsDeleteMode;
        DeleteMove.Content = Strings.SettingsDeleteMove;
        DeleteItems.Content = Strings.SettingsDeleteItems;
        BackgroundLabel.Text = Strings.SettingsBackground;
        BackgroundSystem.Content = Strings.SettingsBackgroundSystem;
        BackgroundDark.Content = Strings.SettingsBackgroundDark;
        BackgroundLight.Content = Strings.SettingsBackgroundLight;
        BackgroundGlass.Content = Strings.SettingsBackgroundGlass;
        BackgroundCustom.Content = Strings.SettingsBackgroundCustom;
        ColorButton.Content = Strings.SettingsColor;
        OpacityLabel.Text = Strings.SettingsOpacity;
        ShadeLabel.Text = Strings.SettingsGlassShade;
        PageGeneralText.Text = Strings.SettingsPageGeneral;
        PagePanelText.Text = Strings.SettingsPagePanel;
        PageShortcutsText.Text = Strings.SettingsPageShortcuts;
        PageIslandText.Text = Strings.SettingsPageIsland;
        PageAboutText.Text = Strings.SettingsPageAbout;
        VersionLabel.Text = Strings.SettingsVersion;
        VersionText.Text = UpdateChecker.CurrentVersion.ToString(3);
        CheckUpdatesButton.Content = Strings.SettingsCheckUpdates;
        CheckAtStartupBox.Content = Strings.SettingsCheckAtStartup;
        ApplyButton.Content = Strings.ButtonApply;
        DoneButton.Content = Strings.ButtonDone;
        _preview.Localize();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        // A half-typed path in the box (bad characters) makes the dialog throw; it then just starts where Windows likes.
        var typed = RootBox.Text.Trim();
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(typed) ? typed : "" };
        if (dialog.ShowDialog(this) == true)
        {
            RootBox.Text = dialog.FolderName;
        }
    }

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        if (ColorDialog.Pick(this, _customColor) is { } color)
        {
            _customColor = color;
            ColorSwatch.Background = new SolidColorBrush(color);
            RefreshPreview();
        }
    }

    /// <summary>Closes without the "save?" question, for when the settings it holds have just been replaced (an import).</summary>
    internal void CloseDiscarding()
    {
        _closeWithoutAsking = true;
        Close();
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();

    // Done, the close button and Esc all end here: changes not applied yet are asked about first.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeWithoutAsking || !HasUnsavedChanges)
        {
            return;
        }

        var answer = MessageBox.Show(this, Strings.SettingsSavePrompt, Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes && !Apply())
        {
            e.Cancel = true; // Something is wrong (the message is shown): stay, so the user can fix it.
        }
    }

    private void OnApply(object sender, RoutedEventArgs e) => Apply();

    // The panel's background for the controls as they are now.
    private PanelBackground BuildBackground()
    {
        var style = SelectedBackground;
        var opacity = Math.Round(OpacitySlider.Value, 2);
        var shade = Math.Round(ShadeSlider.Value, 2);
        return new PanelBackground
        {
            Style = style,
            CustomColor = $"#{_customColor.R:X2}{_customColor.G:X2}{_customColor.B:X2}",
            CustomOpacity = style == PanelBackgroundStyle.Custom ? opacity : _config.PanelBackground.CustomOpacity,
            // Left unset at the style's own default, so the system style stays the acrylic.
            Opacity = style == PanelBackgroundStyle.Custom || SameOpacity(opacity, PanelTheme.DefaultOpacity(style)) ? null : opacity,
            GlassShade = SameOpacity(shade, PanelBackground.DefaultGlassShade) ? null : shade,
        };
    }

    // What the controls hold now, not yet checked.
    private SettingsChoice ReadChoice() => new(
        RootBox.Text.Trim(),
        ColumnsBox.SelectedItem as int? ?? _config.PanelColumns,
        DropCopy.IsChecked == true ? DropMode.Copy : DropMode.Move,
        DeleteItems.IsChecked == true ? DeleteMode.DeleteItems : DeleteMode.MoveToDesktop,
        BuildBackground(),
        OpenCentered.IsChecked == true ? PanelOpenMode.Centered : PanelOpenMode.Nearby,
        LanguageBox.SelectedValue as AppLanguage? ?? _config.Language,
        CheckAtStartupBox.IsChecked == true,
        AllowControlBox.IsChecked == true,
        IslandBox.IsChecked == true,
        MusicOff.IsChecked == true ? IslandMusicMode.Off : MusicWhilePlaying.IsChecked == true ? IslandMusicMode.WhilePlaying : IslandMusicMode.OnChange,
        IslandFoldersBox.IsChecked == true,
        IslandBatteryBox.IsChecked == true,
        Number(IslandSecondsBox, _config.IslandShowSeconds, 0, 3600),
        IslandUsageBox.IsChecked == true,
        Math.Clamp(double.TryParse(UsageLimitBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var limit) ? limit : _config.UsageLimitMillions, 0, 100000),
        Number(UsageHoursBox, _config.UsageHours, 1, 720),
        Number(UsageWarnBox, _config.UsageWarnPercent, 1, 100),
        IslandPrivacyBox.IsChecked == true,
        IslandBluetoothBox.IsChecked == true,
        IslandDownloadsBox.IsChecked == true,
        IslandSystemBox.IsChecked == true,
        Number(IslandTimerBox, _config.IslandTimerMinutes, 1, 600),
        IslandFollowBox.IsChecked == true,
        Time(IslandQuietFromBox),
        Time(IslandQuietToBox),
        IslandHideAppsBox.Text.Trim(),
        IslandShowOverBox.Text.Trim(),
        IslandSizeSlider.Value / 100,
        HoverExpandBox.IsChecked == true,
        HoverFullTextBox.IsChecked == true,
        HoverButtonsBox.IsChecked == true,
        HoverTimeBox.IsChecked == true,
        CardSections.Store(_cardParts.Where(p => p.Shown).Select(p => p.Id)),
        AutoBackupBox.IsChecked == true,
        AutoBackupHourBox.SelectedValue as int? ?? _config.AutoBackupHour,
        AutoBackupDaysBox.SelectedItem as int? ?? _config.AutoBackupDays,
        UsageOnlineBox.IsChecked == true);

    // "HH:mm" as chosen in the scrollable menu, or empty when off.
    private static string Time(ComboBox box) => box.SelectedValue as string ?? "";

    // A whole number typed in a box; what it held before when the text is not one, kept within the range.
    private static int Number(TextBox box, int fallback, int min, int max) =>
        Math.Clamp(int.TryParse(box.Text.Trim(), out var value) ? value : fallback, min, max);

    private RadioButton MusicRadioFor(IslandMusicMode mode) => mode switch
    {
        IslandMusicMode.Off => MusicOff,
        IslandMusicMode.WhilePlaying => MusicWhilePlaying,
        _ => MusicOnChange,
    };

    /// <summary>Checks what the controls hold and hands it to <see cref="Applied"/>; false (with the reason shown) if it is not valid.</summary>
    private bool Apply()
    {
        var choice = ReadChoice();
        if (RootFolder.Prepare(choice.RootPath) is { } problem)
        {
            Error.Text = string.Format(Strings.SettingsRootInvalid, problem);
            Error.Visibility = Visibility.Visible;
            return false;
        }

        Error.Visibility = Visibility.Collapsed;
        var languageChanged = choice.Language != _config.Language;
        Applied?.Invoke(this, choice);
        _appliedKey = Key(choice);
        if (languageChanged)
        {
            // The window stays open, so it speaks the new language from now on too.
            LocalizeTexts();
            LanguageBox.ItemsSource = LanguageOptions();
            LanguageBox.SelectedValue = choice.Language;
            PopulateQuietTime(IslandQuietFromBox, choice.IslandQuietFrom);
            PopulateQuietTime(IslandQuietToBox, choice.IslandQuietTo);
        }

        return true;
    }
}
