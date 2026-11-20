using System.Windows;
using System.Windows.Controls;

namespace BrimDeck;

/// <summary>
/// The base of every BrimDeck window. It merges <see cref="DeckResources"/> into the window, so BrimDeck's styles apply to it and to
/// what it hangs (menus, tips, pop-ups) and to nothing else in the process, and gives it BrimDeck's tooltip timing, which used to be
/// set for every element of the application.
/// </summary>
public class DeckWindow : Window
{
    public DeckWindow()
    {
        Resources.MergedDictionaries.Add(DeckResources.Dictionary);
        ToolTipService.SetInitialShowDelay(this, 120);
        ToolTipService.SetBetweenShowDelay(this, 400);
        ToolTipService.SetShowDuration(this, 20000);
    }

    /// <summary>For tests of the host: whether <paramref name="element"/> belongs to one of BrimDeck's windows.</summary>
    public static bool HoldsForTest(DependencyObject? element) => Holds(element);

    /// <summary>Whether <paramref name="element"/> belongs to one of BrimDeck's windows (its visual or logical parents lead to one).</summary>
    internal static bool Holds(DependencyObject? element)
    {
        for (var node = element; node is not null; node = Parent(node))
        {
            if (node is DeckWindow) return true;
        }

        return false;
    }

    private static DependencyObject? Parent(DependencyObject node)
    {
        try
        {
            return (node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : null)
                ?? LogicalTreeHelper.GetParent(node);
        }
        catch (InvalidOperationException) { return null; }
    }
}
