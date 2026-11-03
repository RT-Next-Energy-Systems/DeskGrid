using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskGrid.Desktop;
using DeskGrid.Shell;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

[Collection(nameof(ShellCollection))]
public sealed class ShellContextMenuTests : IDisposable
{
    private readonly DispatcherThread _sta = new("Test STA");
    private readonly TempDirectory _directory = new();

    public void Dispose()
    {
        _sta.Dispose();
        _directory.Dispose();
    }

    [Fact]
    public async Task Load_FillsTheMenuTheWayExplorerDoes()
    {
        var file = _directory.CreateFile("notes.txt");

        var count = await _sta.InvokeAsync(() =>
        {
            using var menu = ShellContextMenu.Load(file);
            return menu?.ItemCount;
        });

        // Open, Send to, Cut, Copy, Delete, Properties and so on: far more than a handful of entries.
        Assert.True(count > 5, $"The shell put {count} entries in the menu.");
    }

    [Fact]
    public async Task ReadEntries_NamesTheCommandsTheShortMenuOffers()
    {
        var file = _directory.CreateFile("notes.txt");

        var entries = await _sta.InvokeAsync(() =>
        {
            using var menu = ShellContextMenu.Load(file)!;
            return menu.ReadEntries();
        });

        foreach (var verb in new[] { "cut", "copy", "delete", "properties" })
        {
            var entry = Assert.Single(entries, entry => entry.Verb.Equals(verb, StringComparison.OrdinalIgnoreCase));

            // The text is the menu's own, without the keyboard mark or the shortcut shown after it.
            Assert.False(string.IsNullOrWhiteSpace(entry.Text));
            Assert.DoesNotContain('&', entry.Text);
            Assert.DoesNotContain('\t', entry.Text);
        }
    }

    [Fact]
    public async Task Show_ClosedWithoutAChoice_RunsNothing()
    {
        var file = _directory.CreateFile("notes.txt");

        var (chosen, shownFor) = await _sta.InvokeAsync(() =>
        {
            using var owner = new HwndSource(new HwndSourceParameters("DeskGrid.Tests.MenuOwner")
            {
                PositionX = 100,
                PositionY = 100,
                Width = 80,
                Height = 80,
                WindowStyle = unchecked((int)0x90000000), // WS_POPUP | WS_VISIBLE
            });
            using var menu = ShellContextMenu.Load(file)!;

            // The menu loop pumps messages, so this timer runs while the menu is up and closes it, as Esc would.
            var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            closer.Tick += (_, _) => EndMenu();
            closer.Start();
            try
            {
                var clock = Stopwatch.StartNew();
                return (menu.Show(owner, new ScreenPoint(120, 120)), clock.Elapsed);
            }
            finally
            {
                closer.Stop();
            }
        });

        Assert.False(chosen);
        Assert.True(shownFor >= TimeSpan.FromMilliseconds(90), $"The menu was up for only {shownFor.TotalMilliseconds} ms.");
    }

    [DllImport("user32.dll")]
    private static extern bool EndMenu();

    [Fact]
    public async Task Load_MissingFile_GivesNoMenu()
    {
        var gone = _directory.Combine("gone.txt");

        var menu = await _sta.InvokeAsync(() => ShellContextMenu.Load(gone));

        Assert.Null(menu);
    }
}
