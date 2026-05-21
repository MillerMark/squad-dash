namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptExecutionControllerTests {
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

    // ── LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning ──────────────
    // This method is tested independently of ShouldRetainPromptAfterLocalSubmit.

    [TestCase("/activate")]
    [TestCase("/agents")]
    [TestCase("/deactivate")]
    [TestCase("/doctor")]
    [TestCase("/dropTasks")]
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
}
