using System.IO;
using System.Windows;
using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Folders;
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
    bool AnimatePanelOpening,
    bool CheckForUpdatesAtStartup);

/// <summary>Edits a copy of the settings; <see cref="Applied"/> fires on OK with a validated choice.</summary>
internal partial class SettingsWindow : Window
{
    private const int MaxColumns = 8;

    private Color _customColor;

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        LocalizeTexts();
        FluentWindow.UseOpaqueBackground(this);

        // On a small screen the cards scroll instead of the window running off it.
        MaxHeight = SystemParameters.WorkArea.Height;

        RootBox.Text = config.RootPath;
        ColumnsBox.ItemsSource = Enumerable.Range(1, MaxColumns).ToList();
        ColumnsBox.SelectedItem = Math.Clamp(config.PanelColumns, 1, MaxColumns);
        (config.DropMode == DropMode.Copy ? DropCopy : DropMove).IsChecked = true;
        (config.DeleteMode == DeleteMode.DeleteItems ? DeleteItems : DeleteMove).IsChecked = true;

        var background = config.PanelBackground;
        BackgroundRadio(background.Style).IsChecked = true;
        _customColor = PanelTheme.ParseColor(background.CustomColor) ?? PanelTheme.ParseColor(new PanelBackground().CustomColor)!.Value;
        ColorSwatch.Background = new SolidColorBrush(_customColor);
        OpacitySlider.Value = Math.Clamp(background.CustomOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        AnimateOpeningBox.IsChecked = config.AnimatePanelOpening;
        CheckAtStartupBox.IsChecked = config.CheckForUpdatesAtStartup;
    }

    public event EventHandler<SettingsChoice>? Applied;

    /// <summary>Checks for a newer release (showing the update dialog if there is one); returns what to tell the user, if anything.</summary>
    public Func<Task<string>>? CheckForUpdates { get; set; }

    /// <summary>Creates the taskbar shortcut that lists all folders and shows it to the user, who pins it.</summary>
    public Action? PinAllFolders { get; set; }

    private void OnPinAll(object sender, RoutedEventArgs e) => PinAllFolders?.Invoke();

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

    private PanelBackgroundStyle SelectedBackground =>
        Enum.GetValues<PanelBackgroundStyle>().First(style => BackgroundRadio(style).IsChecked == true);

    private System.Windows.Controls.RadioButton BackgroundRadio(PanelBackgroundStyle style) => style switch
    {
        PanelBackgroundStyle.Dark => BackgroundDark,
        PanelBackgroundStyle.Light => BackgroundLight,
        PanelBackgroundStyle.Custom => BackgroundCustom,
        _ => BackgroundSystem,
    };

    private void LocalizeTexts()
    {
        Title = Strings.SettingsTitle;
        RootLabel.Text = Strings.SettingsRootFolder;
        BrowseButton.Content = Strings.SettingsBrowse;
        OpenRootButton.Content = Strings.SettingsOpen;
        TaskbarLabel.Text = Strings.SettingsTaskbar;
        PinAllButton.Content = Strings.SettingsPinAll;
        GitHubLabel.Text = Strings.SettingsGitHub;

        // Without the scheme: shorter, and it is still plainly the web address.
        GitHubLink.Inlines.Add(UpdateChecker.RepositoryUrl["https://".Length..]);
        PrivacyLink.Inlines.Add(Strings.SettingsPrivacy);
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
        BackgroundCustom.Content = Strings.SettingsBackgroundCustom;
        ColorButton.Content = Strings.SettingsColor;
        OpacityLabel.Text = Strings.SettingsOpacity;
        AnimateOpeningBox.Content = Strings.SettingsAnimateOpening;
        VersionLabel.Text = Strings.SettingsVersion;
        VersionText.Text = UpdateChecker.CurrentVersion.ToString(3);
        CheckUpdatesButton.Content = Strings.SettingsCheckUpdates;
        CheckAtStartupBox.Content = Strings.SettingsCheckAtStartup;
        OkButton.Content = Strings.ButtonOk;
        CancelButton.Content = Strings.ButtonCancel;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = RootBox.Text };
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

        var background = new PanelBackground
        {
            Style = SelectedBackground,
            CustomColor = $"#{_customColor.R:X2}{_customColor.G:X2}{_customColor.B:X2}",
            CustomOpacity = Math.Round(OpacitySlider.Value, 2),
        };
        Applied?.Invoke(this, new SettingsChoice(
            rootPath,
            (int)ColumnsBox.SelectedItem,
            DropCopy.IsChecked == true ? DropMode.Copy : DropMode.Move,
            DeleteItems.IsChecked == true ? DeleteMode.DeleteItems : DeleteMode.MoveToDesktop,
            background,
            AnimateOpeningBox.IsChecked == true,
            CheckAtStartupBox.IsChecked == true));
        Close();
    }
}
