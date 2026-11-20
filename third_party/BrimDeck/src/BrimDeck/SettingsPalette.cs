using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using BrimDeck.Core;

namespace BrimDeck;

// Settings own their palette; the floating usage panel deliberately keeps its existing visual design.
internal sealed record SettingsPalette(bool Dark)
{
    // DeskGrid build: the settings pages are drawn in the colours of DeskGrid's own settings window (Fluent's greys and the
    // system's accent colour), not BrimDeck's.
    public string Surface => Dark ? "#202020" : "#F3F3F3";
    public string Sidebar => Dark ? "#1A1A1A" : "#EBEBEB";
    public string Primary => Dark ? "#FFFFFF" : "#1A1A1A";
    public string Secondary => Dark ? "#C5C5C5" : "#5C5C5C";
    public string Tertiary => Dark ? "#A0A0A0" : "#6B6B6B";
    public string Divider => Dark ? "#3A3A3A" : "#DADADA";
    public string RowDivider => Dark ? "#333333" : "#E3E3E3";
    public string Inset => Dark ? "#2D2D2D" : "#EBEBEB";
    public string Input => Dark ? "#2D2D2D" : "#FDFDFD";
    public string Popup => Dark ? "#2C2C2C" : "#F9F9F9";
    public string Border => Dark ? "#454545" : "#D0D0D0";
    public string InputBorder => Dark ? "#8A8A8A" : "#7A7A7A";
    // The system's accent colour.
    public string Accent => SystemAccent;
    public string Negative => Dark ? "#FF99A4" : "#C42B1C";
    public string Positive => Dark ? "#6CCB5F" : "#0F7B0F";
    public string Link => SystemAccent;
    private static string SystemAccent
    {
        get { var c = SystemColors.AccentColor; return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"; }
    }
    public void Apply(ResourceDictionary resources)
    {
        void Brush(string key, string color) => resources[key] = UI.Brush(color);
        Brush("SettingsTextPrimary", Primary); Brush("SettingsTextSecondary", Secondary);
        Brush("SettingsFillHover", Dark ? "#2F2F2F" : "#E6E6E6");
        Brush("SettingsFillPressed", Dark ? "#3A3A3A" : "#DADADA");
        Brush("SettingsFillSelected", Dark ? "#3F3F3F" : "#FFFFFF");
        Brush("SettingsNavSelected", Dark ? "#2D2D2D" : "#FFFFFF");
        Brush("SettingsAccent", SystemAccent);
        // The fill of buttons with white text on the accent: at least 4.5:1 for that text, at rest and under the pointer, in both
        // modes (the dark mode's own accent is too light for it).
        Brush("SettingsAccentFill", SystemAccent);
        // A small label that is on (a category kept): the accent's text on a pale wash of it.
        Brush("SettingsAccentText", Accent); Brush("SettingsAccentTint", Dark ? "#2A3A3C" : "#DCEEF0");
        // A deeper wash, for a part inside something already on the pale wash (the monitor sketch's memory and rates).
        Brush("SettingsAccentTintStrong", Dark ? "#35585C" : "#BFDDE1");
        Brush("SettingsAccentHover", SystemAccent);
        Brush("SettingsSliderAccent", Accent); Brush("SettingsTrackBrush", Dark ? "#4A4A4A" : "#D0D0D0");
        Brush("SettingsBorder", Border); Brush("SettingsInput", Input); Brush("SettingsPopup", Popup);
        Brush("SettingsSwatchBorder", Dark ? "#6A6A6A" : "#B0B0B0");
        Brush("SettingsScrollThumb", Dark ? "#6A6A6A" : "#B8B8B8");
        Brush("SettingsScrollThumbHover", Dark ? "#8A8A8A" : "#989898");
        Brush("SettingsScrollThumbPressed", Dark ? "#A5A5A5" : "#7A7A7A");
        Brush("SettingsInset", Inset); Brush("SettingsSurface", Surface);
        Brush("SettingsSwitchOff", InputBorder); Brush("SettingsInputBorder", InputBorder);
        Brush("SettingsError", Negative);
    }
    public static bool IsDark(SettingsTheme theme)
    {
        if (theme != SettingsTheme.System) return theme == SettingsTheme.Dark;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }
}
