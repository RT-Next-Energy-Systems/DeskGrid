using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using DeskGrid.Config;
using DeskGrid.Resources;

namespace DeskGrid.Tests;

public sealed class UiLanguageTests
{
    [Theory]
    [InlineData("English", "zh-TW", "en")]
    [InlineData("TraditionalChinese", "en-US", "zh-TW")]
    [InlineData("System", "zh-TW", "zh-TW")]
    [InlineData("System", "zh-HK", "zh-TW")]
    [InlineData("System", "zh-Hant", "zh-TW")]
    [InlineData("System", "zh-Hant-TW", "zh-TW")]
    [InlineData("System", "en-US", "en")]
    [InlineData("System", "ja-JP", "en")]
    [InlineData("System", "zh-CN", "en")]
    public void Resolve_FollowsTheChoice_OrWindowsLanguageForSystem(string language, string windows, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(Enum.Parse<AppLanguage>(language), new CultureInfo(windows)).Name);

    // The language policy (see AGENTS.md): every text exists in both languages, and a translation keeps the placeholders.
    [Fact]
    public void EveryEnglishTextHasAChineseTranslation_AndTheOtherWayRound()
    {
        var english = Texts(CultureInfo.InvariantCulture);
        var chinese = Texts(new CultureInfo("zh-TW"));

        Assert.Empty(english.Keys.Except(chinese.Keys));
        Assert.Empty(chinese.Keys.Except(english.Keys));
        foreach (var (key, text) in english)
        {
            Assert.True(Placeholders(text).SequenceEqual(Placeholders(chinese[key])), $"{key}: placeholders differ between the languages");
        }
    }

    private static Dictionary<string, string> Texts(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order()];
}

public sealed class WindowsDialogLanguageTests
{
    // The dialogs Windows shows (color picker, MessageBox buttons) read the language of the thread that shows them.
    [Theory]
    [InlineData("English", "en-US", 0x0409)]
    [InlineData("TraditionalChinese", "zh-TW", 0x0404)]
    public void SetWindowsDialogLanguage_ChangesTheLanguageOfTheCallingThread(string language, string culture, int expectedLangId)
    {
        // A thread of its own: the setting belongs to the thread, and must not leak into the test runner's.
        var langId = 0;
        var thread = new Thread(() =>
        {
            UiLanguage.SetWindowsDialogLanguage(Enum.Parse<AppLanguage>(language), new CultureInfo(culture));
            langId = (int)Windows.Win32.PInvoke.GetThreadUILanguage();
        });
        thread.Start();
        thread.Join();

        Assert.Equal(expectedLangId, langId);
    }
}
