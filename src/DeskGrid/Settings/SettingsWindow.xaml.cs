using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Folders;
using DeskGrid.Hotkeys;
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
    int UsageWarnPercent = 80);

/// <summary>One line of the language list: the language and the name it is shown under.</summary>
internal sealed record LanguageOption(AppLanguage Language, string Label);

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

        var background = config.PanelBackground;
        BackgroundRadioFor(background.Style).IsChecked = true;
        _shownStyle = background.Style;
        _customColor = PanelTheme.ParseColor(background.CustomColor) ?? PanelTheme.ParseColor(new PanelBackground().CustomColor)!.Value;
        ColorSwatch.Background = new SolidColorBrush(_customColor);
        OpacitySlider.Value = Math.Clamp(OpacityOf(background, background.Style), OpacitySlider.Minimum, OpacitySlider.Maximum);
        ShadeSlider.Value = Math.Clamp(background.GlassShade ?? PanelBackground.DefaultGlassShade, ShadeSlider.Minimum, ShadeSlider.Maximum);
        UpdateOpacityHint();
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
        UsageHoursBox.Text = config.UsageHours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UsageWarnBox.Text = config.UsageWarnPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IslandSecondsBox.Text = config.IslandShowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        choice.PanelBackground.Style, choice.PanelBackground.CustomColor, choice.PanelBackground.CustomOpacity,
        choice.PanelBackground.Opacity, choice.PanelBackground.GlassShade);

    /// <summary>The page shown on the right, in the order of the list on the left.</summary>
    internal enum Page
    {
        General,
        Panel,
        Shortcuts,
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

    /// <summary>Puts the island back at the top centre of the screen.</summary>
    public Action? ResetIslandPosition { get; set; }

    private void OnResetIsland(object sender, RoutedEventArgs e) => ResetIslandPosition?.Invoke();

    private void OnExport(object sender, RoutedEventArgs e) => ExportSettings?.Invoke();

    private void OnImport(object sender, RoutedEventArgs e) => ImportSettings?.Invoke();

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
        UpdateOpacityHint();
        RefreshPreview();
    }

    // Two styles need a word about what the slider does: the system one loses its blur, liquid glass is frosted by it.
    private void UpdateOpacityHint()
    {
        var hint = BackgroundSystem.IsChecked == true ? Strings.SettingsOpacityHint
            : BackgroundGlass.IsChecked == true ? Strings.SettingsOpacityHintGlass
            : null;
        OpacityHint.Text = hint;
        OpacityHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
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
        TaskbarLabel.Text = Strings.SettingsTaskbar;
        PinAllButton.Content = Strings.SettingsPinAll;
        HotkeysLabel.Text = Strings.SettingsHotkeys;
        HotkeysButton.Content = Strings.SettingsHotkeysEdit;
        ShowHotkeysHint();
        AllowControlBox.Content = Strings.SettingsAllowControl;
        AllowControlHint.Text = Strings.SettingsAllowControlHint;
        IslandBox.Content = Strings.SettingsIsland;
        IslandHint.Text = Strings.SettingsIslandHint;
        IslandMusicLabel.Text = Strings.SettingsIslandMusic;
        MusicOff.Content = Strings.SettingsIslandMusicOff;
        MusicOnChange.Content = Strings.SettingsIslandMusicChange;
        MusicWhilePlaying.Content = Strings.SettingsIslandMusicWhile;
        IslandFoldersBox.Content = Strings.SettingsIslandFolders;
        IslandBatteryBox.Content = Strings.SettingsIslandBattery;
        IslandUsageBox.Content = Strings.SettingsIslandUsage;
        IslandUsageHint.Text = Strings.SettingsIslandUsageHint;
        UsageLimitLabel.Text = Strings.SettingsUsageLimit;
        UsageHoursLabel.Text = Strings.SettingsUsageHours;
        UsageWarnLabel.Text = Strings.SettingsUsageWarn;
        IslandSecondsLabel.Text = Strings.SettingsIslandSeconds;
        IslandResetButton.Content = Strings.SettingsIslandReset;
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
        Math.Clamp(double.TryParse(UsageLimitBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var limit) ? limit : _config.UsageLimitMillions, 0.01, 100000),
        Number(UsageHoursBox, _config.UsageHours, 1, 720),
        Number(UsageWarnBox, _config.UsageWarnPercent, 1, 100));

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
        }

        return true;
    }
}
