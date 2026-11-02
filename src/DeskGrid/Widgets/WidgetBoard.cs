using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Diagnostics;
using DeskGrid.Folders;
using DeskGrid.Icons;
using DeskGrid.Panel;
using DeskGrid.Shell;

namespace DeskGrid.Widgets;

/// <param name="Bounds">The widget's cell on screen, in physical pixels.</param>
internal sealed record WidgetClickedEventArgs(WidgetFolder Folder, ScreenRect Bounds);

/// <summary>
/// Shows one <see cref="FolderWidget"/> per widget folder on the desktop and keeps them in step with
/// the catalog. Every widget sits on its own <see cref="WidgetGrid"/> cell; positions are saved.
/// Files dropped on a widget are added to its folder by <see cref="ItemImporter"/>;
/// a desktop icon dropped on another one makes a new folder of the two (see <see cref="IconMerger"/>).
/// </summary>
internal sealed class WidgetBoard(DesktopHost host, ConfigStore configStore, IconService icons) : IDisposable
{
    // Names get a dark halo over a light wallpaper. Two thresholds so a wallpaper near the middle doesn't flicker.
    private const double HaloOnAbove = 0.55;
    private const double HaloOffBelow = 0.45;
    private static readonly TimeSpan HaloInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _haloTimer;
    private IconMerger? _merger;
    private DesktopIconGrid? _nativeIcons;

    public event EventHandler<WidgetClickedEventArgs>? WidgetClicked;

    /// <summary>Something being dragged stayed over a widget: its folder should open.</summary>
    public event EventHandler<WidgetClickedEventArgs>? DragHovered;

    /// <summary>A widget started being dragged to another place.</summary>
    public event EventHandler? WidgetDragStarted;

    /// <summary>The user chose "Pin to taskbar" on a widget.</summary>
    public event EventHandler<WidgetFolder>? PinRequested;

    /// <summary>The user chose "New folder" on a widget.</summary>
    public event EventHandler? NewFolderRequested;

    /// <summary>The user chose "Delete folder" on a widget.</summary>
    public event EventHandler<WidgetFolder>? DeleteRequested;

    public void Show(IReadOnlyList<WidgetFolder> folders)
    {
        _nativeIcons = host.ReadIconGrid();
        var present = folders.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _entries.Keys.Where(n => !present.Contains(n)).ToList())
        {
            _entries[name].Release(host);
            _entries.Remove(name);
        }

        // Widgets with a saved position go first, so new ones fill the cells around them.
        var configChanged = false;
        foreach (var folder in folders.Where(f => !_entries.ContainsKey(f.Name)).OrderBy(f => SavedPosition(f.Name) is null))
        {
            configChanged |= Add(folder);
        }

        foreach (var entry in _entries.Values)
        {
            ApplyCellSize(entry.View, entry.Surface);
        }

        foreach (var folder in folders)
        {
            _entries[folder.Name].Update(folder, icons.GetPreviewAsync(folder));
        }

        if (configChanged)
        {
            configStore.Save();
        }

        if (_haloTimer is null)
        {
            _haloTimer = new DispatcherTimer { Interval = HaloInterval };
            _haloTimer.Tick += (_, _) => UpdateHalos();
            _haloTimer.Start();
        }

        if (_merger is null)
        {
            _merger = new IconMerger(host, e => DropEffect(NewFolderPath(), e));
            _merger.MergeRequested += (_, merge) => MergeIcons(merge);
        }

