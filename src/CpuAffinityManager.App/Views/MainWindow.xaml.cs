using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CpuAffinityManager.Cpu;
using CpuAffinityManager.Engine;
using CpuAffinityManager.Enforcement;
using CpuAffinityManager.Monitoring;
using Serilog;

namespace CpuAffinityManager.App;

public partial class MainWindow : Window
{
    private readonly IRuleEngine _ruleEngine;
    private readonly ICpuTopologyService _topoService;
    private readonly IEnforcementService _enforcementService;
    private readonly IProcessMonitor _processMonitor;
    private readonly AffinityEnforcementWatchdog _watchdog;
    private CpuTopology? _topology;
    private bool _isLoaded;
    private List<ProcessListItem> _allProcessItems = new();

    public MainWindow()
    {
        InitializeComponent();
        _ruleEngine = new RuleEngine();
        _topoService = new CpuTopologyService();
        _enforcementService = new EnforcementService(_ruleEngine, _topoService);
        _processMonitor = new WmiProcessMonitor();
        _watchdog = new AffinityEnforcementWatchdog(_ruleEngine, _topoService, _enforcementService);
        Loaded += OnLoaded;
        Closed += (_, _) => _watchdog.Dispose();
    }

    #region Startup

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        LanguageSelector.SelectedIndex = App.LanguageIndex;
        try
        {
            _topology = _topoService.Detect();
            Log.Information("Topology: {Topo}", _topology);
            LoadRules();
            SelectNav("dashboard");
            SafeText(SidebarCpuInfo, $"{_topology.TotalLogicalProcessors} threads · {_topology.PcoreCount}P + {_topology.EcoreCount}E");
            StartProcessMonitor();
            _watchdog.Start();
        }
        catch (Exception ex) { Log.Error(ex, "Startup failed"); TxtStatus.Text = $"Error: {ex.Message}"; }
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

