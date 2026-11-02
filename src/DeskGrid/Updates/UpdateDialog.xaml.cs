using System.Windows;
using DeskGrid.Resources;
using DeskGrid.Settings;

namespace DeskGrid.Updates;

internal enum UpdateChoice
{
    /// <summary>Not now: ask again the next time DeskGrid starts.</summary>
    Later,

    /// <summary>The new version was downloaded and is ready to be put in place.</summary>
    Update,

    /// <summary>Stay on this version and stop asking about the new one.</summary>
    Keep,
}

/// <summary>Tells the user a newer release exists and lets them update, keep this version or decide later.</summary>
internal partial class UpdateDialog : Window
{
    private static readonly TimeSpan InstallingPause = TimeSpan.FromSeconds(1);

    private readonly Func<IProgress<UpdateProgress>, Task<string?>> _apply;
    private bool _downloading;

    /// <param name="apply">
    /// Downloads the new version and starts putting it in place, reporting each stage; returns why it failed,
    /// or <see langword="null"/>.
    /// </param>
    public UpdateDialog(ReleaseInfo release, Func<IProgress<UpdateProgress>, Task<string?>> apply)
    {
        InitializeComponent();
        FluentWindow.UseOpaqueBackground(this);
        _apply = apply;
        Title = Strings.UpdateTitle;
        Message.Text = string.Format(Strings.UpdateAvailable, release.Version.ToString(3), UpdateChecker.CurrentVersion.ToString(3));
        Hint.Text = Strings.UpdateHint;
        UpdateButton.Content = Strings.UpdateButtonUpdate;
        KeepButton.Content = Strings.UpdateButtonKeep;
        LaterButton.Content = Strings.UpdateButtonLater;
        ProgressText.Text = Strings.UpdateDownloading;
    }

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    // The download can't be cancelled, and its result has nowhere to go once the dialog is gone.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = _downloading;
        base.OnClosing(e);
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        Error.Visibility = Visibility.Collapsed;
        _downloading = true;
        UpdateButton.IsEnabled = KeepButton.IsEnabled = LaterButton.IsEnabled = false;
        ProgressArea.Visibility = Visibility.Visible;
        Show(new UpdateProgress(UpdateStage.Downloading));

        string? problem;
        try
        {
            problem = await _apply(new Progress<UpdateProgress>(Show));
        }
        catch (Exception exception)
        {
            // Shown in the dialog; left to escape from an async void handler it would leave the buttons disabled for good.
            Diagnostics.Log.Error("Applying the update failed", exception);
            problem = exception.Message;
        }
        finally
        {
            _downloading = false;
        }

        if (problem is null)
        {
            // Stays up a moment so the last stage is seen before the app exits.
            _downloading = true;
            Show(new UpdateProgress(UpdateStage.Installing));
            await Task.Delay(InstallingPause);
            _downloading = false;
            Choice = UpdateChoice.Update;
            DialogResult = true;
            return;
        }

        ProgressArea.Visibility = Visibility.Collapsed;
        Error.Text = string.Format(Strings.UpdateFailed, problem);
        Error.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = KeepButton.IsEnabled = LaterButton.IsEnabled = true;
    }

    private void Show(UpdateProgress state)
    {
        const double Megabyte = 1024 * 1024;
        Progress.IsIndeterminate = state.Stage != UpdateStage.Downloading || state.Total <= 0;
        Progress.Value = state.Total > 0 ? (double)state.Done / state.Total : 0;
        ProgressText.Text = state.Stage switch
        {
            UpdateStage.Verifying => Strings.UpdateVerifying,
            UpdateStage.Installing => Strings.UpdateInstalling,
            _ when state.Total > 0 => string.Format(
                Strings.UpdateDownloadingProgress, Progress.Value, state.Done / Megabyte, state.Total / Megabyte),
            _ => Strings.UpdateDownloading,
        };
    }

    private void OnKeep(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.Keep;
        DialogResult = true;
    }
}
