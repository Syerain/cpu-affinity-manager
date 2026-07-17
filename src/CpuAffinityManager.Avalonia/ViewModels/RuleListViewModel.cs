using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CpuAffinityManager.Engine;

namespace CpuAffinityManager.Avalonia.ViewModels;

public partial class RuleListViewModel : ViewModelBase
{
    private readonly IRuleEngine _ruleEngine;

    [ObservableProperty] private bool _hasRules;
    public ObservableCollection<RuleItem> Rules { get; } = new();
    public static string[] AvailableModes { get; } = ["all-cores", "p-cores", "e-cores", "p-cores-smt", "p-cores-no-smt", "first-half", "second-half", "custom"];
    public static string[] AvailableLevels { get; } = ["soft-cpu-sets", "hard-affinity", "job-enforced", "job-locked"];

    public MainWindowViewModel? Parent { get; set; }

    public RuleListViewModel(IRuleEngine ruleEngine) { _ruleEngine = ruleEngine; }

    public void Refresh()
    {
        Rules.Clear();
        foreach (var r in _ruleEngine.Rules)
            Rules.Add(new RuleItem { Id = r.Id, Name = r.Name, Enabled = r.Enabled, ProcessPattern = r.Match.Process, PathPattern = r.Match.Path ?? "", Mode = r.Action.Mode, Level = r.Action.Level, LockBreakaway = r.Action.Lock, OnToggled = HandleRuleToggled });
        HasRules = Rules.Count > 0;
    }

    private void HandleRuleToggled(RuleItem item, bool enabled)
    {
        var rule = _ruleEngine.Rules.FirstOrDefault(r => r.Id == item.Id);
        if (rule == null) return;

        // Skip if already in the desired state (e.g. during Refresh / initial load)
        if (rule.Enabled == enabled) return;

        rule.Enabled = enabled;
        Parent?.NotifyRuleChanged();
        Parent?.ApplyRuleToggleToRunningProcesses(rule, enabled);
        if (Parent != null)
            Parent.StatusText = enabled
                ? $"Rule '{rule.Name}' enabled"
                : $"Rule '{rule.Name}' disabled";
    }

    [RelayCommand]
    private void ToggleRule(RuleItem item)
    {
        var rule = _ruleEngine.Rules.FirstOrDefault(r => r.Id == item.Id);
        if (rule != null) { rule.Enabled = !rule.Enabled; Parent?.NotifyRuleChanged(); Refresh(); }
    }

    [RelayCommand]
    private void EditRule(RuleItem item)
    {
        // Let MainWindowViewModel handle dialog
        var rule = _ruleEngine.Rules.FirstOrDefault(r => r.Id == item.Id);
        if (rule != null) Parent?.EditRule(rule);
    }

    [RelayCommand]
    private void DeleteRule(RuleItem item)
    {
        Parent?.RemoveRule(item.Id);
        Refresh();
        Parent?.Dashboard.Refresh();
    }

    [RelayCommand]
    private void AddRule()
    {
        Parent?.EditRule(null);
    }

    public void OnRuleSaved() { Refresh(); Parent?.Dashboard.Refresh(); }
}

public partial class RuleItem : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _processPattern = "";
    [ObservableProperty] private string _pathPattern = "";
    [ObservableProperty] private string _mode = "";
    [ObservableProperty] private string _level = "";
    [ObservableProperty] private bool _lockBreakaway;

    /// <summary>Callback invoked when the user toggles the enable/disable switch.</summary>
    public Action<RuleItem, bool>? OnToggled { get; set; }

    partial void OnEnabledChanged(bool value)
    {
        OnToggled?.Invoke(this, value);
    }
}
