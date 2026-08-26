using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private bool _canApplyWorkspaceUpdate = true;
    private string _applyUnavailableExplanation = string.Empty;

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

    public Func<string, WorkspaceUpgradeFileCategory, Task>? FilePreviewRequested { get; set; }

    public Func<Task>? ApplyWorkspaceUpdateRequested { get; set; }

    public Func<Task>? LaterRequested { get; set; }

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

    public bool CanApplyWorkspaceUpdate
    {
        get => _canApplyWorkspaceUpdate;
        private set
        {
            if (_canApplyWorkspaceUpdate == value)
                return;

            _canApplyWorkspaceUpdate = value;
            RaiseActionStateChanged();
        }
    }

    public bool IsActionButtonsEnabled => !IsRunning;

    public bool IsApplyButtonEnabled => IsActionButtonsEnabled && CanApplyWorkspaceUpdate;

    public bool IsApplyUnavailable => !CanApplyWorkspaceUpdate;

    public string ApplyUnavailableExplanation => _applyUnavailableExplanation;

    public string StateMessage =>
        CurrentState switch
        {
            WorkspaceUpgradeSummaryPanelState.Idle =>
                "Review the dry-run summary and choose how to proceed.",
            WorkspaceUpgradeSummaryPanelState.Running =>
                "Running — workspace upgrade operation is in progress.",
            WorkspaceUpgradeSummaryPanelState.Succeeded =>
                "Succeeded — workspace upgrade completed successfully.",
            WorkspaceUpgradeSummaryPanelState.Failed =>
                "Failed — workspace upgrade could not be completed.",
            WorkspaceUpgradeSummaryPanelState.PartiallyApplied =>
                "Partially applied — some files were updated before completion stopped.",
            _ =>
                "Review the dry-run summary and choose how to proceed.",
        };

    public void SetPartiallyAppliedFiles(IEnumerable<string> files)
    {
        PartiallyAppliedFiles.Clear();
        foreach (var file in files)
            PartiallyAppliedFiles.Add(file);

        OnPropertyChanged(nameof(IsPartiallyAppliedFilesEmpty));
    }

    public void SetApplyAvailability(bool isAvailable, string? unavailableExplanation = null)
    {
        var normalizedExplanation = string.IsNullOrWhiteSpace(unavailableExplanation)
            ? "Apply is disabled because Squad CLI is unavailable for this workspace."
            : unavailableExplanation.Trim();

        _applyUnavailableExplanation = isAvailable ? string.Empty : normalizedExplanation;
        OnPropertyChanged(nameof(ApplyUnavailableExplanation));
        CanApplyWorkspaceUpdate = isAvailable;
        OnPropertyChanged(nameof(IsApplyUnavailable));
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
        RaiseActionStateChanged();
    }

    private void RaiseActionStateChanged()
    {
        OnPropertyChanged(nameof(IsActionButtonsEnabled));
        OnPropertyChanged(nameof(IsApplyButtonEnabled));
        OnPropertyChanged(nameof(IsApplyUnavailable));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void WorkspaceUpgradeSummaryPanel_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => ApplyWorkspaceUpdateButton.Focus()));
    }

    private async void SafeFileLink_Click(object sender, RoutedEventArgs e) =>
        await ExecuteFilePreviewAsync(sender, WorkspaceUpgradeFileCategory.SafeAdditionOrRefresh).ConfigureAwait(true);

    private async void TrackedFileLink_Click(object sender, RoutedEventArgs e) =>
        await ExecuteFilePreviewAsync(sender, WorkspaceUpgradeFileCategory.TrackedLocalChange).ConfigureAwait(true);

    private async void GeneratedFileLink_Click(object sender, RoutedEventArgs e) =>
        await ExecuteFilePreviewAsync(sender, WorkspaceUpgradeFileCategory.GeneratedFile).ConfigureAwait(true);

    private async void ApplyWorkspaceUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await ExecuteCallbackAsync(ApplyWorkspaceUpdateRequested, "Apply Workspace Update").ConfigureAwait(true);

    private async void LaterButton_Click(object sender, RoutedEventArgs e) =>
        await ExecuteCallbackAsync(LaterRequested, "Later").ConfigureAwait(true);

    private async Task ExecuteFilePreviewAsync(object sender, WorkspaceUpgradeFileCategory category)
    {
        if (sender is not Hyperlink { DataContext: string path } || string.IsNullOrWhiteSpace(path))
            return;

        if (FilePreviewRequested is null)
        {
            Debug.WriteLine($"WorkspaceUpgradeSummaryPanel preview clicked without callback wiring: {path}");
            return;
        }

        try
        {
            await FilePreviewRequested(path, category).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Workspace", $"Workspace upgrade file preview failed for '{path}': {ex.Message}");
        }
    }

    private async Task ExecuteCallbackAsync(Func<Task>? callback, string actionName)
    {
        if (callback is null)
        {
            Debug.WriteLine($"WorkspaceUpgradeSummaryPanel {actionName} clicked without callback wiring.");
            return;
        }

        try
        {
            await callback().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Workspace", $"Workspace upgrade action '{actionName}' failed: {ex.Message}");
            CurrentState = WorkspaceUpgradeSummaryPanelState.Failed;
            ErrorMessage = ex.Message;
        }
    }
}
