using System.IO;
using System.Windows;
using System.Windows.Threading;
using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Folders;
using DeskGrid.Icons;
using DeskGrid.Panel;
using DeskGrid.Resources;
using DeskGrid.Settings;
using DeskGrid.Shell;
using DeskGrid.Taskbar;
using DeskGrid.Updates;
using DeskGrid.Tray;
using DeskGrid.Widgets;

namespace DeskGrid;

public partial class App : Application
{
    // Per sign-in session: a second launch (e.g. auto-start plus a manual start) just exits.
    private const string SingleInstanceName = @"Local\DeskGrid.SingleInstance";

    private static readonly TimeSpan StartupUpdateCheckDelay = TimeSpan.FromSeconds(8);

    // How long a start at sign-in waits for Explorer to lay out the desktop icons before going ahead without.
    private static readonly TimeSpan DesktopSettleTimeout = TimeSpan.FromSeconds(45);

    private readonly CancellationTokenSource _exiting = new();
    private Mutex? _singleInstance;
    private ConfigStore? _configStore;
    private AutoStart? _autoStart;
    private DesktopHost? _desktopHost;
    private IconService? _icons;
    private PanelController? _panels;
    private TrayIcon? _tray;
    private WidgetBoard? _board;
    private WidgetCatalog? _catalog;
    private SettingsWindow? _settings;
    private InstanceChannel? _instanceChannel;
    private string? _pendingOpen;
    private bool _checkingUpdates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Info("DeskGrid is already running; this instance exits.");
            if (OpenRequest.Parse(e.Args) is { } folderName)
            {
                InstanceChannel.Send(folderName);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        Log.Info("DeskGrid starting.");

        _configStore = ConfigStore.Open(ConfigStore.DefaultFilePath);
        EnsureUsableRootFolder(_configStore);
        _autoStart = AutoStart.ForCurrentUser();
        _autoStart.UpdatePath();

        _desktopHost = new DesktopHost();
        _desktopHost.IconsChanged += (_, _) => _board?.ClearIconsOffWidgets();
        _desktopHost.Start();
        _icons = new IconService();
        _panels = new PanelController(_configStore, _icons);
        _panels.ItemReordered += (_, e) =>
        {
            if (e.FolderName == ItemReorderedEventArgs.FolderList)
            {
                _catalog?.MoveFolder(e.ItemName, e.Index);
            }
            else
            {
                _catalog?.MoveItem(e.FolderName, e.ItemName, e.Index);
            }
        };
        _panels.ItemsDropped += (_, e) => AddDroppedItems(e);
        _panels.NewFolderRequested += (_, _) => CreateFolder();
        _panels.DeleteRequested += (_, folder) => DeleteFolder(folder);
        _panels.FolderRenamer = RenameFolder;
        _tray = new TrayIcon(Strings.AppName, TrayMenu);
        _pendingOpen = OpenRequest.Parse(e.Args);
        _instanceChannel = new InstanceChannel(name => Dispatcher.BeginInvoke(() => OpenPanel(name)));
        _ = StartWidgetsAsync(startedByWindows: AutoStart.WasStartedByWindows(e.Args));
        _ = CheckForUpdatesAtStartupAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting.Cancel();
        _settings?.Close();
        _instanceChannel?.Dispose();
        StopWidgets();
        _tray?.Dispose();
        _icons?.Dispose();
        _desktopHost?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("DeskGrid exited.");
        base.OnExit(e);
    }

