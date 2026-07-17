using System.Diagnostics;
using System.Runtime.Versioning;
using CpuAffinityManager.Cpu;
using CpuAffinityManager.Engine;
using Serilog;

namespace CpuAffinityManager.Enforcement;

/// <summary>
/// Re-applies affinity rules when a running process resets its own affinity.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AffinityEnforcementWatchdog : IDisposable
{
    private readonly IRuleEngine _ruleEngine;
    private readonly ICpuTopologyService _topologyService;
    private readonly IEnforcementService _enforcementService;
    private readonly TimeSpan _period;
    private Timer? _timer;
    private int _isTicking;

    public AffinityEnforcementWatchdog(
        IRuleEngine ruleEngine,
        ICpuTopologyService topologyService,
        IEnforcementService enforcementService,
        TimeSpan? period = null)
    {
        _ruleEngine = ruleEngine;
        _topologyService = topologyService;
        _enforcementService = enforcementService;
        _period = period ?? TimeSpan.FromMilliseconds(250);
    }

    public void Start()
    {
        _timer ??= new Timer(Tick, null, _period, _period);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public int EnforceOnce()
    {
        int changed = 0;
        CpuTopology topology = _topologyService.Detect();

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

                RuleEntry? rule = _ruleEngine.Match(name, path ?? "");
                if (rule?.Action == null || !RequiresOngoingEnforcement(rule))
                    continue;

                ulong expected = CpuTopology.BuildMask(topology, rule.Action.Mode, rule.Action.GetCustomMask());
                if (expected == 0)
                    continue;

                ulong current = CpuTopology.ClampToLogicalProcessors(
                    (ulong)process.ProcessorAffinity.ToInt64(),
                    topology.TotalLogicalProcessors);

                if (current == expected)
                    continue;

                if (_enforcementService.Apply(pid, rule, topology))
                {
                    changed++;
                    Log.Information(
                        "Re-applied affinity rule '{Rule}' to {Process} PID {Pid}: 0x{Current:X} -> 0x{Expected:X}",
                        rule.Name, name, pid, current, expected);
                }
            }
            catch
            {
                // Process may exit or deny access while scanning.
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        return changed;
    }

    private void Tick(object? state)
    {
        if (Interlocked.Exchange(ref _isTicking, 1) == 1)
            return;

        try
        {
            EnforceOnce();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Affinity watchdog tick failed");
        }
        finally
        {
            Volatile.Write(ref _isTicking, 0);
        }
    }

    private static bool RequiresOngoingEnforcement(RuleEntry rule)
    {
        return rule.Action.Level is "hard-affinity" or "job-enforced" or "job-locked";
    }

    public void Dispose() => Stop();
}
