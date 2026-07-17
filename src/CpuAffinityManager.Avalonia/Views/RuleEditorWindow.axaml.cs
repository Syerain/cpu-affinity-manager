using Avalonia.Controls;
using Avalonia.Interactivity;
using CpuAffinityManager.Engine;

namespace CpuAffinityManager.Avalonia.Views;

public partial class RuleEditorWindow : Window
{
    public RuleEntry? Result { get; private set; }
    private readonly RuleEntry? _editTarget;

    public RuleEditorWindow() : this(null) { }

    public RuleEditorWindow(RuleEntry? editTarget)
    {
        InitializeComponent();
        _editTarget = editTarget;

        if (editTarget != null)
        {
            DlgTitle.Text = App.GetText("Loc.EditRule");
            BtnSave.Content = App.GetText("Loc.UpdateRule");
            TxtName.Text = editTarget.Name;
            TxtProcess.Text = editTarget.Match.Process;
            TxtPath.Text = editTarget.Match.Path ?? "";
            SetCombo(CmbMode, editTarget.Action.Mode);
            SetCombo(CmbLevel, editTarget.Action.Level);
            ChkEnabled.IsChecked = editTarget.Enabled;
            ChkLock.IsChecked = editTarget.Action.Lock;
        }
    }

    private static void SetCombo(ComboBox cb, string value)
    {
        foreach (var item in cb.Items)
            if (item is ComboBoxItem cbi && cbi.Content?.ToString() == value)
                { cb.SelectedItem = cbi; return; }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        string name = TxtName.Text?.Trim() ?? "";
        string process = TxtProcess.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(process)) return;

        Result = new RuleEntry
        {
            Id = _editTarget?.Id ?? $"rule-{Guid.NewGuid():N}"[..8],
            Name = name,
            Enabled = ChkEnabled.IsChecked == true,
            Match = new RuleMatch { Process = process, Path = string.IsNullOrWhiteSpace(TxtPath.Text) ? null : TxtPath.Text?.Trim() },
            Action = new RuleAction
            {
                Mode = (CmbMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "all-cores",
                Level = (CmbLevel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "hard-affinity",
                Lock = ChkLock.IsChecked == true
            }
        };
        Close(Result);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
