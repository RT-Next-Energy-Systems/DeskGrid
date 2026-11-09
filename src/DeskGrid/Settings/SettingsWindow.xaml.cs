using System.IO;
using System.Windows;
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
    bool AllowControl);

/// <summary>One line of the language list: the language and the name it is shown under.</summary>
internal sealed record LanguageOption(AppLanguage Language, string Label);

/// <summary>Edits a copy of the settings; <see cref="Applied"/> fires on OK with a validated choice.</summary>
internal partial class SettingsWindow : Window
{
    private const int MaxColumns = 8;

    private readonly AppConfig _config;
    private Color _customColor;
    private PanelBackgroundStyle _shownStyle; // The style whose opacity the slider is showing.

    public SettingsWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        LocalizeTexts();
        FluentWindow.UseOpaqueBackground(this);

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
        UpdateOpacityHint();
        foreach (var style in Enum.GetValues<PanelBackgroundStyle>())
        {
            BackgroundRadioFor(style).Checked += OnBackgroundChanged;
        }

        CheckAtStartupBox.IsChecked = config.CheckForUpdatesAtStartup;
        AllowControlBox.IsChecked = config.AllowControl;
    }

    public event EventHandler<SettingsChoice>? Applied;

    /// <summary>Checks for a newer release (showing the update dialog if there is one); returns what to tell the user, if anything.</summary>
    public Func<Task<string>>? CheckForUpdates { get; set; }

    /// <summary>Creates the taskbar shortcut that lists all folders and shows it to the user, who pins it.</summary>
    public Action? PinAllFolders { get; set; }

    /// <summary>Lets the user change the keyboard shortcuts (and applies them); the window then shows the new one.</summary>
    public Action? EditHotkeys { get; set; }

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
        OpenModeHint.Text = Strings.SettingsOpenModeHint;
        RootLabel.Text = Strings.SettingsRootFolder;
        BrowseButton.Content = Strings.SettingsBrowse;
        OpenRootButton.Content = Strings.SettingsOpen;
        TaskbarLabel.Text = Strings.SettingsTaskbar;
        PinAllButton.Content = Strings.SettingsPinAll;
        HotkeysLabel.Text = Strings.SettingsHotkeys;
        HotkeysButton.Content = Strings.SettingsHotkeysEdit;
        ShowHotkeysHint();
        AllowControlBox.Content = Strings.SettingsAllowControl;
        AllowControlHint.Text = Strings.SettingsAllowControlHint;
        GitHubLabel.Text = Strings.SettingsGitHub;

        // Only owner/name: the label already says GitHub, and the whole address would not fit beside the button.
        GitHubLink.Inlines.Add(new Uri(UpdateChecker.RepositoryUrl).AbsolutePath.Trim('/'));
        GitHubLink.ToolTip = UpdateChecker.RepositoryUrl;
        PrivacyLink.Inlines.Add(Strings.SettingsPrivacy);
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
        VersionLabel.Text = Strings.SettingsVersion;
        VersionText.Text = UpdateChecker.CurrentVersion.ToString(3);
        CheckUpdatesButton.Content = Strings.SettingsCheckUpdates;
        CheckAtStartupBox.Content = Strings.SettingsCheckAtStartup;
        OkButton.Content = Strings.ButtonOk;
        CancelButton.Content = Strings.ButtonCancel;
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
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var rootPath = RootBox.Text.Trim();
        if (RootFolder.Prepare(rootPath) is { } problem)
        {
            Error.Text = string.Format(Strings.SettingsRootInvalid, problem);
            Error.Visibility = Visibility.Visible;
            return;
        }

        var style = SelectedBackground;
        var opacity = Math.Round(OpacitySlider.Value, 2);
        var background = new PanelBackground
        {
            Style = style,
            CustomColor = $"#{_customColor.R:X2}{_customColor.G:X2}{_customColor.B:X2}",
            CustomOpacity = style == PanelBackgroundStyle.Custom ? opacity : _config.PanelBackground.CustomOpacity,
            // Left unset at the style's own default, so the system style stays the acrylic.
            Opacity = style == PanelBackgroundStyle.Custom || SameOpacity(opacity, PanelTheme.DefaultOpacity(style)) ? null : opacity,
        };
        Applied?.Invoke(this, new SettingsChoice(
            rootPath,
            (int)ColumnsBox.SelectedItem,
            DropCopy.IsChecked == true ? DropMode.Copy : DropMode.Move,
            DeleteItems.IsChecked == true ? DeleteMode.DeleteItems : DeleteMode.MoveToDesktop,
            background,
            OpenCentered.IsChecked == true ? PanelOpenMode.Centered : PanelOpenMode.Nearby,
            (AppLanguage)LanguageBox.SelectedValue,
            CheckAtStartupBox.IsChecked == true,
            AllowControlBox.IsChecked == true));
        Close();
    }
}
