namespace DeskGrid.Island;

/// <summary>
/// The parts the expanded NotiLand card can show. The user picks which, and in what order, in Settings; the choice is stored as
/// the ids of the shown parts joined by commas (<see langword="null"/> = every part in the default order).
/// </summary>
internal static class CardSections
{
    public const string Players = "players";
    public const string Usage = "usage";
    public const string System = "system";
    public const string Folders = "folders";
    public const string Shelf = "shelf";

    /// <summary>Every part, in the default order.</summary>
    public static readonly IReadOnlyList<string> All = [Players, Usage, System, Folders, Shelf];

    /// <summary>The parts to show, in order: the stored choice with unknown and repeated ids dropped; every part when nothing is stored.</summary>
    public static IReadOnlyList<string> Parse(string? stored)
    {
        if (stored is null)
        {
            return All;
        }

        var shown = new List<string>();
        foreach (var id in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var known = All.FirstOrDefault(a => a.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (known is not null && !shown.Contains(known))
            {
                shown.Add(known);
            }
        }

        return shown;
    }

    /// <summary>What to store for these shown parts: <see langword="null"/> when it is the default (all, in order).</summary>
    public static string? Store(IEnumerable<string> shown)
    {
        var list = shown.ToList();
        return list.SequenceEqual(All) ? null : string.Join(',', list);
    }

    /// <summary>Every part as the settings list shows it: the shown ones in their order, then the hidden ones.</summary>
    public static IReadOnlyList<(string Id, bool Shown)> ForEditing(string? stored)
    {
        var shown = Parse(stored);
        return [.. shown.Select(id => (id, true)), .. All.Where(id => !shown.Contains(id)).Select(id => (id, false))];
    }
}
