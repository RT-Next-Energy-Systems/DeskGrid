using System.Windows;

namespace BrimDeck;

/// <summary>
/// BrimDeck's styles, brushes and fonts. They used to be the WPF application's resources; BrimDeck now runs inside DeskGrid's process
/// and must not restyle DeskGrid's own windows, so they live in one dictionary that BrimDeck's windows merge into their own
/// resources (<see cref="DeckWindow"/>). The theme and the fonts are published into it, and every window that merged it follows.
/// </summary>
public static class DeckResources
{
    private static ResourceDictionary? _dictionary;

    public static ResourceDictionary Dictionary => _dictionary ??= Load();

    private static ResourceDictionary Load() =>
        new() { Source = new Uri("pack://application:,,,/BrimDeck;component/DeckResources.xaml", UriKind.Absolute) };
}