    private static void EnsureUsableRootFolder(ConfigStore configStore)
    {
        var config = configStore.Config;
        if (RootFolder.Prepare(config.RootPath) is not { } problem)
        {
            return;
        }

        var fallback = AppConfig.DefaultRootPath;
        if (RootFolder.Prepare(fallback) is { } fallbackProblem)
        {
            throw new InvalidOperationException($"Root folder \"{config.RootPath}\" is unusable ({problem}), and so is the default \"{fallback}\" ({fallbackProblem}).");
        }

        Log.Info($"Root folder \"{config.RootPath}\" rejected ({problem}); falling back to \"{fallback}\".");
        MessageBox.Show(string.Format(Strings.RootFolderRejected, config.RootPath, problem, fallback),
            Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        config.RootPath = fallback;
        configStore.Save();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        Log.Error("Unhandled exception", e.Exception);

    private IReadOnlyList<TrayMenuItem> TrayMenu() =>
    [
        new(Strings.TrayNewFolder, CreateFolder),
        new(Strings.TraySettings, ShowSettings),
        TrayMenuItem.Separator,
        new(Strings.TrayAutoStart, ToggleAutoStart, _autoStart!.IsEnabled),
        TrayMenuItem.Separator,
        new(Strings.MenuExit, Shutdown),
    ];

    private void ToggleAutoStart() => ReportAutoStart(_autoStart!.SetEnabled(!_autoStart.IsEnabled));

    private static void ReportAutoStart(bool changed)
    {
        if (!changed)
        {
            MessageBox.Show(Strings.AutoStartFailed, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // The first time, once the widgets are up: should DeskGrid start with Windows? Whatever the answer, not asked again.
    private void OfferAutoStart()
    {
        var config = _configStore!.Config;
        if (config.AutoStartAsked)
        {
            return;
        }

        config.AutoStartAsked = true;
        _configStore.Save();
        if (!_autoStart!.IsEnabled
            && MessageBox.Show(Strings.AutoStartOffer, Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ReportAutoStart(_autoStart.SetEnabled(true));
        }
    }

    // At sign-in Explorer is still putting the desktop together: wait for its icons to settle before placing widgets
    // on the same grid. A start by the user finds the desktop ready.
    private async Task StartWidgetsAsync(bool startedByWindows)
    {
        if (startedByWindows)
        {
            try
            {
                var settled = await DesktopHost.WaitUntilSettledAsync(DesktopSettleTimeout, _exiting.Token);
                Log.Info(settled ? "Started at sign-in; the desktop has settled." : "Started at sign-in; the desktop did not settle in time, going ahead.");
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Nobody awaits this method: a failure here has to be logged, or it vanishes with the task.
        try
        {
            StartWidgets();
            _ = Dispatcher.BeginInvoke(OfferAutoStart, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception exception)
        {
            Log.Error("Starting the widgets failed", exception);
            throw;
        }
    }

    // The widgets of the current root folder; rebuilt when the root changes.
    private void StartWidgets()
    {
        var board = new WidgetBoard(_desktopHost!, _configStore!, _icons!);
        board.WidgetClicked += (_, click) => _panels!.Toggle(click.Folder, click.Bounds);
        board.DragHovered += (_, hover) => _panels!.OpenForDrag(hover.Folder, hover.Bounds);
        board.WidgetDragStarted += (_, _) => _panels!.Close(); // The panel would stay where the widget was.
        board.PinRequested += (_, folder) => _ = PinToTaskbarAsync(folder);
        board.NewFolderRequested += (_, _) => CreateFolder();
        board.DeleteRequested += (_, folder) => DeleteFolder(folder);

        var catalog = new WidgetCatalog(_configStore!);
        catalog.Changed += (_, _) =>
        {
            _icons!.Retain(catalog.Widgets);
            board.Show(catalog.Widgets);
            _panels!.Refresh(catalog.Widgets);

            if (_pendingOpen is { } name)
            {
                _pendingOpen = null;
                OpenPanel(name);
            }
        };

        (_board, _catalog) = (board, catalog);
        catalog.Start();
    }

    private void StopWidgets()
    {
        _panels?.Close();
        _catalog?.Dispose();
        _board?.Dispose();
        (_board, _catalog) = (null, null);
    }

    // A taskbar button was clicked: show that folder's panel, or the list of all folders, above it.
    private void OpenPanel(string folderName)
    {
        if (folderName == OpenRequest.AllFolders)
        {
            if (_catalog is { } catalog)
            {
                _panels!.ToggleListAtCursor(catalog.Widgets);
            }
        }
        else if (_catalog?.Widgets.FirstOrDefault(w => string.Equals(w.Name, folderName, StringComparison.OrdinalIgnoreCase)) is { } folder)
        {
            _panels!.ToggleAtCursor(folder);
        }
        else
        {
            Log.Info($"Taskbar asked for \"{folderName}\", which is not a widget folder.");
        }
    }

    // Items were dropped on an open panel: add them to its folder and put them where they were dropped.
    private void AddDroppedItems(ItemsDroppedEventArgs drop)
    {
        var config = _configStore!.Config;
        if (_catalog?.Widgets.FirstOrDefault(w => string.Equals(w.Name, drop.FolderName, StringComparison.OrdinalIgnoreCase)) is not { } folder)
        {
            return;
        }

        var added = ItemImporter.ImportTo(drop.Paths, folder.FullPath, config.DropMode, FolderPaths.Desktops, config.RootPath);
        for (var i = 0; i < added.Count; i++)
        {
            _catalog.PlaceNewItem(folder.Name, Path.GetFileName(added[i]), drop.Index + i);
        }
    }

    // Renames a widget folder on disk; the scanner moves its settings along. Returns why not, or null.
    private string? RenameFolder(string oldName, string newName)
    {
        var root = _configStore!.Config.RootPath;
        var caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
        if (WidgetFolderName.Validate(root, newName) is { } problem && !(caseOnly && problem == Strings.NewFolderExists))
        {
            return problem;
        }

        try
        {
            var from = Path.Combine(root, oldName);
            var to = Path.Combine(root, newName);
            if (caseOnly)
            {
                // A move onto the same name (ignoring case) is refused, so go through a temporary name.
                var temporary = Path.Combine(root, $"{newName}.{Guid.NewGuid():N}.tmp");
                Directory.Move(from, temporary);
                Directory.Move(temporary, to);
            }
            else
            {
                Directory.Move(from, to);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Renaming \"{oldName}\" to \"{newName}\" failed", exception);
            return string.Format(Strings.RenameFailed, exception.Message);
        }
    }

    // Asks, then deletes a widget's folder (the Recycle Bin keeps it); the scanner takes the widget down.
    private void DeleteFolder(WidgetFolder folder)
    {
        var mode = _configStore!.Config.DeleteMode;
        var count = FolderDeleter.Entries(folder.FullPath).Count;
        var question = count == 0 ? Strings.DeleteConfirmEmpty
            : mode == DeleteMode.MoveToDesktop ? Strings.DeleteConfirmMove
            : Strings.DeleteConfirmItems;
        if (MessageBox.Show(string.Format(question, folder.Name, count), Strings.AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (FolderDeleter.Delete(folder.FullPath, mode, desktop, RecycleBin.Send) is { } problem)
        {
            MessageBox.Show(problem, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        PinShortcut.Remove(folder.Name);
    }

    private async Task PinToTaskbarAsync(WidgetFolder folder)
    {
        try
        {
            var shortcut = PinShortcut.Create(folder.Name, await _icons!.GetPreviewAsync(folder));
            ShellItem.Reveal(shortcut);
            MessageBox.Show(string.Format(Strings.PinCreated, folder.Name), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Log.Error($"Creating the taskbar shortcut for \"{folder.Name}\" failed", exception);
            MessageBox.Show(string.Format(Strings.PinFailed, exception.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinAllFoldersToTaskbar()
    {
        try
        {
            ShellItem.Reveal(PinShortcut.CreateForAllFolders(Strings.AppName));
            MessageBox.Show(string.Format(Strings.PinCreated, Strings.AppName), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Log.Error("Creating the taskbar shortcut for all folders failed", exception);
            MessageBox.Show(string.Format(Strings.PinFailed, exception.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // A few seconds after start, so it doesn't compete with the desktop coming up. Quiet unless there is news.
    // Skipped, with no network use at all, when the user turned the startup check off in Settings.
    private async Task CheckForUpdatesAtStartupAsync()
    {
        await Task.Delay(StartupUpdateCheckDelay);
        if (_configStore!.Config.CheckForUpdatesAtStartup)
        {
            await CheckForUpdatesAsync(manual: false, owner: null);
        }
    }

    // Asks GitHub for the latest release and, if it is newer than this one, offers the update.
    // A manual check also offers a release the user chose to keep the current version over.
    // Returns what the settings window shows next to its button.
    private async Task<string> CheckForUpdatesAsync(bool manual, Window? owner)
    {
        if (_checkingUpdates)
        {
            return Strings.SettingsChecking;
        }

        _checkingUpdates = true;
        try
        {
            if (await UpdateChecker.GetLatestAsync() is not { } latest)
            {
                return Strings.SettingsCheckFailed;
            }

            Log.Info($"Update check: latest release {latest.Tag}, running {UpdateChecker.CurrentVersion.ToString(3)}.");
            if (!UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion))
            {
                return Strings.SettingsUpToDate;
            }

            var config = _configStore!.Config;
            if (!manual && config.SkippedUpdateVersion == latest.Tag)
            {
                return "";
            }

            return OfferUpdate(latest, owner);
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    private string OfferUpdate(ReleaseInfo latest, Window? owner)
    {
        var replacing = false;
        var dialog = new UpdateDialog(latest, async progress =>
        {
            if (!UpdateInstaller.CanReplaceSelf)
            {
                // A development build can't replace itself; the release page is opened below.
                return null;
            }

            var (path, error) = await UpdateInstaller.DownloadAsync(latest, progress);
            if (path is null)
            {
                return error;
            }

            progress.Report(new UpdateProgress(UpdateStage.Installing));
            var problem = UpdateInstaller.StartReplacement(path);
            replacing = problem is null;
            return problem;
        });
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        switch (dialog.Choice)
        {
            case UpdateChoice.Keep:
                _configStore!.Config.SkippedUpdateVersion = latest.Tag;
                _configStore.Save();
                break;

            case UpdateChoice.Update when replacing:
                // The helper script waits for this process to exit, swaps the exe and starts it again.
                Shutdown();
                break;

            case UpdateChoice.Update:
                ShellItem.Open(latest.PageUrl);
                break;
        }

        return string.Format(Strings.SettingsUpdateAvailable, latest.Version.ToString(3));
    }

    private void CreateFolder()
    {
        var rootPath = _configStore!.Config.RootPath;
        var dialog = new NewFolderDialog(rootPath);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // The watcher turns it into a widget.
            Directory.CreateDirectory(Path.Combine(rootPath, dialog.FolderName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Creating a widget folder failed", exception);
            MessageBox.Show(exception.Message, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSettings()
    {
        if (_settings is { } open)
        {
            open.Activate();
            return;
        }

        var settings = new SettingsWindow(_configStore!.Config);
        settings.Applied += (_, choice) => ApplySettings(choice);
        settings.CheckForUpdates = () => CheckForUpdatesAsync(manual: true, settings);
        settings.PinAllFolders = PinAllFoldersToTaskbar;
        settings.Closed += (_, _) => _settings = null;
        _settings = settings;
        settings.Show();
        settings.Activate();
    }

    private void ApplySettings(SettingsChoice choice)
    {
        var config = _configStore!.Config;
        var rootChanged = !string.Equals(Path.GetFullPath(choice.RootPath), Path.GetFullPath(config.RootPath), StringComparison.OrdinalIgnoreCase);
        if (rootChanged)
        {
            StopWidgets();
        }

        config.RootPath = choice.RootPath;
        config.PanelColumns = choice.PanelColumns;
        config.DropMode = choice.DropMode;
        config.DeleteMode = choice.DeleteMode;
        config.PanelBackground = choice.PanelBackground;
        config.CheckForUpdatesAtStartup = choice.CheckForUpdatesAtStartup;
        _configStore.Save();

        if (rootChanged)
        {
            StartWidgets();
        }
        else
        {
            // Panels read their look when they open.
            _panels!.Close();
        }
    }
}
