using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptExecutionControllerTests {

    [Test]
    public void ShouldInjectTasksContext_SuppressesOrdinaryBacklogDuringPlanIteration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PromptExecutionController.ShouldInjectTasksContext(false), Is.True);
            Assert.That(PromptExecutionController.ShouldInjectTasksContext(true), Is.False);
        });
    }
    [Test]
    public void PromptDispatchDiagnosticsStore_AppendsExactPromptTextAndQueueMetadata() {
        using var workspace = new TestWorkspace();
        var item = new PromptQueueItem {
            Text = "Can you tell me what model you are?",
            QueueNumber = 53,
            SequenceNumber = 1,
            IsDictated = true,
            SourceTag = "voice"
        };

        var record = PromptDispatchDiagnosticsStore.CreateRecord(
            workspace.RootPath,
            sessionId: "session-123",
            configDirectory: Path.Combine(workspace.RootPath, ".squad", "sessions", "session-123"),
            queueItem: item,
            pendingQueueItemCount: 4,
            visiblePrompt: "Can you tell me what model you are?\n(some or all of this prompt was dictated by voice)",
            bridgePrompt: "FULL BRIDGE PROMPT\nTASK CONTEXT\nCan you tell me what model you are?",
            capturedAt: new DateTimeOffset(2026, 6, 17, 20, 2, 0, TimeSpan.Zero));

        PromptDispatchDiagnosticsStore.Append(workspace.RootPath, record);

        var saved = System.Text.Json.JsonSerializer.Deserialize<PromptDispatchDiagnosticRecord[]>(
            File.ReadAllText(PromptDispatchDiagnosticsStore.GetPath(workspace.RootPath)))!;

        Assert.Multiple(() => {
            Assert.That(saved, Has.Length.EqualTo(1));
            Assert.That(saved[0].QueueNumber, Is.EqualTo(53));
            Assert.That(saved[0].QueueItemId, Is.EqualTo(item.Id));
            Assert.That(saved[0].PendingQueueItemCount, Is.EqualTo(4));
            Assert.That(saved[0].IsDictated, Is.True);
            Assert.That(saved[0].SourceTag, Is.EqualTo("voice"));
            Assert.That(saved[0].VisiblePrompt, Does.Contain("what model you are"));
            Assert.That(saved[0].BridgePrompt, Does.Contain("TASK CONTEXT"));
            Assert.That(saved[0].BridgePromptLength, Is.EqualTo(saved[0].BridgePrompt.Length));
        });
    }

    [Test]
    public void PromptDispatchDiagnosticsStore_PrunesToMostRecentTwentyRecords() {
        using var workspace = new TestWorkspace();

        for (var i = 0; i < 25; i++) {
            var record = PromptDispatchDiagnosticsStore.CreateRecord(
                workspace.RootPath,
                sessionId: null,
                configDirectory: null,
                queueItem: null,
                pendingQueueItemCount: 0,
                visiblePrompt: $"visible-{i}",
                bridgePrompt: $"bridge-{i}",
                capturedAt: new DateTimeOffset(2026, 6, 17, 20, 0, 0, TimeSpan.Zero).AddMinutes(i));
            PromptDispatchDiagnosticsStore.Append(workspace.RootPath, record);
        }

        var saved = System.Text.Json.JsonSerializer.Deserialize<PromptDispatchDiagnosticRecord[]>(
            File.ReadAllText(PromptDispatchDiagnosticsStore.GetPath(workspace.RootPath)))!;

        Assert.Multiple(() => {
            Assert.That(saved, Has.Length.EqualTo(20));
            Assert.That(saved[0].VisiblePrompt, Is.EqualTo("visible-5"));
            Assert.That(saved[^1].VisiblePrompt, Is.EqualTo("visible-24"));
        });
    }

    [Test]
    public void ShouldCountPromptActivity_IgnoresDelayOnlyPowerShellReads_WhenRestartPending() {
        var evt = new SquadSdkEvent {
            Type = "tool_complete",
            ToolName = "read_powershell",
            Args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                """{"delay":60,"shellId":"0"}""")
        };

        Assert.That(PromptExecutionController.ShouldCountPromptActivity(evt, restartPending: true), Is.False);
    }

    [Test]
    public void ShouldCountPromptActivity_IgnoresSubagentDelayOnlyPowerShellReads_WhenRestartPending() {
        var evt = new SquadSdkEvent {
            Type = "subagent_tool_complete",
            ToolName = "read_powershell",
            Args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                """{"delay":120,"shellId":"0"}""")
        };

        Assert.That(PromptExecutionController.ShouldCountPromptActivity(evt, restartPending: true), Is.False);
    }

    [Test]
    public void ShouldCountPromptActivity_CountsDelayOnlyPowerShellReads_WhenRestartIsNotPending() {
        var evt = new SquadSdkEvent {
            Type = "tool_complete",
            ToolName = "read_powershell",
            Args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                """{"delay":60,"shellId":"0"}""")
        };

        Assert.That(PromptExecutionController.ShouldCountPromptActivity(evt, restartPending: false), Is.True);
    }

    [Test]
    public void ShouldCountPromptActivity_IgnoresUsageBookkeeping_WhenRestartPending() {
        var evt = new SquadSdkEvent { Type = "usage" };

        Assert.That(PromptExecutionController.ShouldCountPromptActivity(evt, restartPending: true), Is.False);
    }

    [Test]
    public void ShouldCountPromptActivity_CountsRealToolWork_WhenRestartPending() {
        var evt = new SquadSdkEvent {
            Type = "tool_complete",
            ToolName = "powershell",
            Args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                """{"command":"dotnet build","initial_wait":120}""")
        };

        Assert.That(PromptExecutionController.ShouldCountPromptActivity(evt, restartPending: true), Is.True);
    }

    [Test]
    public void ShouldCountPromptActivity_CountsStreamingContent_WhenRestartPending() {
        var evt = new SquadSdkEvent { Type = "response_delta", Chunk = "still working" };

        Assert.That(PromptExecutionController.ShouldCountPromptActivity(evt, restartPending: true), Is.True);
    }

    [Test]
    public void SilentCompletionWatchdog_InjectsFollowUp_WhenOnlyHiddenProtocolResponseArrives() {
        var enqueued = new List<(string Prompt, bool IsSystemInjected)>();
        var controller = BuildPromptExecutionControllerForLocalCommandTests(
            showCastAgentWindow: () => null,
            transcriptLines: [],
            getIsPromptRunning: () => true,
            enqueuePrompt: (prompt, isSystemInjected) => enqueued.Add((prompt, isSystemInjected)),
            getVisibleCoordinatorResponseLength: () => 0);
        var thread = BuildCompletedBackgroundAgentThread();

        controller.NotifySubagentCompletedDuringTurn(thread, "Cassian Rook");
        controller.MarkActivity(new SquadSdkEvent {
            Type = "response_delta",
            Chunk = "<system_notification>{\"notification\":\"Done.\"}</system_notification>"
        });
        controller.StopPromptHealthMonitoringForTests("completed");

        Assert.Multiple(() => {
            Assert.That(enqueued, Has.Count.EqualTo(1));
            Assert.That(enqueued[0].IsSystemInjected, Is.True);
            Assert.That(enqueued[0].Prompt, Does.Contain("Cassian Rook"));
            Assert.That(enqueued[0].Prompt, Does.Contain("agent-1"));
            Assert.That(enqueued[0].Prompt, Does.Contain("Committed responsiveness changes."));
        });
    }

    [Test]
    public void SilentCompletionWatchdog_DoesNotInject_WhenVisibleCoordinatorResponseAppearsAfterAgentReport() {
        var visibleResponseChars = 0;
        var enqueued = new List<(string Prompt, bool IsSystemInjected)>();
        var controller = BuildPromptExecutionControllerForLocalCommandTests(
            showCastAgentWindow: () => null,
            transcriptLines: [],
            getIsPromptRunning: () => true,
            enqueuePrompt: (prompt, isSystemInjected) => enqueued.Add((prompt, isSystemInjected)),
            getVisibleCoordinatorResponseLength: () => visibleResponseChars);
        var thread = BuildCompletedBackgroundAgentThread();

        controller.NotifySubagentCompletedDuringTurn(thread, "Cassian Rook");
        visibleResponseChars = "I reviewed Cassian Rook's completed work.".Length;
        controller.StopPromptHealthMonitoringForTests("completed");

        Assert.That(enqueued, Is.Empty);
    }

    [Test]
    public void FormatCharsPerSecond_ReturnsExpectedRate_WhenThinkingWindowIsKnown() {
        var first = new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.FromHours(-4));
        var last = first.AddSeconds(10);

        var rate = PromptTraceMetrics.FormatCharsPerSecond(120, first, last);

        Assert.That(rate, Is.EqualTo("12.0"));
    }

    [Test]
    public void FormatCharsPerSecond_ReturnsNotApplicable_WhenWindowIsMissing() {
        var first = new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.FromHours(-4));

        var rate = PromptTraceMetrics.FormatCharsPerSecond(120, first, null);

        Assert.That(rate, Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatAverageChunkSize_ReturnsExpectedValue() {
        var average = PromptTraceMetrics.FormatAverageChunkSize(96, 12);

        Assert.That(average, Is.EqualTo("8.0"));
    }

    [Test]
    public void BuildSilentCompletionFollowUpPrompt_IncludesExactThreadReportAndStaleLookupGuard() {
        var completedAt = new DateTimeOffset(2026, 5, 17, 12, 31, 4, TimeSpan.FromHours(-4));

        var prompt = SilentCompletionFollowUpPromptBuilder.Build([
            new SilentCompletionFollowUpReport(
                "Orion Vale",
                "thread-123",
                completedAt,
                "Use explicit key list (safer hue filter)",
                "Created TintKeys.cs with 74 explicit keys.")
        ]);

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("Orion Vale"));
            Assert.That(prompt, Does.Contain("Thread ID: thread-123"));
            Assert.That(prompt, Does.Contain("Use explicit key list (safer hue filter)"));
            Assert.That(prompt, Does.Contain("Created TintKeys.cs with 74 explicit keys."));
            Assert.That(prompt, Does.Contain("do not search by agent name"));
        });
    }

    // ── InteractiveControlStateCalculator ────────────────────────────────────
    // Tests for state transitions in the prompt-execution lifecycle.

    [Test]
    public void Calculate_DisablesInstallButton_WhenNoWorkspaceOpen() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: false,
            squadInstalled: false,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: null);

        Assert.That(state.InstallSquadEnabled, Is.False);
    }

    [Test]
    public void Calculate_EnablesInstallButton_WhenWorkspaceExistsAndSquadNotInstalled() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: false,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: null);

        Assert.That(state.InstallSquadEnabled, Is.True);
    }

    [Test]
    public void Calculate_DisablesAllInteractions_WhileSquadIsInstalling() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: false,
            isInstallingSquad: true,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: null);

        Assert.Multiple(() => {
            Assert.That(state.PromptEnabled, Is.False);
            Assert.That(state.RunEnabled, Is.False);
            Assert.That(state.AgentItemsEnabled, Is.False);
            Assert.That(state.OutputEnabled, Is.False);
            Assert.That(state.InstallSquadEnabled, Is.False);
        });
    }

    [Test]
    public void Calculate_DisablesAbort_WhenPromptIsIdle() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.That(state.AbortEnabled, Is.False);
    }

    [Test]
    public void Calculate_EnablesAbort_WhenPromptIsRunning() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: true,
            canAbortBackgroundTask: false,
            currentPromptText: "building something");

        Assert.That(state.AbortEnabled, Is.True);
    }

    [Test]
    public void Calculate_EnablesRunDoctor_WhenIdleAndSquadInstalled() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: string.Empty);

        Assert.That(state.RunDoctorEnabled, Is.True);
    }

    [Test]
    public void Calculate_DisablesRunDoctor_WhenPromptIsRunning() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: true,
            canAbortBackgroundTask: false,
            currentPromptText: "running");

        Assert.That(state.RunDoctorEnabled, Is.False);
    }

    [Test]
    public void Calculate_EnablesAllInteractiveControls_WhenFullyReadyAndIdle() {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: true,
            squadInstalled: true,
            isInstallingSquad: false,
            isPromptRunning: false,
            canAbortBackgroundTask: false,
            currentPromptText: "some prompt");

        Assert.Multiple(() => {
            Assert.That(state.PromptEnabled, Is.True);
            Assert.That(state.RunEnabled, Is.True);
            Assert.That(state.AgentItemsEnabled, Is.True);
            Assert.That(state.OutputEnabled, Is.True);
            Assert.That(state.CopyEnabled, Is.True);
        });
    }

    // ── PromptHistoryNavigator ────────────────────────────────────────────────

    [Test]
    public void Navigate_EmptyHistory_ReturnsUnchangedResult() {
        var result = PromptHistoryNavigator.Navigate(
            history: Array.Empty<PromptHistoryEntry>(),
            historyIndex: null,
            historyDraft: null,
            historyDraftAttachments: null,
            currentText: "current", currentAttachments: [],
            direction: -1);

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Text, Is.EqualTo("current"));
        });
    }

    [Test]
    public void Navigate_FromNullIndex_NavigatingBack_LoadsMostRecentEntry() {
        PromptHistoryEntry[] history = [new("oldest", []), new("newest", [])];

        var result = PromptHistoryNavigator.Navigate(
            history,
            historyIndex: null,
            historyDraft: null,
            historyDraftAttachments: null,
            currentText: "current draft", currentAttachments: [],
            direction: -1);

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.True);
            Assert.That(result.Text, Is.EqualTo("newest")); // last = most recent
            Assert.That(result.HistoryIndex, Is.EqualTo(1));
            Assert.That(result.HistoryDraft, Is.EqualTo("current draft"));
        });
    }

    [Test]
    public void Navigate_AtCurrentDraftPosition_NavigatingForward_ReturnsUnchanged() {
        // historyIndex == null means we're already at the "live" draft position.
        // Navigating forward (direction = +1) should clamp and return unchanged.
        PromptHistoryEntry[] history = [new("first", [])];

        var result = PromptHistoryNavigator.Navigate(
            history,
            historyIndex: null,
            historyDraft: null,
            historyDraftAttachments: null,
            currentText: "draft", currentAttachments: [],
            direction: +1);

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Text, Is.EqualTo("draft"));
        });
    }

    [Test]
    public void Navigate_AtOldestEntry_NavigatingBackFurther_ReturnsUnchanged() {
        PromptHistoryEntry[] history = [new("oldest", []), new("newest", [])];

        // Navigate backward twice to reach index 0 (oldest).
        var step1 = PromptHistoryNavigator.Navigate(history, null, null, null, "draft", [], -1);
        var atOldest = PromptHistoryNavigator.Navigate(history, step1.HistoryIndex, step1.HistoryDraft, step1.HistoryDraftAttachments, step1.Text, [], -1);

        Assert.That(atOldest.HistoryIndex, Is.EqualTo(0), "Precondition: should be at oldest entry");

        // Attempt to navigate further back — must clamp.
        var result = PromptHistoryNavigator.Navigate(
            history,
            atOldest.HistoryIndex,
            atOldest.HistoryDraft,
            atOldest.HistoryDraftAttachments,
            atOldest.Text,
            currentAttachments: [],
            direction: -1);

        Assert.Multiple(() => {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.HistoryIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void Navigate_FromMidHistory_BackwardStep_LoadsOlderEntry() {
        PromptHistoryEntry[] history = [new("oldest", []), new("middle", []), new("newest", [])];

        // Navigate to "newest" (index 2).
        var atNewest = PromptHistoryNavigator.Navigate(history, null, null, null, "draft", [], -1);
        Assert.That(atNewest.HistoryIndex, Is.EqualTo(2), "Precondition: at newest entry");

        // Navigate back one step → should load "middle" (index 1).
        var atMiddle = PromptHistoryNavigator.Navigate(
            history,
            atNewest.HistoryIndex,
            atNewest.HistoryDraft,
            atNewest.HistoryDraftAttachments,
            atNewest.Text,
            currentAttachments: [],
            direction: -1);

        Assert.Multiple(() => {
            Assert.That(atMiddle.Changed, Is.True);
            Assert.That(atMiddle.Text, Is.EqualTo("middle"));
            Assert.That(atMiddle.HistoryIndex, Is.EqualTo(1));
        });
    }

    // ── PromptExecutionController local-command behavior ─────────────────────

    [Test]
    public void TryHandleLocalCommand_Cast_IsHandledAndShowsCastAgentWindow() {
        var transcriptLines = new List<string>();
        var showCastAgentWindowCallCount = 0;

        var controller = BuildPromptExecutionControllerForLocalCommandTests(
            showCastAgentWindow: () => {
                showCastAgentWindowCallCount++;
                return null;
            },
            transcriptLines: transcriptLines);

        var handled = controller.TryHandleLocalCommand("/cast", addToHistory: false, clearPromptBox: false);

        Assert.Multiple(() => {
            Assert.That(handled, Is.True);
            Assert.That(showCastAgentWindowCallCount, Is.EqualTo(1));
            Assert.That(transcriptLines, Has.Count.EqualTo(0));
        });
    }

    [TestCase("/hire")]
    [TestCase(" /HIRE ")]
    public void TryHandleLocalCommand_HireAlias_IsHandledShowsCastAgentWindowAndEmitsDeprecationHint(string command) {
        var transcriptLines = new List<string>();
        var showCastAgentWindowCallCount = 0;
        const string expectedHint = "💡 `/hire` is an alias — use `/cast` going forward.";

        var controller = BuildPromptExecutionControllerForLocalCommandTests(
            showCastAgentWindow: () => {
                showCastAgentWindowCallCount++;
                return null;
            },
            transcriptLines: transcriptLines);

        var handled = controller.TryHandleLocalCommand(command, addToHistory: false, clearPromptBox: false);

        Assert.Multiple(() => {
            Assert.That(handled, Is.True);
            Assert.That(showCastAgentWindowCallCount, Is.EqualTo(1));
            Assert.That(transcriptLines, Has.One.EqualTo(expectedHint));
            Assert.That(transcriptLines.Any(line => line.Contains("/hire", StringComparison.Ordinal)), Is.True);
        });
    }

    // ── LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning ──────────────
    // This method is tested independently of ShouldRetainPromptAfterLocalSubmit.

    [TestCase("/activate")]
    [TestCase("/agents")]
    [TestCase("/deactivate")]
    [TestCase("/cast")]
    [TestCase("/doctor")]
    [TestCase("/dropTasks")]
    [TestCase("/health")]
    [TestCase("/help")]
    [TestCase("/hire")]
    [TestCase("/model")]
    [TestCase("/retire")]
    [TestCase("/status")]
    [TestCase("/tasks")]
    [TestCase("/version")]
    public void CanSubmitWhilePromptRunning_ForBusySafeCommand_ReturnsTrue(string command) {
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(command), Is.True);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_ForRegularFreeformPrompt_ReturnsFalse() {
        Assert.That(
            LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("build the authentication module"),
            Is.False);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_ForNullPrompt_ReturnsFalse() {
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(null), Is.False);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_ForEmptyPrompt_ReturnsFalse() {
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(string.Empty), Is.False);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_ForClearCommand_ReturnsFalse() {
        // /clear is intentionally absent from the busy-safe list.
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("/clear"), Is.False);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_ForSessionCommand_ReturnsFalse() {
        // /sessions and /session are not in the busy-safe list.
        Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("/sessions"), Is.False);
    }

    [Test]
    public void CanSubmitWhilePromptRunning_IsCaseInsensitive() {
        Assert.Multiple(() => {
            Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("/TASKS"), Is.True);
            Assert.That(LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning("/Status"), Is.True);
        });
    }

    // ── SlashCommandParameterPolicy ───────────────────────────────────────────

    [TestCase("/activate")]
    [TestCase("/deactivate")]
    [TestCase("/retire")]
    public void RequiresParameter_ForParameterRequiredCommand_ReturnsTrue(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.True);
    }

    [TestCase("/help")]
    [TestCase("/tasks")]
    [TestCase("/status")]
    [TestCase("/agents")]
    [TestCase("/model")]
    [TestCase("/version")]
    [TestCase("/clear")]
    public void RequiresParameter_ForSimpleOrUnknownCommand_ReturnsFalse(string command) {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter(command), Is.False);
    }

    [Test]
    public void RequiresParameter_IsCaseInsensitive() {
        Assert.Multiple(() => {
            Assert.That(SlashCommandParameterPolicy.RequiresParameter("/ACTIVATE"), Is.True);
            Assert.That(SlashCommandParameterPolicy.RequiresParameter("/Deactivate"), Is.True);
            Assert.That(SlashCommandParameterPolicy.RequiresParameter("/RETIRE"), Is.True);
        });
    }

    [Test]
    public void RequiresParameter_TrimsWhitespaceBeforeChecking() {
        Assert.That(SlashCommandParameterPolicy.RequiresParameter("  /activate  "), Is.True);
    }

    // ── Attachment-strip history algorithm (PEC lines 1964–1966) ──────────────

    [Test]
    public void AttachmentStrip_PromptWithAttachmentBlock_HistoryTextIsCleanUserMessage() {
        // Reproduce the exact 3-line algorithm from
        // PromptExecutionController.ExecuteCoordinatorTurnAsync (lines 1964–1966).
        var block = AttachmentBlockFormatter.BuildTypedAttachmentBlock("code", "snippet.cs", "int x = 1;");
        var visiblePrompt = block + "\n\nPlease review the above.";

        var historyText = visiblePrompt;
        var bodyStart = AttachmentBlockFormatter.StripTypedAttachmentHeaders(visiblePrompt);
        if (bodyStart >= 0) historyText = visiblePrompt[bodyStart..];

        Assert.That(historyText, Is.EqualTo("Please review the above."));
    }

    [Test]
    public void AttachmentStrip_PlainPromptWithoutAttachmentBlock_HistoryTextIsUnchanged() {
        // When there is no attachment block, StripTypedAttachmentHeaders returns -1
        // and the algorithm leaves visiblePrompt untouched.
        var visiblePrompt = "Just a plain prompt.";

        var historyText = visiblePrompt;
        var bodyStart = AttachmentBlockFormatter.StripTypedAttachmentHeaders(visiblePrompt);
        if (bodyStart >= 0) historyText = visiblePrompt[bodyStart..];

        Assert.That(historyText, Is.EqualTo(visiblePrompt));
    }

    [Test]
    public void AttachmentStrip_PlainMultiParagraphPrompt_HistoryTextIsUnchanged() {
        var visiblePrompt = "First paragraph.\n\nSecond paragraph.";

        var historyText = visiblePrompt;
        var bodyStart = AttachmentBlockFormatter.StripTypedAttachmentHeaders(visiblePrompt);
        if (bodyStart >= 0) historyText = visiblePrompt[bodyStart..];

        Assert.That(historyText, Is.EqualTo(visiblePrompt));
    }

    private static PromptExecutionController BuildPromptExecutionControllerForLocalCommandTests(
        Func<CastAgentWindow.CastAgentSubmission?> showCastAgentWindow,
        List<string> transcriptLines,
        Func<bool>? getIsPromptRunning = null,
        Action<string, bool>? enqueuePrompt = null,
        Func<int>? getVisibleCoordinatorResponseLength = null) {
        IWorkspaceContext workspaceContext = new StubWorkspaceContext();
        IPromptBoxState promptBoxState = new StubPromptBoxState();

        var coordinatorThread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Squad",
            DateTimeOffset.UtcNow);

        ITranscriptRenderSink transcriptSink = new TranscriptPanelController(
            beginTranscriptTurn: _ => null!,
            finalizeCurrentTurnResponse: () => { },
            appendLine: (text, _) => transcriptLines.Add(text),
            selectTranscriptThread: _ => { },
            getCoordinatorThread: () => coordinatorThread,
            getLastQuickReplyEntry: () => null,
            clearLastQuickReplyEntry: () => { },
            renderResponseEntry: _ => { },
            ensureThreadFooterAtEnd: _ => { },
            scrollToEndIfAtBottom: () => { },
            getToolEntries: () => [],
            renderToolEntry: _ => { },
            updateToolSpinnerState: () => { });

        IAgentRosterView agentRosterView = new AgentRosterController(
            getAgents: () => [],
            getCurrentSessionState: () => null);

        var conversationManager = MakeConversationManager(coordinatorThread);
        var backgroundTaskPresenter = MakeBackgroundTaskPresenter();
        var squadCliAdapter = new SquadCliAdapter(new FakeWorkspacePaths(), new StubResolutionService());

        return new PromptExecutionController(
            runPromptAsync: (_, _, _, _) => Task.CompletedTask,
            runNamedAgentDelegationAsync: (_, _, _, _, _) => Task.CompletedTask,
            runNamedAgentDirectAsync: (_, _, _, _, _, _) => Task.CompletedTask,
            workspaceContext: workspaceContext,
            promptBoxState: promptBoxState,
            transcriptSink: transcriptSink,
            agentRosterView: agentRosterView,
            conversationManager: conversationManager,
            backgroundTaskPresenter: backgroundTaskPresenter,
            squadCliAdapter: squadCliAdapter,
            getIsPromptRunning: getIsPromptRunning ?? (() => false),
            setIsPromptRunning: _ => { },
            getIsClosing: () => false,
            getRestartPending: () => false,
            close: () => { },
            getPendingRoutingRepairRecheck: () => false,
            setPendingRoutingRepairRecheck: _ => { },
            getPendingSupplementalInstruction: () => null,
            clearPendingSupplementalInstruction: () => { },
            getPendingQuickReplyRoutingInstruction: () => null,
            getPendingQuickReplyRouteMode: () => null,
            updateInteractiveControlState: () => { },
            updateLeadAgent: (_, _, _) => { },
            updateSessionState: _ => { },
            refreshAgentCards: () => { },
            refreshSidebar: () => { },
            setInstallStatus: _ => { },
            canShowOwnedWindow: () => false,
            showTextWindow: (_, _) => { },
            clearSessionView: () => { },
            showTasksStatusWindow: () => { },
            hideTasksStatusWindow: () => { },
            showApprovalWindow: () => { },
            showLiveTraceWindow: () => { },
            showScreenshotHealthWindow: () => { },
            runDoctor: () => { },
            showCastAgentWindow: showCastAgentWindow,
            enqueuePrompt: enqueuePrompt ?? ((_, _) => { }),
            showScreenshotOverlay: () => { },
            triggerCodeHealthCycle: () => { },
            showRuntimeIssue: _ => new WorkspaceIssuePresentation("test", "test"),
            clearRuntimeIssue: () => { },
            waitForRoutingRepairSettleAsync: () => Task.CompletedTask,
            maybePublishRoutingIssue: (_, _) => { },
            promptHealthTimer: new DispatcherTimer(),
            waitForBridgeUiEventsAppliedAsync: () => Task.CompletedTask,
            getModelObservedThisSession: () => false,
            workspacePaths: new FakeWorkspacePaths(),
            getVisibleCoordinatorResponseLength: getVisibleCoordinatorResponseLength);
    }

    private static TranscriptThreadState BuildCompletedBackgroundAgentThread() {
        var thread = new TranscriptThreadState(
            "agent-1",
            TranscriptThreadKind.Agent,
            "Cassian Rook",
            new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(-4))) {
            WasObservedAsBackgroundTask = true,
            StatusText = "Completed",
            CompletedAt = new DateTimeOffset(2026, 10, 15, 20, 49, 17, TimeSpan.FromHours(-4)),
            Prompt = "Fix the audio-start slowdown.",
            LatestResponse = "Committed responsiveness changes.",
            LastCoordinatorAnnouncedResponse = "Committed responsiveness changes."
        };

        return thread;
    }

    private static TranscriptConversationManager MakeConversationManager(TranscriptThreadState coordinatorThread) {
        return new TranscriptConversationManager(
            getWorkspace: () => null,
            getPromptText: () => string.Empty,
            setPromptText: (_, _, _, _) => { },
            getPromptCaretState: () => (0, 0, 0),
            isClosing: () => false,
            renderPersistedTurn: (_, _, _) => { },
            coordinatorThread: () => coordinatorThread,
            selectedThread: () => null,
            maybePublishRoutingIssue: _ => { },
            syncAgentCardsWithThreads: () => { },
            dispatcher: Dispatcher.CurrentDispatcher,
            scrollOutputToEnd: _ => { },
            agentThreadRegistry: MakeRegistry(),
            getToolEntries: () => new Dictionary<string, ToolTranscriptEntry>(),
            getCurrentTurn: () => null,
            setCurrentTurnNull: () => { },
            beginBulkDocumentLoad: _ => { },
            endBulkDocumentLoad: _ => { },
            prependTurnsBatch: (_, _) => { },
            getScrollableHeight: () => 0d,
            getVerticalOffset: () => 0d,
            scrollToAbsoluteOffset: _ => { },
            updateScrollLayout: () => { });
    }

    private static BackgroundTaskPresenter MakeBackgroundTaskPresenter() {
        return new BackgroundTaskPresenter(
            agentThreadRegistry: MakeRegistry(),
            appendLine: (_, _) => { },
            syncAgentCards: () => { },
            isPromptRunning: () => false,
            currentTurn: () => null,
            themeBrush: _ => Brushes.Transparent,
            tryPostToUi: (action, _) => action(),
            isClosing: () => false,
            updateLeadAgent: (_, _, _) => { },
            updateSessionState: _ => { },
            persistAgentThreadSnapshot: _ => { },
            currentTurnSnapshot: () => new CurrentTurnStatusSnapshot(false, false, false),
            agentActiveDisplayLinger: TimeSpan.FromSeconds(30),
            dynamicAgentHistoryRetention: TimeSpan.FromDays(7));
    }

    private static AgentThreadRegistry MakeRegistry() =>
        new(
            beginTranscriptTurn: (_, _) => { },
            finalizeCurrentTurnResponse: _ => { },
            collapseCurrentTurnThinking: _ => { },
            renderToolEntry: _ => { },
            updateToolSpinnerState: () => { },
            syncActiveToolName: () => { },
            syncThreadChip: _ => { },
            syncTaskToolTranscriptLink: _ => { },
            appendText: (_, _) => { },
            syncAgentCards: () => { },
            syncAgentCardsWithThreads: () => { },
            getKnownTeamAgentDescriptors: () => Array.Empty<TeamAgentDescriptor>(),
            updateTranscriptThreadBadge: () => { },
            isThreadActiveForDisplay: _ => false,
            observeBackgroundAgentActivity: (_, _) => { },
            renderConversationHistory: (_, _) => Task.CompletedTask,
            resolveBackgroundAgentDisplayLabel: _ => string.Empty,
            buildAgentLabel: _ => string.Empty);

    private sealed class StubResolutionService : ISquadCliResolutionService {
        private static readonly ResolvedCliCandidate Candidate = new(
            SelectionMode: CliSelectionMode.Auto,
            DetectedSource: CliInstallSource.Unknown,
            RuntimeType: CliRuntimeType.NativeExecutable,
            ExecutablePath: "squad",
            EntryScriptPath: null,
            Version: null,
            IsAvailable: false,
            AgentCmdSupport: CliCapabilityStatus.Unknown,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, null));

        public Task<ResolvedCliCandidate> ResolveAsync(string workspaceDir) => Task.FromResult(Candidate);
        public Task<ResolvedCliCandidate> ResolveWithModeAsync(CliSelectionMode mode, string workspaceDir) => Task.FromResult(Candidate);
        public Task<ResolvedCliCandidate> ProbeVersionAsync(ResolvedCliCandidate candidate) => Task.FromResult(candidate);
    }

    private sealed class FakeWorkspacePaths : IWorkspacePaths {
        public string ApplicationRoot => @"C:\fake\app";
        public string SquadSdkDirectory => Path.Combine(ApplicationRoot, "Squad.SDK");
        public string RunRootDirectory => Path.Combine(ApplicationRoot, "Run");
        public string AgentImageAssetsDirectory => Path.Combine(ApplicationRoot, "Assets", "Agents");
        public string RoleIconAssetsDirectory => Path.Combine(ApplicationRoot, "Assets", "Roles");
        public string ScreenshotsDirectory => Path.Combine(ApplicationRoot, "docs", "screenshots");
    }

    private sealed class StubWorkspaceContext : IWorkspaceContext {
        public SessionWorkspace? GetCurrentWorkspace() => null;

        public ApplicationSettingsSnapshot GetSettingsSnapshot() =>
            new(
                LastOpenedFolder: null,
                RecentFolders: Array.Empty<string>(),
                AgentAccentColorsByWorkspace: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                WindowPlacementByWorkspace: new Dictionary<string, WorkspaceWindowPlacement>(StringComparer.OrdinalIgnoreCase),
                PromptFontSize: 14,
                AgentImagePathsByWorkspace: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
                IgnoredRoutingIssueFingerprintsByWorkspace: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class StubPromptBoxState : IPromptBoxState {
        public void ClearPromptTextBox() { }
        public void FocusPromptTextBox() { }
        public bool IsPromptTextBoxEnabled => true;
        public int QueueCount => 0;
        public string PromptBoxText => string.Empty;
        public void SetPromptBoxText(string text) { }
        public void EnqueueSimItem(PromptQueueItem item) { }
    }
}
