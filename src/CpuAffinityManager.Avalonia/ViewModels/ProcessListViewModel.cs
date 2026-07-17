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

public partial class ProcessListViewModel : ViewModelBase
{
    private readonly IRuleEngine _ruleEngine;
    private readonly IEnforcementService _enforcementService;
    private readonly ICpuTopologyService _topoService;
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterMode = "All Processes";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadError = "";

    private List<ProcessItem> _allItems = new();
    public ObservableCollection<ProcessItem> Processes { get; } = new();
    public static string[] FilterModes { get; } = ["All Processes", "With Rules", "Job Enforced"];

    public MainWindowViewModel? Parent { get; set; }

    public ProcessListViewModel(IRuleEngine ruleEngine, IEnforcementService enforcementService, ICpuTopologyService topoService)
    {
        _ruleEngine = ruleEngine;
        _enforcementService = enforcementService;
        _topoService = topoService;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async void Refresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;
        IsLoading = true; LoadError = "";
        try
        {
            var items = await Task.Run(() => EnumerateProcesses(ct), ct);
            if (!ct.IsCancellationRequested) { _allItems = items; ApplyFilter(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error(ex, "Enum failed"); LoadError = ex.Message; }
        finally { if (!ct.IsCancellationRequested) IsLoading = false; }
    }

    private void ApplyFilter()
    {
        string query = (SearchText ?? "").Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(p => ProcessSearch.Matches(query, p.Name, p.Path, p.Pid)).ToList();
        Processes.Clear();
        foreach (var item in filtered) Processes.Add(item);
    }

    private List<ProcessItem> EnumerateProcesses(CancellationToken ct)
    {
        var result = new List<ProcessItem>();
        Process[]? procs = null;
        try { procs = Process.GetProcesses(); } catch (Exception ex) { Log.Warning(ex, "GetProcesses failed"); return result; }
        foreach (var proc in procs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                int pid; string name;
                try { pid = proc.Id; } catch { continue; }
                try { name = proc.ProcessName + ".exe"; } catch { continue; }
                string? path = null; try { if (!IsSystemPid(pid)) path = proc.MainModule?.FileName; } catch { }
                string aff = "N/A"; try { if (!IsSystemPid(pid)) aff = $"0x{proc.ProcessorAffinity.ToInt64():X}"; } catch { }
                var rule = _ruleEngine.Match(name, path ?? "");
                result.Add(new ProcessItem { Pid = pid, Name = name, Path = path ?? "(protected)", Affinity = aff, MatchedRule = rule?.Name ?? "", RuleLevel = rule?.Action.Level ?? "" });
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            finally { try { proc.Dispose(); } catch { } }
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
    }

    private static bool IsSystemPid(int pid) => pid is 0 or 4;

    // ── Context menu / inline actions ──

    [RelayCommand] private void SetAffinityPCores(ProcessItem item) => ApplyQuick(item, "p-cores|first-half", "hard-affinity");
    [RelayCommand] private void SetAffinityECores(ProcessItem item) => ApplyQuick(item, "e-cores|second-half", "hard-affinity");
    [RelayCommand] private void SetAffinityAllCores(ProcessItem item) => ApplyQuick(item, "all-cores", "hard-affinity");
    [RelayCommand] private void SetAffinityFirstHalf(ProcessItem item) => ApplyQuick(item, "first-half", "hard-affinity");
    [RelayCommand] private void SetAffinitySecondHalf(ProcessItem item) => ApplyQuick(item, "second-half", "hard-affinity");
    [RelayCommand] private void SetJobEnforced(ProcessItem item) => ApplyQuick(item, "p-cores|all-cores", "job-enforced");
    [RelayCommand] private void SetJobLocked(ProcessItem item) => ApplyQuick(item, "all-cores", "job-locked");

    [RelayCommand]
    private void ApplyMatchedRule(ProcessItem item)
    {
        try
        {
            var rule = _ruleEngine.Match(item.Name, item.Path);
            if (rule != null)
            {
                _enforcementService.Apply(item.Pid, rule, _topoService.Detect());
                if (Parent != null) Parent.StatusText = $"Applied rule '{rule.Name}' to PID {item.Pid}";
            }
        }
        catch (Exception ex) { Log.Error(ex, "Apply matched rule failed"); }
    }

    private void ApplyQuick(ProcessItem item, string mode, string level)
    {
        try
        {
            var topo = _topoService.Detect();
            var rule = new RuleEntry { Id = "quick", Name = "Quick Action", Action = new RuleAction { Mode = mode, Level = level } };
            bool ok = _enforcementService.Apply(item.Pid, rule, topo);
            if (Parent != null) Parent.StatusText = ok ? $"Applied {mode} [{level}] to PID {item.Pid}" : $"Failed PID {item.Pid}";
        }
        catch (Exception ex) { Log.Error(ex, "Quick apply failed"); }
    }
}

public partial class ProcessItem : ObservableObject
{
    [ObservableProperty] private int _pid;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _affinity = "";
    [ObservableProperty] private string _matchedRule = "";
    [ObservableProperty] private string _ruleLevel = "";
}
