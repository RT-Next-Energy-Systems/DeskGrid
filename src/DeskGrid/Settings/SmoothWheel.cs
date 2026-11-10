using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskGrid.Settings;

/// <summary>
/// Makes the mouse wheel and touchpad scroll by how far they were moved. WPF's scroll viewer moves a fixed 48 px for every wheel
/// message whatever its size, and a precision touchpad sends many small ones: a slight move of the fingers scrolled the page
/// by a lot. Here a notch (120) is 48 px and a smaller message a proportionally smaller step.
/// </summary>
internal static class SmoothWheel
{
    private const double PixelsPerNotch = 48;
    private const double NotchDelta = 120;

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));

    private static ScrollViewer? Innermost(DependencyObject? from)
    {
        for (var node = from; node is not null; node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer { CanContentScroll: false, ScrollableHeight: > 0 } viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        // Lists that scroll by item (a combo box's drop-down) and Shift / Ctrl wheel are left to WPF; so is a viewer with nothing to scroll.
        if (e.Handled || sender is not ScrollViewer viewer || viewer.CanContentScroll || viewer.ScrollableHeight <= 0
            || (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != ModifierKeys.None)
        {
            return;
        }

        // The preview tunnels from the outermost viewer inwards: only the innermost one under the pointer that can scroll acts.
        if (Innermost(e.OriginalSource as DependencyObject) != viewer)
        {
            return;
        }

        var target = Math.Clamp(viewer.VerticalOffset - e.Delta * PixelsPerNotch / NotchDelta, 0, viewer.ScrollableHeight);
        if (Math.Abs(target - viewer.VerticalOffset) < 0.01)
        {
            return; // At the end: the viewer around it may scroll instead.
        }

        viewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }
}
