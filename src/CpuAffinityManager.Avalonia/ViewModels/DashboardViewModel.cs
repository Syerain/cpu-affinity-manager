using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CpuAffinityManager.Cpu;
using CpuAffinityManager.Engine;
using Avalonia.Media;

namespace CpuAffinityManager.Avalonia.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IRuleEngine _ruleEngine;
    private readonly ICpuTopologyService _topoService;

    [ObservableProperty] private string _processCount = "--";
    [ObservableProperty] private string _rulesActive = "--";
    [ObservableProperty] private string _pCoreCount = "--";
    [ObservableProperty] private string _eCoreCount = "--";
    [ObservableProperty] private bool _hasTopology;

    public ObservableCollection<CoreVisualItem> CoreItems { get; } = new();
    public ObservableCollection<RuleSummaryItem> RuleSummaries { get; } = new();

    public DashboardViewModel(IRuleEngine ruleEngine, ICpuTopologyService topoService)
    {
        _ruleEngine = ruleEngine;
        _topoService = topoService;
    }

    public void Refresh()
    {
        var topo = _topoService.Detect();
        HasTopology = topo != null;

        try { ProcessCount = System.Diagnostics.Process.GetProcesses().Length.ToString(); }
        catch { ProcessCount = "??"; }
        RulesActive = _ruleEngine.Rules.Count(r => r.Enabled).ToString();
        PCoreCount = topo.PcoreCount.ToString();
        ECoreCount = topo.EcoreCount.ToString();

        // Core visualization
        CoreItems.Clear();
        for (int i = 0; i < topo.TotalLogicalProcessors && i < 64; i++)
        {
            ulong bit = 1UL << i;
            string type;
            IBrush color;

            if ((topo.PcoreMask & bit) != 0)
            {
                type = (topo.Smt1Mask & bit) != 0 ? "P-core (SMT)" : "P-core";
                color = (topo.Smt1Mask & bit) != 0
                    ? Brush.Parse("#F97316")
                    : Brush.Parse("#EF4444");
            }
            else if ((topo.EcoreMask & bit) != 0)
            {
                type = "E-core";
                color = Brush.Parse("#3B82F6");
            }
            else
            {
                type = "Logical";
                color = Brush.Parse("#94A3B8");
            }

            CoreItems.Add(new CoreVisualItem
            {
                Index = i,
                ColorBrush = color,
                Tooltip = $"LP#{i}: {type}"
            });
        }

        // Rule summaries
        RuleSummaries.Clear();
        foreach (var r in _ruleEngine.Rules.Where(r => r.Enabled))
        {
            RuleSummaries.Add(new RuleSummaryItem
            {
                DisplayText = $"{r.Name}  →  {r.Action.Mode}  [{r.Action.Level}]",
                LevelColor = GetLevelBrush(r.Action.Level)
            });
        }
    }

    private static IBrush GetLevelBrush(string level) => level switch
    {
        "job-locked" => Brush.Parse("#991B1B"),
        "job-enforced" => Brush.Parse("#EF4444"),
        "hard-affinity" => Brush.Parse("#F97316"),
        "soft-cpu-sets" => Brush.Parse("#3B82F6"),
        _ => Brush.Parse("#94A3B8")
    };
}

public partial class CoreVisualItem : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private IBrush _colorBrush = Brushes.Gray;
    [ObservableProperty] private string _tooltip = "";
}

public partial class RuleSummaryItem : ObservableObject
{
    [ObservableProperty] private string _displayText = "";
    [ObservableProperty] private IBrush _levelColor = Brushes.Gray;
}
