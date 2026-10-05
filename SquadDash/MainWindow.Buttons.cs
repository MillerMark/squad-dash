using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

public partial class MainWindow {
    private ButtonsPanelController? _buttonsPanel;
    private ButtonsStore? _buttonsStore;
    private readonly HashSet<ButtonEditorWindow> _buttonEditors = [];
    private long _buttonsWorkspaceGeneration;
    private bool _buttonsPanelVisible;
    private bool _restoringButtonsFilter;

    private void InitializeButtonsPanel() {
        _buttonsPanel = new ButtonsPanelController(ButtonsListPanel, ButtonsPanelBorder, ButtonsFilterBox,
            () => OpenButtonEditor(null), OpenButtonEditor, DeleteWorkspaceButton,
            async definition => await ExecuteWorkspaceButtonAsync(definition)) {
                SaveOrderAsync = SaveWorkspaceButtonOrderAsync,
                ReloadOrderAsync = ReloadWorkspaceButtonOrderAsync,
                ReportReorderError = ex => HandleUiCallbackException("Reorder workspace buttons", ex)
            };
        Closed += (_, _) => InvalidateButtonsWorkspace();
    }

    private Task<IReadOnlyList<ButtonDefinition>> SaveWorkspaceButtonOrderAsync(
        IReadOnlyList<Guid> expectedOrder, IReadOnlyList<Guid> newOrder) {
        var store = _buttonsStore ?? throw new InvalidOperationException("No workspace button store is available.");
        return Task.Run(() => store.Reorder(expectedOrder, newOrder));
    }

    private Task<IReadOnlyList<ButtonDefinition>> ReloadWorkspaceButtonOrderAsync() {
        var store = _buttonsStore ?? throw new InvalidOperationException("No workspace button store is available.");
        return Task.Run(store.Load);
    }

    private void InvalidateButtonsWorkspace() {
        _buttonsWorkspaceGeneration++;
        _buttonsStore = null;
        foreach (var editor in _buttonEditors.ToArray())
            editor.InvalidateWorkspace();
        _buttonsPanel?.Refresh([]);
        SetButtonsFilter(string.Empty);
    }

    private void LoadButtonsWorkspace(string stateDirectory) {
        try {
            _buttonsStore = new ButtonsStore(stateDirectory);
            RefreshWorkspaceButtons();
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            _buttonsStore = null;
            _buttonsPanel?.ShowError($"Unable to load buttons: {ex.Message}");
            HandleUiCallbackException("Load workspace buttons", ex);
        }
    }

    private void RefreshWorkspaceButtons() {
        if (_buttonsStore is null)
            throw new InvalidOperationException("No workspace button store is available.");
        try {
            _buttonsPanel?.Refresh(_buttonsStore.Load());
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            _buttonsPanel?.ShowError($"Unable to load buttons: {ex.Message}");
            throw;
        }
    }

    private void RequireCurrentPanelItem(ButtonDefinition definition) {
        if (_buttonsPanel is null || !_buttonsPanel.ViewModel.Buttons.Any(item => ReferenceEquals(item, definition)))
            throw new ButtonConflictException("This button view is no longer current. Use the current workspace's Buttons panel.");
    }

    private void OpenButtonEditor(ButtonDefinition? definition) {
        try {
            if (_currentWorkspace is null || _buttonsStore is null || !CanShowOwnedWindow())
                throw new InvalidOperationException("Open a workspace before editing buttons.");
            if (definition is not null)
                RequireCurrentPanelItem(definition);
            var store = _buttonsStore;
            var workspace = _currentWorkspace;
            var generation = _buttonsWorkspaceGeneration;
            var session = new ButtonEditorSession(store, definition, store.Load().Count,
                () => !_isClosing && generation == _buttonsWorkspaceGeneration &&
                    ReferenceEquals(workspace, _currentWorkspace) && ReferenceEquals(store, _buttonsStore));
            var editor = new ButtonEditorWindow(this, session, RefreshWorkspaceButtons);
            _buttonEditors.Add(editor);
            editor.Closed += (_, _) => _buttonEditors.Remove(editor);
            editor.Show();
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            HandleUiCallbackException("Edit workspace button", ex);
        }
    }

    private void DeleteWorkspaceButton(ButtonDefinition definition) {
        try {
            RequireCurrentPanelItem(definition);
            if (_buttonsStore is null)
                throw new InvalidOperationException("No workspace button store is available.");
            _buttonsStore.Delete(definition.Id, definition.ContentRevision);
            RefreshWorkspaceButtons();
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            HandleUiCallbackException("Delete workspace button", ex);
        }
    }

