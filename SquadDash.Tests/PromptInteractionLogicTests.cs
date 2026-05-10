namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptInteractionLogicTests {
    [Test]
    public void ResolveAction_UsesEnterToSubmitWhenRunIsEnabled() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.SubmitPrompt));
    }

    [Test]
    public void ResolveAction_LeavesShiftEnterAsTextEntry() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false,
            shiftPressed: true,
            runButtonEnabled: true,
            isMultiLinePrompt: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    [TestCase(PromptInputKey.Up, PromptInputAction.NavigateHistoryPrevious)]
    [TestCase(PromptInputKey.Down, PromptInputAction.NavigateHistoryNext)]
    public void ResolveAction_MapsCtrlArrowToHistoryNavigation(
        PromptInputKey key,
        PromptInputAction expectedAction) {
        var action = PromptInputBehavior.ResolveAction(
            key,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: true);

        Assert.That(action, Is.EqualTo(expectedAction));
    }

    [Test]
    public void ResolveAction_CtrlEnterReturnsPrioritizeInQueue() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.PrioritizeInQueue));
    }

    [Test]
    public void ResolveAction_CtrlEnterPrioritizesEvenWhenRunIsDisabled() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: false,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.PrioritizeInQueue));
    }

    [Test]
    public void Navigate_CapturesDraftAndRestoresItWhenReturningToTop() {
        var history = new[] { "first", "second" };

        var previous = PromptHistoryNavigator.Navigate(
            history,
            historyIndex: null,
            historyDraft: null,
            currentText: "draft prompt",
            direction: -1);

        Assert.Multiple(() => {
            Assert.That(previous.Changed, Is.True);
            Assert.That(previous.Text, Is.EqualTo("second"));
            Assert.That(previous.HistoryIndex, Is.EqualTo(1));
            Assert.That(previous.HistoryDraft, Is.EqualTo("draft prompt"));
        });

        var restored = PromptHistoryNavigator.Navigate(
            history,
            previous.HistoryIndex,
            previous.HistoryDraft,
            currentText: previous.Text,
            direction: 1);

        Assert.Multiple(() => {
            Assert.That(restored.Changed, Is.True);
            Assert.That(restored.Text, Is.EqualTo("draft prompt"));
            Assert.That(restored.HistoryIndex, Is.Null);
            Assert.That(restored.HistoryDraft, Is.EqualTo("draft prompt"));
        });
    }

    [Test]
    public void Calculate_DisablesPromptUntilSquadIsInstalled() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: false,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.Multiple(() => {
            Assert.That(state.InstallSquadEnabled, Is.True);
            Assert.That(state.PromptEnabled, Is.False);
            Assert.That(state.RunEnabled, Is.False);
            Assert.That(state.OutputEnabled, Is.False);
        });
    }

    [Test]
    public void Calculate_DisablesSendButtonWhileRunIsInFlight() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: true,
            canAbortBackgroundTask: false,
            currentPromptText: "build the docs");

        Assert.Multiple(() => {
            Assert.That(state.PromptEnabled, Is.True);
            Assert.That(state.RunEnabled, Is.False);
            Assert.That(state.RunDoctorEnabled, Is.False);
            Assert.That(state.AgentItemsEnabled, Is.True);
            Assert.That(state.InstallSquadEnabled, Is.False);
        });
    }

    [TestCase("/tasks")]
    [TestCase("/dropTasks")]
    [TestCase("/status")]
    [TestCase("/agents")]
    [TestCase("/model")]
    [TestCase("/version")]
    [TestCase("/help")]
    public void Calculate_EnablesRunForBusySafeLocalCommands(string prompt) {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: true,
            canAbortBackgroundTask: false,
            currentPromptText: prompt);

        Assert.That(state.RunEnabled, Is.True);
    }

    [Test]
    public void Calculate_DoesNotEnableRunForClearWhileBusy() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: true,
            canAbortBackgroundTask: false,
            currentPromptText: "/clear");

        Assert.That(state.RunEnabled, Is.False);
    }

    [Test]
    public void Calculate_EnablesAbort_WhenBackgroundTaskCanBeCancelled() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: true,
            currentPromptText: string.Empty);

        Assert.That(state.AbortEnabled, Is.True);
    }

    [Test]
    public void LocalPromptSubmissionPolicy_ClearsBareCommandWhilePromptRunning() {
        // A bare slash command (no arguments) is always cleared after execution.
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/tasks", isPromptRunning: true),
            Is.False);
    }

    [Test]
    public void LocalPromptSubmissionPolicy_RetainsBusySafeCommandWithArgsWhilePromptRunning() {
        // When there are arguments after the command, retain so text isn't lost.
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/activate SomeAgent", isPromptRunning: true),
            Is.True);
    }

    [Test]
    public void LocalPromptSubmissionPolicy_ClearsDropTasksCommandWhilePromptRunning() {
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/dropTasks", isPromptRunning: true),
            Is.False);
    }

    [Test]
    public void LocalPromptSubmissionPolicy_DoesNotRetainBusySafeCommandWhenPromptIsIdle() {
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/tasks", isPromptRunning: false),
            Is.False);
    }

    // ── PromptInputBehavior — 5-arg overload edge cases ───────────────────────

    [Test]
    public void ResolveAction_EnterIsIgnoredWhenRunIsDisabled() {
        // Run button disabled → Enter must not submit; there is nothing to run.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: false,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    [Test]
    public void ResolveAction_CtrlQAddsQueueSlot() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Q,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.AddQueueSlot));
    }

    [TestCase(PromptInputKey.Other)]
    [TestCase(PromptInputKey.Tab)]
    [TestCase(PromptInputKey.Escape)]
    public void ResolveAction_UnboundKeys_ReturnNone(PromptInputKey key) {
        var action = PromptInputBehavior.ResolveAction(
            key,
            ctrlPressed: false,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    // ── PromptInputBehavior — 6-arg IntelliSense overload ────────────────────

    [Test]
    public void ResolveAction_IntelliSenseOpen_EscapeDismisses() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Escape,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseDismiss));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_TabAccepts() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Tab,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseAccept));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_UpWithoutCtrlNavigatesUp() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Up,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseUp));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_DownWithoutCtrlNavigatesDown() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Down,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseDown));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_EnterAccepts_WhenRunEnabled() {
        // Plain Enter with IntelliSense open accepts the suggestion (not submits).
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.IntelliSenseAccept));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_EnterReturnsNone_WhenRunDisabled() {
        // Enter with IntelliSense open and run disabled falls through to base
        // behaviour — IntelliSense Enter only fires when runButtonEnabled is true.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: false, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_CtrlUpStillNavigatesHistory() {
        // Ctrl+Up is not intercepted by the IntelliSense handler; it falls
        // through to the base 5-arg overload which maps it to history navigation.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Up,
            ctrlPressed: true, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.NavigateHistoryPrevious));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_CtrlEnterStillPrioritizes() {
        // Ctrl+Enter is not intercepted by the IntelliSense handler.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.PrioritizeInQueue));
    }

    [Test]
    public void ResolveAction_IntelliSenseClosed_TabReturnsNone() {
        // Tab with IntelliSense closed must fall through to base behaviour,
        // which does not bind Tab at all.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Tab,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.None));
    }

    [Test]
    public void ResolveAction_IntelliSenseClosed_BehavesLikeBaseOverload() {
        // With IntelliSense closed the 6-arg overload must produce the same
        // result as the 5-arg overload for all base-handled inputs.
        var baseAction = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false);

        var extendedAction = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: false, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: false);

        Assert.That(extendedAction, Is.EqualTo(baseAction));
    }

    // ── PromptHistoryNavigator — edge cases ───────────────────────────────────

    [Test]
    public void Navigate_EmptyHistory_ReturnsUnchangedResult() {
        var result = PromptHistoryNavigator.Navigate(
            history: Array.Empty<string>(),
            historyIndex: null,
            historyDraft: null,
            currentText: "working",
            direction: -1);

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Text, Is.EqualTo("working"));
            Assert.That(result.HistoryIndex, Is.Null);
            Assert.That(result.HistoryDraft, Is.Null);
        });
    }

    [Test]
    public void Navigate_BackwardFromFirstEntry_ClampsAndReportsNoChange() {
        var history = new[] { "first", "second" };

        // Navigate to the first entry (index 0).
        var atFirst = PromptHistoryNavigator.Navigate(
            history, historyIndex: 0, historyDraft: "draft",
            currentText: "first", direction: -1);

        Assert.Multiple(() => {
            Assert.That(atFirst.Changed, Is.False,
                "Cannot go further back than index 0; result must report no change.");
            Assert.That(atFirst.HistoryIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void Navigate_ForwardAtTopLevel_ReportsNoChange() {
        // historyIndex == null means we are already at the live-draft position
        // (conceptually index == history.Count). Going forward from there is a no-op.
        var history = new[] { "first", "second" };

        var result = PromptHistoryNavigator.Navigate(
            history, historyIndex: null, historyDraft: null,
            currentText: "live text", direction: +1);

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Text, Is.EqualTo("live text"));
        });
    }

    [Test]
    public void Navigate_WithPreExistingHistoryIndexAndDraft_PreservesDraft() {
        // When the caller is already mid-history (historyIndex set, historyDraft set),
        // a further navigation step must preserve the original draft rather than
        // overwriting it with the currently-displayed history entry.
        var history = new[] { "first", "second", "third" };

        var result = PromptHistoryNavigator.Navigate(
            history,
            historyIndex: 2,            // currently viewing "third"
            historyDraft: "my draft",   // original draft already captured
            currentText: "third",
            direction: -1);             // go one step back to "second"

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.True);
            Assert.That(result.Text, Is.EqualTo("second"));
            Assert.That(result.HistoryIndex, Is.EqualTo(1));
            Assert.That(result.HistoryDraft, Is.EqualTo("my draft"),
                "Original draft must be preserved across further navigation steps.");
        });
    }

    [Test]
    public void Navigate_ReturnsToLiveDraftFromFirstEntry() {
        // Navigate all the way back to index 0, then forward step-by-step
        // to verify the draft is restored when we exit history entirely.
        var history = new[] { "first", "second" };

        var atFirst = PromptHistoryNavigator.Navigate(
            history, historyIndex: null, historyDraft: null,
            currentText: "live", direction: -1);    // → index 1 ("second")

        var atSecond = PromptHistoryNavigator.Navigate(
            history, atFirst.HistoryIndex, atFirst.HistoryDraft,
            currentText: atFirst.Text, direction: -1);   // → index 0 ("first")

        var restored = PromptHistoryNavigator.Navigate(
            history, atSecond.HistoryIndex, atSecond.HistoryDraft,
            currentText: atSecond.Text, direction: +1);  // → index 1 ("second")

        var backToLive = PromptHistoryNavigator.Navigate(
            history, restored.HistoryIndex, restored.HistoryDraft,
            currentText: restored.Text, direction: +1);  // → live draft

        Assert.Multiple(() => {
            Assert.That(backToLive.Changed, Is.True);
            Assert.That(backToLive.Text, Is.EqualTo("live"),
                "Original live-draft text must be restored when navigating back to the top.");
            Assert.That(backToLive.HistoryIndex, Is.Null);
        });
    }

    // ── InteractiveControlStateCalculator — missing branches ─────────────────

    [Test]
    public void Calculate_DisablesAllInteractions_WhileSquadIsInstalling() {
        // During installation, interactionsEnabled = false AND InstallSquadEnabled = false
        // (we don't want the user to click Install again mid-install).
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: false,
            isInstallingSquad: true,    // ← installing right now
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.Multiple(() => {
            Assert.That(state.PromptEnabled, Is.False);
            Assert.That(state.RunEnabled, Is.False);
            Assert.That(state.AgentItemsEnabled, Is.False);
            Assert.That(state.OutputEnabled, Is.False);
            Assert.That(state.CopyEnabled, Is.False);
            Assert.That(state.AbortEnabled, Is.False);
            Assert.That(state.RunDoctorEnabled, Is.False);
            Assert.That(state.InstallSquadEnabled, Is.False,
                "Install button must be disabled while an installation is already in progress.");
        });
    }

    [Test]
    public void Calculate_DisablesInstallButton_WhenNoWorkspaceIsOpen() {
        // The Install Squad button is only meaningful when there is a workspace
        // to install into — suppress it when none is selected.
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: false,        // ← no workspace
            squadInstalled: false,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.That(state.InstallSquadEnabled, Is.False);
    }

    [Test]
    public void Calculate_EnablesAllControls_WhenFullyOperational() {
        // When Squad is installed, idle, and a workspace is open, every interactive
        // control should be enabled (except Install, which is suppressed when installed).
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.Multiple(() => {
            Assert.That(state.AgentItemsEnabled, Is.True);
            Assert.That(state.OutputEnabled, Is.True);
            Assert.That(state.CopyEnabled, Is.True);
            Assert.That(state.PromptEnabled, Is.True);
            Assert.That(state.RunEnabled, Is.True);
            Assert.That(state.RunDoctorEnabled, Is.True);
            Assert.That(state.AbortEnabled, Is.False,
                "Nothing is running — Abort must be disabled.");
            Assert.That(state.InstallSquadEnabled, Is.False,
                "Squad is already installed — Install must be disabled.");
        });
    }

    [Test]
    public void Calculate_EnablesAbort_WhenPromptIsRunning() {
        // AbortEnabled = (isPromptRunning || canAbortBackgroundTask).
        // isPromptRunning alone must be sufficient — no background task required.
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: true,
            canAbortBackgroundTask: false,  // ← explicitly off
            currentPromptText: string.Empty);

        Assert.That(state.AbortEnabled, Is.True,
            "Abort must be available whenever a prompt is actively running.");
    }

    [Test]
    public void Calculate_EnablesRunDoctor_WhenInstalledAndIdle() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.That(state.RunDoctorEnabled, Is.True);
    }

    // ── LocalPromptSubmissionPolicy — missing branches ────────────────────────

    [TestCase("/agents")]
    [TestCase("/approval")]
    [TestCase("/doctor")]
    [TestCase("/dropTasks")]
    [TestCase("/help")]
    [TestCase("/hire")]
    [TestCase("/model")]
    [TestCase("/sessions")]
    [TestCase("/status")]
    [TestCase("/tasks")]
    [TestCase("/trace")]
    [TestCase("/version")]
    public void IsImmediateLocalCommand_ReturnsTrueForLocalCommands(string command) {
        Assert.That(LocalPromptSubmissionPolicy.IsImmediateLocalCommand(command), Is.True);
    }

    [TestCase("/activate")]
    [TestCase("/deactivate")]
    [TestCase("/retire")]
    public void IsImmediateLocalCommand_ReturnsFalseForCommandsThatRequireAiRoundtrip(string command) {
        // /activate, /deactivate, /retire are in BusySafeCommands but NOT in
        // ImmediateLocalCommands — they still go through the AI.
        Assert.That(LocalPromptSubmissionPolicy.IsImmediateLocalCommand(command), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("regular prompt text")]
    public void IsImmediateLocalCommand_ReturnsFalseForNullEmptyOrPlainText(string? input) {
        Assert.That(LocalPromptSubmissionPolicy.IsImmediateLocalCommand(input), Is.False);
    }

    [TestCase("/activate")]
    [TestCase("/deactivate")]
    [TestCase("/retire")]
    public void CanSubmitWhilePromptRunning_ReturnsTrueForBusySafeCommands(string command) {
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(command), Is.True);
    }

    [TestCase("/clear")]
    [TestCase("build the docs")]
    [TestCase("/unknown")]
    public void CanSubmitWhilePromptRunning_ReturnsFalseForNonBusySafeCommands(string prompt) {
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(prompt), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CanSubmitWhilePromptRunning_ReturnsFalseForNullOrEmpty(string? prompt) {
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(prompt), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ShouldRetainPromptAfterLocalSubmit_ReturnsFalseForNullOrEmpty(string? prompt) {
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit(prompt, isPromptRunning: true),
            Is.False);
    }

    [Test]
    public void ShouldRetainPromptAfterLocalSubmit_ReturnsFalseForNonBusySafeCommand() {
        // /clear is not in BusySafeCommands, so CanSubmitWhilePromptRunning returns false,
        // and therefore ShouldRetain must also return false.
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/clear", isPromptRunning: true),
            Is.False);
    }
}

