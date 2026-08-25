using System;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

internal partial class WorkspaceUpgradeSimulationWindow : Window
{
    private static readonly string[] SamplePartiallyAppliedFiles =
    [
        ".squad\\agents\\release-notes.md",
        ".squad\\orchestration\\state-cache.json",
        "README.md",
    ];

    public WorkspaceUpgradeSimulationWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;

        SummaryPanel.DryRunResult = new WorkspaceUpgradeDryRunResult(
            SafeAdditionsOrRefreshes:
            [
                ".squad\\templates\\new-task-template.md",
                ".squad\\docs\\operator-guide.md",
                "tools\\health-check.ps1",
            ],
            TrackedLocalChangesAtRisk:
            [
                ".squad\\agents\\lyra-morn\\charter.md",
                ".squad\\decisions.md",
                "README.md",
            ],
            GeneratedFilesAtRisk:
            [
                ".squad\\cache\\workspace-index.json",
                ".squad\\cache\\prompt-state.json",
            ]);

        SummaryPanel.SetPartiallyAppliedFiles(SamplePartiallyAppliedFiles);
        SummaryPanel.ErrorMessage = "Failed to apply workspace update: access denied while updating .squad\\cache\\workspace-index.json.";
        SummaryPanel.CurrentState = WorkspaceUpgradeSummaryPanelState.Idle;
    }

    private void StateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        if (!Enum.TryParse<WorkspaceUpgradeSummaryPanelState>(tag, out var state))
            return;

        if (SummaryPanel is null) return;
        SummaryPanel.CurrentState = state;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
