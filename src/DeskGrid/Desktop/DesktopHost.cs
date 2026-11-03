using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskGrid.Diagnostics;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DeskGrid.Desktop;

/// <summary>
/// Keeps <see cref="DesktopSurface"/>s attached to Explorer's desktop icon layer and re-attaches
/// them whenever Explorer recreates the desktop (explorer.exe restart or crash).
/// </summary>
internal sealed class DesktopHost : IDisposable
{
    private static readonly TimeSpan SettlePollInterval = TimeSpan.FromMilliseconds(500);

    // Windows sends a burst of display changes while monitors are plugged in or dragged around in Settings.
    private static readonly TimeSpan DisplaySettleDelay = TimeSpan.FromMilliseconds(1500);

    private readonly List<DesktopSurface> _surfaces = [];
    private readonly DispatcherTimer _retryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _driftTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _displaySettled = new() { Interval = DisplaySettleDelay };
    private readonly DesktopIconWatcher _iconWatcher = new();
    private HwndSource? _shellEventsWindow;
    private IconLayer? _layer;

    public DesktopHost()
    {
        _retryTimer.Tick += (_, _) => EnsureAttached();
        _driftTimer.Tick += (_, _) => PutSurfacesBack();
        _displaySettled.Tick += (_, _) =>
        {
            _displaySettled.Stop();
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Monitors were added, removed or rearranged, or the work area, icon spacing or DPI scaling changed, and Explorer
    /// has had a moment to adapt. The surfaces are back at their <see cref="DesktopSurface.Position"/> by then; what
    /// belongs where is for the owner to decide.
    /// </summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Desktop icons were added, removed or moved.</summary>
    public event EventHandler? IconsChanged
    {
        add => _iconWatcher.Changed += value;
        remove => _iconWatcher.Changed -= value;
    }

    /// <summary>A mouse button went down on the desktop, which may turn into a drag of its icons.</summary>
    public event EventHandler? DesktopPressed
    {
        add => _iconWatcher.Pressed += value;
        remove => _iconWatcher.Pressed -= value;
    }

    /// <summary>Starts managing <paramref name="surface"/>; attaches it right away once started.</summary>
    public void Add(DesktopSurface surface)
    {
        _surfaces.Add(surface);
        surface.Lost += OnSurfaceLost;

        if (_layer is { } layer && PInvoke.IsWindow(layer.Handle))
        {
            TryAttach(surface, layer);
        }
        else if (_shellEventsWindow is not null)
        {
            EnsureAttached();
        }
    }

    /// <summary>Stops managing <paramref name="surface"/> and destroys its window.</summary>
    public void Remove(DesktopSurface surface)
    {
        if (!_surfaces.Remove(surface))
        {
            return;
        }

        surface.Lost -= OnSurfaceLost;
        surface.Dispose();
    }

    /// <summary>The desktop's own icon grid, or <see langword="null"/> if it can't be read right now.</summary>
    /// <param name="log">Whether to write what was read to the log (not for the reads that every click on an icon makes).</param>
    public DesktopIconGrid? ReadIconGrid(bool log = true)
    {
        var grid = _layer is { } layer && PInvoke.IsWindow(layer.Handle) ? DesktopIconGrid.Read(layer) : null;
        if (log)
        {
            Log.Info(grid is null
                ? "Desktop icon grid unavailable; widgets use their own grid."
                : $"Desktop icon grid: cell {grid.CellWidth}x{grid.CellHeight} px, {grid.Icons.Count} icon(s).");
        }

        return grid;
    }

    /// <summary>The name shown under each of the first <paramref name="count"/> desktop icons, or <see langword="null"/> if they can't be read.</summary>
    public string[]? ReadIconNames(int count) =>
        _layer is { } layer && PInvoke.IsWindow(layer.Handle) ? DesktopIconGrid.ReadNames(layer, count) : null;

    /// <summary>Moves a desktop icon to the cell whose top-left corner is <paramref name="cell"/>.</summary>
    public void MoveIcon(DesktopIcon icon, ScreenPoint cell)
    {
        var moved = _layer is { } layer && PInvoke.IsWindow(layer.Handle) && DesktopIconGrid.Move(layer, icon, cell);
        Log.Info(moved
            ? $"Moved desktop icon {icon.Index} off a widget, from ({icon.Cell.Left}, {icon.Cell.Top}) to ({cell.X}, {cell.Y})."
            : $"Could not move desktop icon {icon.Index} off a widget.");
    }

    public void Start()
    {
        _shellEventsWindow = ShellMessageWindow.Create("DeskGrid.ShellEvents", ShellEventsHook);
        _driftTimer.Start();
        EnsureAttached();
    }

    /// <summary>
    /// Waits until the desktop's icons are in and have stopped moving (see <see cref="DesktopReadiness"/>), for a
    /// start at sign-in. Returns <see langword="false"/> if they never did within <paramref name="timeout"/>.
    /// </summary>
    public static Task<bool> WaitUntilSettledAsync(TimeSpan timeout, CancellationToken cancel) =>
        DesktopReadiness.WaitUntilSettledAsync(IconPlaces, SettlePollInterval, timeout, cancel);

    // Where the icons are, as one string to compare; null while there are no icons to see.
    private static string? IconPlaces() =>
        IconLayer.Find() is { } layer && DesktopIconGrid.Read(layer) is { Icons.Count: > 0 } grid
            ? string.Join(';', grid.Icons.Select(icon => $"{icon.Cell.Left},{icon.Cell.Top}"))
            : null;

    public void Dispose()
    {
        _retryTimer.Stop();
        _driftTimer.Stop();
        _displaySettled.Stop();
        _iconWatcher.Dispose();
        foreach (var surface in _surfaces)
        {
            surface.Lost -= OnSurfaceLost;
            surface.Dispose();
        }

        _surfaces.Clear();
        _shellEventsWindow?.Dispose();
    }

    private void EnsureAttached()
    {
        if (IconLayer.Find() is not { } layer)
        {
            if (!_retryTimer.IsEnabled)
            {
                Log.Info("Desktop icon layer not found; waiting for Explorer.");
                _retryTimer.Start();
            }

            return;
        }

        _retryTimer.Stop();
        _layer = layer;
        _iconWatcher.Watch(layer.FindListView());
        var attached = _surfaces.Where(s => !s.IsAttachedTo(layer.Handle)).ToList().Count(s => TryAttach(s, layer));
        if (attached > 0)
        {
            Log.Info($"Attached {attached} surface(s) to SHELLDLL_DefView 0x{(nint)layer.Handle:X} ({layer.Layout} layout).");
        }
    }

    private bool TryAttach(DesktopSurface surface, IconLayer layer)
    {
        try
        {
            surface.AttachTo(layer.Handle);
            return true;
        }
        catch (Win32Exception exception)
        {
            // The layer went away between finding it and creating the window.
            Log.Error("Attaching a surface failed; retrying", exception);
            _retryTimer.Start();
            return false;
        }
    }

    private void OnSurfaceLost(object? sender, EventArgs e)
    {
        // We are inside the window's teardown and Explorer may be half gone: retry later instead.
        Log.Info("Surface window destroyed externally; re-attaching once Explorer is back.");
        _retryTimer.Start();
    }

    private IntPtr ShellEventsHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == ShellMessageWindow.TaskbarCreatedMessage)
        {
            Log.Info("TaskbarCreated received.");
            EnsureAttached();
        }
        else if ((uint)msg == PInvoke.WM_DISPLAYCHANGE)
        {
            Log.Info("Display configuration changed.");
            PutSurfacesBack();
            _displaySettled.Stop();
            _displaySettled.Start();
        }
        else if ((uint)msg == PInvoke.WM_SETTINGCHANGE && ChangesDesktopLayout((nuint)wParam))
        {
            Log.Info($"Desktop layout setting changed (SPI 0x{(nuint)wParam:X}).");
            _displaySettled.Stop();
            _displaySettled.Start();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Whether a <c>WM_SETTINGCHANGE</c> with this <c>SPI_*</c> value in <c>wParam</c> moves the cells widgets sit on:
    /// the work area (the taskbar moved or was resized), the desktop icon spacing, or the DPI scaling.
    /// </summary>
    internal static bool ChangesDesktopLayout(nuint action) => action is
        0x000D // SPI_ICONHORIZONTALSPACING
        or 0x0018 // SPI_ICONVERTICALSPACING
        or 0x002E // SPI_SETICONMETRICS
        or 0x002F // SPI_SETWORKAREA
        or 0x009F; // SPI_SETLOGICALDPIOVERRIDE

    // The desktop window spans all monitors and moves when one is added on its left or top; its children, our
    // surfaces, move with it on the screen. Each surface knows where it belongs and goes back there.
    private void PutSurfacesBack()
    {
        var moved = _surfaces.Count(s => s.Resync());
        if (moved > 0)
        {
            Log.Info($"Put {moved} surface(s) back where they belong; the desktop window had moved.");
        }
    }
}