// ── RunButtonLabelPolicy tests ────────────────────────────────────────────────

[TestFixture]
internal sealed class RunButtonLabelPolicyTests {

    // ── Normal mode (no active tab, coordinator idle) ─────────────────────────

    [Test]
    public void Compute_IdleCoordinator_EmptyQueue_ReturnsSend() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 0, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_IdleCoordinator_ItemsInQueue_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 3, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_BusyCoordinator_EmptyQueue_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 0, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_BusyCoordinator_ItemsInQueue_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 2, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    // ── Queue-paused state ────────────────────────────────────────────────────

    [Test]
    public void Compute_QueuePaused_WithItemsInQueue_ReturnsSend() {
        // Key scenario: AI asked a question, queue has remaining items —
        // button must show "Send" so user knows their reply fires immediately.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: true,
            queueCount: 2, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_QueuePaused_EmptyQueue_ReturnsSend() {
        // Paused but queue is now empty: coordinator is idle, so "Send".
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: true,
            queueCount: 0, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_QueuePausedFlag_OnlyActivatesOverride_WhenQueueNonEmpty() {
        // Paused flag is set but queue is empty → falls through to normal logic → Send.
        // This is not the special override path, just coincidentally also "Send".
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: true,
            queueCount: 0, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_QueuePaused_BusyCoordinator_WithItemsInQueue_ReturnsSend() {
        // Even if coordinator is technically marked busy (e.g. re-entering state),
        // the paused-queue override fires first.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: true,
            queueCount: 1, activeTabId: null);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    // ── Active tab (editing a queued item) ────────────────────────────────────

    [Test]
    public void Compute_ActiveTab_CoordinatorIdle_ReturnsSend() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 1, activeTabId: "tab-42");
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_ActiveTab_CoordinatorBusy_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 1, activeTabId: "tab-42");
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_QueuePaused_CoordinatorIdle_ReturnsSend() {
        // Active-tab path ignores queuePausedAwaitingInput entirely.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: true,
            queueCount: 1, activeTabId: "tab-99");
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_ActiveTab_QueuePaused_CoordinatorBusy_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: true,
            queueCount: 1, activeTabId: "tab-99");
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    // ── Edge: queueCount boundary ─────────────────────────────────────────────

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(10)]
    public void Compute_Normal_QueueCount_VariousValues(int count) {
        var expected = count > 0 ? RunButtonLabelPolicy.LabelQueue : RunButtonLabelPolicy.LabelSend;
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: count, activeTabId: null);
        Assert.That(label, Is.EqualTo(expected));
    }
}

