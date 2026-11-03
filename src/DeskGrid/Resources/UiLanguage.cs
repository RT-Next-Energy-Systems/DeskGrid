using System.Globalization;
using DeskGrid.Config;

namespace DeskGrid.Resources;

/// <summary>
/// Picks the language of DeskGrid's texts. <see cref="Apply"/> switches it for the whole process at once: texts are
/// looked up when a window, menu or dialog is built, so what opens next speaks the new language.
/// </summary>
internal static class UiLanguage
{
    private static readonly CultureInfo English = new("en");
    private static readonly CultureInfo TraditionalChinese = new("zh-TW");

    // Windows' own language, read before Apply changes it, so that "System" can be chosen again later.
    private static readonly CultureInfo Windows = CultureInfo.CurrentUICulture;

    /// <summary>The culture whose resources <paramref name="language"/> stands for, given Windows' <paramref name="windows"/> language.</summary>
    public static CultureInfo Resolve(AppLanguage language, CultureInfo windows) => language switch
    {
        AppLanguage.English => English,
        AppLanguage.TraditionalChinese => TraditionalChinese,
        _ => IsTraditionalChinese(windows) ? TraditionalChinese : English,
    };

    public static void Apply(AppLanguage language)
    {
        var culture = Resolve(language, Windows);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Strings.Culture = culture;
    }

    // zh-TW, zh-HK, zh-MO and the script-tagged zh-Hant-*; Simplified Chinese has no texts of its own and gets English.
    private static bool IsTraditionalChinese(CultureInfo culture) =>
        culture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
        || culture.Name is "zh-TW" or "zh-HK" or "zh-MO";
}
