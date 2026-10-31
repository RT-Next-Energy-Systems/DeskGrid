using System.IO;
using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Folders;
using DeskGrid.Icons;
using DeskGrid.Panel;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

public sealed class PanelPlacementTests
{
    private const int Gap = 8;
    private static readonly ScreenRect WorkArea = new(0, 0, 1000, 800);

    [Fact]
    public void Place_BelowAndCentered_WhenThereIsRoom() =>
        Assert.Equal(new ScreenPoint(300, 108), PanelPlacement.Place(new ScreenRect(400, 0, 500, 100), 300, 200, WorkArea, Gap));

    [Fact]
    public void Place_ShiftsSidewaysToStayOnScreen() =>
        Assert.Equal(new ScreenPoint(700, 108), PanelPlacement.Place(new ScreenRect(900, 0, 1000, 100), 300, 200, WorkArea, Gap));

    [Fact]
    public void Place_Above_WhenBelowDoesNotFit() =>
        Assert.Equal(new ScreenPoint(300, 492), PanelPlacement.Place(new ScreenRect(400, 700, 500, 800), 300, 200, WorkArea, Gap));

    [Fact]
    public void Place_Left_WhenNeitherBelowNorAboveFits() =>
        Assert.Equal(new ScreenPoint(592, 100), PanelPlacement.Place(new ScreenRect(900, 300, 1000, 400), 300, 500, WorkArea, Gap));

    [Fact]
    public void Place_Right_WhenOnlyRightFits() =>
        Assert.Equal(new ScreenPoint(108, 100), PanelPlacement.Place(new ScreenRect(0, 300, 100, 400), 300, 500, WorkArea, Gap));

    [Fact]
    public void Place_OnAGivenSide_StaysThere_WhenThePanelGrows()
    {
        // Measured on a 2880×1704 work area: the panel of a widget low on the screen opened below it; growing a row
        // for an item dragged in used to move it to the right, away from the cursor, and back, again and again.
        var anchor = new ScreenRect(150, 730, 300, 912);
        var workArea = new ScreenRect(0, 0, 2880, 1704);
        var side = PanelPlacement.Side(anchor, 504, 682, workArea, 16);

        Assert.Equal(PanelSide.Below, side);
        Assert.Equal(PanelSide.Right, PanelPlacement.Side(anchor, 504, 874, workArea, 16));
        Assert.Equal(new ScreenPoint(0, 830), PanelPlacement.Place(anchor, 504, 874, workArea, 16, side));
    }

    [Fact]
    public void Place_TooBigForAnySide_ClampsIntoTheWorkArea() =>
        Assert.Equal(new ScreenPoint(0, 0), PanelPlacement.Place(new ScreenRect(400, 300, 500, 400), 2000, 2000, WorkArea, Gap));
}

public sealed class PanelThemeTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void System_UsesAcrylicWhereSupported_AndFollowsTheSystemTheme(bool dark, bool acrylicSupported)
    {
        var theme = PanelTheme.Resolve(new PanelBackground(), dark, acrylicSupported);

        Assert.Equal(acrylicSupported, theme.UseAcrylic);
        Assert.Equal(dark, theme.IsDark);
        Assert.Equal(255, theme.Background.A);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Presets_AreTranslucentAndIgnoreTheSystemTheme(bool dark)
    {
        var style = dark ? PanelBackgroundStyle.Dark : PanelBackgroundStyle.Light;
        var theme = PanelTheme.Resolve(new PanelBackground { Style = style }, systemDark: !dark, acrylicSupported: true);

        Assert.False(theme.UseAcrylic);
        Assert.Equal(dark, theme.IsDark);
        Assert.InRange(theme.Background.A, 1, 254);
    }

    [Fact]
    public void Custom_UsesColorAndOpacity_WithReadableText()
    {
        var bright = PanelTheme.Resolve(Custom("#F4C430", 0.5), systemDark: true, acrylicSupported: true);
        var dim = PanelTheme.Resolve(Custom("#1E3A5F", 2), systemDark: false, acrylicSupported: true);

        Assert.Equal(Color.FromArgb(128, 0xF4, 0xC4, 0x30), bright.Background);
        Assert.False(bright.IsDark);
        Assert.Equal(255, dim.Background.A);
        Assert.True(dim.IsDark);
    }

    [Theory]
    [InlineData("not a color")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    public void Custom_InvalidColor_FallsBackToTheDefault(string color)
    {
        var theme = PanelTheme.Resolve(Custom(color, 1), systemDark: false, acrylicSupported: false);

        Assert.Equal(ColorConverter.ConvertFromString(new PanelBackground().CustomColor), theme.Background);
    }

    private static PanelBackground Custom(string color, double opacity) =>
        new() { Style = PanelBackgroundStyle.Custom, CustomColor = color, CustomOpacity = opacity };
}

public sealed class PanelDragDataTests
{
    [Theory]
    [InlineData("Games", true)]
    [InlineData("GAMES", true)]
    [InlineData("Work", false)]
    public void IsFrom_TellsWhichPanelAnItemComesFrom(string folder, bool expected)
    {
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { @"C:\DeskGrid\Games\Chess.lnk" });
        data.SetData(PanelDragData.ItemFormat, "Chess.lnk");
        data.SetData(PanelDragData.FolderFormat, "Games");

        Assert.Equal(expected, PanelDragData.IsFrom(data, folder));
    }

    [Fact]
    public void IsFrom_IsFalse_ForFilesFromElsewhere()
    {
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { @"C:\DeskGrid\Games\Chess.lnk" });

        Assert.False(PanelDragData.IsFrom(data, "Games"));
    }
}

