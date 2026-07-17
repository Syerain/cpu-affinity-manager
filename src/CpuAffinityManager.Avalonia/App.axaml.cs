using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using CpuAffinityManager.Avalonia.ViewModels;
using CpuAffinityManager.Avalonia.Views;
using CpuAffinityManager.Engine;
using Serilog;

namespace CpuAffinityManager.Avalonia;

public partial class App : Application
{
    private const string LanguageResourcePrefix = "avares://CpuAffinityManager.Avalonia/Assets/Strings.";

    public static int LanguageIndex { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        SetLanguage(UiPreferences.LoadLanguageIndex(), persist: false);
    }

    public static void SetLanguage(int languageIndex, bool persist = true)
    {
        if (Current is not App app)
            return;

        var existing = app.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(dictionary => dictionary.Source?.OriginalString.StartsWith(LanguageResourcePrefix, StringComparison.OrdinalIgnoreCase) == true);
        if (existing != null)
            app.Resources.MergedDictionaries.Remove(existing);

        LanguageIndex = languageIndex == 1 ? 1 : 0;
        var source = new Uri($"{LanguageResourcePrefix}{(LanguageIndex == 1 ? "zh-CN" : "en-US")}.axaml");
        app.Resources.MergedDictionaries.Add(new ResourceInclude(source) { Source = source });
        if (persist)
            UiPreferences.SaveLanguageIndex(LanguageIndex);
    }

    public static string GetText(string key)
    {
        return Current is App app && app.Resources.TryGetValue(key, out var value)
            ? value as string ?? key
            : key;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate data annotation validations
            DisableAvaloniaDataAnnotationValidation();

            var mainVm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var pluginsToRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();
        foreach (var plugin in pluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
