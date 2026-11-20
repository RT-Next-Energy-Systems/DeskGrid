using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Folders;
using DeskGrid.Hotkeys;
using DeskGrid.NotiLand;
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
    bool NotiLandEnabled,
    bool AutoBackupEnabled = false,
    int AutoBackupHour = AppConfig.DefaultAutoBackupHour,
    int AutoBackupDays = AppConfig.DefaultAutoBackupDays);

/// <summary>One line of the language list: the language and the name it is shown under.</summary>
internal sealed record LanguageOption(AppLanguage Language, string Label);

/// <summary>One hour in the daily automatic backup dropdown.</summary>
internal sealed record BackupHourOption(int Hour, string Label);


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
    private NotiLandHost? _host;
    private NotiLandSettings? _noti; // NotiLand's pages while it runs.
    private readonly List<ListBoxItem> _notiItems = []; // Their entries in the list on the left (captions too).
    private bool _notiExpanded = true; // NotiLand's pages are shown below it (else folded away).
    private int _lastIndex; // The entry that was chosen before, to step over a caption in the direction of travel.

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
        IslandBox.IsChecked = config.NotiLandEnabled;
        Pages.SelectedItem = PageGeneralItem;

        // Wired after the values are set, so filling the controls does not draw the preview again and again.
        ColumnsBox.SelectionChanged += (_, _) => RefreshPreview();
        OpacitySlider.ValueChanged += (_, _) => RefreshPreview();
        ShadeSlider.ValueChanged += (_, _) => RefreshPreview();
        Loaded += (_, _) => RefreshPreview(); // The preview needs the window's DPI.
        Deactivated += (_, _) => _noti?.WindowDeactivated();
        _appliedKey = Key(ReadChoice());
    }

    // The sample panel for what the controls show now.
    private void RefreshPreview() => _preview.Update(BuildBackground(), ColumnsBox.SelectedItem as int? ?? _config.PanelColumns);

    /// <summary>Whether the controls differ from what was last applied.</summary>
    internal bool HasUnsavedChanges => Key(ReadChoice()) != _appliedKey;

    // PanelBackground has no value equality, so what the window holds is compared as text.
    private static string Key(SettingsChoice choice) => string.Join('|',
        choice.RootPath, choice.PanelColumns, choice.DropMode, choice.DeleteMode, choice.PanelOpenMode, choice.Language,
        choice.CheckForUpdatesAtStartup, choice.AllowControl, choice.NotiLandEnabled,
        choice.AutoBackupEnabled, choice.AutoBackupHour, choice.AutoBackupDays,
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

    /// <summary>The page the list on the left has chosen; any of NotiLand's own pages counts as <see cref="Page.Island"/>.</summary>
    internal Page CurrentPage => Pages.SelectedItem is ListBoxItem { Tag: NotiLandPage }
        ? Page.Island
        : PageItems.FindIndex(pair => pair.Item == Pages.SelectedItem) is var i and >= 0 ? (Page)i : Page.General;

    internal void ShowPage(Page page) => Pages.SelectedItem = PageItems[(int)page].Item;

    // Each entry of the list and the page it stands for, in <see cref="Page"/> order. NotiLand's own pages are entries after
    // the Island one, added while it runs (<see cref="RefreshNotiLandPages"/>).
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
        // A caption is not a page: the choice goes on in the direction it was moving.
        if (Pages.SelectedItem is ListBoxItem { Tag: string } caption)
        {
            var index = Pages.Items.IndexOf(caption);
            Pages.SelectedIndex = index > _lastIndex ? index + 1 : index - 1;
            return;
        }

        _lastIndex = Pages.SelectedIndex;
        var notiLand = Pages.SelectedItem is ListBoxItem { Tag: NotiLandPage page } ? page : null;
        foreach (var (item, content) in PageItems)
        {
            content.Visibility = item.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        PageNotiLand.Visibility = notiLand is null ? Visibility.Collapsed : Visibility.Visible;
        if (notiLand is not null)
        {
            _noti?.Show(notiLand.Number);
        }
        else
        {
            _noti?.Leave();
        }
    }

    /// <summary>NotiLand (BrimDeck) and what shows its settings: its pages are listed below "NotiLand" while it runs.</summary>
    internal NotiLandHost? NotiLand
    {
        get => _host;
        set
        {
            _host = value;
            RefreshNotiLandPages();
        }
    }

    /// <summary>Lists NotiLand's pages if it runs and takes them out of the list if it does not; <paramref name="languageChanged"/> draws them again.</summary>
    internal void RefreshNotiLandPages(bool languageChanged = false)
    {
        var settings = _host?.OpenSettingsPages();
        var same = settings is not null && ReferenceEquals(settings, _noti);
        if (same && !languageChanged)
        {
            return;
        }

        // What is chosen among the entries about to go: the same page again when only the titles change, else the NotiLand entry itself.
        var keep = Pages.SelectedItem is ListBoxItem { Tag: NotiLandPage current } ? current.Number : -1;
        if (!same && keep >= 0)
        {
            Pages.SelectedItem = PageIslandItem;
        }

        PageNotiLand.Child = null;
        _noti = settings;
        BuildNotiLandItems();
        if (settings is null)
        {
            return;
        }

        PageNotiLand.Child = settings.Element;
        if (same)
        {
            settings.Refresh();
            if (keep >= 0)
            {
                ShowNotiLandPage(keep);
            }
        }
    }

    /// <summary>Chooses one of NotiLand's pages (a request from NotiLand itself): the one numbered, or the first.</summary>
    internal void ShowNotiLandPage(int number)
    {
        SetNotiLandExpanded(true);
        var target = _notiItems.FirstOrDefault(item => item.Tag is NotiLandPage page && page.Number == number)
            ?? _notiItems.FirstOrDefault(item => item.Tag is NotiLandPage);
        if (target is not null)
        {
            Pages.SelectedItem = target;
        }
    }

    /// <summary>Whether NotiLand's pages are shown below it in the list.</summary>
    internal bool NotiLandExpanded => _notiExpanded;

    /// <summary>Shows NotiLand's pages below it in the list, or folds them away (the chosen one, if among them, gives way to NotiLand itself).</summary>
    internal void SetNotiLandExpanded(bool expanded)
    {
        _notiExpanded = expanded;
        NotiLandChevron.Text = expanded ? "" : "";
        foreach (var item in _notiItems)
        {
            item.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!expanded && Pages.SelectedItem is ListBoxItem { Tag: NotiLandPage })
        {
            Pages.SelectedItem = PageIslandItem;
        }
    }

    private void OnNotiLandChevron(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetNotiLandExpanded(!_notiExpanded);
    }

    // Left folds NotiLand's pages away and Right shows them again, on the NotiLand entry.
    private void OnPagesKeyDown(object sender, KeyEventArgs e)
    {
        if (Pages.SelectedItem != PageIslandItem || _noti is null || e.Key is not (System.Windows.Input.Key.Left or System.Windows.Input.Key.Right))
        {
            return;
        }

        e.Handled = true;
        SetNotiLandExpanded(e.Key == System.Windows.Input.Key.Right);
    }

    // The entries for NotiLand's pages, each group of them under a caption, between "NotiLand" and "About".
    private void BuildNotiLandItems()
    {
        foreach (var item in _notiItems)
        {
            Pages.Items.Remove(item);
        }

        _notiItems.Clear();
        NotiLandChevron.Visibility = _noti is null ? Visibility.Collapsed : Visibility.Visible;
        if (_noti is null)
        {
            return;
        }

        var at = Pages.Items.IndexOf(PageIslandItem) + 1;
        foreach (var page in _noti.Pages)
        {
            if (page.Group.Length > 0)
            {
                Insert(new ListBoxItem
                {
                    Tag = page.Group,
                    Focusable = false,
                    IsHitTestVisible = false,
                    Padding = new Thickness(10, 12, 10, 2),
                    Content = new TextBlock { Text = page.Group, FontSize = 11, Opacity = 0.6, Margin = new Thickness(26, 0, 0, 0) },
                });
            }

            Insert(new ListBoxItem
            {
                Tag = page,
                Padding = new Thickness(10, 6, 10, 6),
                Content = new TextBlock { Text = page.Title, Margin = new Thickness(26, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
            });
        }

        void Insert(ListBoxItem item)
        {
            item.Visibility = _notiExpanded ? Visibility.Visible : Visibility.Collapsed;
            Pages.Items.Insert(at++, item);
            _notiItems.Add(item);
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

    private void OnAutoBackupSwitched(object sender, RoutedEventArgs e) => UpdateAutoBackupEnabled();

    private void UpdateAutoBackupEnabled() => AutoBackupOptions.IsEnabled = AutoBackupBox.IsChecked == true;

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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _host?.CloseSettingsPages();
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
        AutoBackupBox.IsChecked == true,
        AutoBackupHourBox.SelectedValue as int? ?? _config.AutoBackupHour,
        AutoBackupDaysBox.SelectedItem as int? ?? _config.AutoBackupDays);

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
        }

        // NotiLand may have started or stopped, or changed language, with what was applied.
        RefreshNotiLandPages(languageChanged);
        return true;
    }
}
