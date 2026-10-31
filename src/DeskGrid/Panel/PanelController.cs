using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Folders;
using DeskGrid.Icons;
using DeskGrid.Shell;
using Windows.Win32;

namespace DeskGrid.Panel;

/// <summary>Opens at most one <see cref="PopupPanel"/> at a time and keeps it in step with its folder.</summary>
internal sealed class PanelController(ConfigStore configStore, IconService icons)
{
    private const int MinColumns = 1;
    private const int MaxColumns = 8;

    // What a taskbar request asks for when it wants the list of all folders; no folder can have this name.
    private const string ListKey = "*";

    // From a click on a taskbar button to its request arriving: the shortcut has to start a DeskGrid process first.
    private static readonly TimeSpan TaskbarRequestDelay = TimeSpan.FromSeconds(3);

    private PopupPanel? _panel;

    // The panel an item is being dragged out of, while another panel (opened by holding the drag over a widget)
    // is the one shown.
    private PopupPanel? _dragSource;

    // The panel that a click on the taskbar closed (by taking the activation from it), and when. The request that
    // click sends comes a moment later and must not open the same panel again.
    private (string Key, long Time)? _closedForTaskbar;

    /// <summary>Raised when the user dragged an item to another place in its panel.</summary>
    public event EventHandler<ItemReorderedEventArgs>? ItemReordered;

    /// <summary>Raised when items from another place were dropped on the open panel.</summary>
    public event EventHandler<ItemsDroppedEventArgs>? ItemsDropped;

    /// <summary>Raised when "New folder" was chosen from an open panel's menu.</summary>
    public event EventHandler? NewFolderRequested;

    /// <summary>Raised when "Delete folder" was chosen from an open panel's menu.</summary>
    public event EventHandler<WidgetFolder>? DeleteRequested;

    /// <summary>Renames a widget folder (old name, new name); returns why not, or <see langword="null"/> on success.</summary>
    public Func<string, string, string?>? FolderRenamer { get; set; }

    /// <summary>
    /// Opens <paramref name="folder"/>'s panel next to <paramref name="anchor"/>, or closes it if it is
    /// already open. Widgets never take activation, so an open panel is still open when its widget is clicked.
    /// </summary>
    public void Toggle(WidgetFolder folder, ScreenRect anchor)
    {
        var wasOpen = _panel is { IsFolderList: false, Folder: { } shown } && Same(shown, folder);
        _panel?.Dismiss();
        if (wasOpen)
        {
            return;
        }

        Open(anchor, activate: true, panel => panel.ShowFolder(folder));
    }

    /// <summary>Opens the list of all folders at the cursor (asked for from the taskbar), or closes it if it is open.</summary>
    public void ToggleListAtCursor(IReadOnlyList<WidgetFolder> folders)
    {
        if (ClosedByThisRequest(ListKey))
        {
            return;
        }

        var wasOpen = _panel is { IsFolderList: true };
        _panel?.Dismiss();
        if (wasOpen)
        {
            return;
        }

        Open(CursorRect(), activate: true, panel => panel.ShowList(folders));
    }

    /// <summary>
    /// Opens the panel of the widget something is being dragged over, so the item can be dropped inside. The panel
    /// the drag started in disappears but lives until the drag ends.
    /// </summary>
    public void OpenForDrag(WidgetFolder folder, ScreenRect anchor)
    {
        if (_panel is { IsDragSource: true } source)
        {
            _dragSource = source;
        }

        // Back over the folder the item came from: its panel shows again, so the item can be put back.
        if (_dragSource is { IsDragSource: true, Folder: { } home } origin && Same(home, folder))
        {
            if (_panel != origin)
            {
                _panel?.Dismiss();
                _panel = origin;
            }

            origin.ShowAgainForDrag();
            return;
        }

        if (_panel is { } current)
        {
            if (current.Folder is { } shown && Same(shown, folder))
            {
                return;
            }

            if (current.IsDragSource)
            {
                current.HideForDrag();
            }
            else
            {
                current.Dismiss();
            }
        }

        Open(anchor, activate: false, panel => panel.ShowFolder(folder));
    }