// ── SlashCommandParameterPolicy tests ─────────────────────────────────────────

[TestFixture]
internal sealed class SlashCommandParameterPolicyTests {

    [TestCase("/activate")]
    [TestCase("/deactivate")]
    [TestCase("/retire")]
    public void RequiresParameter_ReturnsTrueForParameterRequiredCommands(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.True);
    }

    [TestCase("/status")]
    [TestCase("/help")]
    [TestCase("/agents")]
    [TestCase("/tasks")]
    [TestCase("/version")]
    [TestCase("/model")]
    [TestCase("/trace")]
    [TestCase("/doctor")]
    [TestCase("/hire")]
    [TestCase("/sessions")]
    public void RequiresParameter_ReturnsFalseForCommandsWithNoArgument(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.False);
    }

    [TestCase(" /activate ")]
    [TestCase("  /deactivate  ")]
    [TestCase("\t/retire")]
    public void RequiresParameter_TrimsWhitespaceBeforeChecking(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("activate")]   // missing leading slash — not in the set
    public void RequiresParameter_ReturnsFalseForEmptyOrUnrecognizedInput(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.False);
    }

    [TestCase("/ACTIVATE")]
    [TestCase("/Deactivate")]
    [TestCase("/RETIRE")]
    public void RequiresParameter_IsCaseInsensitive(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.True);
    }
}