using System.Globalization;
using DeskGrid.Config;
using Windows.Win32;
using Windows.Win32.Foundation;

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
        SetWindowsDialogLanguage(language, culture);
    }

    // Windows' own dialogs (the color picker, the folder picker, a MessageBox's buttons) take their language from the
    // thread that shows them, not from CultureInfo. A chosen language is set on this thread explicitly; "follow
    // Windows" clears it. Call this on the UI thread.
    internal static unsafe void SetWindowsDialogLanguage(AppLanguage language, CultureInfo culture)
    {
        // A list of names, each ended by a terminator, plus one more at its end (the string's own); empty clears the
        // thread's list. The neutral "en" of the resources is "en-US" to Windows.
        var names = language == AppLanguage.System ? "\0" : (culture.Name == "en" ? "en-US" : culture.Name) + "\0";
        fixed (char* list = names)
        {
            PInvoke.SetThreadPreferredUILanguages(PInvoke.MUI_LANGUAGE_NAME, new PCZZWSTR(list), out _);
        }
    }

    // zh-TW, zh-HK, zh-MO and the script-tagged zh-Hant-*; Simplified Chinese has no texts of its own and gets English.
    private static bool IsTraditionalChinese(CultureInfo culture) =>
        culture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
        || culture.Name is "zh-TW" or "zh-HK" or "zh-MO";
}
