using System.Windows;
using CpuAffinityManager;  // LogConfig
using CpuAffinityManager.Engine;
using Serilog;

namespace CpuAffinityManager.App;

public partial class App : Application
{
    public static int LanguageIndex { get; private set; }
    private const string LanguageDictionaryPrefix = "Resources/Strings.";

    public static void SetLanguage(int languageIndex, bool persist = true)
    {
        if (Current is not App app)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.StartsWith(LanguageDictionaryPrefix, StringComparison.OrdinalIgnoreCase) == true);
        if (existing != null)
            dictionaries.Remove(existing);

        LanguageIndex = languageIndex == 1 ? 1 : 0;
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"{LanguageDictionaryPrefix}{(LanguageIndex == 1 ? "zh-CN" : "en-US")}.xaml", UriKind.Relative)
        });
        if (persist)
            UiPreferences.SaveLanguageIndex(LanguageIndex);
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        SetLanguage(UiPreferences.LoadLanguageIndex(), persist: false);
        LogConfig.Initialize("wpf");
        Log.Information("CPU Affinity Manager (WPF) starting...");

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogConfig.Shutdown();
        base.OnExit(e);
    }
}