[Collection(nameof(ShellCollection))]
public sealed class PopupPanelTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task ShowFolder_AfterTheList_GrowsToTheFolderItems()
    {
        var (listHeight, folderHeight) = await _ui.InvokeAsync(() =>
        {
            using var icons = new IconService();
            var theme = PanelTheme.Resolve(new PanelBackground { Style = PanelBackgroundStyle.Dark }, false, false);
            var panel = new PopupPanel(new ScreenRect(0, 0, 1, 1), 3, theme, icons);
            var big = Folder("Big", 17);
            panel.ShowList([Folder("A", 1), Folder("B", 1), big]);
            var listHeight = panel.Height;
            panel.ShowFolder(big);
            var folderHeight = panel.Height;
            panel.Close();
            return (listHeight, folderHeight);
        });

        // One row of folders, then the most rows a panel shows (4) of the 17 items.
        Assert.Equal(3 * 96, folderHeight - listHeight, precision: 0);
    }

    [Fact]
    public async Task RightClickOnAnItem_ShowsTheShellMenu_UntilItIsClosed()
    {
        using var directory = new TempDirectory();
        var file = directory.CreateFile("notes.txt");
        var folder = new WidgetFolder("Notes", directory.Path, [new FolderItem("notes.txt", file, DateTime.UnixEpoch)]);

        var shownFor = await _ui.InvokeAsync(() =>
        {
            using var icons = new IconService();
            var theme = PanelTheme.Resolve(new PanelBackground { Style = PanelBackgroundStyle.Dark }, false, false);
            var panel = new PopupPanel(new ScreenRect(200, 200, 201, 201), 3, theme, icons) { ShowActivated = false };
            panel.ShowFolder(folder);
            panel.Show();

            // The menu loop pumps messages, so this timer runs while the menu is up and closes it, as Esc would.
            var closer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            closer.Tick += (_, _) => EndMenu();
            closer.Start();
            try
            {
                // The container is a ContentPresenter; the button the template makes is its only child.
                var item = (System.Windows.Controls.Button)System.Windows.Media.VisualTreeHelper.GetChild(
                    panel.Items.ItemContainerGenerator.ContainerFromIndex(0), 0);
                var click = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
                {
                    RoutedEvent = System.Windows.UIElement.PreviewMouseRightButtonUpEvent,
                    Source = item,
                };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                item.RaiseEvent(click);
                return clock.Elapsed;
            }
            finally
            {
                closer.Stop();
                panel.Close();
            }
        });

        Assert.True(shownFor >= TimeSpan.FromMilliseconds(90), $"The menu was up for only {shownFor.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task PanelOpenedByADrag_ClosesItself_WhenTheDragEndsWithoutADrop()
    {
        var closed = new TaskCompletionSource();
        await _ui.InvokeAsync(() =>
        {
            using var icons = new IconService();
            var theme = PanelTheme.Resolve(new PanelBackground { Style = PanelBackgroundStyle.Dark }, false, false);

            // Opened as for a drag held over its widget; no mouse button is down, so that drag is over.
            var panel = new PopupPanel(new ScreenRect(200, 200, 201, 201), 3, theme, icons) { ActivateWhenShown = false, ShowActivated = false };
            panel.ShowFolder(Folder("Dragged", 2));
            panel.Closed += (_, _) => closed.SetResult();
            panel.Show();
        });

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EndMenu();

    private static WidgetFolder Folder(string name, int itemCount) =>
        new(name, @"C:\DeskGrid.Tests\" + name,
            [.. Enumerable.Range(1, itemCount).Select(i => new FolderItem($"Item {i}.txt", $@"C:\DeskGrid.Tests\{name}\Item {i}.txt", DateTime.UnixEpoch))]);
}

public sealed class DragGhostTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Test UI");

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task ShowAndLand_DrawTheIcon()
    {
        var landed = new TaskCompletionSource();
        var ghost = await _ui.InvokeAsync(() =>
        {
            var icon = new DrawingImage(new GeometryDrawing(Brushes.SteelBlue, null, new RectangleGeometry(new(0, 0, 48, 48))));
            var ghost = new DragGhost(icon, new ScreenPoint(200, 200));
            ghost.Show();
            ghost.Lift();
            ghost.Land(new ScreenPoint(260, 260), vanish: false, landed.SetResult);
            return ghost;
        });
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // A window WPF also renders into refuses UpdateLayeredWindow, and the ghost shows as a black square.
        Assert.Equal(0, ghost.FailedDraws);
    }
}
