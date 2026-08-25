using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash;

internal enum WorkspaceUpgradeSummaryPanelState
{
    Idle,
    Running,
    Succeeded,
    Failed,
    PartiallyApplied
}

internal partial class WorkspaceUpgradeSummaryPanel : UserControl, INotifyPropertyChanged
{
    private WorkspaceUpgradeDryRunResult _dryRunResult = WorkspaceUpgradeDryRunResult.Empty;
    private WorkspaceUpgradeSummaryPanelState _currentState = WorkspaceUpgradeSummaryPanelState.Idle;
    private string _errorMessage = string.Empty;

    public WorkspaceUpgradeSummaryPanel()
    {
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> SafeAdditionsOrRefreshes { get; } = new();

    public ObservableCollection<string> TrackedLocalChangesAtRisk { get; } = new();

    public ObservableCollection<string> GeneratedFilesAtRisk { get; } = new();

    public ObservableCollection<string> PartiallyAppliedFiles { get; } = new();

    public WorkspaceUpgradeDryRunResult DryRunResult
    {
        get => _dryRunResult;
        set
        {
            _dryRunResult = value ?? WorkspaceUpgradeDryRunResult.Empty;
            RefreshDryRunCollections();
            RaiseSectionChanged();
            RaiseStateChanged();
        }
    }

    public WorkspaceUpgradeSummaryPanelState CurrentState
    {
        get => _currentState;
        set
        {
            if (_currentState == value)
                return;

            _currentState = value;
            RaiseStateChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_errorMessage, normalized, StringComparison.Ordinal))
                return;

            _errorMessage = normalized;
            OnPropertyChanged();
        }
    }

    public bool IsRunning => CurrentState == WorkspaceUpgradeSummaryPanelState.Running;

    public bool IsFailed => CurrentState == WorkspaceUpgradeSummaryPanelState.Failed;

    public bool IsPartiallyApplied => CurrentState == WorkspaceUpgradeSummaryPanelState.PartiallyApplied;

    public bool IsSafeAdditionsEmpty => SafeAdditionsOrRefreshes.Count == 0;

    public bool IsTrackedChangesEmpty => TrackedLocalChangesAtRisk.Count == 0;

    public bool IsGeneratedFilesEmpty => GeneratedFilesAtRisk.Count == 0;

    public bool IsPartiallyAppliedFilesEmpty => PartiallyAppliedFiles.Count == 0;

    public string StateMessage =>
        CurrentState switch
        {
            WorkspaceUpgradeSummaryPanelState.Idle =>
                "Idle — review the dry-run summary and choose how to proceed.",
            WorkspaceUpgradeSummaryPanelState.Running =>
                "Running — workspace upgrade operation is in progress.",
            WorkspaceUpgradeSummaryPanelState.Succeeded =>
                "Succeeded — workspace upgrade completed successfully.",
            WorkspaceUpgradeSummaryPanelState.Failed =>
                "Failed — workspace upgrade could not be completed.",
            WorkspaceUpgradeSummaryPanelState.PartiallyApplied =>
                "Partially applied — some files were updated before completion stopped.",
            _ =>
                "Idle — review the dry-run summary and choose how to proceed.",
        };

    public void SetPartiallyAppliedFiles(IEnumerable<string> files)
    {
        PartiallyAppliedFiles.Clear();
        foreach (var file in files)
            PartiallyAppliedFiles.Add(file);

        OnPropertyChanged(nameof(IsPartiallyAppliedFilesEmpty));
    }

    private void RefreshDryRunCollections()
    {
        CopyToCollection(SafeAdditionsOrRefreshes, DryRunResult.SafeAdditionsOrRefreshes);
        CopyToCollection(TrackedLocalChangesAtRisk, DryRunResult.TrackedLocalChangesAtRisk);
        CopyToCollection(GeneratedFilesAtRisk, DryRunResult.GeneratedFilesAtRisk);
    }

    private static void CopyToCollection(ObservableCollection<string> destination, IReadOnlyList<string> source)
    {
        destination.Clear();
        foreach (var item in source)
            destination.Add(item);
    }

    private void RaiseSectionChanged()
    {
        OnPropertyChanged(nameof(IsSafeAdditionsEmpty));
        OnPropertyChanged(nameof(IsTrackedChangesEmpty));
        OnPropertyChanged(nameof(IsGeneratedFilesEmpty));
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(StateMessage));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsPartiallyApplied));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void WorkspaceUpgradeSummaryPanel_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => ReviewChangesButton.Focus()));
    }

    private void ReviewChangesButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("WorkspaceUpgradeSummaryPanel Review Changes clicked (stub only).");
    }

    private void ApplyWorkspaceUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("WorkspaceUpgradeSummaryPanel Apply Workspace Update clicked (stub only).");
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("WorkspaceUpgradeSummaryPanel Later clicked (stub only).");
    }
}
