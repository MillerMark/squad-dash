using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SquadDash.GuidedTours;

namespace SquadDash;

/// <summary>
/// Orchestrates the active guided tour: step transitions, callout lifecycle,
/// pre-actions, and layout save/restore.
/// </summary>
internal sealed class GuidedTourController
{
    private GuidedTour?              _activeTour;
    private List<GuidedTour>         _allTours = new();
    private int                      _currentStepIndex;
    private FrmUltimateCallout?      _activeCallout;
    private readonly List<string>    _tourInjectedThreadIds = new();

    // Callbacks wired by MainWindow
    private readonly Func<string, FrameworkElement?>      _elementLocator;
    private readonly Action?                              _savePreTourLayout;
    private readonly Action?                              _restorePreTourLayout;
    private readonly Action<string, string>?              _executePreAction;
    private readonly Window                               _ownerWindow;
    private readonly Func<string?>?                       _workspaceFolderProvider;
    private readonly GuidedTourCommandRegistry?           _commandRegistry;
    private readonly Action?                              _onStepChanging;
    private readonly GuidedTourAdvanceTriggerRegistry?    _triggerRegistry;
    private IDisposable?                                  _activeTriggerSubscription;

    /// <summary>
    /// Creates a new <see cref="GuidedTourController"/>.
    /// </summary>
    /// <param name="ownerWindow">The main window — used as callout parent and for dialog ownership.</param>
    /// <param name="elementLocator">Returns a FrameworkElement by x:Name from the visual tree, or null.</param>
    /// <param name="savePreTourLayout">Called once when the tour starts to snapshot the current layout.</param>
    /// <param name="restorePreTourLayout">Called when the tour ends to restore the pre-tour layout.</param>
    /// <param name="executePreAction">
    /// Called for step preActions other than None/SaveLayout.
    /// First arg = action kind (e.g. "OpenPanel"), second arg = argument (e.g. "Notes").
    /// </param>
    /// <param name="workspaceFolderProvider">Returns the current workspace folder path, used when saving tours.</param>
    /// <param name="onStepChanging">Called just before transitioning to a new step or stopping the tour.</param>
    public GuidedTourController(
        Window                          ownerWindow,
        Func<string, FrameworkElement?> elementLocator,
        Action?                         savePreTourLayout    = null,
        Action?                         restorePreTourLayout = null,
        Action<string, string>?         executePreAction     = null,
        Func<string?>?                  workspaceFolderProvider = null,
        GuidedTourCommandRegistry?      commandRegistry      = null,
        Action?                         onStepChanging       = null,
        GuidedTourAdvanceTriggerRegistry? triggerRegistry    = null)
    {
        _ownerWindow             = ownerWindow;
        _elementLocator          = elementLocator;
        _savePreTourLayout       = savePreTourLayout;
        _restorePreTourLayout    = restorePreTourLayout;
        _executePreAction        = executePreAction;
        _workspaceFolderProvider = workspaceFolderProvider;
        _commandRegistry         = commandRegistry;
        _onStepChanging          = onStepChanging;
        _triggerRegistry         = triggerRegistry;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public bool IsActive => _activeTour is not null;

    public GuidedTour?  ActiveTour         => _activeTour;
    public int          CurrentStepIndex   => _currentStepIndex;

    /// <summary>The workspace folder path resolved at the moment of the call, or null.</summary>
    public string? WorkspaceFolderPath => _workspaceFolderProvider?.Invoke();

    /// <summary>All tours in scope for the current session (used when saving edits).</summary>
    public List<GuidedTour> AllTours => _allTours;

    /// <summary>The element locator callback (used by the step editor for control browsing).</summary>
    public Func<string, FrameworkElement?> ElementLocator => _elementLocator;

    /// <summary>The owner window (used as dialog owner by the step editor).</summary>
    public Window OwnerWindow => _ownerWindow;

    /// <summary>The layout-capture callback (used by the step editor's Capture Layout button).</summary>
    public Action? CaptureLayout => _savePreTourLayout;

    /// <summary>The command registry used by this controller.</summary>
    public GuidedTourCommandRegistry? CommandRegistry => _commandRegistry;

    /// <summary>Read-only list of thread IDs created by <c>InjectTranscriptText</c> tour commands.</summary>
    public IReadOnlyList<string> InjectedThreadIds => _tourInjectedThreadIds;

    /// <summary>Registers a thread ID that was created by a tour injection command and should be finalized when the tour ends.</summary>
    public void TrackInjectedThread(string threadId) =>
        _tourInjectedThreadIds.Add(threadId);

    /// <summary>Starts the tour at step 0, saving the current layout as a restore point.</summary>
    public void StartTour(GuidedTour tour, List<GuidedTour>? allTours = null)
    {
        if (IsActive) StopTourInternal(showHint: false);

        _activeTour        = tour;
        _allTours          = allTours ?? new List<GuidedTour> { tour };
        _currentStepIndex  = 0;

        _savePreTourLayout?.Invoke();

        ShowCurrentStep();
    }

    /// <summary>
    /// Refreshes the callout and navigator heading after an in-place step edit.
    /// </summary>
    public void NotifyStepEdited()
    {
        CloseActiveCallout();
        ShowStepCallout(CurrentStep);
    }

    /// <summary>Moves to the next step, or ends the tour if already at the last step.</summary>
    public void Next()
    {
        if (!IsActive) return;
        _commandRegistry?.Execute(CurrentStep.CommandAfter);
        if (_currentStepIndex >= _activeTour!.Steps.Count - 1)
        {
            var allToursSnapshot = _allTours;
            var currentTourId    = _activeTour.Id;
            StopTourInternal(showHint: false);

            var remaining = allToursSnapshot
                .Where(t => t.Id != currentTourId && !GuidedTourStateStore.Shared.IsCompleted(t.Id))
                .ToList();

            if (remaining.Count > 0)
            {
                var selected = FrmGuidedTourSelector.ShowForResult(
                    _ownerWindow,
                    remaining,
                    id => GuidedTourStateStore.Shared.IsCompleted(id));
                if (selected is not null)
                    StartTour(selected, allToursSnapshot);
            }
            return;
        }
        FrmUltimateCallout.RecordTourAdvance();
        _currentStepIndex++;
        ShowCurrentStep();
    }

    /// <summary>Moves to the previous step.</summary>
    public void Prev()
    {
        if (!IsActive || _currentStepIndex <= 0) return;
        _commandRegistry?.Execute(CurrentStep.CommandAfter);
        _currentStepIndex--;
        ShowCurrentStep();
    }

    /// <summary>
    /// Stops the tour, shows the "restart from Help" callout, and restores the pre-tour layout.
    /// </summary>
    public void StopTour() => StopTourInternal(showHint: true);

    // ── Private helpers ──────────────────────────────────────────────────────

    private void HandleEditStep()
    {
        if (_activeTour is null) return;

        var editor = new FrmGuidedTourStepEditor(
            step:                CurrentStep,
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: NotifyStepEdited,
            commandRegistry:     _commandRegistry);
        editor.ShowDialog();
        if (editor.WasSaved)
            NotifyStepEdited();
    }

    private GuidedTourStep CurrentStep =>
        _activeTour!.Steps[_currentStepIndex];

    private void ShowCurrentStep()
    {
        _activeTriggerSubscription?.Dispose();
        _activeTriggerSubscription = null;
        _onStepChanging?.Invoke();
        CloseActiveCallout();
        RunPreAction(CurrentStep);
        var step = CurrentStep;
        _commandRegistry?.Execute(step.CommandBefore);
        // Defer by one layout pass so that any UI changes made by RunPreAction or
        // CommandBefore (e.g. queue items added, panel opened) are fully rendered
        // before ShowStepCallout checks target.IsVisible.  Without this, the callout
        // is silently skipped on the first visit to a step that changes the UI.
        // The ReferenceEquals guard ensures we don't show a stale callout if the user
        // navigates before the deferred callback fires.
        _ownerWindow.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsActive && ReferenceEquals(CurrentStep, step))
            {
                ShowStepCallout(step);
                _activeTriggerSubscription?.Dispose();
                _activeTriggerSubscription = _triggerRegistry?.Subscribe(step.AdvanceTrigger, () =>
                    _ownerWindow.Dispatcher.InvokeAsync(Next));
            }
        });
    }

    private void ShowStepCallout(GuidedTourStep step)
    {
        if (string.IsNullOrWhiteSpace(step.TargetControlId))
        {
            // No target defined — show centered on screen with no dangle
            ShowCenteredCallout(step);
            return;
        }

        var target = _elementLocator(step.TargetControlId);
        if (target is null || !target.IsVisible)
        {
            // Target not found or not visible — show centered on screen with no dangle
            ShowCenteredCallout(step);
            return;
        }

        _activeCallout = FrmUltimateCallout.ShowCalloutBesideTarget(
            step.MarkdownText,
            target,
            width:     320,
            fontSize:  Application.Current.Resources.Contains("FontSizeCallout")
                           ? (double)Application.Current.Resources["FontSizeCallout"]
                           : 18.0,
            placement: step.ParsedCalloutPlacement);

        if (_activeCallout is not null)
        {
            _activeCallout.IsSticky = true;
            _activeCallout.TourNavAdvanceCountProvider = () => GuidedTourStateStore.Shared.TourNavAdvanceCount;
            _activeCallout.TourNavAdvanceRecorder = GuidedTourStateStore.Shared.RecordTourNavAdvance;
            _activeCallout.IsTourMode = true;
            _activeCallout.IsTourEditModeVisible = SquadDashEnvironment.IsDeveloperMode;
            _activeCallout.TourNextRequested         += (_, _) => Next();
            _activeCallout.TourPrevRequested         += (_, _) => Prev();
            _activeCallout.TourEditRequested         += (_, _) => HandleEditStep();
            _activeCallout.TourNewStepAfterRequested  += (_, _) => HandleNewStepAfter();
            _activeCallout.TourNewStepBeforeRequested += (_, _) => HandleNewStepBefore();
            _activeCallout.TourDeleteRequested       += (_, _) => HandleDeleteStep();
            _activeCallout.UserDismissed             += (_, _) => StopTour();
        }
    }

    private void ShowCenteredCallout(GuidedTourStep step)
    {
        _activeCallout = FrmUltimateCallout.ShowCalloutCenteredOnScreen(
            step.MarkdownText,
            _ownerWindow,
            width:    320,
            fontSize: Application.Current.Resources.Contains("FontSizeCallout")
                          ? (double)Application.Current.Resources["FontSizeCallout"]
                          : 18.0);

        if (_activeCallout is not null)
        {
            _activeCallout.IsSticky = true;
            _activeCallout.TourNavAdvanceCountProvider = () => GuidedTourStateStore.Shared.TourNavAdvanceCount;
            _activeCallout.TourNavAdvanceRecorder = GuidedTourStateStore.Shared.RecordTourNavAdvance;
            _activeCallout.IsTourMode = true;
            _activeCallout.IsTourEditModeVisible = SquadDashEnvironment.IsDeveloperMode;
            _activeCallout.TourNextRequested         += (_, _) => Next();
            _activeCallout.TourPrevRequested         += (_, _) => Prev();
            _activeCallout.TourEditRequested         += (_, _) => HandleEditStep();
            _activeCallout.TourNewStepAfterRequested  += (_, _) => HandleNewStepAfter();
            _activeCallout.TourNewStepBeforeRequested += (_, _) => HandleNewStepBefore();
            _activeCallout.TourDeleteRequested       += (_, _) => HandleDeleteStep();
            _activeCallout.UserDismissed             += (_, _) => StopTour();
        }
    }

    private void RunPreAction(GuidedTourStep step)
    {
        var action = step.ParsedPreAction;
        switch (action.Kind)
        {
            case GuidedTourPreActionKind.None:
                break;
            case GuidedTourPreActionKind.SaveLayout:
                _savePreTourLayout?.Invoke();
                break;
            case GuidedTourPreActionKind.LoadLayout:
            case GuidedTourPreActionKind.OpenPanel:
                _executePreAction?.Invoke(action.Kind.ToString(), action.Argument ?? string.Empty);
                break;
        }
    }

    private void StopTourInternal(bool showHint)
    {
        var wasActive  = IsActive;
        var tourId     = _activeTour?.Id;

        _onStepChanging?.Invoke();

        _activeTriggerSubscription?.Dispose();
        _activeTriggerSubscription = null;

        if (wasActive && _activeTour is not null)
            _commandRegistry?.Execute(CurrentStep.CommandAfter);

        _activeTour       = null;
        _currentStepIndex = 0;
        _tourInjectedThreadIds.Clear();

        CloseActiveCallout();

        if (wasActive)
        {
            _restorePreTourLayout?.Invoke();

            if (showHint && tourId is not null)
                GuidedTourStateStore.Shared.MarkCompleted(tourId);

            if (showHint)
                ShowRestartHint();
        }
    }

    private void HandleNewStepAfter()
    {
        if (_activeTour is null) return;

        var newStep = new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" };
        var insertIndex = _currentStepIndex + 1;
        _activeTour.Steps.Insert(insertIndex, newStep);
        _currentStepIndex = insertIndex;

        var editor = new FrmGuidedTourStepEditor(
            step:                newStep,
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: NotifyStepEdited,
            commandRegistry:     _commandRegistry);
        editor.ShowDialog();

        if (editor.WasSaved)
        {
            ShowCurrentStep();
        }
        else
        {
            _activeTour.Steps.RemoveAt(insertIndex);
            _currentStepIndex = Math.Max(0, insertIndex - 1);
            ShowCurrentStep();  // restore callout for original step
        }
    }

    private void HandleNewStepBefore()
    {
        if (_activeTour is null) return;

        var newStep = new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" };
        var insertIndex = _currentStepIndex;  // Insert BEFORE current step
        _activeTour.Steps.Insert(insertIndex, newStep);

        var editor = new FrmGuidedTourStepEditor(
            step:                newStep,
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: NotifyStepEdited,
            commandRegistry:     _commandRegistry);
        editor.ShowDialog();

        if (editor.WasSaved)
        {
            ShowCurrentStep();
        }
        else
        {
            _activeTour.Steps.RemoveAt(insertIndex);
            // _currentStepIndex stays the same (the original step is back)
            ShowCurrentStep();  // restore callout for original step
        }
    }

    private void HandleDeleteStep()
    {
        if (_activeTour is null || _activeTour.Steps.Count == 0) return;

        var result = MessageBox.Show(
            $"Delete step {_currentStepIndex + 1} of {_activeTour.Steps.Count}?\n\n\"{CurrentStep.Title}\"",
            "Delete Step",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var deleteIndex = _currentStepIndex;
        _activeTour.Steps.RemoveAt(deleteIndex);

        if (!string.IsNullOrWhiteSpace(WorkspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, WorkspaceFolderPath); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Step deleted from memory but could not be saved to disk:\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        if (_activeTour.Steps.Count == 0)
        {
            StopTour();
            return;
        }

        _currentStepIndex = Math.Min(deleteIndex, _activeTour.Steps.Count - 1);
        ShowCurrentStep();
    }

    private void ShowRestartHint()
    {
        // Point at the Help menu item if it exists; fall back to top-right of owner window
        var helpMenuItem = _elementLocator("HelpMenuItem");
        if (helpMenuItem is not null && helpMenuItem.IsVisible)
        {
            FrmUltimateCallout.ShowCalloutBesideTarget(
                "You can start guided tours from inside the **Help** menu.",
                helpMenuItem,
                width:     280,
                fontSize:  Application.Current.Resources.Contains("FontSizeCallout")
                               ? (double)Application.Current.Resources["FontSizeCallout"]
                               : 18.0,
                placement: CalloutPlacement.South);
        }
    }

    private void CloseActiveCallout()
    {
        if (_activeCallout is null) return;
        try { _activeCallout.Close(); } catch { /* already closed */ }
        _activeCallout = null;
    }
}
