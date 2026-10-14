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
    public void ResolveAction_CtrlEnterReturnsSteerPrompt() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: true,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.SteerPrompt));
    }

    [Test]
    public void ResolveAction_CtrlEnterSteersEvenWhenRunIsDisabled() {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true,
            shiftPressed: false,
            runButtonEnabled: false,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.SteerPrompt));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResolveAction_CtrlShiftEnterReturnsPrioritizeInQueue(bool runButtonEnabled) {
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true,
            shiftPressed: true,
            runButtonEnabled: runButtonEnabled,
            isMultiLinePrompt: false);

        Assert.That(action, Is.EqualTo(PromptInputAction.PrioritizeInQueue));
    }

    [Test]
    public void Navigate_CapturesDraftAndRestoresItWhenReturningToTop() {
        PromptHistoryEntry[] history = [new("first", []), new("second", [])];

        var previous = PromptHistoryNavigator.Navigate(
            history,
            historyIndex: null,
            historyDraft: null,
            historyDraftAttachments: null,
            currentText: "draft prompt", currentAttachments: [],
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
            null, currentText: previous.Text, currentAttachments: [],
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
    public void ResolveAction_IntelliSenseOpen_CtrlEnterStillSteers() {
        // Ctrl+Enter is not intercepted by the IntelliSense handler.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true, shiftPressed: false,
            runButtonEnabled: true, isMultiLinePrompt: false,
            isIntelliSenseOpen: true);

        Assert.That(action, Is.EqualTo(PromptInputAction.SteerPrompt));
    }

    [Test]
    public void ResolveAction_IntelliSenseOpen_CtrlShiftEnterStillPrioritizes() {
        // Ctrl+Shift+Enter is not intercepted by the IntelliSense handler.
        var action = PromptInputBehavior.ResolveAction(
            PromptInputKey.Enter,
            ctrlPressed: true, shiftPressed: true,
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
            history: Array.Empty<PromptHistoryEntry>(),
            historyIndex: null,
            historyDraft: null,
            historyDraftAttachments: null,
            currentText: "working", currentAttachments: [],
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
        PromptHistoryEntry[] history = [new("first", []), new("second", [])];

        // Navigate to the first entry (index 0).
        var atFirst = PromptHistoryNavigator.Navigate(
            history, historyIndex: 0, historyDraft: "draft",
            historyDraftAttachments: null,
            currentText: "first", currentAttachments: [], direction: -1);

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
        PromptHistoryEntry[] history = [new("first", []), new("second", [])];

        var result = PromptHistoryNavigator.Navigate(
            history, historyIndex: null, historyDraft: null,
            historyDraftAttachments: null,
            currentText: "live text", currentAttachments: [], direction: +1);

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
        PromptHistoryEntry[] history = [new("first", []), new("second", []), new("third", [])];

        var result = PromptHistoryNavigator.Navigate(
            history,
            historyIndex: 2,            // currently viewing "third"
            historyDraft: "my draft",   // original draft already captured
            historyDraftAttachments: null,
            currentText: "third", currentAttachments: [],
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
        PromptHistoryEntry[] history = [new("first", []), new("second", [])];

        var atFirst = PromptHistoryNavigator.Navigate(
            history, historyIndex: null, historyDraft: null,
            historyDraftAttachments: null,
            currentText: "live", currentAttachments: [], direction: -1);    // → index 1 ("second")

        var atSecond = PromptHistoryNavigator.Navigate(
            history, atFirst.HistoryIndex, atFirst.HistoryDraft,
            null, currentText: atFirst.Text, currentAttachments: [], direction: -1);   // → index 0 ("first")

        var restored = PromptHistoryNavigator.Navigate(
            history, atSecond.HistoryIndex, atSecond.HistoryDraft,
            null, currentText: atSecond.Text, currentAttachments: [], direction: +1);  // → index 1 ("second")

        var backToLive = PromptHistoryNavigator.Navigate(
            history, restored.HistoryIndex, restored.HistoryDraft,
            null, currentText: restored.Text, currentAttachments: [], direction: +1);  // → live draft

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
    [TestCase("/cast")]
    [TestCase("/doctor")]
    [TestCase("/dropTasks")]
    [TestCase("/health")]
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
    [TestCase("/cast")]
    [TestCase("/health")]
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

    // ── LocalPromptSubmissionPolicy — GetSlashCommand extraction ─────────────

    [Test]
    public void CanSubmitWhilePromptRunning_ReturnsTrueForBusySafeCommandWithTrailingArguments() {
        // GetSlashCommand must extract only the slash token before the first space,
        // so "/tasks some argument" is still treated as the busy-safe "/tasks" command.
        Assert.That(
            LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("/tasks some argument"),
            Is.True);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_ExtractsCommandBeforeNewline() {
        // GetSlashCommand splits on '\n' as well as space, matching the IndexOfAny
        // that includes '\n' and '\r'.
        Assert.That(
            LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("/tasks\nsome text"),
            Is.True);
    }

    [Test]
    public void ShouldRetainPromptAfterLocalSubmit_ReturnsFalseForCommandWithTrailingWhitespaceOnly() {
        // "/tasks  " trims to "/tasks" — the trimmed length equals the command length,
        // so there are no real arguments and the prompt must NOT be retained.
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/tasks  ", isPromptRunning: true),
            Is.False);
    }

    // ── LocalPromptSubmissionPolicy — IsImmediateLocalCommand extras ─────────

    [TestCase("/queue-sim")]
    [TestCase("/test-queue")]
    public void IsImmediateLocalCommand_ReturnsTrueForInternalDevCommands(string command) {
        // /queue-sim and /test-queue are in ImmediateLocalCommands for development
        // testing but are not in the BusySafeCommands set.
        Assert.That(LocalPromptSubmissionPolicy.IsImmediateLocalCommand(command), Is.True);
    }

    [TestCase("/queue-sim")]
    [TestCase("/test-queue")]
    public void CanSubmitWhilePromptRunning_ReturnsFalseForInternalDevCommands(string command) {
        // /queue-sim and /test-queue are NOT in BusySafeCommands — they are
        // immediate-local-only and must not be submitted while a prompt is running.
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(command), Is.False);
    }

    // ── PromptHistoryNavigator — single-item history round-trip ──────────────

    [Test]
    public void Navigate_SingleItemHistory_CanGoBackAndRestoreDraft() {
        // With exactly one history entry, navigate back to it and then forward
        // to verify the live draft is correctly restored.
        PromptHistoryEntry[] history = [new("only entry", [])];

        var atEntry = PromptHistoryNavigator.Navigate(
            history, historyIndex: null, historyDraft: null,
            historyDraftAttachments: null,
            currentText: "live draft", currentAttachments: [], direction: -1);

        Assert.Multiple(() => {
            Assert.That(atEntry.Changed, Is.True);
            Assert.That(atEntry.Text, Is.EqualTo("only entry"));
            Assert.That(atEntry.HistoryIndex, Is.EqualTo(0));
            Assert.That(atEntry.HistoryDraft, Is.EqualTo("live draft"));
        });

        var restored = PromptHistoryNavigator.Navigate(
            history, atEntry.HistoryIndex, atEntry.HistoryDraft,
            null, currentText: atEntry.Text, currentAttachments: [], direction: +1);

        Assert.Multiple(() => {
            Assert.That(restored.Changed, Is.True);
            Assert.That(restored.Text, Is.EqualTo("live draft"),
                "The original live-draft text must be restored when navigating forward past the single history entry.");
            Assert.That(restored.HistoryIndex, Is.Null);
        });
    }

    [Test]
    public void Navigate_SingleItemHistory_CannotGoBackBeyondFirstEntry() {
        // At index 0, further backward navigation must clamp and report no change.
        PromptHistoryEntry[] history = [new("only entry", [])];

        var atEntry = PromptHistoryNavigator.Navigate(
            history, historyIndex: 0, historyDraft: "draft",
            historyDraftAttachments: null,
            currentText: "only entry", currentAttachments: [], direction: -1);

        Assert.Multiple(() => {
            Assert.That(atEntry.Changed, Is.False,
                "Cannot navigate before index 0 in a single-item history.");
            Assert.That(atEntry.HistoryIndex, Is.EqualTo(0));
        });
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
    public void Compute_ActiveTab_RightmostTab_CoordinatorIdle_ReturnsSend() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 1, activeTabId: "tab-42", isRightmostTab: true);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_ActiveTab_NotRightmostTab_CoordinatorIdle_ReturnsSubmit() {
        // Non-rightmost tab: items exist to the right, so submitting queues behind them.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 2, activeTabId: "tab-42", isRightmostTab: false);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSubmit));
    }

    // ── queueManuallyPaused: draft tab (null activeTabId) ─────────────────────

    [Test]
    public void Compute_DraftTab_QueueManuallyPaused_CoordinatorIdle_ReturnsQueue() {
        // Active draft with manual pause: "Queue" because the button enqueues — nothing dispatches while paused.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 3, activeTabId: null, queueManuallyPaused: true);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_DraftTab_QueueManuallyPaused_CoordinatorBusy_ReturnsQueue() {
        // Busy coordinator still takes priority over the manual-pause override.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 2, activeTabId: null, queueManuallyPaused: true);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    // ── queueManuallyPaused: queued tabs show "Queue" (button focuses draft, not dispatches) ──

    [Test]
    public void Compute_ActiveTab_NotRightmost_QueueManuallyPaused_ReturnsQueue() {
        // While the queue is manually paused, button focuses draft — label is "Queue".
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 3, activeTabId: "tab-42", queueManuallyPaused: true, isRightmostTab: false);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_Rightmost_QueueManuallyPaused_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: false,
            queueCount: 1, activeTabId: "tab-42", queueManuallyPaused: true, isRightmostTab: true);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_QueueManuallyPaused_CoordinatorBusy_ReturnsQueue() {
        // Busy coordinator still takes priority even when manually paused.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 2, activeTabId: "tab-42", queueManuallyPaused: true, isRightmostTab: false);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_CoordinatorBusy_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 1, activeTabId: "tab-42");
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_NotRightmostTab_CoordinatorBusy_ReturnsQueue() {
        // Busy coordinator always returns Queue regardless of tab position.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: false,
            queueCount: 2, activeTabId: "tab-42", isRightmostTab: false);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_QueuePaused_RightmostTab_CoordinatorIdle_ReturnsSend() {
        // QRB state always shows "Send" on any non-busy active tab.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: true,
            queueCount: 1, activeTabId: "tab-99", isRightmostTab: true);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_ActiveTab_QRBState_NonRightmostTab_CoordinatorIdle_ReturnsSend() {
        // QRB state overrides "Submit" on non-rightmost tabs — user can dispatch any tab immediately.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: false, queuePausedAwaitingInput: true,
            queueCount: 2, activeTabId: "tab-99", isRightmostTab: false);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
    }

    [Test]
    public void Compute_ActiveTab_QueuePaused_CoordinatorBusy_ReturnsQueue() {
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: true,
            queueCount: 1, activeTabId: "tab-99");
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelQueue));
    }

    [Test]
    public void Compute_ActiveTab_QueuePaused_CoordinatorBusy_RightmostTab_ReturnsSend() {
        // QRB + rightmost tab bypasses the coordinator-busy guard so the user can
        // answer the quick-reply directly from the queued tab even while the loop runs.
        var label = RunButtonLabelPolicy.Compute(
            coordinatorBusy: true, queuePausedAwaitingInput: true,
            queueCount: 1, activeTabId: "tab-99", isRightmostTab: true);
        Assert.That(label, Is.EqualTo(RunButtonLabelPolicy.LabelSend));
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

// ── RunButtonClickPolicy tests ────────────────────────────────────────────────

[TestFixture]
internal sealed class RunButtonClickPolicyTests {

    // ── Draft tab (activeTabId = null) ────────────────────────────────────────

    [Test]
    public void Resolve_QueueIdle_DraftTab_ReturnsSend() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: false, isNativeLoopRunning: false,
            isManuallyPaused: false, activeTabId: null);
        Assert.That(action, Is.EqualTo(RunButtonClickAction.SendPrompt));
    }

    [Test]
    public void Resolve_PromptRunning_DraftTab_ReturnsEnqueue() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: true, isNativeLoopRunning: false,
            isManuallyPaused: false, activeTabId: null);
        Assert.That(action, Is.EqualTo(RunButtonClickAction.EnqueuePrompt));
    }

    [Test]
    public void Resolve_NativeLoopRunning_DraftTab_ReturnsEnqueue() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: false, isNativeLoopRunning: true,
            isManuallyPaused: false, activeTabId: null);
        Assert.That(action, Is.EqualTo(RunButtonClickAction.EnqueuePrompt));
    }

    [Test]
    public void Resolve_ManuallyPaused_DraftTab_ReturnsEnqueue() {
        // Queue paused + draft focused → enqueue (do not send, do not unpause).
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: false, isNativeLoopRunning: false,
            isManuallyPaused: true, activeTabId: null);
        Assert.That(action, Is.EqualTo(RunButtonClickAction.EnqueuePrompt));
    }

    [Test]
    public void Resolve_ManuallyPaused_PromptAlsoRunning_DraftTab_ReturnsEnqueue() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: true, isNativeLoopRunning: false,
            isManuallyPaused: true, activeTabId: null);
        Assert.That(action, Is.EqualTo(RunButtonClickAction.EnqueuePrompt));
    }

    // ── Queued tab (activeTabId != null) ──────────────────────────────────────

    [Test]
    public void Resolve_QueueIdle_QueuedTab_ReturnsDispatch() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: false, isNativeLoopRunning: false,
            isManuallyPaused: false, activeTabId: "tab-1");
        Assert.That(action, Is.EqualTo(RunButtonClickAction.DispatchTab));
    }

    [Test]
    public void Resolve_PromptRunning_QueuedTab_ReturnsFocusDraft() {
        // Queue running + queued tab selected → return focus to main draft.
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: true, isNativeLoopRunning: false,
            isManuallyPaused: false, activeTabId: "tab-1");
        Assert.That(action, Is.EqualTo(RunButtonClickAction.FocusDraft));
    }

    [Test]
    public void Resolve_NativeLoopRunning_QueuedTab_ReturnsFocusDraft() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: false, isNativeLoopRunning: true,
            isManuallyPaused: false, activeTabId: "tab-1");
        Assert.That(action, Is.EqualTo(RunButtonClickAction.FocusDraft));
    }

    [Test]
    public void Resolve_ManuallyPaused_QueuedTab_ReturnsFocusDraft() {
        // Queue paused + queued tab selected → return focus to main draft.
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: false, isNativeLoopRunning: false,
            isManuallyPaused: true, activeTabId: "tab-99");
        Assert.That(action, Is.EqualTo(RunButtonClickAction.FocusDraft));
    }

    [Test]
    public void Resolve_ManuallyPaused_PromptAlsoRunning_QueuedTab_ReturnsFocusDraft() {
        var action = RunButtonClickPolicy.Resolve(
            isPromptRunning: true, isNativeLoopRunning: false,
            isManuallyPaused: true, activeTabId: "tab-2");
        Assert.That(action, Is.EqualTo(RunButtonClickAction.FocusDraft));
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
    [TestCase("/cast")]
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


// ── PromptScrollNavMath tests ─────────────────────────────────────────────────

[TestFixture]
internal sealed class PromptScrollNavMathTests {

    // The exact live-trace regression: viewport well below the document bottom, with
    // prompts both above and below the viewport top. Before the fix, viewportTop was
    // read from a stale ScrollViewer pinned at scrollableMax, making this interval empty
    // and permanently disabling ↓. With correct geometry, ↓ must be enabled.
    [Test]
    public void Compute_RegressionScenario_EnablesDownWhenViewportBelowBottom() {
        var promptAbsYs = new[] { 120.0, 900.0, 1800.0, 3399.0, 3525.0, 3970.0 };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 3399.0, scrollableMax: 4052.0);

        Assert.Multiple(() => {
            Assert.That(state.CanGoDown, Is.True, "↓ must be enabled with prompts below the viewport top.");
            // 3525 is the nearest prompt below 3399 + deadZone (3449).
            Assert.That(state.NearestBelowIdx, Is.EqualTo(4));
            Assert.That(state.CanGoUp, Is.True);
            // 1800 is the nearest prompt above 3399 − deadZone (3349).
            Assert.That(state.NearestAboveIdx, Is.EqualTo(2));
        });
    }

    // A prompt in the final viewport-height (absY > scrollableMax) is still a legitimate
    // ↓ target as long as we are not already pinned at the bottom — scrolling clamps to
    // the document bottom but still moves downward.
    [Test]
    public void Compute_PromptInFinalViewportHeight_IsStillADownTarget() {
        var promptAbsYs = new[] { 100.0, 4000.0 };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 1000.0, scrollableMax: 3500.0);

        Assert.Multiple(() => {
            Assert.That(state.CanGoDown, Is.True,
                "A prompt beyond scrollableMax is reachable by scrolling to the bottom.");
            Assert.That(state.NearestBelowIdx, Is.EqualTo(1));
        });
    }

    // When truly pinned at the bottom, no downward movement is possible, so ↓ is disabled.
    [Test]
    public void Compute_PinnedAtBottom_DisablesDown() {
        var promptAbsYs = new[] { 100.0, 4000.0, 4050.0 };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 4052.0, scrollableMax: 4052.0);

        Assert.Multiple(() => {
            Assert.That(state.CanGoDown, Is.False,
                "No downward movement is possible when already at the document bottom.");
            Assert.That(state.NearestBelowIdx, Is.EqualTo(-1));
            Assert.That(state.CanGoUp, Is.True, "Earlier prompts remain ↑ targets.");
        });
    }

    [Test]
    public void Compute_AtTop_NoPromptsAbove_DownEnabledUpDisabled() {
        var promptAbsYs = new[] { 0.0, 500.0, 1200.0 };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 0.0, scrollableMax: 2000.0);

        Assert.Multiple(() => {
            Assert.That(state.CanGoUp, Is.False);
            Assert.That(state.NearestAboveIdx, Is.EqualTo(-1));
            Assert.That(state.CanGoDown, Is.True);
            Assert.That(state.NearestBelowIdx, Is.EqualTo(1));
        });
    }

    [Test]
    public void Compute_SelectsNearestNeighboursOnBothSides() {
        var promptAbsYs = new[] { 100.0, 800.0, 1500.0, 2600.0, 3400.0 };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 1500.0, scrollableMax: 4000.0);

        Assert.Multiple(() => {
            // nearest above 1500 − 50 = 1450 → 800 (idx 1)
            Assert.That(state.NearestAboveIdx, Is.EqualTo(1));
            // nearest below 1500 + 50 = 1550 → 2600 (idx 3)
            Assert.That(state.NearestBelowIdx, Is.EqualTo(3));
        });
    }

    [Test]
    public void Compute_PromptsWithinDeadZone_AreNeitherTarget() {
        var promptAbsYs = new[] { 1480.0, 1500.0, 1540.0 };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 1500.0, scrollableMax: 4000.0);

        Assert.Multiple(() => {
            Assert.That(state.CanGoUp, Is.False);
            Assert.That(state.CanGoDown, Is.False);
        });
    }

    [Test]
    public void Compute_SkipsUnmeasurablePrompts() {
        var promptAbsYs = new[] { double.NaN, 2500.0, double.NaN };
        var state = PromptScrollNavMath.Compute(
            promptAbsYs, viewportTop: 1000.0, scrollableMax: 4000.0);

        Assert.Multiple(() => {
            Assert.That(state.NearestBelowIdx, Is.EqualTo(1));
            Assert.That(state.CanGoUp, Is.False);
        });
    }

    [Test]
    public void Compute_EmptyPromptList_DisablesBothButtons() {
        var state = PromptScrollNavMath.Compute(
            System.Array.Empty<double>(), viewportTop: 0.0, scrollableMax: 0.0);

        Assert.Multiple(() => {
            Assert.That(state.CanGoUp, Is.False);
            Assert.That(state.CanGoDown, Is.False);
        });
    }
}