        UpdateHalos();
    }

    public void Dispose()
    {
        _haloTimer?.Stop();
        _merger?.Dispose();
        foreach (var entry in _entries.Values)
        {
            entry.Release(host);
        }

        _entries.Clear();
    }

    // Cells are exactly as big as the desktop's icon cells, so a widget looks and sits like a shortcut.
    private (int Width, int Height) CellSize(MonitorArea monitor) =>
        _nativeIcons is { } icons
            ? (icons.CellWidth, icons.CellHeight)
            : (monitor.ToPixels(FolderWidget.CellWidth), monitor.ToPixels(FolderWidget.CellHeight));

    private WidgetGrid GridOn(MonitorArea monitor)
    {
        var (width, height) = CellSize(monitor);
        return new WidgetGrid(monitor.WorkArea, width, height, _nativeIcons?.Anchor, _nativeIcons?.Icons.Select(i => i.Cell).ToList());
    }

    // Returns whether the saved position changed.
    private bool Add(WidgetFolder folder)
    {
        var taken = TakenCells(except: null);
        ScreenPoint position;
        var keep = false;
        if (SavedPosition(folder.Name) is { } saved)
        {
            // Without the desktop's icons to line up with (Explorer hasn't filled the desktop yet), snapping would move
            // a widget onto the fallback grid and save that: it stays where it was put until the icons can be read.
            var grid = GridOn(MonitorArea.At(saved));
            keep = !IsOnAMonitor(saved);
            position = grid.FollowsIcons || keep ? grid.NearestFree(saved, taken) : saved;
        }
        else
        {
            position = GridOn(MonitorArea.Primary).FirstFree(taken);
        }

        var entry = new Entry(this, folder, position);
        _entries[folder.Name] = entry;
        ApplyCellSize(entry.View, entry.Surface);
        host.Add(entry.Surface);

        // A widget whose monitor is gone waits on another one but remembers where it was put.
        return !keep && SavePosition(folder.Name, position);
    }

    /// <summary>
    /// The monitors changed. A widget belongs where it was put, and that is saved in screen coordinates, which don't
    /// change when a monitor is added: the widgets stay on their monitor. One whose place is on no monitor now (the
    /// monitor was unplugged) waits on the nearest one, without forgetting its place, and goes back when the monitor does.
    /// </summary>
    public void OnDisplayChanged()
    {
        _nativeIcons = host.ReadIconGrid();
        foreach (var entry in _entries.Values)
        {
            var saved = SavedPosition(entry.Folder.Name) ?? entry.Surface.Position;
            var position = IsOnAMonitor(saved)
                ? saved
                : GridOn(MonitorArea.At(saved)).NearestFree(saved, TakenCells(except: entry.Surface));
            entry.Surface.MoveTo(position);
            ApplyCellSize(entry.View, entry.Surface);
        }

        Log.Info($"Display configuration changed; {_entries.Count} widget(s) checked.");
    }

    // Whether a widget put at this position would be seen: its middle is on a monitor.
    private bool IsOnAMonitor(ScreenPoint position)
    {
        var (width, height) = CellSize(MonitorArea.At(position));
        return MonitorArea.Containing(position.Offset(width / 2, height / 2)) is not null;
    }

    /// <summary>
    /// Desktop icons changed: an icon that ended up on a widget (a new shortcut Explorer put in the first
    /// cell it thinks is free, or one the user dragged there) moves to the nearest free cell. When Explorer
    /// arranges the icons itself they can't be moved, so the widgets make way instead.
    /// </summary>
    public void ClearIconsOffWidgets()
    {
        _nativeIcons = host.ReadIconGrid();
        if (_nativeIcons is not { } grid)
        {
            return;
        }

        var placed = Placed().ToList();
        var widgetCells = placed.Select(p => BoundsOf(p.Surface)).ToList();
        var covering = grid.Icons.Where(i => widgetCells.Any(cell => WidgetGrid.Covers(i.Cell, cell))).ToList();
        if (covering.Count == 0)
        {
            return;
        }

        if (grid.AutoArranged)
        {
            var configChanged = false;
            foreach (var (surface, savePosition) in placed.Where(p => covering.Any(i => WidgetGrid.Covers(i.Cell, BoundsOf(p.Surface)))))
            {
                configChanged |= Place(surface, savePosition);
            }

            if (configChanged)
            {
                configStore.Save();
            }

            return;
        }

        // What a moved icon must stay off: the widgets and the icons staying where they are.
        var occupied = widgetCells.Concat(grid.Icons.Except(covering).Select(i => i.Cell)).ToList();
        foreach (var icon in covering)
        {
            var from = new ScreenPoint(icon.Cell.Left, icon.Cell.Top);
            var to = new WidgetGrid(MonitorArea.At(from).WorkArea, grid.CellWidth, grid.CellHeight, grid.Anchor, occupied)
                .NearestFree(from, taken: new HashSet<ScreenPoint>());
            var target = new ScreenRect(to.X, to.Y, to.X + grid.CellWidth, to.Y + grid.CellHeight);
            if (occupied.Any(o => WidgetGrid.Covers(o, target)))
            {
                continue; // The desktop is full.
            }

            host.MoveIcon(icon, to);
            occupied.Add(target);
        }
    }

    // A widget was dragged and let go: snap it to the nearest free cell.
    private void Snap(DesktopSurface surface, Func<ScreenPoint, bool> savePosition)
    {
        _nativeIcons = host.ReadIconGrid();
        if (Place(surface, savePosition))
        {
            configStore.Save();
        }

        UpdateHalos();
    }

    // Whether the pointer is on the desktop's Recycle Bin icon, which is told from the others by the name under it.
    private bool IsOverRecycleBin() =>
        host.ReadIconGrid(log: false) is { } grid
        && grid.IconAt(ScreenPoint.AtCursor()) is { } icon
        && host.ReadIconNames(grid.Icons.Count) is { } names
        && string.Equals(names[icon.Index], RecycleBin.DisplayName, StringComparison.CurrentCultureIgnoreCase);

    // Looks at the wallpaper behind each name. Skipped where a window covers the widget (the screen would
    // show the window, not the wallpaper); the next tick tries again.
    private void UpdateHalos()
    {
        foreach (var view in _entries.Values.Select(e => e.View))
        {
            if (view.LabelBounds() is not { } bounds || ScreenSampler.DesktopBrightness(bounds) is not { } brightness)
            {
                continue;
            }

            view.HasHalo = view.HasHalo ? brightness > HaloOffBelow : brightness > HaloOnAbove;
        }
    }

    // Moves the widget to the free cell nearest to where it is. Returns whether its saved position changed.
    private bool Place(DesktopSurface surface, Func<ScreenPoint, bool> savePosition)
    {
        var current = surface.Position;
        var position = GridOn(MonitorArea.At(current)).NearestFree(current, TakenCells(except: surface));
        surface.MoveTo(position);
        return savePosition(position);
    }

    // Where the folder for merged icons would be created.
    private string NewFolderPath()
    {
        var root = configStore.Config.RootPath;
        return Path.Combine(root, WidgetFolderName.Suggest(root));
    }

    // Icons were dropped on another desktop icon: a new folder takes that icon's place and gets both.
    private void MergeIcons(IconMergeEventArgs merge)
    {
        var config = configStore.Config;
        var folder = NewFolderPath();
        var name = Path.GetFileName(folder);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Creating a widget folder for merged icons failed", exception);
            merge.Drop.Effects = DragDropEffects.None;
            return;
        }

        // The folder is made known with its place before the watcher turns it into a widget.
        config.Widgets[name] = new WidgetConfig { Position = new ScreenPoint(merge.TargetCell.Left, merge.TargetCell.Top) };
        var added = ItemImporter.Import([merge.TargetPath, .. merge.DroppedPaths], folder, config.DropMode, FolderPaths.Desktops, config.RootPath);
        if (added == 0)
        {
            config.Widgets.Remove(name);
            try
            {
                Directory.Delete(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Removing the empty folder \"{folder}\" failed", exception);
            }

            merge.Drop.Effects = DragDropEffects.None;
            return;
        }

        configStore.Save();
        Log.Info($"Dropped {merge.DroppedPaths.Length} item(s) on \"{Path.GetFileName(merge.TargetPath)}\": new folder \"{name}\" with {added} item(s).");

        // See ImportDropped: never report Move to the source.
        merge.Drop.Effects = DragDropEffects.Copy;
    }

    private static string[] DroppedPaths(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];

    // The cursor shows what the first droppable item would do.
    private DragDropEffects DropEffect(string folderPath, DragEventArgs e)
    {
        var action = DroppedPaths(e)
            .Select(path => ItemImporter.Plan(path, folderPath, configStore.Config.DropMode, FolderPaths.Desktops, configStore.Config.RootPath))
            .FirstOrDefault(a => a != ImportAction.None);
        var effect = action switch
        {
            ImportAction.Move => DragDropEffects.Move,
            ImportAction.Copy => DragDropEffects.Copy,
            ImportAction.CreateShortcut => DragDropEffects.Link,
            _ => DragDropEffects.None,
        };

        // Show Copy if the source doesn't offer the natural effect; the drop is handled the same either way.
        return effect == DragDropEffects.None || (e.AllowedEffects & effect) != 0
            ? effect
            : e.AllowedEffects & DragDropEffects.Copy;
    }

    private void ImportDropped(string folderPath, DragEventArgs e)
    {
        var added = ItemImporter.Import(DroppedPaths(e), folderPath, configStore.Config.DropMode, FolderPaths.Desktops, configStore.Config.RootPath);

        // We moved, copied or linked the files ourselves. Never report Move: the source would then
        // delete what it thinks it still has.
        e.Effects = added > 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private ScreenRect BoundsOf(DesktopSurface surface)
    {
        var position = surface.Position;
        var (width, height) = CellSize(MonitorArea.At(position));
        return new ScreenRect(position.X, position.Y, position.X + width, position.Y + height);
    }

    // The view is laid out in DIP, the cell is in physical pixels.
    private void ApplyCellSize(FolderWidget view, DesktopSurface surface)
    {
        var monitor = MonitorArea.At(surface.Position);
        var (width, height) = CellSize(monitor);
        view.SetCellSize(width * 96.0 / monitor.Dpi, height * 96.0 / monitor.Dpi);
    }

    private HashSet<ScreenPoint> TakenCells(DesktopSurface? except) =>
        Placed().Select(p => p.Surface).Where(s => s != except).Select(s => s.Position).ToHashSet();

    // Every widget, with how to save its position.
    private IEnumerable<(DesktopSurface Surface, Func<ScreenPoint, bool> SavePosition)> Placed() =>
        _entries.Values.Select<Entry, (DesktopSurface, Func<ScreenPoint, bool>)>(
            entry => (entry.Surface, position => SavePosition(entry.Folder.Name, position)));

    private ScreenPoint? SavedPosition(string name) =>
        configStore.Config.Widgets.TryGetValue(name, out var widget) ? widget.Position : null;

    private bool SavePosition(string name, ScreenPoint position)
    {
        if (!configStore.Config.Widgets.TryGetValue(name, out var widget) || widget.Position == position)
        {
            return false;
        }

        widget.Position = position;
        return true;
    }

    /// <summary>One widget: its folder, its view and the desktop window hosting the view.</summary>
    private sealed class Entry
    {
        private static readonly TimeSpan HoverOpenDelay = TimeSpan.FromMilliseconds(550);

        private readonly DispatcherTimer _hover;
        private ScreenPoint _dragOrigin;

        public Entry(WidgetBoard board, WidgetFolder folder, ScreenPoint position)
        {
            Folder = folder;
            View = new FolderWidget();
            Surface = new DesktopSurface(View, position);
            _hover = new DispatcherTimer { Interval = HoverOpenDelay };

            View.Clicked += (_, _) => board.WidgetClicked?.Invoke(board, new WidgetClickedEventArgs(Folder, board.BoundsOf(Surface)));
            View.PinRequested += (_, _) => board.PinRequested?.Invoke(board, Folder);
            View.NewFolderRequested += (_, _) => board.NewFolderRequested?.Invoke(board, EventArgs.Empty);
            View.DeleteRequested += (_, _) => board.DeleteRequested?.Invoke(board, Folder);
            View.DragStarted += (_, _) =>
            {
                _dragOrigin = Surface.Position;
                Surface.BringToFront(); // Carried over the other widgets, not under them.
                board.WidgetDragStarted?.Invoke(board, EventArgs.Empty);
            };
            View.Dragged += (_, offset) => Surface.MoveTo(_dragOrigin.Offset(offset.X, offset.Y));
            View.DragCompleted += (_, _) =>
            {
                // Let go over the Recycle Bin: the widget goes back where it came from, and its folder is offered for
                // deletion (after this input handler, since that asks).
                var overRecycleBin = board.IsOverRecycleBin();
                if (overRecycleBin)
                {
                    Surface.MoveTo(_dragOrigin);
                }

                board.Snap(Surface, position => board.SavePosition(Folder.Name, position));
                if (overRecycleBin)
                {
                    View.Dispatcher.BeginInvoke(() => board.DeleteRequested?.Invoke(board, Folder));
                }
            };
            View.DragOver += (_, e) =>
            {
                e.Effects = board.DropEffect(Folder.FullPath, e);
                e.Handled = true;
            };
            View.Drop += (_, e) =>
            {
                _hover.Stop();
                board.ImportDropped(Folder.FullPath, e);
                e.Handled = true;
            };

            // Held over a widget for a moment, a drag opens its folder, so the item can be put inside; an item dragged
            // out of this folder's panel opens it again, so it can be put back.
            _hover.Tick += (_, _) =>
            {
                _hover.Stop();
                if (View.IsCursorOver())
                {
                    board.DragHovered?.Invoke(board, new WidgetClickedEventArgs(Folder, board.BoundsOf(Surface)));
                }
            };
            View.DragEnter += (_, e) =>
            {
                if (board.DropEffect(Folder.FullPath, e) != DragDropEffects.None || PanelDragData.IsFrom(e.Data, Folder.Name))
                {
                    _hover.Start();
                }
            };
            View.DragLeave += (_, _) =>
            {
                // Also raised when the cursor only moves from one part of the widget to another.
                if (!View.IsCursorOver())
                {
                    _hover.Stop();
                }
            };
        }

        public WidgetFolder Folder { get; private set; }

        public FolderWidget View { get; }

        public DesktopSurface Surface { get; }

        // Takes the widget off the desktop; a pending "hold to open" must not fire for it afterwards.
        public void Release(DesktopHost host)
        {
            _hover.Stop();
            host.Remove(Surface);
        }

        public void Update(WidgetFolder folder, Task<BitmapSource> preview)
        {
            Folder = folder;
            View.Show(folder.Name, preview);
        }
    }
}
