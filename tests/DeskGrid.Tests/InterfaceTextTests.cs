using System.Collections;
using System.Globalization;
using DeskGrid.Resources;
using Xunit;

namespace DeskGrid.Tests;

public class InterfaceTextTests
{
    // The few hints that stay: each shows a state or names what a button does. See "Interface text" in AGENTS.md.
    private static readonly string[] AllowedHints = ["SettingsHotkeysHint", "SettingsHotkeysHintNone", "UpdateHint", "CardShelfHint"];

    [Fact]
    public void NoNewExplanatoryTexts_KeysEndingInHintOrDescription_AreOnTheShortAllowList()
    {
        var keys = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>().Select(entry => (string)entry.Key);
        var offenders = keys.Where(key => (key.EndsWith("Hint", StringComparison.Ordinal) || key.EndsWith("Description", StringComparison.Ordinal))
            && !AllowedHints.Contains(key)).ToList();
        Assert.True(offenders.Count == 0, "The interface does not explain itself in sentences (AGENTS.md, \"Interface text\"): " + string.Join(", ", offenders));
    }
}
