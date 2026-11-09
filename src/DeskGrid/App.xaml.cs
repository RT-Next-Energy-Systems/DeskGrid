using System.IO;
using System.Windows;
using System.Windows.Threading;
using DeskGrid.Config;
using DeskGrid.Control;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Folders;
using DeskGrid.Hotkeys;
using DeskGrid.Icons;
using DeskGrid.Panel;
using DeskGrid.Resources;
using DeskGrid.Settings;
using DeskGrid.Shell;
using DeskGrid.Taskbar;
using DeskGrid.Updates;
using DeskGrid.Tray;
using DeskGrid.Widgets;
using Windows.Win32;

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
    private ControlChannel? _controlChannel;
    private HotkeyService? _hotkeys;
    private OpenMessage? _pendingOpen;
    private bool _checkingUpdates;
    private bool _runningHotkey;
    private bool _started;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (ControlCli.IsRequest(e.Args))
        {
            // A command for the running DeskGrid (from a script or an AI agent): send it, print the answer, exit.
            using var output = ControlCli.OpenOutput();
            Shutdown(ControlCli.Run(e.Args, output));
            return;
        }

        var openRequest = OpenRequest.Parse(e.Args) is { } requested ? OpenMessageAtCursor(requested) : (OpenMessage?)null;
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Info("DeskGrid is already running; this instance exits.");
            if (openRequest is { } request)
            {
                InstanceChannel.Send(request);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception on a background thread", (Exception)args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Error("Unobserved task exception", args.Exception);
        Log.Info("DeskGrid starting.");

        _configStore = ConfigStore.Open(ConfigStore.DefaultFilePath);
        UiLanguage.Apply(_configStore.Config.Language);
        EnsureUsableRootFolder(_configStore);
        _autoStart = AutoStart.ForCurrentUser();
        _autoStart.UpdatePath();

        _desktopHost = new DesktopHost();
        _desktopHost.IconsChanged += (_, _) => _board?.ClearIconsOffWidgets();
        _desktopHost.DisplayChanged += (_, _) =>
        {
            _panels?.Close(); // Its widget may be on another monitor now.
            _board?.OnDisplayChanged();
        };
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
        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += (_, id) => RunHotkey(id);
        ApplyHotkeys(report: false);
        _pendingOpen = openRequest;
        _instanceChannel = new InstanceChannel(message => Dispatcher.BeginInvoke(() => OpenPanel(message)));
        var commands = new ControlCommands(new ControlHost(this), FolderPaths.Desktops);
        _controlChannel = new ControlChannel(request => Dispatcher.InvokeAsync(() => commands.Execute(request)).Task);
        _ = StartWidgetsAsync(startedByWindows: AutoStart.WasStartedByWindows(e.Args));
        _ = CheckForUpdatesAtStartupAsync();
        _started = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting.Cancel();
        _settings?.CloseDiscarding(); // Exiting: no question to ask.
        _instanceChannel?.Dispose();
        _controlChannel?.Dispose();
        _hotkeys?.Dispose();
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

    // Once running, a failure in one handler (a drag, a menu command, a timer) is logged and the app carries on: ending
    // the process would take every widget off the desktop. Before that there is nothing to carry on with.
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);
        e.Handled = _started && e.Exception is not OutOfMemoryException;
    }

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

    // Once the widgets are up: the first-run question, then what's new if the version changed (after an update).
    private void AfterFirstShow()
    {
        var firstRun = !_configStore!.Config.AutoStartAsked;
        OfferAutoStart();
        ShowWhatsNew(firstRun);
    }

    // After an update, shows what changed since the last version the user saw (and how to use it), in the interface
    // language. A first run has nothing to compare with, so it only records the version. Settings from before the
    // version was recorded count as updated from an older one.
    private void ShowWhatsNew(bool firstRun)
    {
        var config = _configStore!.Config;
        var current = UpdateChecker.CurrentVersion;
        _ = Version.TryParse(config.LastSeenVersion, out var seen);
        if (seen == current)
        {
            return;
        }

        config.LastSeenVersion = current.ToString(3);
        _configStore.Save(); // Before the window: a crash must not show it again at every start.
        if (!firstRun && ChangeLogWindow.HasNews(seen, current))
        {
            new ChangeLogWindow(seen, current).ShowDialog();
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
            catch (Exception exception)
            {
                // Reading the desktop's icons failed: place the widgets anyway rather than never showing them.
                Log.Error("Waiting for the desktop at sign-in failed; going ahead", exception);
            }
        }

        // Nobody awaits this method: a failure here has to be logged, or it vanishes with the task.
        try
        {
            StartWidgets();
            _ = Dispatcher.BeginInvoke(AfterFirstShow, DispatcherPriority.ApplicationIdle);
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
            ApplyHotkeys(report: false); // A renamed folder keeps its shortcut under the new name; a deleted one loses it.

            if (_pendingOpen is { } request)
            {
                _pendingOpen = null;
                OpenPanel(request);
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

    // The cursor is read at once: it is where the user clicked the taskbar button, not where it is a second later.
    private static OpenMessage OpenMessageAtCursor(string name)
    {
        PInvoke.GetCursorPos(out var cursor);
        return new OpenMessage(name, cursor.X, cursor.Y);
    }

    // A taskbar button was clicked: show that folder's panel, or the list of all folders, where it was clicked.
    private void OpenPanel(OpenMessage request)
    {
        if (_catalog is not { } catalog)
        {
            // The widgets aren't up yet (a start at sign-in waits for the desktop): opened once the folders are read.
            _pendingOpen = request;
            return;
        }

        var at = new ScreenRect(request.CursorX, request.CursorY, request.CursorX + 1, request.CursorY + 1);
        if (request.Name == OpenRequest.AllFolders)
        {
            _panels!.ToggleList(catalog.Widgets, at);
        }
        else if (catalog.Widgets.FirstOrDefault(w => string.Equals(w.Name, request.Name, StringComparison.OrdinalIgnoreCase)) is { } folder)
        {
            _panels!.ToggleFolderFromTaskbar(folder, at);
        }
        else
        {
            // A button pinned for a folder that has been deleted: show the folders there are instead of doing nothing.
            Log.Info($"Taskbar asked for \"{request.Name}\", which is not a widget folder; showing all folders.");
            _panels!.ToggleList(catalog.Widgets, at);
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

            // At once, not when the file watcher reports it: a rescan in between (the control pipe does one) would otherwise
            // find the new name without the old settings and start it afresh.
            _catalog?.MoveSettings(oldName, newName);
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
        if (!_configStore!.Config.CheckForUpdatesAtStartup)
        {
            return;
        }

        // Nobody awaits this method: a failure has to be logged, or it vanishes with the task.
        try
        {
            await CheckForUpdatesAsync(manual: false, owner: null);
        }
        catch (Exception exception)
        {
            Log.Error("The startup update check failed", exception);
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

    // Keeps the shortcuts Windows knows in step with the config. Quiet at start-up and after a rescan: a shortcut another
    // program owns is only logged there, and reported when the user changes shortcuts.
    private void ApplyHotkeys(bool report)
    {
        var failed = _hotkeys!.Apply(HotkeyBindings.All(_configStore!.Config));
        if (report && failed.Count > 0)
        {
            var list = string.Join("\n", failed.Select(binding => $"{binding.Key}  –  {HotkeysWindow.LabelOf(binding.Id)}"));
            MessageBox.Show(string.Format(Strings.HotkeysTaken, list), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RunHotkey(string id)
    {
        // A second press while a dialog from the first is still open would stack another dialog on it.
        if (_runningHotkey)
        {
            return;
        }

        _runningHotkey = true;
        Log.Info($"Shortcut pressed: {id}");
        try
        {
            switch (id)
            {
                case HotkeyBindings.NewFolder:
                    CreateFolder();
                    break;

                case HotkeyBindings.Settings:
                    ShowSettings();
                    break;

                case HotkeyBindings.AllFolders:
                    OpenPanel(OpenMessageAtCursor(OpenRequest.AllFolders));
                    break;

                default:
                    if (HotkeyBindings.FolderNameOf(id) is { } folder)
                    {
                        OpenPanel(OpenMessageAtCursor(folder));
                    }

                    break;
            }
        }
        finally
        {
            _runningHotkey = false;
        }
    }

    private void EditHotkeys(Window? owner)
    {
        var config = _configStore!.Config;
        var window = new HotkeysWindow(config, _catalog?.Widgets.Select(widget => widget.Name).ToList() ?? []);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        var changed = false;
        window.Applied += (_, choices) =>
        {
            foreach (var choice in choices)
            {
                HotkeyBindings.Set(config, choice.Id, choice.Key);
            }

            _configStore.Save();
            changed = true;
        };

        // The shortcuts in force would swallow the very keys being recorded.
        _hotkeys!.Suspend();
        try
        {
            window.ShowDialog();
        }
        finally
        {
            ApplyHotkeys(report: changed);
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
        settings.EditHotkeys = () => EditHotkeys(settings);
        settings.ExportSettings = () => ExportSettings(settings);
        settings.ImportSettings = () => ImportSettings(settings);
        settings.Closed += (_, _) => _settings = null;
        _settings = settings;
        settings.Show();
        settings.Activate();
    }

    private void ExportSettings(Window owner)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = ConfigPorter.SuggestedFileName(DateTime.Now),
            DefaultExt = ConfigPorter.Extension,
            Filter = Strings.PortFileFilter,
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            ConfigPorter.Export(_configStore!.Config, dialog.FileName);
            MessageBox.Show(owner, string.Format(Strings.ExportDone, dialog.FileName), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Exporting the settings to {dialog.FileName} failed", exception);
            MessageBox.Show(owner, string.Format(Strings.ExportFailed, exception.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Takes over a settings file: everything is replaced, so the widgets are rebuilt from the new folder order and positions.
    private void ImportSettings(SettingsWindow owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { DefaultExt = ConfigPorter.Extension, Filter = Strings.PortFileFilter };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        if (ConfigPorter.TryImport(dialog.FileName, out var problem) is not { } imported)
        {
            MessageBox.Show(owner, string.Format(Strings.ImportFailed, problem), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (MessageBox.Show(owner, string.Format(Strings.ImportConfirm, dialog.FileName), Strings.AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var config = _configStore!.Config;
        var note = "";
        if (RootFolder.Prepare(imported.RootPath) is { } rootProblem)
        {
            note = "\n\n" + string.Format(Strings.ImportRootKept, imported.RootPath, rootProblem);
            imported.RootPath = config.RootPath;
        }

        StopWidgets();
        config.ReplaceWith(imported);
        UiLanguage.Apply(config.Language);
        _configStore.Save();
        StartWidgets();
        ApplyHotkeys(report: true);
        owner.CloseDiscarding(); // It shows the settings that were just replaced.
        MessageBox.Show(Strings.ImportDone + note, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplySettings(SettingsChoice choice)
    {
        var config = _configStore!.Config;

        // Widgets that aren't up yet (a start at sign-in still waits for the desktop) start from the new root anyway;
        // starting them here as well would put up a second set.
        var rootChanged = _catalog is not null
            && !string.Equals(Path.GetFullPath(choice.RootPath), Path.GetFullPath(config.RootPath), StringComparison.OrdinalIgnoreCase);
        if (rootChanged)
        {
            StopWidgets();
        }

        config.RootPath = choice.RootPath;
        config.PanelColumns = choice.PanelColumns;
        config.DropMode = choice.DropMode;
        config.DeleteMode = choice.DeleteMode;
        config.PanelBackground = choice.PanelBackground;
        config.PanelOpenMode = choice.PanelOpenMode;
        config.Language = choice.Language;
        UiLanguage.Apply(config.Language);
        config.CheckForUpdatesAtStartup = choice.CheckForUpdatesAtStartup;
        config.AllowControl = choice.AllowControl;
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

    /// <summary>What the control pipe's commands act on: this app's folders, widgets and panel.</summary>
    private sealed class ControlHost(App app) : IControlHost
    {
        public AppConfig Config => app._configStore!.Config;

        public IReadOnlyList<WidgetFolder>? Widgets => app._catalog?.Widgets;

        public string? OpenPanel => app._panels?.OpenPanelName;

        public void Rescan() => app._catalog?.Rescan();

        public string? RenameFolder(string oldName, string newName) => app.RenameFolder(oldName, newName);

        public void MoveItem(string folderName, string itemName, int index) => app._catalog?.MoveItem(folderName, itemName, index);

        public void OpenFolder(WidgetFolder folder)
        {
            if (!string.Equals(OpenPanel, folder.Name, StringComparison.OrdinalIgnoreCase))
            {
                app._panels!.Toggle(folder, app._board?.BoundsOf(folder.Name) ?? AtCursor());
            }
        }

        public void OpenList()
        {
            if (OpenPanel != ControlCommands.AllFolders && app._catalog is { } catalog)
            {
                app._panels!.ToggleList(catalog.Widgets, AtCursor());
            }
        }

        public void ClosePanel() => app._panels?.Close();

        private static ScreenRect AtCursor()
        {
            PInvoke.GetCursorPos(out var cursor);
            return new ScreenRect(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1);
        }
    }
}
