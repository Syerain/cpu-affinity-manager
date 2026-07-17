using CpuAffinityManager.Engine;

namespace CpuAffinityManager.Tests;

public class UiPreferencesTests
{
    [Fact]
    public void LanguageIndex_RoundTripsAndNormalizesInvalidValues()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"cpu-affinity-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directory, "ui-preferences.json");

        try
        {
            UiPreferences.SaveLanguageIndex(1, filePath);
            Assert.Equal(1, UiPreferences.LoadLanguageIndex(filePath));

            UiPreferences.SaveLanguageIndex(99, filePath);
            Assert.Equal(0, UiPreferences.LoadLanguageIndex(filePath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
