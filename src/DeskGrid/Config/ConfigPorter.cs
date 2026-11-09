using System.IO;
using System.Text.Json;

namespace DeskGrid.Config;

/// <summary>Exports the settings to a file the user keeps, and reads such a file back (e.g. on another computer).</summary>
internal static class ConfigPorter
{
    public const string Extension = ".json";

    /// <summary>The name an export is offered under: <c>DeskGrid_2026_11_09_14_05.json</c>.</summary>
    public static string SuggestedFileName(DateTime now) => $"DeskGrid_{now:yyyy_MM_dd_HH_mm}{Extension}";

    /// <summary>Writes <paramref name="config"/> to <paramref name="path"/>; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it can't.</summary>
    public static void Export(AppConfig config, string path) => File.WriteAllText(path, ConfigStore.Serialize(config));

    /// <summary>
    /// Reads an exported file. Returns <see langword="null"/> and the reason when the file can't be read or isn't a DeskGrid
    /// settings file (valid JSON that has none of the settings in it counts as not one).
    /// </summary>
    public static AppConfig? TryImport(string path, out string problem)
    {
        problem = "";
        try
        {
            var json = File.ReadAllText(path);
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.EnumerateObject().Any(IsSetting))
                {
                    problem = "no DeskGrid settings in it";
                    return null;
                }
            }

            return ConfigStore.Deserialize(json);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = exception.Message;
            return null;
        }
    }

    private static bool IsSetting(JsonProperty property) =>
        typeof(AppConfig).GetProperty(property.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase) is not null;
}
