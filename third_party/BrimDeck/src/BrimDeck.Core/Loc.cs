using System.Globalization;
using System.Text.RegularExpressions;

namespace BrimDeck.Core;

// The interface language. Each text is written as a pair where it is used: Loc.T("中文", "English").
// Some Chinese labels also serve as identifiers: quota names that are compared, split at "·" and kept with retained
// quotas, and player names that route commands and lyrics. They stay Chinese in the data and are translated where they
// are shown, by Label.
public static class Loc
{
    // "zh-CN" is the tag BrimDeck stores in its settings for the Chinese interface; the text itself is Traditional Chinese in this build.
    public const string Chinese = "zh-CN", English = "en-US";
    public static bool IsEnglish { get; private set; }
    public static string Language => IsEnglish ? English : Chinese;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    public static void Use(string language) => IsEnglish = Normalize(language) == English;
    public static string Normalize(string? language) => language == Chinese ? Chinese : English;
    public static string T(string chinese, string english) => IsEnglish ? english : chinese;
    // An English count with its noun: "1 item", "2 items".
    public static string Count(long count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    // The Windows display language decides the language of a first start and of "恢復預設". Every Chinese variant,
    // Traditional included, shows Simplified Chinese; any other language shows English.
    public static string SystemLanguage() => SystemLanguage(CultureInfo.CurrentUICulture);
    public static string SystemLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName == "zh" ? Chinese : English;

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["5 小時額度"] = "5-hour limit", ["每週額度"] = "Weekly limit", ["每月額度"] = "Monthly limit", ["主要額度"] = "Primary limit", ["次要額度"] = "Secondary limit",
        ["賬期額度"] = "Billing period limit", ["套餐額度"] = "Plan limit",
        ["忙時每日額度"] = "Daily busy-hour limit", ["閒時每日額度"] = "Daily idle-hour limit", ["外部模型"] = "Outside models", ["內部模型"] = "Internal models",
        ["5 小時"] = "5-hour", ["每日"] = "Daily", ["每週"] = "Weekly", ["每月"] = "Monthly", ["主要"] = "Primary", ["次要"] = "Secondary",
        ["賬期"] = "Billing period", ["套餐"] = "Plan", ["其他"] = "Other",
        ["Cursor 模型"] = "Cursor models", ["其他模型"] = "Other models", ["工具呼叫"] = "Tool calls",
        ["賬戶餘額"] = "Account balance", ["金鑰餘額"] = "Key balance", ["今日扣費"] = "Charged today", ["本月扣費"] = "Charged this month", ["預算餘額"] = "Budget left", ["金鑰累計"] = "Key total",
        ["贈送餘額"] = "Granted balance", ["充值餘額"] = "Topped-up balance", ["凍結金額"] = "Frozen", ["現金餘額"] = "Cash balance", ["代金券餘額"] = "Voucher balance",["累計消費"] = "Total cost", ["加油包"] = "Extra usage", ["額外積分"] = "Extra credits", ["本月加油包"] = "Extra usage this month",
        ["次"] = "calls", ["積分"] = "credits", ["令牌"] = "tokens",
        ["網易雲音樂"] = "NetEase Cloud Music", ["QQ 音樂"] = "QQ Music", ["酷狗音樂"] = "KuGou Music", ["愛奇藝"] = "iQIYI",
    };

    // Translates a label that the data keeps in Chinese. Labels made of parts joined by "·" are translated part by part;
    // labels this table does not know, such as those a custom script returns, are shown as they are.
    public static string Label(string text)
    {
        if (!IsEnglish || text.Length == 0) return text;
        if (text.Contains('·')) return string.Join(" · ", text.Split('·').Select(part => Label(part.Trim())));
        if (Labels.TryGetValue(text, out var english)) return english;
        if (Regex.Match(text, @"^(\d+) 分鐘(額度)?$") is { Success: true } minutes)
            return minutes.Groups[1].Value + "-minute" + (minutes.Groups[2].Success ? " limit" : "");
        if (Regex.Match(text, @"^(\d+) 小時(額度)?$") is { Success: true } hours)
            return hours.Groups[1].Value + "-hour" + (hours.Groups[2].Success ? " limit" : "");
        if (text.EndsWith(" 每周", StringComparison.Ordinal)) return text[..^3] + " weekly";
        return text;
    }
}
