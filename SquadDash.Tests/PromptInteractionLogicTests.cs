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
    public void LocalPromptSubmissionPolicy_RetainsBusySafeCommandWhilePromptRunning() {
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/tasks", isPromptRunning: true),
            Is.True);
    }

    [Test]
    public void LocalPromptSubmissionPolicy_RetainsDropTasksWhilePromptRunning() {
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/dropTasks", isPromptRunning: true),
            Is.True);
    }

    [Test]
    public void LocalPromptSubmissionPolicy_DoesNotRetainBusySafeCommandWhenPromptIsIdle() {
        Assert.That(
            LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit("/tasks", isPromptRunning: false),
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