    /// <summary>Like <see cref="Toggle"/>, for a panel asked for from outside the desktop (e.g. the taskbar): opens at the cursor.</summary>
    public void ToggleAtCursor(WidgetFolder folder)
    {
        if (!ClosedByThisRequest(folder.Name))
        {
            Toggle(folder, CursorRect());
        }
    }

    /// <summary>Refreshes the open panel after a rescan, or closes it if its folder is gone.</summary>
    public void Refresh(IReadOnlyList<WidgetFolder> widgets)
    {
        if (_panel is { } panel && !panel.Refresh(widgets))
        {
            panel.Dismiss();
        }
    }

    public void Close() => _panel?.Dismiss();

    private static ScreenRect CursorRect()
    {
        PInvoke.GetCursorPos(out var cursor);
        return new ScreenRect(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1);
    }

    private void Open(ScreenRect anchor, bool activate, Action<PopupPanel> fill)
    {
        var config = configStore.Config;
        var theme = PanelTheme.Resolve(config.PanelBackground, PanelTheme.SystemUsesDarkTheme, Backdrop.IsAcrylicSupported);
        var panel = new PopupPanel(anchor, Math.Clamp(config.PanelColumns, MinColumns, MaxColumns), theme, icons)
        {
            ActivateWhenShown = activate,
            ShowActivated = activate,
            AnimateOpening = config.AnimatePanelOpening && System.Windows.SystemParameters.ClientAreaAnimation,
            Renamer = (oldName, newName) => FolderRenamer?.Invoke(oldName, newName),
        };
        fill(panel);
        panel.ItemInvoked += (_, item) =>
        {
            ShellItem.Open(item.FullPath);
            panel.Dismiss();
        };
        panel.ItemReordered += (_, e) => ItemReordered?.Invoke(this, e);
        panel.ItemsDropped += (_, e) => ItemsDropped?.Invoke(this, e);
        panel.NewFolderRequested += (_, _) => NewFolderRequested?.Invoke(this, EventArgs.Empty);
        panel.DeleteRequested += (_, folder) => DeleteRequested?.Invoke(this, folder);
        panel.DragFinished += (_, _) =>
        {
            if (_dragSource == panel)
            {
                _dragSource = null;
            }

            // A panel opened during the drag takes over if the item went into it, and is done otherwise.
            if (_panel is { } current && current != panel)
            {
                if (current.TookDrop)
                {
                    current.Activate();
                }
                else
                {
                    current.Dismiss();
                }
            }
        };
        panel.Closed += (_, _) =>
        {
            if (_panel == panel)
            {
                _panel = null;
            }

            if (TaskbarIsForeground())
            {
                _closedForTaskbar = (panel.IsFolderList ? ListKey : panel.Folder?.Name ?? "", Environment.TickCount64);
            }
        };

        _panel = panel;
        panel.Show();
        if (activate)
        {
            panel.Activate();
        }
    }

    private static bool Same(WidgetFolder a, WidgetFolder b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    // Whether the panel a taskbar request asks for (by key) was closed by the very click that sent the request: the
    // click meant "close", which has happened already.
    private bool ClosedByThisRequest(string key)
    {
        var closed = _closedForTaskbar;
        _closedForTaskbar = null;
        return _panel is null && closed is { } c && string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)
            && Environment.TickCount64 - c.Time < TaskbarRequestDelay.TotalMilliseconds;
    }

    private static unsafe bool TaskbarIsForeground()
    {
        const int MaxLength = 64;
        var name = stackalloc char[MaxLength];
        var length = PInvoke.GetClassName(PInvoke.GetForegroundWindow(), name, MaxLength);
        return new ReadOnlySpan<char>(name, length) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}
