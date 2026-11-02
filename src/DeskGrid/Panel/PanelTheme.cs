using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Diagnostics;
using Microsoft.Win32;

namespace DeskGrid.Panel;

/// <summary>How a panel looks, resolved from the user's <see cref="PanelBackground"/> and the system theme.</summary>
/// <param name="UseAcrylic">Let DWM paint the system acrylic; <paramref name="Background"/> is then only the fallback.</param>
internal sealed record PanelTheme(bool UseAcrylic, Color Background, Color Foreground)
{
    private static readonly Color DarkSurface = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color LightSurface = Color.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly Color DarkText = Color.FromRgb(0x1A, 0x1A, 0x1A);

    public bool IsDark => Luminance(Foreground) > 0.5;

    public Color Hover => IsDark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00);

    /// <summary>The rounded square behind a folder's preview, like a widget's on the desktop.</summary>
    public Color Tile => IsDark ? Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

    public Color Outline =>IsDark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

    /// <summary>Whether apps use the dark theme (Settings > Personalization > Colors).</summary>
    public static bool SystemUsesDarkTheme =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;

    public static PanelTheme Resolve(PanelBackground setting, bool systemDark, bool acrylicSupported) => setting.Style switch
    {
        PanelBackgroundStyle.Dark => new(false, WithOpacity(DarkSurface, 0.82), Colors.White),
        PanelBackgroundStyle.Light => new(false, WithOpacity(Colors.White, 0.8), DarkText),
        PanelBackgroundStyle.Custom => Custom(setting),
        _ => new(acrylicSupported, systemDark ? DarkSurface : LightSurface, systemDark ? Colors.White : DarkText),
    };

    private static PanelTheme Custom(PanelBackground setting)
    {
        var color = ParseColor(setting.CustomColor) ?? ParseColor(new PanelBackground().CustomColor)!.Value;
        return new(false, WithOpacity(color, setting.CustomOpacity), Luminance(color) > 0.5 ? DarkText : Colors.White);
    }

    /// <summary>Parses "#RRGGBB" (or a named color); <see langword="null"/> if invalid.</summary>
    internal static Color? ParseColor(string text)
    {
        try
        {
            return ColorConverter.ConvertFromString(text) is Color color ? color : null;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            Log.Info($"Invalid panel color \"{text}\"; using the default.");
            return null;
        }
    }

    private static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    // Perceived brightness, 0 (black) to 1 (white).
    private static double Luminance(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
}
