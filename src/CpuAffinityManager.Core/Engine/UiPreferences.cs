using System.Text.Json;
using System.Text.Json.Serialization;

namespace CpuAffinityManager.Engine;

/// <summary>
/// Persists frontend-only preferences outside the rule configuration shipped with the application.
/// </summary>
public static class UiPreferences
{
    private const string FileName = "ui-preferences.json";

    public static int LoadLanguageIndex(string? filePath = null)
    {
        try
        {
            filePath ??= FilePath;
            if (!File.Exists(filePath))
                return 0;

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize(json, UiPreferencesJsonContext.Default.UiPreferencesData)?.LanguageIndex == 1 ? 1 : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static void SaveLanguageIndex(int languageIndex, string? filePath = null)
    {
        try
        {
            filePath ??= FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string json = JsonSerializer.Serialize(
                new UiPreferencesData { LanguageIndex = languageIndex == 1 ? 1 : 0 },
                UiPreferencesJsonContext.Default.UiPreferencesData);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Preference persistence must never prevent the application from starting or switching languages.
        }
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CpuAffinityManager",
        FileName);
}

public sealed class UiPreferencesData
{
    [JsonPropertyName("languageIndex")]
    public int LanguageIndex { get; set; }
}

[JsonSerializable(typeof(UiPreferencesData))]
public partial class UiPreferencesJsonContext : JsonSerializerContext
{
}