    private async Task ExecuteWorkspaceButtonAsync(ButtonDefinition definition) {
        try {
            RequireCurrentPanelItem(definition);
            if (_buttonsStore is null || _currentWorkspace is null)
                throw new InvalidOperationException("No workspace button store is available.");
            var store = _buttonsStore;
            var snapshot = ButtonWorkspaceSnapshot.Capture(_currentWorkspace,
                _conversationManager.ConversationStore.GetWorkspaceStateDirectory(_currentWorkspace.FolderPath));
            // Store locking and the invocation-time branch lookup must not block the UI thread.
            var result = await Task.Run(async () => {
                var saved = store.Load().SingleOrDefault(item => item.Id == definition.Id)
                    ?? throw new ButtonConflictException("The button was deleted before execution.");
                return await new ButtonCommandExecutionService().ExecuteAsync(saved, snapshot);
            });
            if (result.Error is not null || result.CleanupError is not null || result.ExitCode != 0)
                SquadDashTrace.Write("Buttons", $"Execution {result.Button.Id}: exit={result.ExitCode}; error={result.Error}; cleanup={result.CleanupError}");
            var output = FormatButtonExecutionOutput(result);
            if (!CanShowOwnedWindow()) {
                SquadDashTrace.Write("Buttons", $"Owner closed before command output could be displayed:\n{output}");
                return;
            }
            var window = new SquadCommandOutputWindow(
                $"Button: {ButtonCaptionPresentation.VisibleText(result.Button.Content.Caption)}", output) { Owner = this };
            window.SetResourceReference(Control.BackgroundProperty, "AppSurface");
            window.SetResourceReference(Control.ForegroundProperty, "LabelText");
            window.Show();
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            HandleUiCallbackException("Run workspace button", ex);
        }
    }

    internal static string FormatButtonExecutionOutput(ButtonExecutionResult result) =>
        $"Workspace: {result.Workspace.FolderPath}\n" +
        $"Exit code: {result.ExitCode?.ToString() ?? "(not started)"}\n" +
        $"Execution error: {result.Error ?? "(none)"}\n" +
        $"Cleanup error: {result.CleanupError ?? "(none)"}\n\n" +
        $"--- stdout ---\n{result.StandardOutput}\n\n--- stderr ---\n{result.StandardError}";

    private void SyncButtonsPanel() {
        ButtonsPanelBorder.Visibility = _buttonsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        ViewButtonsPanelMenuItem.IsChecked = _buttonsPanelVisible;
        _dockingService?.EnsurePanelSlot("buttons");
        _dockingService?.OnPanelVisibilityChanged("buttons", _buttonsPanelVisible);
        NotifyToolPanelVisibilityChanged("buttons", _buttonsPanelVisible);
        UpdateMainGridSideMargins();
    }

    private void PersistButtonsPanelVisible() {
        try {
            var state = _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath);
            _docsPanelState = state with { ButtonsPanelVisible = _buttonsPanelVisible };
            _settingsManager.Replace(_settingsStore.SaveDocsPanelState(_currentWorkspace?.FolderPath, _docsPanelState));
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            HandleUiCallbackException("Save Buttons panel visibility", ex);
        }
    }

    private void RestoreButtonsPanelState() {
        var state = _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath);
        _buttonsPanelVisible = state.ButtonsPanelVisible == true;
        SyncButtonsPanel();
        SetButtonsFilter(state.ButtonsPanelFilter ?? string.Empty);
    }

    private void ViewButtonsPanelMenuItem_Click(object sender, RoutedEventArgs e) {
        _buttonsPanelVisible = ViewButtonsPanelMenuItem.IsChecked;
        SyncButtonsPanel();
        PersistButtonsPanelVisible();
    }

    private void ButtonsPanelCloseButton_Click(object sender, RoutedEventArgs e) {
        _buttonsPanelVisible = false;
        SyncButtonsPanel();
        PersistButtonsPanelVisible();
    }

    private void SetButtonsFilter(string text) {
        _restoringButtonsFilter = true;
        try {
            ButtonsFilterBox.Text = text;
            _buttonsPanel?.SetFilter(text);
        }
        finally {
            _restoringButtonsFilter = false;
        }
    }

    private void ButtonsFilterBox_TextChanged(object sender, TextChangedEventArgs e) {
        if (ButtonsFilterClearButton is null) return; // XAML initialization precedes controller construction.
        var text = ButtonsFilterBox.Text;
        ButtonsFilterClearButton.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        _buttonsPanel?.SetFilter(text);
        if (_restoringButtonsFilter || _buttonsPanel is null || _currentWorkspace is null) return;
        try {
            var state = _settingsStore.GetDocsPanelState(_currentWorkspace.FolderPath);
            _docsPanelState = state with { ButtonsPanelFilter = text };
            _settingsManager.Replace(_settingsStore.SaveDocsPanelState(_currentWorkspace.FolderPath, _docsPanelState));
        }
        catch (Exception ex) when (ButtonsUiErrors.IsExpected(ex)) {
            HandleUiCallbackException("Save Buttons filter", ex);
        }
    }

    private void ButtonsFilterClearButton_Click(object sender, RoutedEventArgs e) => ButtonsFilterBox.Clear();
}
