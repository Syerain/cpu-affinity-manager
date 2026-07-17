namespace CpuAffinityManager.Engine;

/// <summary>
/// Resolves the default rules file from app, test, and published output folders.
/// </summary>
public static class RuleConfigPath
{
    public const string DefaultFileName = "default-rules.json";

    public static string FindDefaultRules(string? baseDirectory = null)
    {
        baseDirectory = Path.GetFullPath(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory);

        foreach (string directory in EnumerateSelfAndParents(baseDirectory))
        {
            string candidate = Path.Combine(directory, "config", DefaultFileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(baseDirectory, "config", DefaultFileName);
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
