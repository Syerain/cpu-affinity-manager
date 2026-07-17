using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CpuAffinityManager.Cpu;
using CpuAffinityManager.Engine;
using CpuAffinityManager.Enforcement;
using CpuAffinityManager.Monitoring;
using Serilog;

namespace CpuAffinityManager.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IRuleEngine _ruleEngine;
    private readonly ICpuTopologyService _topoService;
    private readonly IEnforcementService _enforcementService;
    private readonly IProcessMonitor _processMonitor;
    private readonly AffinityEnforcementWatchdog _watchdog;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _windowTitle = "CPU Affinity Manager";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _sidebarCpuInfo = "Detecting...";
    [ObservableProperty] private bool _isDashboardSelected = true;
    [ObservableProperty] private bool _isProcessesSelected;
    [ObservableProperty] private bool _isRulesSelected;
    [ObservableProperty] private bool _isSettingsSelected;

    public DashboardViewModel Dashboard { get; }
    public ProcessListViewModel ProcessList { get; }
    public RuleListViewModel RuleList { get; }
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel()
    {
        _ruleEngine = new RuleEngine();
        _topoService = new CpuTopologyService();
        _enforcementService = new EnforcementService(_ruleEngine, _topoService);
        _processMonitor = new WmiProcessMonitor();
        _watchdog = new AffinityEnforcementWatchdog(_ruleEngine, _topoService, _enforcementService);

        Dashboard = new DashboardViewModel(_ruleEngine, _topoService);
        ProcessList = new ProcessListViewModel(_ruleEngine, _enforcementService, _topoService) { Parent = this };
        RuleList = new RuleListViewModel(_ruleEngine) { Parent = this };
        Settings = new SettingsViewModel();

        CurrentPage = Dashboard;
    }

    [RelayCommand]
    private void Initialize()
    {
        try
        {
            var topo = _topoService.Detect();
            Log.Information("Topology: {Topo}", topo);
            SidebarCpuInfo = $"{topo.TotalLogicalProcessors} threads · {topo.PcoreCount}P + {topo.EcoreCount}E";
            LoadRules();
            Dashboard.Refresh();
            StartProcessMonitor();
            _watchdog.Start();
        }
        catch (Exception ex) { Log.Error(ex, "Init failed"); StatusText = $"Error: {ex.Message}"; }
    }

    private void LoadRules()
    {
        try
        {
            string path = RuleConfigPath.FindDefaultRules();
            if (System.IO.File.Exists(path)) _ruleEngine.Load(path);
            Log.Information("Loaded {N} rules from {Path}", _ruleEngine.Rules.Count, path);
        }
        catch (Exception ex) { Log.Warning(ex, "Load rules failed"); }
    }

    private void SaveRules()
    {
        try { _ruleEngine.Save(RuleConfigPath.FindDefaultRules()); }
        catch (Exception ex) { Log.Error(ex, "Save rules failed"); }
    }

    private void StartProcessMonitor()
    {
        try
        {
            _processMonitor.Start(e =>
            {
                try
                {
                    string? path = EnforcementService.GetProcessPath(e.Pid);
                    if (path == null) return;
                    var rule = _ruleEngine.Match(e.ProcessName, path);
                    if (rule != null)
                    {
                        _enforcementService.Apply(e.Pid, rule, _topoService.Detect());
                        StatusText = $"Auto-applied '{rule.Name}' → {e.ProcessName} (PID {e.Pid})";
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    // ── Rule management (exposed for RuleListViewModel) ──

    public void AddOrUpdateRule(RuleEntry rule)
    {
        _ruleEngine.AddRule(rule);
        SaveRules();
    }

    public void RemoveRule(string ruleId)
    {
        _ruleEngine.RemoveRule(ruleId);
        SaveRules();
    }

    public void NotifyRuleChanged()
    {
        SaveRules();
        Dashboard.Refresh();
    }

    /// <summary>
    /// When a rule is toggled on/off, scan all running processes and apply or relax affinity.
    /// Mirrors the WPF MainWindow.ApplyRuleToggleToRunningProcesses behaviour.
    /// </summary>
    public void ApplyRuleToggleToRunningProcesses(RuleEntry toggledRule, bool enabled)
    {
        Task.Run(() =>
        {
            int affected = 0;
            var topology = _topoService.Detect();

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    int pid = process.Id;
                    if (pid is 0 or 4)
                        continue;

                    string name = process.ProcessName + ".exe";
                    string? path = null;
                    try { path = process.MainModule?.FileName; } catch { }

                    if (!RuleMatchesProcess(toggledRule, name, path ?? ""))
                        continue;

                    if (enabled)
                    {
                        var activeRule = _ruleEngine.Match(name, path ?? "");
                        if (activeRule?.Id == toggledRule.Id &&
                            _enforcementService.Apply(pid, toggledRule, topology))
                        {
                            affected++;
                        }
                    }
                    else
                    {
                        var replacementRule = _ruleEngine.Match(name, path ?? "");
                        bool ok = replacementRule != null
                            ? _enforcementService.Apply(pid, replacementRule, topology)
                            : _enforcementService.Relax(pid, topology);

                        if (ok)
                            affected++;
                    }
                }
                catch
                {
                    // Process may exit or deny access during toggle scan.
                }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }

            StatusText = enabled
                ? $"Rule '{toggledRule.Name}' enabled — applied to {affected} process(es)"
                : $"Rule '{toggledRule.Name}' disabled — relaxed {affected} process(es)";
            ProcessList.Refresh();
        });
    }

    private static bool RuleMatchesProcess(RuleEntry rule, string processName, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(rule.Match.Process) ||
            !Wildcard.Match(processName, rule.Match.Process, ignoreCase: true))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.Path) &&
            !Wildcard.MatchPath(fullPath, rule.Match.Path, ignoreCase: true))
        {
            return false;
        }

        return rule.Match.Exclude == null ||
               !rule.Match.Exclude.Any(pattern =>
                   Wildcard.Match(processName, pattern, ignoreCase: true));
    }

    /// <summary>Called by RuleListViewModel when user wants to add/edit a rule.</summary>
    public void EditRule(RuleEntry? existing)
    {
        RuleEditRequested?.Invoke(existing);
    }

    public event Action<RuleEntry?>? RuleEditRequested;

    // ── Navigation ──

    [RelayCommand] private void NavigateToDashboard() { CurrentPage = Dashboard; SetNav(true, false, false, false); Dashboard.Refresh(); }
    [RelayCommand] private void NavigateToProcesses() { CurrentPage = ProcessList; SetNav(false, true, false, false); ProcessList.Refresh(); }
    [RelayCommand] private void NavigateToRules() { CurrentPage = RuleList; SetNav(false, false, true, false); RuleList.Refresh(); }
    [RelayCommand] private void NavigateToSettings() { CurrentPage = Settings; SetNav(false, false, false, true); }

    private void SetNav(bool d, bool p, bool r, bool s) { IsDashboardSelected = d; IsProcessesSelected = p; IsRulesSelected = r; IsSettingsSelected = s; }

    [RelayCommand]
    private async Task ScanNowAsync()
    {
        IsScanning = true; StatusText = "Scanning...";
        int n = await Task.Run(() => _enforcementService.ScanAndEnforce());
        StatusText = $"Scan complete — {n} process(es) affected";
        IsScanning = false;
    }
}
