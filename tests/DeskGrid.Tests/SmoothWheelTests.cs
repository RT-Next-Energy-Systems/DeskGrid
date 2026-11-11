using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskGrid.Settings;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

[Collection(nameof(ShellCollection))]
public sealed class SmoothWheelTests : IDisposable
{
    private readonly DispatcherThread _ui = new("Wheel UI");

    public void Dispose() => _ui.Dispose();

    // A page (a scroll viewer that scrolls by pixel) holding a list (which scrolls by item), as a Settings page can.
    private static (Window Window, ScrollViewer Page, ListBox List, Border Filler) PageWithList()
    {
        TestApplication.Ensure();
        SmoothWheel.Install();
        SmoothWheel.Install(); // Twice on purpose: still one handler.
        var list = new ListBox { Height = 80 };
        for (var i = 0; i < 40; i++)
        {
            list.Items.Add(new ListBoxItem { Content = $"Item {i}", Height = 20 });
        }

        var filler = new Border { Height = 600, Background = System.Windows.Media.Brushes.Transparent };
        var page = new ScrollViewer { CanContentScroll = false, Content = new StackPanel { Children = { list, filler } } };
        var window = new Window
        {
            Width = 300, Height = 300, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, Content = page,
        };
        window.Show();
        window.UpdateLayout();
        return (window, page, list, filler);
    }

    // What the input system does for one wheel notch: the tunnelling preview, then (unless handled) the bubbling event.
    private static void Wheel(UIElement over, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        over.RaiseEvent(preview);
        if (!preview.Handled)
        {
            over.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.MouseWheelEvent });
        }
    }

    [Fact]
    public async Task TheWheelOverAListInAPage_ScrollsTheList_NotThePage()
    {
        var (pageOffset, listScrolled) = await _ui.InvokeAsync(() =>
        {
            var (window, page, list, _) = PageWithList();
            try
            {
                var listViewer = (ScrollViewer)((Border)System.Windows.Media.VisualTreeHelper.GetChild(list, 0)).Child;
                Wheel((ListBoxItem)list.Items[0], -120);
                window.UpdateLayout();
                return (page.VerticalOffset, listViewer.VerticalOffset > 0);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(0, pageOffset); // Used to scroll the page by 48 px while the list under the pointer stayed.
        Assert.True(listScrolled);
    }

    [Fact]
    public async Task OneNotchOverThePage_ScrollsItByAFixedStep_ASmallTouchpadMoveByLess()
    {
        var (notch, small) = await _ui.InvokeAsync(() =>
        {
            var (window, page, _, filler) = PageWithList();
            try
            {
                Wheel(filler, -120);
                window.UpdateLayout();
                var afterNotch = page.VerticalOffset;
                Wheel(filler, -30);
                window.UpdateLayout();
                return (afterNotch, page.VerticalOffset - afterNotch);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(48, notch, 1); // One handler: a second one would make it 96.
        Assert.Equal(12, small, 1);
    }
}
