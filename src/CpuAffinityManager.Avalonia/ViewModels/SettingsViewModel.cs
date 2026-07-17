using CommunityToolkit.Mvvm.ComponentModel;

namespace CpuAffinityManager.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private bool _enableWmiMonitor = true;
    [ObservableProperty] private bool _confirmBeforeApply;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private int _selectedLanguageIndex = App.LanguageIndex;
    [ObservableProperty] private string _appVersion = "v2.0.0";

    public static string[] ThemeOptions { get; } = ["System Default", "Light", "Dark"];
    public static string[] LanguageOptions { get; } = ["English", "简体中文"];

    partial void OnSelectedLanguageIndexChanged(int value) => App.SetLanguage(value);
}
