using System.Runtime.CompilerServices;
using BrimDeck.Core;

namespace BrimDeck;

// DeskGrid build: the first thing that runs when the BrimDeck assembly loads. BrimDeck is not the entry assembly any more.
internal static class DeckStartup
{
    [ModuleInitializer]
    internal static void Init() => ApplicationVersion.UseAssembly(typeof(DeckStartup).Assembly);
}
