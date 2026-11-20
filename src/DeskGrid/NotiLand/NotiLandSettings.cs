using System.Windows;
using BrimDeck;

namespace DeskGrid.NotiLand;

/// <summary>One page of NotiLand's settings: BrimDeck's own page number, the group caption it starts (empty when it starts none) and its title.</summary>
internal sealed record NotiLandPage(int Number, string Group, string Title);

/// <summary>
/// NotiLand's settings pages (BrimDeck's, <see cref="SettingsPages"/>), shown inside DeskGrid's settings window: the window lists
/// <see cref="Pages"/> on its left, puts <see cref="Element"/> on its right and calls <see cref="Show"/> for the one chosen.
/// </summary>
internal sealed class NotiLandSettings : IDisposable
{
    private readonly SettingsPages _pages;

    public NotiLandSettings(SettingsPages pages) => _pages = pages;

    /// <summary>What shows the page chosen (it scrolls by itself).</summary>
    public FrameworkElement Element => _pages;

    /// <summary>The pages in order, titled in NotiLand's language (which is DeskGrid's).</summary>
    public IReadOnlyList<NotiLandPage> Pages => [.. SettingsPages.Pages().Select(page => new NotiLandPage(page.Page, page.Group, page.Title))];

    /// <summary>The page being shown.</summary>
    public int Current => _pages.CurrentPage;

    public void Show(int page) => _pages.ShowPage(page);

    /// <summary>Draws the page again (the language changed).</summary>
    public void Refresh() => _pages.Refresh();

    /// <summary>The settings window lost the focus: a preview of NotiLand's panel ends.</summary>
    public void WindowDeactivated() => _pages.HostDeactivated();

    /// <summary>The user went to a page that is not NotiLand's.</summary>
    public void Leave() => _pages.Leave();

    public void Dispose() => _pages.Release();
}
