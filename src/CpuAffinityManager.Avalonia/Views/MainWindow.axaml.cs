using Avalonia.Controls;
using CpuAffinityManager.Avalonia.ViewModels;
using CpuAffinityManager.Engine;

namespace CpuAffinityManager.Avalonia.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        _vm = vm;

        // Handle rule edit dialog requests
        vm.RuleEditRequested += async (existing) =>
        {
            var dlg = new RuleEditorWindow(existing);
            var result = await dlg.ShowDialog<RuleEntry?>(this);
            if (result != null)
            {
                vm.AddOrUpdateRule(result!);
                vm.RuleList.OnRuleSaved();
            }
        };

        vm.InitializeCommand.Execute(null);
    }
}