    private void StartProcessMonitor()
    {
        try
        {
            _processMonitor.Start(e =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        string? path = EnforcementService.GetProcessPath(e.Pid);
                        if (path == null) return;
                        var rule = _ruleEngine.Match(e.ProcessName, path);
                        if (rule != null)
                        {
                            _enforcementService.Apply(e.Pid, rule, _topoService.Detect());
                            TxtStatus.Text = $"Auto-applied '{rule.Name}' → {e.ProcessName} (PID {e.Pid})";
                        }
                    }
                    catch { }
                });
            });
        }
        catch { }
    }

    #endregion

    #region Navigation

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string page)
            SelectNav(page);
    }

    private void SelectNav(string page)
    {
        SafeSet(() => DashboardPage.Visibility = page == "dashboard" ? Visibility.Visible : Visibility.Collapsed);
        SafeSet(() => ProcessesPage.Visibility = page == "processes" ? Visibility.Visible : Visibility.Collapsed);
        SafeSet(() => RulesPage.Visibility = page == "rules" ? Visibility.Visible : Visibility.Collapsed);
        SafeSet(() => SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed);

        switch (page)
        {
            case "dashboard": SafeText(PageTitle, Localized("Loc.Dashboard")); RefreshDashboard(); break;
            case "processes": SafeText(PageTitle, Localized("Loc.Processes")); RefreshProcessList(); break;
            case "rules": SafeText(PageTitle, Localized("Loc.Rules")); RefreshRulesList(); break;
            case "settings": SafeText(PageTitle, Localized("Loc.Settings")); break;
        }
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || LanguageSelector.SelectedIndex < 0)
            return;

        App.SetLanguage(LanguageSelector.SelectedIndex);
        SelectNav((NavList.SelectedItem as ListBoxItem)?.Tag as string ?? "dashboard");
    }

    private string Localized(string key) => FindResource(key) as string ?? key;

    #endregion

    #region Dashboard

    private void RefreshDashboard()
    {
        if (_topology == null || !_isLoaded) return;
        try
        {
            SafeText(StatProcessCount, System.Diagnostics.Process.GetProcesses().Length.ToString());
        }
        catch { SafeText(StatProcessCount, "??"); }
        SafeText(StatRulesCount, _ruleEngine.Rules.Count(r => r.Enabled).ToString());
        SafeText(StatPcoreCount, _topology.PcoreCount.ToString());
        SafeText(StatEcoreCount, _topology.EcoreCount.ToString());

        var coreItems = new List<CoreVisualItem>();
        for (int i = 0; i < _topology.TotalLogicalProcessors && i < 64; i++)
        {
            ulong bit = 1UL << i;
            coreItems.Add(new CoreVisualItem
            {
                Index = i,
                ColorBrush = (_topology.PcoreMask & bit) != 0
                    ? ((_topology.Smt1Mask & bit) != 0
                        ? new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16))
                        : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)))
                    : (_topology.EcoreMask & bit) != 0
                        ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
                        : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Tooltip = $"LP#{i}"
            });
        }
        CoreVisualList.ItemsSource = coreItems;
        RuleSummaryList.ItemsSource = _ruleEngine.Rules.Where(r => r.Enabled).Select(r =>
            new RuleSummaryItem { DisplayText = $"{r.Name} → {r.Action.Mode} [{r.Action.Level}]", LevelColor = LevelBrush(r.Action.Level) }).ToList();
    }

    #endregion

    #region Process List

    private void ProcessRefresh_Click(object sender, RoutedEventArgs e) => RefreshProcessList();

    private void RefreshProcessList()
    {
        if (!_isLoaded) return;
        TxtStatus.Text = "Scanning processes...";

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            var items = new List<ProcessListItem>();
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        int pid; string name;
                        try { pid = p.Id; } catch { continue; }
                        try { name = p.ProcessName + ".exe"; } catch { continue; }
                        string? path = null; try { if (pid is not 0 and not 4) path = p.MainModule?.FileName; } catch { }
                        string aff = "N/A"; try { if (pid is not 0 and not 4) aff = $"{p.ProcessorAffinity.ToInt64():X8}"; } catch { }
                        var rule = _ruleEngine.Match(name, path ?? "");
                        items.Add(new ProcessListItem
                        {
                            Pid = pid, Name = name, Path = path ?? "(protected)", AffinityShort = aff,
                            RuleLevelText = rule?.Action.Level ?? "",
                            HasMatchedRule = rule != null
                        });
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch (Exception ex) { Log.Error(ex, "Process enum failed"); }

            Dispatcher.Invoke(() =>
            {
                _allProcessItems = items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                ApplyProcessFilter();
                TxtStatus.Text = $"Loaded {items.Count} processes";
            });
        });
    }

    private void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyProcessFilter();

    private void ApplyProcessFilter()
    {
        string filter = (ProcessSearchBox.Text ?? "").Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allProcessItems
            : _allProcessItems.Where(p => p.Name.ToLowerInvariant().Contains(filter)
                || p.Path.ToLowerInvariant().Contains(filter)
                || p.Pid.ToString().Contains(filter)).ToList();
        ProcessListBox.ItemsSource = filtered;
    }

    #endregion

    #region Process Context Menu Actions

    private ProcessListItem? GetSelectedProcess() => ProcessListBox.SelectedItem as ProcessListItem;

    private void ProcessListItem_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
            item.ContextMenu = BuildProcessContextMenu();
        }
    }

    private void ProcessListItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem item)
            return;

        item.ContextMenu = BuildProcessContextMenu();
    }

    private ContextMenu BuildProcessContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateProcessMenuItem("Set Affinity: P-Cores", CtxSetPCores_Click));
        menu.Items.Add(CreateProcessMenuItem("Set Affinity: E-Cores", CtxSetECores_Click));
        menu.Items.Add(CreateProcessMenuItem("Set Affinity: All Cores", CtxSetAllCores_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateProcessMenuItem("Set Affinity: First Half", CtxSetFirstHalf_Click));
        menu.Items.Add(CreateProcessMenuItem("Set Affinity: Second Half", CtxSetSecondHalf_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateProcessMenuItem("Enforce via Job Object (anti-tamper)", CtxJobEnforced_Click));
        menu.Items.Add(CreateProcessMenuItem("Lock via Job Object (no escape)", CtxJobLocked_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateProcessMenuItem("Apply Matched Rule", CtxApplyMatchedRule_Click));
        return menu;
    }

    private static MenuItem CreateProcessMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void ApplyAffinityToSelected(string mode, string level)
    {
        var item = GetSelectedProcess();
        if (item == null) return;
        try
        {
            var topo = _topoService.Detect();
            var rule = new RuleEntry { Id = "ctx", Name = "Context Menu", Action = new RuleAction { Mode = mode, Level = level } };
            bool ok = _enforcementService.Apply(item.Pid, rule, topo);
            TxtStatus.Text = ok ? $"Applied {mode} [{level}] to PID {item.Pid}" : $"Failed to apply to PID {item.Pid}";
        }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
    }

    private void CtxSetPCores_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("p-cores|first-half", "hard-affinity");
    private void CtxSetECores_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("e-cores|second-half", "hard-affinity");
    private void CtxSetAllCores_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("all-cores", "hard-affinity");
    private void CtxSetFirstHalf_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("first-half", "hard-affinity");
    private void CtxSetSecondHalf_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("second-half", "hard-affinity");
    private void CtxJobEnforced_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("p-cores|all-cores", "job-enforced");
    private void CtxJobLocked_Click(object sender, RoutedEventArgs e) => ApplyAffinityToSelected("all-cores", "job-locked");

    private void CtxApplyMatchedRule_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedProcess();
        if (item == null) return;
        try
        {
            var rule = _ruleEngine.Match(item.Name, item.Path);
            if (rule != null)
            {
                _enforcementService.Apply(item.Pid, rule, _topoService.Detect());
                TxtStatus.Text = $"Applied rule '{rule.Name}' to PID {item.Pid}";
            }
            else { TxtStatus.Text = "No rule matches this process"; }
        }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
    }

    #endregion

    #region Rule Editor

    private void AddRule_Click(object sender, RoutedEventArgs e) => OpenRuleEditor(null);

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
            OpenRuleEditor(id);
    }

    private void OpenRuleEditor(string? ruleId)
    {
        RuleEntry? edit = null;
        if (ruleId != null)
            edit = _ruleEngine.Rules.FirstOrDefault(r => r.Id == ruleId);

        var dlg = new RuleEditorWindow(edit) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _ruleEngine.AddRule(dlg.Result);
            SaveRules();
            RefreshRulesList();
            RefreshDashboard();
            TxtStatus.Text = $"Rule '{dlg.Result.Name}' saved";
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        if (MessageBox.Show($"Delete rule '{id}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _ruleEngine.RemoveRule(id);
            SaveRules();
            RefreshRulesList();
            RefreshDashboard();
            TxtStatus.Text = $"Rule '{id}' deleted";
        }
    }

    private void RuleEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.Tag is not string id)
            return;

        var rule = _ruleEngine.Rules.FirstOrDefault(r => r.Id == id);
        if (rule == null)
            return;

        rule.Enabled = checkBox.IsChecked == true;
        SaveRules();
        ApplyRuleToggleToRunningProcesses(rule, rule.Enabled);
        RefreshDashboard();
        TxtStatus.Text = rule.Enabled
            ? $"Rule '{rule.Name}' enabled"
            : $"Rule '{rule.Name}' disabled";
    }

    private void ApplyRuleToggleToRunningProcesses(RuleEntry toggledRule, bool enabled)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            int affected = 0;
            var topology = _topoService.Detect();

            foreach (var process in System.Diagnostics.Process.GetProcesses())
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

            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = enabled
                    ? $"Rule '{toggledRule.Name}' enabled — applied to {affected} process(es)"
                    : $"Rule '{toggledRule.Name}' disabled — relaxed {affected} process(es)";
                RefreshProcessList();
            });
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

    private void SaveRules()
    {
        try
        {
            string path = RuleConfigPath.FindDefaultRules();
            _ruleEngine.Save(path);
        }
        catch (Exception ex) { Log.Error(ex, "Save rules failed"); }
    }

    #endregion

    #region Rules List

    private void RefreshRulesList()
    {
        RulesList.ItemsSource = _ruleEngine.Rules.Select(r => new RuleListItem
        {
            Id = r.Id, Name = r.Name, Enabled = r.Enabled,
            ModeDisplay = r.Action.Mode.ToUpper(), LevelText = r.Action.Level,
            MatchDisplay = $"Matches: {r.Match.Process}" + (string.IsNullOrEmpty(r.Match.Path) ? "" : $" in {r.Match.Path}")
        }).ToList();
    }

    #endregion

    #region Scan

    private void ScanNow_Click(object sender, RoutedEventArgs e)
    {
        BtnScanNow.IsEnabled = false;
        BtnScanNow.Content = "⏳ Scanning...";
        TxtStatus.Text = "Scanning all processes...";
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            int n = _enforcementService.ScanAndEnforce();
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = $"Scan complete — {n} process(es) affected";
                BtnScanNow.IsEnabled = true;
                BtnScanNow.Content = "🔍  Scan Now";
                RefreshProcessList();
            });
        });
    }

    #endregion

    #region Window Chrome

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();

    #endregion

    #region Settings

    private void Settings_Changed(object sender, RoutedEventArgs e) { /* Auto-save handled by SaveRules */ }

    #endregion

    #region Helpers

    private void SafeSet(Action a) { if (!_isLoaded) return; try { a(); } catch { } }
    private void SafeText(TextBlock? tb, string val) { if (tb != null) tb.Text = val; }

    private static Brush LevelBrush(string level) => level switch
    {
        "job-locked" => new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B)),
        "job-enforced" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        "hard-affinity" => new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)),
        _ => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
    };

    #endregion
}

#region ViewModels

public class CoreVisualItem { public int Index { get; set; } public Brush ColorBrush { get; set; } = Brushes.Gray; public string Tooltip { get; set; } = ""; }
public class RuleSummaryItem { public string DisplayText { get; set; } = ""; public Brush LevelColor { get; set; } = Brushes.Gray; }
public class ProcessListItem { public string Name { get; set; } = ""; public int Pid { get; set; } public string Path { get; set; } = ""; public string AffinityShort { get; set; } = ""; public string RuleLevelText { get; set; } = ""; public bool HasMatchedRule { get; set; } }
public class RuleListItem { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public bool Enabled { get; set; } public string ModeDisplay { get; set; } = ""; public string LevelText { get; set; } = ""; public string MatchDisplay { get; set; } = ""; }

#endregion
