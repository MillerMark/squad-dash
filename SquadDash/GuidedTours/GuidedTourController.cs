using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private FrmGuidedTourStepEditor? _activeEditor;
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
    private readonly Action?                              _onCalloutShown;
    private readonly GuidedTourAdvanceTriggerRegistry?    _triggerRegistry;
    private IDisposable?                                  _activeTriggerSubscription;
    private readonly Func<bool>?                          _isTypeAnimationRunning;
    private readonly Func<IReadOnlyList<Window>?>?        _extraPickWindowsProvider;
    private readonly Func<IReadOnlyList<string>>?         _elementNamesProvider;
    private readonly Func<string?>?                       _getLastEditorTourName;
    private readonly Action<string>?                      _saveLastEditorTourName;
    private readonly Func<int>?                           _getLastEditorStepIndex;
    private readonly Action<int>?                         _saveLastEditorStepIndex;
    private CancellationTokenSource?                      _readingNudgeCts;
    private readonly GuidedTourContextRegistry?           _contextRegistry;

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
    /// <param name="onCalloutShown">Called after a step callout is shown and again after its animation settles.</param>
    public GuidedTourController(
        Window                          ownerWindow,
        Func<string, FrameworkElement?> elementLocator,
        Action?                         savePreTourLayout    = null,
        Action?                         restorePreTourLayout = null,
        Action<string, string>?         executePreAction     = null,
        Func<string?>?                  workspaceFolderProvider = null,
        GuidedTourCommandRegistry?      commandRegistry      = null,
        Action?                         onStepChanging       = null,
        Action?                         onCalloutShown       = null,
        GuidedTourAdvanceTriggerRegistry? triggerRegistry    = null,
        Func<bool>?                     isTypeAnimationRunning = null,
        Func<IReadOnlyList<Window>?>?   extraPickWindowsProvider = null,
        Func<IReadOnlyList<string>>?    elementNamesProvider = null,
        Func<string?>?                  getLastEditorTourName  = null,
        Action<string>?                 saveLastEditorTourName = null,
        Func<int>?                      getLastEditorStepIndex  = null,
        Action<int>?                    saveLastEditorStepIndex = null,
        GuidedTourContextRegistry?      contextRegistry         = null)
    {
        _ownerWindow             = ownerWindow;
        _elementLocator          = elementLocator;
        _savePreTourLayout       = savePreTourLayout;
        _restorePreTourLayout    = restorePreTourLayout;
        _executePreAction        = executePreAction;
        _workspaceFolderProvider = workspaceFolderProvider;
        _commandRegistry         = commandRegistry;
        _onStepChanging          = onStepChanging;
        _onCalloutShown          = onCalloutShown;
        _triggerRegistry         = triggerRegistry;
        _isTypeAnimationRunning  = isTypeAnimationRunning;
        _extraPickWindowsProvider = extraPickWindowsProvider;
        _elementNamesProvider    = elementNamesProvider;
        _getLastEditorTourName   = getLastEditorTourName;
        _saveLastEditorTourName  = saveLastEditorTourName;
        _getLastEditorStepIndex  = getLastEditorStepIndex;
        _saveLastEditorStepIndex = saveLastEditorStepIndex;
        _contextRegistry         = contextRegistry;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public bool IsActive => _activeTour is not null;

    public GuidedTour?  ActiveTour         => _activeTour;
    public int          CurrentStepIndex   => _currentStepIndex;

    /// <summary>True when the standalone guided tour editor is currently open.</summary>
    public bool IsEditorOpen => _activeEditor is { IsLoaded: true };

    /// <summary>True when the editor is open and contains keyboard focus.</summary>
    public bool IsEditorFocused => _activeEditor is { IsLoaded: true, IsKeyboardFocusWithin: true };

    /// <summary>Synchronously persists fields currently visible in the editor.</summary>
    public bool FlushEditorChanges() => _activeEditor?.FlushPendingChanges() ?? true;

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

    /// <summary>The context registry used by this controller.</summary>
    public GuidedTourContextRegistry? ContextRegistry => _contextRegistry;

    /// <summary>Read-only list of thread IDs created by <c>InjectTranscriptText</c> tour commands.</summary>
    public IReadOnlyList<string> InjectedThreadIds => _tourInjectedThreadIds;

    /// <summary>Registers a thread ID that was created by a tour injection command and should be finalized when the tour ends.</summary>
    public void TrackInjectedThread(string threadId) =>
        _tourInjectedThreadIds.Add(threadId);

    /// <summary>Starts the tour at step 0, saving the current layout as a restore point.</summary>
    public void StartTour(GuidedTour tour, List<GuidedTour>? allTours = null)
    {
        if (IsActive) StopTourInternal(showHint: false);

        var oldCounts = TourStepCounts(_allTours);
        var replacement = allTours ?? new List<GuidedTour> { tour };
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourController.StartTour: replacing _allTours, oldSteps=[{oldCounts}], newSteps=[{TourStepCounts(replacement)}], tour=\"{tour.Name}\", workspacePath=\"{WorkspaceFolderPath ?? "(none)"}\"");

        // If the editor is open it holds a reference to the old _allTours list.
        // Flush any pending edits first (using the old, still-valid references),
        // then rebind the editor so its _allTours and _activeTour point into the
        // replacement list.  Without this the editor's EnsureActiveTourIsAttached
        // check fails for every subsequent click, silently blocking all interaction.
        if (_activeEditor is { IsLoaded: true })
        {
            _activeEditor.FlushPendingChanges();
            _activeEditor.RebindTourList(replacement);
        }

        _activeTour        = tour;
        _allTours          = replacement;
        _currentStepIndex  = 0;

        _savePreTourLayout?.Invoke();

        ShowCurrentStep();
    }

    /// <summary>
    /// Opens the guided tour step editor in standalone mode (no active tour required).
    /// If an editor is already open, activates it. Loads all tours from the workspace;
    /// opens to the first tour's first step, or shows a message if no tours exist.
    /// </summary>
    public void OpenEditorStandalone(List<GuidedTour> allTours)
    {
        if (_activeEditor is { IsLoaded: true }) { _activeEditor.Activate(); return; }

        var previousActiveTour = _activeTour;
        var previousStepIndex = _currentStepIndex;
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourController.OpenEditorStandalone: replacing _allTours with freshly-loaded list, oldSteps=[{TourStepCounts(_allTours)}], newSteps=[{TourStepCounts(allTours)}], workspacePath=\"{WorkspaceFolderPath ?? "(none)"}\"");
        _allTours = allTours;
        var tour  = _allTours.FirstOrDefault();
        if (tour is null)
        {
            MessageBox.Show(
                "No tours found in the tracked source asset.\nUse Developer > New Guided Tour... to create one first.",
                "Guided Tour Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // A running tour belongs to the old object graph. Rebind it to the matching
        // instance in the freshly loaded list so editor mutations are included when
        // _allTours is serialized. Retaining the old reference silently loses edits.
        var reboundTour = GuidedTourObjectGraph.Rebind(previousActiveTour, _allTours);

        if (reboundTour is not null)
        {
            _activeTour = reboundTour;
            _currentStepIndex = Math.Clamp(previousStepIndex, 0, Math.Max(0, reboundTour.Steps.Count - 1));
        }
        else
        {
            var lastName = _getLastEditorTourName?.Invoke();
            var restored = string.IsNullOrWhiteSpace(lastName)
                ? null
                : _allTours.FirstOrDefault(t =>
                    string.Equals(t.Name, lastName, StringComparison.OrdinalIgnoreCase));
            _activeTour = restored ?? tour;

            // Restore last step index for the selected tour.
            var lastStep = _getLastEditorStepIndex?.Invoke() ?? 0;
            _currentStepIndex = (lastStep >= 0 && lastStep < _activeTour.Steps.Count) ? lastStep : 0;
        }

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourController.OpenEditorStandalone: activeTour=\"{_activeTour?.Name ?? "(none)"}\", activeTourId=\"{_activeTour?.Id ?? "(none)"}\", rebound={reboundTour is not null}, activeTourInAllTours={_activeTour is not null && _allTours.Contains(_activeTour)}, stepIndex={_currentStepIndex}");

        if (_activeTour!.Steps.Count == 0)
        {
            _activeTour.Steps.Add(new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" });
            _currentStepIndex = 0;
        }

        var editor = new FrmGuidedTourStepEditor(
            step:                _activeTour.Steps[_currentStepIndex],
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            workspaceFolderProvider: _workspaceFolderProvider,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: IsActive ? NotifyStepEdited : null,
            jumpToStepCallback:  IsActive ? JumpToStep : null,
            commandRegistry:     _commandRegistry,
            triggerRegistry:     _triggerRegistry,
            contextRegistry:     _contextRegistry,
            onClosed:            _ => { _activeEditor = null; },
            addStepAfterCallback: HandleNewStepAfterFromEditor,
            deleteStepCallback:   HandleDeleteStepFromEditor,
            switchTourCallback:   HandleSwitchTourFromEditor,
            addTourCallback:      HandleAddTourFromEditor,
            deleteTourCallback:   HandleDeleteTourFromEditor,
            renameTourCallback:   HandleRenameTourFromEditor,
            extraPickWindowsProvider: _extraPickWindowsProvider,
            elementNamesProvider: _elementNamesProvider,
            onStepChanged:       idx => _saveLastEditorStepIndex?.Invoke(idx));
        _activeEditor = editor;
        editor.Show();
    }

    /// <summary>
    /// Refreshes the callout and navigator heading after an in-place step edit.
    /// </summary>
    public void NotifyStepEdited()
    {
        if (!IsActive) return;
        CloseActiveCallout();
        ShowStepCallout(CurrentStep);
    }

    /// <summary>Jumps directly to the given step index, closing the current callout and showing the new one.</summary>
    public void JumpToStep(int index)
    {
        if (!IsActive || _activeTour is null) return;
        if (index < 0 || index >= _activeTour.Steps.Count) return;
        _currentStepIndex = index;
        CloseActiveCallout();
        ShowStepCallout(CurrentStep);
    }

    /// <summary>Moves to the next step, or ends the tour if already at the last step.</summary>
    public void Next()
    {
        if (!IsActive) return;
        var commandsAfter = CurrentStep.EffectiveCommandsAfter;
        if (_currentStepIndex >= _activeTour!.Steps.Count - 1)
        {
            var allToursSnapshot = _allTours;
            var currentTourId    = _activeTour.Id;
            StopTourInternal(showHint: false, commandsAfter: commandsAfter);

            // Mark the tour completed before showing the selector so the completion
            // badge renders correctly for the tour that was just finished.
            GuidedTourStateStore.Shared.MarkCompleted(currentTourId);

            var hasOtherTours = allToursSnapshot.Any(t => t.Id != currentTourId);

            if (hasOtherTours)
            {
                var selected = FrmGuidedTourSelector.ShowForResult(
                    _ownerWindow,
                    allToursSnapshot,
                    id => GuidedTourStateStore.Shared.IsCompleted(id));
                if (selected is not null)
                    StartTour(selected, allToursSnapshot);
            }
            return;
        }
        FrmUltimateCallout.RecordTourAdvance();
        _currentStepIndex++;
        ShowCurrentStep(prevCommandsAfter: commandsAfter);
    }

    /// <summary>Moves to the previous step.</summary>
    public void Prev()
    {
        if (!IsActive || _currentStepIndex <= 0) return;
        var commandsAfter = CurrentStep.EffectiveCommandsAfter;
        _currentStepIndex--;
        ShowCurrentStep(prevCommandsAfter: commandsAfter, navigatingForward: false);
    }

    /// <summary>
    /// Marks the current tour completed and immediately starts the first uncompleted tour that
    /// isn't the current one.  Called when the user clicks "Next Tour" on the last step.
    /// </summary>
    private void NextTour()
    {
        if (!IsActive) return;
        var commandsAfter    = CurrentStep.EffectiveCommandsAfter;
        var allToursSnapshot = _allTours;
        var currentTourId    = _activeTour!.Id;
        StopTourInternal(showHint: false, commandsAfter: commandsAfter);
        GuidedTourStateStore.Shared.MarkCompleted(currentTourId);

        var nextTour = allToursSnapshot.FirstOrDefault(t =>
            t.Id != currentTourId &&
            !GuidedTourStateStore.Shared.IsCompleted(t.Id));
        if (nextTour is not null)
            StartTour(nextTour, allToursSnapshot);
    }

    /// <summary>
    /// Opens the guided tour selector as a modeless window. When the user picks a tour,
    /// the current tour is stopped first and then the chosen tour is started.
    /// </summary>
    private void ShowTourSelector()
    {
        // The "More Tours..." button is only visible on the last step, so arriving here
        // from that button means the user finished the tour. Mark it completed now so
        // the completion badge shows immediately when the selector opens.
        var completingTourId = (IsActive && _activeTour is not null &&
                                _currentStepIndex >= _activeTour.Steps.Count - 1)
                               ? _activeTour.Id : null;
        if (completingTourId is not null)
            GuidedTourStateStore.Shared.MarkCompleted(completingTourId);

        CloseActiveCallout();
        var allToursSnapshot = _allTours;
        FrmGuidedTourSelector.ShowModeless(
            _ownerWindow,
            allToursSnapshot,
            id => GuidedTourStateStore.Shared.IsCompleted(id),
            selected => {
                // Carry through any CommandsAfter on the step being abandoned.
                var commandsAfter = IsActive ? CurrentStep.EffectiveCommandsAfter : null;
                // If a new tour was selected, skip the "restart from Help" hint —
                // the new tour's own callout is about to appear.
                StopTourInternal(showHint: selected is null, commandsAfter: commandsAfter);
                if (selected is not null)
                    StartTour(selected, allToursSnapshot);
            });
    }

    /// <summary>
    /// Stops the tour, shows the "restart from Help" callout, and restores the pre-tour layout.
    /// </summary>
    public void StopTour()
    {
        var commandsAfter = IsActive ? CurrentStep.EffectiveCommandsAfter : null;
        StopTourInternal(showHint: true, commandsAfter: commandsAfter);
    }

    /// <summary>
    /// Synchronously clears all active-tour state during application shutdown.
    /// Unlike <see cref="StopTour"/>, this method does not run <c>commandsAfter</c> (which are
    /// async and cannot be awaited on the UI thread during <c>Closing</c>), does not restore the
    /// pre-tour layout, and does not show the restart hint — none of which are meaningful when
    /// the process is about to exit. The caller is expected to follow up with its own synchronous
    /// cleanup (e.g. removing dummy queue items and demo agents) before persisting state to disk.
    /// </summary>
    public void StopTourForShutdown()
    {
        if (!IsActive) return;

        _onStepChanging?.Invoke();
        _activeTriggerSubscription?.Dispose();
        _activeTriggerSubscription = null;
        _activeTour       = null;
        _currentStepIndex = 0;
        CloseActiveCallout();
        _tourInjectedThreadIds.Clear();
        _commandRegistry?.Execute("ClearShortcutTarget");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void HandleEditStep()
    {
        if (_activeTour is null) return;
        if (_activeEditor is { IsLoaded: true }) { _activeEditor.Activate(); return; }

        var editor = new FrmGuidedTourStepEditor(
            step:                CurrentStep,
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            workspaceFolderProvider: _workspaceFolderProvider,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: NotifyStepEdited,
            jumpToStepCallback:  JumpToStep,
            commandRegistry:     _commandRegistry,
            triggerRegistry:     _triggerRegistry,
            contextRegistry:     _contextRegistry,
            onClosed:            wasSaved => { _activeEditor = null; if (wasSaved) NotifyStepEdited(); },
            addStepAfterCallback: HandleNewStepAfterFromEditor,
            deleteStepCallback:   HandleDeleteStepFromEditor,
            switchTourCallback:   HandleSwitchTourFromEditor,
            addTourCallback:      HandleAddTourFromEditor,
            deleteTourCallback:   HandleDeleteTourFromEditor,
            renameTourCallback:   HandleRenameTourFromEditor,
            extraPickWindowsProvider: _extraPickWindowsProvider,
            elementNamesProvider: _elementNamesProvider);
        _activeEditor = editor;
        editor.Show();
    }

    private GuidedTourStep CurrentStep =>
        _activeTour!.Steps[_currentStepIndex];

    /// <summary>The currently active tour step, or null if no tour is running.</summary>
    public GuidedTourStep? PublicCurrentStep =>
        IsActive ? _activeTour!.Steps[_currentStepIndex] : null;

    private static string TourStepCounts(IEnumerable<GuidedTour> tours) =>
        string.Join(",", tours.Select(t => t.Steps.Count));

    private async void ShowCurrentStep(IReadOnlyList<string>? prevCommandsAfter = null, bool navigatingForward = true)
    {
        _activeTriggerSubscription?.Dispose();
        _activeTriggerSubscription = null;
        _onStepChanging?.Invoke();
        CloseActiveCallout();
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"ShowCurrentStep: stepIndex={_currentStepIndex}, tourStepCount={_activeTour?.Steps.Count ?? -1}");
        // Run the previous step's CommandsAfter now that its callout is closed,
        // before showing the new step. This supports async commands (e.g. InjectTranscriptTextWithReplies).
        foreach (var cmd in prevCommandsAfter ?? [])
            await (_commandRegistry?.ExecuteAsync(cmd) ?? Task.CompletedTask);
        RunPreAction(CurrentStep);
        var step = CurrentStep;

        // Evaluate context condition — skip step silently if not satisfied.
        // Do NOT run before/after commands on a skipped step.
        if (!string.IsNullOrWhiteSpace(step.RequiredContext) && _contextRegistry is not null)
        {
            var ctxResult = _contextRegistry.Evaluate(step.RequiredContext);
            if (ctxResult.HasValue && ctxResult.Value != step.RequiredContextValue)
            {
                SquadDashTrace.Write(TraceCategory.Callouts,
                    $"ShowCurrentStep: skipping step \"{step.Title}\" — context \"{step.RequiredContext}\"={ctxResult.Value}, required={step.RequiredContextValue}");
                if (navigatingForward)
                {
                    if (_currentStepIndex < _activeTour!.Steps.Count - 1)
                    {
                        _currentStepIndex++;
                        ShowCurrentStep(prevCommandsAfter: null, navigatingForward: true);
                        return;
                    }
                    else
                    {
                        StopTour();
                        return;
                    }
                }
                else // navigating backward
                {
                    if (_currentStepIndex > 0)
                    {
                        _currentStepIndex--;
                        ShowCurrentStep(prevCommandsAfter: null, navigatingForward: false);
                        return;
                    }
                    // At step 0 going backward with condition fail: fall through and show step 0 anyway
                }
            }
        }

        foreach (var cmd in step.EffectiveCommandsBefore)
            await (_commandRegistry?.ExecuteAsync(cmd) ?? Task.CompletedTask);
        // Defer by one layout pass so that any UI changes made by RunPreAction or
        // CommandBefore (e.g. queue items added, panel opened) are fully rendered
        // before ShowStepCallout checks target.IsVisible.  Without this, the callout
        // is silently skipped on the first visit to a step that changes the UI.
        // The ReferenceEquals guard ensures we don't show a stale callout if the user
        // navigates before the deferred callback fires.
        await _ownerWindow.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        // Wait for any TypeIntoPrompt animation to complete before showing the callout.
        // The animation is fire-and-forget (DispatcherTimer), so ExecuteAsync for the
        // TypeIntoPrompt command returns immediately while typing is still in progress.
        // Without this wait, ShowStepCallout runs before the on-complete command fires
        // (e.g. ShowAtIntelliSense) and the target element (IntelliSensePopup) doesn't
        // exist in _tourNamedElements yet.
        if (_isTypeAnimationRunning is not null && _isTypeAnimationRunning())
        {
            while (IsActive && ReferenceEquals(CurrentStep, step) && _isTypeAnimationRunning())
                await Task.Delay(50);
            // Give the on-complete command (e.g. ShowAtIntelliSense) and resulting UI
            // changes a moment to settle before we try to locate the target element.
            if (IsActive && ReferenceEquals(CurrentStep, step))
                await Task.Delay(200);
        }
        if (IsActive && ReferenceEquals(CurrentStep, step))
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"ShowCurrentStep: guard passed, calling ShowStepCallout for \"{step.Title}\" (target=\"{step.TargetControlId}\")");
            ShowStepCallout(step);
            if (GuidedTourStateStore.Shared.MaintainFocus)
                _ownerWindow.Activate();
            _activeEditor?.SyncToActiveTourStep(_activeTour!, _currentStepIndex);
            _readingNudgeCts?.Cancel();
            _readingNudgeCts = new CancellationTokenSource();
            _ = StartReadingNudgeAsync(step, _readingNudgeCts.Token);
            _activeTriggerSubscription?.Dispose();
            _activeTriggerSubscription = _triggerRegistry?.Subscribe(step.AdvanceTrigger, () =>
                _ownerWindow.Dispatcher.InvokeAsync(Next));
        }
        else
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"ShowCurrentStep: guard BLOCKED — IsActive={IsActive}, ReferenceEquals={ReferenceEquals(CurrentStep, step)} — skipping ShowStepCallout for \"{step.Title}\"");
        }
    }

    private void ShowStepCallout(GuidedTourStep step)
    {
        if (string.IsNullOrWhiteSpace(step.TargetControlId))
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"ShowStepCallout: no target — showing centered callout for \"{step.Title}\"");
            // No target defined — show centered on screen with no dangle
            ShowCenteredCallout(step);
            return;
        }

        // Check for ControlName:Selection+N syntax
        var targetId = step.TargetControlId;
        int selectionIdx = targetId.IndexOf(":selection", StringComparison.OrdinalIgnoreCase);
        if (selectionIdx > 0)
        {
            string baseName = targetId[..selectionIdx];
            string suffix   = targetId[(selectionIdx + ":selection".Length)..].Trim();
            int padding = 0;
            if (suffix.StartsWith("+", StringComparison.Ordinal) &&
                int.TryParse(suffix[1..], out int parsed))
                padding = Math.Max(0, parsed);

            SquadDashTrace.Write(TraceCategory.Callouts,
                $"ShowStepCallout: detected :Selection syntax — baseName=\"{baseName}\", padding={padding}");

            var selElement = _elementLocator(baseName);
            if (selElement is TextBoxBase textBox)
            {
                var presSourceSel = System.Windows.PresentationSource.FromVisual(textBox);
                bool isRenderedSel = textBox.IsVisible
                                  || (textBox.ActualWidth > 0 && presSourceSel != null);
                if (isRenderedSel)
                {
                    var selRect = ComputeSelectionRect(textBox, padding);
                    if (!selRect.IsEmpty && selRect.Width > 0 && selRect.Height > 0)
                    {
                        SquadDashTrace.Write(TraceCategory.Callouts,
                            $"ShowStepCallout: :Selection rect=({selRect.X:F1},{selRect.Y:F1} {selRect.Width:F1}×{selRect.Height:F1}) for \"{step.Title}\"");
                        _activeCallout = FrmUltimateCallout.ShowCalloutBesideRect(
                            step.MarkdownText,
                            selRect,
                            _ownerWindow,
                            width:     320,
                            fontSize:  Application.Current.Resources.Contains("FontSizeCallout")
                                           ? (double)Application.Current.Resources["FontSizeCallout"]
                                           : 18.0,
                            placement: step.ParsedCalloutPlacement);
                        WireCalloutEvents();
                        return;
                    }
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"ShowStepCallout: :Selection rect empty/zero-area — falling back to element targeting for \"{step.Title}\"");
                }
                else
                {
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"ShowStepCallout: :Selection base element not visible — falling back for \"{step.Title}\"");
                }
                // Fall through to normal element-based targeting using baseName
                targetId = baseName;
            }
            else
            {
                SquadDashTrace.Write(TraceCategory.Callouts,
                    $"ShowStepCallout: :Selection base \"{baseName}\" not found or not a TextBoxBase — falling back to full targetId for \"{step.Title}\"");
                // Fall through using original targetId (whole control if it exists)
                targetId = baseName;
            }
        }

        var target = _elementLocator(targetId);
        if (target is null)
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"ShowStepCallout: target \"{step.TargetControlId}\" not found — falling back to centered callout for \"{step.Title}\"");
            ShowCenteredCallout(step);
            return;
        }

        // WPF Popup children live in a separate HwndSource and return IsVisible=false even when
        // the popup is open.  Treat an element with actual size and a valid PresentationSource
        // as rendered, regardless of IsVisible.
        var presSource = System.Windows.PresentationSource.FromVisual(target);
        bool isRendered = target.IsVisible
                       || (target.ActualWidth > 0 && presSource != null);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"ShowStepCallout: target \"{step.TargetControlId}\" found — type={target.GetType().Name}, "
            + $"IsVisible={target.IsVisible}, ActualW={target.ActualWidth:F1}, ActualH={target.ActualHeight:F1}, "
            + $"PresentationSource={(presSource != null ? "non-null" : "null")}, isRendered={isRendered}");
        if (!isRendered)
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"ShowStepCallout: target \"{step.TargetControlId}\" not visible — falling back to centered callout for \"{step.Title}\"");
            ShowCenteredCallout(step);
            return;
        }

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"ShowStepCallout: showing beside target \"{step.TargetControlId}\" for \"{step.Title}\", placement={step.CalloutPlacement}, markdownLen={step.MarkdownText.Length}");
        _activeCallout = FrmUltimateCallout.ShowCalloutBesideTarget(
            step.MarkdownText,
            target,
            width:     320,
            fontSize:  Application.Current.Resources.Contains("FontSizeCallout")
                           ? (double)Application.Current.Resources["FontSizeCallout"]
                           : 18.0,
            placement: step.ParsedCalloutPlacement);

        WireCalloutEvents();
    }

    /// <summary>
    /// Computes the screen-space bounding rect (logical DIP) for the current selection in
    /// <paramref name="textBox"/>, inflated by <paramref name="padding"/> pixels on all sides.
    /// Returns <see cref="Rect.Empty"/> when no valid selection rect can be determined.
    /// </summary>
    private static Rect ComputeSelectionRect(TextBoxBase textBoxBase, int padding)
    {
        if (textBoxBase is not TextBox textBox)
            return Rect.Empty;

        int start  = textBox.SelectionStart;
        int length = textBox.SelectionLength;

        // Use the same index for both endpoints when there is no selection.
        int endIndex = length > 0 ? start + Math.Max(0, length - 1) : start;

        Rect startRect = textBox.GetRectFromCharacterIndex(start);
        Rect endRect   = textBox.GetRectFromCharacterIndex(endIndex);

        if (startRect.IsEmpty && endRect.IsEmpty)
            return Rect.Empty;

        // Union of the two rects gives the bounding box in TextBox local coords.
        Rect localRect = startRect.IsEmpty ? endRect
                       : endRect.IsEmpty   ? startRect
                       : Rect.Union(startRect, endRect);

        if (localRect.IsEmpty || (localRect.Width == 0 && localRect.Height == 0))
            return Rect.Empty;

        // Inflate by padding.
        if (padding > 0)
            localRect.Inflate(padding, padding);

        // Convert from TextBox local coords to screen logical (DIP) coords.
        var physTL = textBox.PointToScreen(new Point(localRect.X,     localRect.Y));
        var physBR = textBox.PointToScreen(new Point(localRect.Right, localRect.Bottom));
        var logTL  = DpiHelper.PhysicalToLogical(textBox, physTL);
        var logBR  = DpiHelper.PhysicalToLogical(textBox, physBR);
        return new Rect(logTL, logBR);
    }

    /// <summary>Wires the standard tour event handlers onto <see cref="_activeCallout"/>.</summary>
    private void WireCalloutEvents()
    {
        if (_activeCallout is not null)
        {
            _activeCallout.Settled += (_, _) => _onCalloutShown?.Invoke();
            _activeCallout.HorizontalPercentOffset = (CurrentStep!.TargetOffsetX - 0.5) * 2;
            _activeCallout.VerticalPercentOffset   = (CurrentStep!.TargetOffsetY - 0.5) * 2;
            _activeCallout.IsSticky = true;
            _activeCallout.TourNavAdvanceCountProvider = () => GuidedTourStateStore.Shared.TourNavAdvanceCount;
            _activeCallout.TourNavAdvanceRecorder = GuidedTourStateStore.Shared.RecordTourNavAdvance;
            _activeCallout.IsTourMode = true;
            _activeCallout.IsTourFirstStep = (_currentStepIndex == 0);
            bool isLastStep = (_currentStepIndex == _activeTour!.Steps.Count - 1);
            _activeCallout.IsTourLastStep = isLastStep;
            var nextTour = isLastStep
                ? _allTours.FirstOrDefault(t =>
                    t.Id != _activeTour.Id &&
                    !GuidedTourStateStore.Shared.IsCompleted(t.Id))
                : null;
            _activeCallout.IsTourHasNextTour  = nextTour is not null;
            _activeCallout.IsTourNextTourName = nextTour?.Name;
            _activeCallout.IsTourEditModeVisible = SquadDashEnvironment.IsDeveloperMode;
            _activeCallout.TourNextRequested         += (_, _) => Next();
            _activeCallout.TourPrevRequested         += (_, _) => Prev();
            _activeCallout.TourEditRequested         += (_, _) => HandleEditStep();
            _activeCallout.TourNewStepAfterRequested  += (_, _) => HandleNewStepAfter();
            _activeCallout.TourNewStepBeforeRequested += (_, _) => HandleNewStepBefore();
            _activeCallout.TourDeleteRequested       += (_, _) => HandleDeleteStep();
            _activeCallout.TourNextTourRequested     += (_, _) => NextTour();
            _activeCallout.TourMoreToursRequested    += (_, _) => ShowTourSelector();
            _activeCallout.UserDismissStarting       += (_, _) => StopTour();
            _activeCallout.UserDismissed             += (_, _) => StopTour();
            _onCalloutShown?.Invoke();
        }
    }

    private void ShowCenteredCallout(GuidedTourStep step)
    {
        _activeCallout = FrmUltimateCallout.ShowCalloutCenteredOnScreen(
            step.MarkdownText,
            _ownerWindow,
            width:    640,
            fontSize: Application.Current.Resources.Contains("FontSizeCallout")
                          ? (double)Application.Current.Resources["FontSizeCallout"]
                          : 18.0);

        if (_activeCallout is not null)
        {
            _activeCallout.IsSticky = true;
            _activeCallout.TourNavAdvanceCountProvider = () => GuidedTourStateStore.Shared.TourNavAdvanceCount;
            _activeCallout.TourNavAdvanceRecorder = GuidedTourStateStore.Shared.RecordTourNavAdvance;
            _activeCallout.IsTourMode = true;
            _activeCallout.IsTourFirstStep = (_currentStepIndex == 0);
            bool isLastStep = (_currentStepIndex == _activeTour!.Steps.Count - 1);
            _activeCallout.IsTourLastStep = isLastStep;
            var nextTour = isLastStep
                ? _allTours.FirstOrDefault(t =>
                    t.Id != _activeTour.Id &&
                    !GuidedTourStateStore.Shared.IsCompleted(t.Id))
                : null;
            _activeCallout.IsTourHasNextTour  = nextTour is not null;
            _activeCallout.IsTourNextTourName = nextTour?.Name;
            _activeCallout.IsTourEditModeVisible = SquadDashEnvironment.IsDeveloperMode;
            _activeCallout.TourNextRequested         += (_, _) => Next();
            _activeCallout.TourPrevRequested         += (_, _) => Prev();
            _activeCallout.TourEditRequested         += (_, _) => HandleEditStep();
            _activeCallout.TourNewStepAfterRequested  += (_, _) => HandleNewStepAfter();
            _activeCallout.TourNewStepBeforeRequested += (_, _) => HandleNewStepBefore();
            _activeCallout.TourDeleteRequested       += (_, _) => HandleDeleteStep();
            _activeCallout.TourNextTourRequested     += (_, _) => NextTour();
            _activeCallout.TourMoreToursRequested    += (_, _) => ShowTourSelector();
            _activeCallout.UserDismissStarting       += (_, _) => StopTour();
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

    private async void StopTourInternal(bool showHint, IReadOnlyList<string>? commandsAfter = null)
    {
        var wasActive  = IsActive;
        var tourId     = _activeTour?.Id;

        _onStepChanging?.Invoke();

        _activeTriggerSubscription?.Dispose();
        _activeTriggerSubscription = null;

        _activeTour       = null;
        _currentStepIndex = 0;

        CloseActiveCallout();

        // Run CommandsAfter after the callout is closed, supporting async commands.
        if (wasActive)
            foreach (var cmd in commandsAfter ?? [])
                await (_commandRegistry?.ExecuteAsync(cmd) ?? Task.CompletedTask);

        if (wasActive)
        {
            _restorePreTourLayout?.Invoke();

            // Do NOT mark completed here — completion is only recorded when the user
            // reaches the last step via Next() or NextTour(). Stopping mid-tour should
            // not count as having completed it.
            if (showHint)
                ShowRestartHint();
        }

        // Clear after restorePreTourLayout so CleanUpTourInjectedThreads can still read the list.
        _tourInjectedThreadIds.Clear();
        _commandRegistry?.Execute("ClearShortcutTarget");
    }

    private void HandleNewStepAfter()
    {
        if (_activeTour is null) return;
        if (_activeEditor is { IsLoaded: true }) { _activeEditor.Activate(); return; }

        var newStep = new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" };
        var insertIndex = _currentStepIndex + 1;
        _activeTour.Steps.Insert(insertIndex, newStep);
        _currentStepIndex = insertIndex;
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"HandleNewStepAfter: inserted new step at index {insertIndex}, tour now has {_activeTour.Steps.Count} steps");

        var editor = new FrmGuidedTourStepEditor(
            step:                newStep,
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            workspaceFolderProvider: _workspaceFolderProvider,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: NotifyStepEdited,
            jumpToStepCallback:  JumpToStep,
            commandRegistry:     _commandRegistry,
            triggerRegistry:     _triggerRegistry,
            onClosed:            wasSaved =>
            {
                _activeEditor = null;
                SquadDashTrace.Write(TraceCategory.Callouts,
                    $"HandleNewStepAfter: editor closed — WasSaved={wasSaved}, stepIndex={_currentStepIndex}, stepCount={_activeTour.Steps.Count}");
                if (wasSaved)
                {
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"HandleNewStepAfter: save confirmed — title=\"{_activeTour.Steps[_currentStepIndex].Title}\", target=\"{_activeTour.Steps[_currentStepIndex].TargetControlId}\", markdown length={_activeTour.Steps[_currentStepIndex].MarkdownText.Length}");
                    ShowCurrentStep();
                }
                else
                {
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"HandleNewStepAfter: cancelled — removing step at {insertIndex}, reverting to {Math.Max(0, insertIndex - 1)}");
                    _activeTour.Steps.RemoveAt(insertIndex);
                    _currentStepIndex = Math.Max(0, insertIndex - 1);
                    ShowCurrentStep();
                }
            },
            addStepAfterCallback: HandleNewStepAfterFromEditor,
            deleteStepCallback:   HandleDeleteStepFromEditor,
            switchTourCallback:   HandleSwitchTourFromEditor,
            addTourCallback:      HandleAddTourFromEditor,
            deleteTourCallback:   HandleDeleteTourFromEditor,
            renameTourCallback:   HandleRenameTourFromEditor,
            extraPickWindowsProvider: _extraPickWindowsProvider,
            elementNamesProvider: _elementNamesProvider);
        _activeEditor = editor;
        editor.Show();
    }

    private void HandleNewStepBefore()
    {
        if (_activeTour is null) return;
        if (_activeEditor is { IsLoaded: true }) { _activeEditor.Activate(); return; }

        var newStep = new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" };
        var insertIndex = _currentStepIndex;  // Insert BEFORE current step
        _activeTour.Steps.Insert(insertIndex, newStep);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"HandleNewStepBefore: inserted new step at index {insertIndex}, tour now has {_activeTour.Steps.Count} steps");

        var editor = new FrmGuidedTourStepEditor(
            step:                newStep,
            stepIndex:           _currentStepIndex,
            activeTour:          _activeTour,
            allTours:            _allTours,
            workspaceFolderPath: WorkspaceFolderPath,
            workspaceFolderProvider: _workspaceFolderProvider,
            owner:               _ownerWindow,
            captureLayout:       _savePreTourLayout,
            livePreviewCallback: NotifyStepEdited,
            jumpToStepCallback:  JumpToStep,
            commandRegistry:     _commandRegistry,
            triggerRegistry:     _triggerRegistry,
            onClosed:            wasSaved =>
            {
                _activeEditor = null;
                SquadDashTrace.Write(TraceCategory.Callouts,
                    $"HandleNewStepBefore: editor closed — WasSaved={wasSaved}, stepIndex={_currentStepIndex}, stepCount={_activeTour.Steps.Count}");
                if (wasSaved)
                {
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"HandleNewStepBefore: save confirmed — title=\"{_activeTour.Steps[_currentStepIndex].Title}\", target=\"{_activeTour.Steps[_currentStepIndex].TargetControlId}\", markdown length={_activeTour.Steps[_currentStepIndex].MarkdownText.Length}");
                    ShowCurrentStep();
                }
                else
                {
                    SquadDashTrace.Write(TraceCategory.Callouts,
                        $"HandleNewStepBefore: cancelled — removing step at {insertIndex}");
                    _activeTour.Steps.RemoveAt(insertIndex);
                    // _currentStepIndex stays the same (the original step is back)
                    ShowCurrentStep();
                }
            },
            addStepAfterCallback: HandleNewStepAfterFromEditor,
            deleteStepCallback:   HandleDeleteStepFromEditor,
            switchTourCallback:   HandleSwitchTourFromEditor,
            addTourCallback:      HandleAddTourFromEditor,
            deleteTourCallback:   HandleDeleteTourFromEditor,
            renameTourCallback:   HandleRenameTourFromEditor,
            extraPickWindowsProvider: _extraPickWindowsProvider,
            elementNamesProvider: _elementNamesProvider);
        _activeEditor = editor;
        editor.Show();
    }

    private void HandleNewStepAfterFromEditor()
    {
        if (_activeTour is null || _activeEditor is null) return;
        var newStep = new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" };
        var insertIndex = _currentStepIndex + 1;
        _activeTour.Steps.Insert(insertIndex, newStep);
        if (!string.IsNullOrWhiteSpace(WorkspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, WorkspaceFolderPath); }
            catch { /* ignore */ }
        }
        _currentStepIndex = insertIndex;
        _activeEditor.RefreshStepList(insertIndex);
        ShowCurrentStep();
    }

    private void HandleDeleteStepFromEditor()
    {
        if (_activeTour is null || _activeTour.Steps.Count == 0 || _activeEditor is null) return;

        var result = MessageBox.Show(
            _activeEditor,
            $"Delete step {_currentStepIndex + 1} of {_activeTour.Steps.Count}?\n\n\"{CurrentStep.Title}\"",
            "Delete Step",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        var deleteIndex = _currentStepIndex;
        _activeTour.Steps.RemoveAt(deleteIndex);

        if (!string.IsNullOrWhiteSpace(WorkspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, WorkspaceFolderPath); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _activeEditor,
                    $"Step deleted from memory but could not be saved to disk:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (_activeTour.Steps.Count == 0)
        {
            _activeEditor.Close();
            StopTour();
            return;
        }

        _currentStepIndex = Math.Min(deleteIndex, _activeTour.Steps.Count - 1);
        _activeEditor.RefreshStepList(_currentStepIndex);
        ShowCurrentStep();
    }

    private void HandleDeleteStep()
    {
        if (_activeTour is null || _activeTour.Steps.Count == 0) return;
        _activeEditor?.Close();

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
                "Start guided tours from the **Help** menu.",
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
        _readingNudgeCts?.Cancel();
        _readingNudgeCts = null;
        if (_activeCallout is null) return;
        try { _activeCallout.Close(); } catch { /* already closed */ }
        _activeCallout = null;
    }

    private async Task StartReadingNudgeAsync(GuidedTourStep step, CancellationToken ct)
    {
        try
        {
            // Wait for TypeIntoPrompt animation to finish first
            if (_isTypeAnimationRunning is not null)
            {
                while (_isTypeAnimationRunning())
                    await Task.Delay(100, ct);
            }

            // Delay = words × 250ms × 2, minimum 2 seconds
            int wordCount = CountMarkdownWords(step.MarkdownText);
            int delayMs   = Math.Max(2000, wordCount * 500);
            await Task.Delay(delayMs, ct);

            if (ct.IsCancellationRequested) return;

            await _ownerWindow.Dispatcher.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    _activeCallout?.StartNextButtonGlow();
            });
        }
        catch (OperationCanceledException) { /* expected on step change */ }
    }

    private static int CountMarkdownWords(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return 0;
        var text = System.Text.RegularExpressions.Regex.Replace(markdown, @"[#*`\[\]()>~_]", " ");
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private void HandleSwitchTourFromEditor(int tourIndex)
    {
        if (tourIndex < 0 || tourIndex >= _allTours.Count) return;
        var newTour = _allTours[tourIndex];
        if (ReferenceEquals(newTour, _activeTour)) return;

        _activeTour       = newTour;
        _currentStepIndex = 0;
        _saveLastEditorTourName?.Invoke(newTour.Name);
        _saveLastEditorStepIndex?.Invoke(0);

        _activeEditor?.SwitchActiveTour(newTour, 0);
        ShowCurrentStep();
    }

    private void HandleAddTourFromEditor()
    {
        var newTour = new GuidedTour
        {
            Id    = Guid.NewGuid().ToString("N")[..8],
            Name  = "New Tour",
            Steps = new List<GuidedTourStep> { new GuidedTourStep { Title = "New Step", CalloutPlacement = "Auto" } }
        };
        _allTours.Add(newTour);

        if (!string.IsNullOrWhiteSpace(WorkspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, WorkspaceFolderPath); }
            catch { /* ignore */ }
        }

        _activeTour       = newTour;
        _currentStepIndex = 0;

        _activeEditor?.RefreshTourList(_allTours.Count - 1);
        _activeEditor?.SwitchActiveTour(newTour, 0);
        ShowCurrentStep();
        _activeEditor?.BeginTourRename(_allTours.Count - 1);
    }

    private void HandleRenameTourFromEditor(int tourIndex, string newName)
    {
        if (tourIndex < 0 || tourIndex >= _allTours.Count) return;
        if (string.IsNullOrWhiteSpace(newName)) return;
        _allTours[tourIndex].Name = newName;
        if (!string.IsNullOrWhiteSpace(WorkspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, WorkspaceFolderPath); }
            catch { /* ignore */ }
        }
        if (ReferenceEquals(_allTours[tourIndex], _activeTour))
            _activeEditor?.UpdateWindowTitle();
    }

    private void HandleDeleteTourFromEditor()
    {
        if (_activeTour is null || _allTours.Count <= 1)
        {
            MessageBox.Show(_activeEditor, "Cannot delete the only tour.", "Delete Tour", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            _activeEditor,
            $"Delete tour \"{_activeTour.Name}\"? This will remove all {_activeTour.Steps.Count} step(s).",
            "Delete Tour",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        var deleteIndex = _allTours.IndexOf(_activeTour);
        _allTours.Remove(_activeTour);

        if (!string.IsNullOrWhiteSpace(WorkspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, WorkspaceFolderPath); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _activeEditor,
                    $"Tour deleted from memory but could not be saved to disk:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        var newTourIndex  = Math.Min(deleteIndex, _allTours.Count - 1);
        _activeTour       = _allTours[newTourIndex];
        _currentStepIndex = 0;

        _activeEditor?.RefreshTourList(newTourIndex);
        _activeEditor?.SwitchActiveTour(_activeTour, 0);
        ShowCurrentStep();
    }
}
