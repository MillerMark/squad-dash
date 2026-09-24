namespace SquadDash.Tests;

[TestFixture]
internal sealed class InterruptedPromptRecoveryTests {
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp() {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SquadDash-RecoveryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown() {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch { }
    }

    [Test]
    public void IsRecoverableInactivityTimeout_RequiresKnownBridgeOrSessionTimeout() {
        Assert.Multiple(() => {
            Assert.That(InterruptedPromptRecoveryPresentation.IsRecoverableInactivityTimeout(
                new TimeoutException("Timed out waiting for Squad to finish responding after 600 seconds without bridge activity.")), Is.True);
            Assert.That(InterruptedPromptRecoveryPresentation.IsRecoverableInactivityTimeout(
                new InvalidOperationException("Timeout after 3600000ms waiting for session.idle")), Is.True);
            Assert.That(InterruptedPromptRecoveryPresentation.IsRecoverableInactivityTimeout(
                new InvalidOperationException("Timeout after 3600000ms without meaningful activity in the request tree")), Is.True);
            Assert.That(InterruptedPromptRecoveryPresentation.IsRecoverableInactivityTimeout(
                new InvalidOperationException("Provider request timed out. The provider did not complete the request within the allowed time.")), Is.True);
            Assert.That(InterruptedPromptRecoveryPresentation.IsRecoverableInactivityTimeout(
                new TimeoutException("Timed out opening a file.")), Is.False);
            Assert.That(InterruptedPromptRecoveryPresentation.IsRecoverableInactivityTimeout(
                new InvalidOperationException("Timed out waiting for Squad without bridge activity.")), Is.False);
        });
    }

    [Test]
    public void SessionIdleTimeout_PreservesExactReasonAndConvertsMillisecondsToDuration() {
        const string error = "Timeout after 3600000ms waiting for session.idle";
        var state = CreateState() with {
            ErrorMessage = error,
            InactivitySeconds = InterruptedPromptRecoveryPresentation.ExtractInactivitySeconds(error),
            FailureTitle = "Provider request timed out",
            FailureCategory = ProviderFailureCategory.Timeout.ToString(),
        };

        var card = InterruptedPromptRecoveryPresentation.BuildCardText(state);

        Assert.Multiple(() => {
            Assert.That(state.InactivitySeconds, Is.EqualTo(3600));
            Assert.That(card, Does.Contain("Provider request timed out after 1 hour"));
            Assert.That(card, Does.Contain("Interrupted—recoverable"));
            Assert.That(card, Does.Contain("does not by itself indicate").And.Contain("tokens or budget"));
            Assert.That(card, Does.Not.Contain("stopped producing activity"));
        });
    }

    [Test]
    public void PrimaryAction_UsesKnownDirectAgent() {
        var state = CreateState(
            InterruptedPromptExecutionKind.NamedAgentDirect,
            targetAgentHandle: "shuri",
            targetAgentLabel: "Shuri");

        Assert.That(
            InterruptedPromptRecoveryPresentation.BuildPrimaryActionLabel(state),
            Is.EqualTo("Continue with Shuri"));
    }

    [Test]
    public void PrimaryAction_UsesCoordinatorWhenMultipleAgentsWereActive() {
        var state = CreateState(activeAgentLabels: ["Shuri", "Talia", "Arjun"]);

        var card = InterruptedPromptRecoveryPresentation.BuildCardText(state);

        Assert.Multiple(() => {
            Assert.That(
                InterruptedPromptRecoveryPresentation.BuildPrimaryActionLabel(state),
                Is.EqualTo("Continue with Squad"));
            Assert.That(card, Does.Contain("Previously active agents: Shuri, Talia, and Arjun."));
        });
    }

    [Test]
    public void PrimaryAction_UsesCoordinatorWhenNoNamedAgentWasActive() {
        var state = CreateState(coordinatorLabel: "Squad");

        Assert.That(
            InterruptedPromptRecoveryPresentation.BuildPrimaryActionLabel(state),
            Is.EqualTo("Continue with Squad"));
    }

    [Test]
    public void Card_OffersDoctorOnlyAfterRepeatedFailure() {
        var firstFailure = InterruptedPromptRecoveryPresentation.BuildCardText(CreateState(failureCount: 1));
        var secondFailure = InterruptedPromptRecoveryPresentation.BuildCardText(CreateState(failureCount: 2));

        Assert.Multiple(() => {
            Assert.That(firstFailure, Does.Contain("[Continue with Squad]"));
            Assert.That(firstFailure, Does.Contain("paused queued prompts and loop progress"));
            Assert.That(firstFailure, Does.Contain($"[{InterruptedPromptRecoveryPresentation.DismissAction}]"));
            Assert.That(firstFailure, Does.Not.Contain(InterruptedPromptRecoveryPresentation.RunDoctorAction));
            Assert.That(secondFailure, Does.Contain($"[{InterruptedPromptRecoveryPresentation.RunDoctorAction}]"));
        });
    }

    [Test]
    public void LatestUnresolved_AcceptsFailedTailTurnButRejectsLaterInteraction() {
        var interruptedAt = DateTimeOffset.UtcNow;
        var state = CreateState() with { InterruptedAt = interruptedAt };
        var failedTurn = CreateTurn(
            interruptedAt.AddMinutes(-10),
            state.Prompt,
            "Squad stopped producing activity, so SquadDash restarted the runtime.");
        var laterTurn = CreateTurn(
            interruptedAt.AddSeconds(1),
            "A later question",
            "A later answer");

        var failedTail = CreateConversation([failedTurn]);
        var superseded = CreateConversation([failedTurn, laterTurn]);

        Assert.Multiple(() => {
            Assert.That(
                InterruptedPromptRecoveryPresentation.IsStillLatestUnresolved(state, failedTail),
                Is.True);
            Assert.That(
                InterruptedPromptRecoveryPresentation.IsStillLatestUnresolved(state, superseded),
                Is.False);
        });
    }

    [Test]
    public void LatestUnresolved_AcceptsRestoredCardAndIgnoresLaterSystemNotice() {
        var interruptedAt = DateTimeOffset.UtcNow;
        var state = CreateState() with { InterruptedAt = interruptedAt };
        var failedTurn = CreateTurn(
            interruptedAt.AddMinutes(-10),
            state.Prompt,
            "[error] The prompt stopped before Squad returned a complete answer.");
        var restoredCard = CreateTurn(
            interruptedAt.AddMinutes(1),
            string.Empty,
            InterruptedPromptRecoveryPresentation.BuildCardText(state, restoredAfterRestart: true));
        var systemNotice = CreateTurn(
            interruptedAt.AddMinutes(2),
            string.Empty,
            "Queued prompts remain paused until you resolve the recovery card.");

        Assert.That(
            InterruptedPromptRecoveryPresentation.IsStillLatestUnresolved(
                state,
                CreateConversation([failedTurn, restoredCard, systemNotice])),
            Is.True);
    }

    [Test]
    public void ContinuationPrompt_ReconcilesExistingWorkAndNamesPriorAgents() {
        var state = CreateState(
            prompt: "Implement the cache fix",
            activeAgentLabels: ["Shuri", "Talia"],
            queueContext: new InterruptedPromptQueueContext("q1", 7, "user", false, "Cache fix")) with {
                ActiveAgentThreadIds = ["thread-shuri", "thread-talia"],
            };

        var prompt = InterruptedPromptRecoveryPresentation.BuildContinuationPrompt(state);

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain($"Recovery lineage: {state.RecoveryId}"));
            Assert.That(prompt, Does.Contain("inspect the current working tree"));
            Assert.That(prompt, Does.Contain("determine whether anything remains"));
            Assert.That(prompt, Does.Contain("Continue only work the audit finds unfinished"));
            Assert.That(prompt, Does.Contain("candidate work from this interrupted request"));
            Assert.That(prompt, Does.Contain("Do not reject or abandon them merely because they are already present"));
            Assert.That(prompt, Does.Contain("clean up temporary processes, configuration files, and validation artifacts"));
            Assert.That(prompt, Does.Contain("Previously active agents: Shuri, Talia"));
            Assert.That(prompt, Does.Contain("Previously active agent thread IDs: thread-shuri, thread-talia"));
            Assert.That(prompt, Does.Contain("continue these exact threads before spawning replacements"));
            Assert.That(prompt, Does.Contain("queue item #7 (Cache fix)"));
            Assert.That(prompt, Does.EndWith("Implement the cache fix"));
        });
    }

    [Test]
    public void ResumeAttemptText_ExplicitlyAnnouncesIncompleteWorkRecovery() {
        var state = CreateState(
            InterruptedPromptExecutionKind.NamedAgentDirect,
            targetAgentHandle: "shuri",
            targetAgentLabel: "Shuri");

        var text = InterruptedPromptRecoveryPresentation.BuildResumeAttemptText(state);

        Assert.Multiple(() => {
            Assert.That(text, Does.StartWith("Attempting to recover the interrupted request"));
            Assert.That(text, Does.Contain("Shuri's preserved request"));
            Assert.That(text, Does.Contain("determines whether any work remains"));
            Assert.That(text, Does.Contain("partial edits").And.Contain("child-agent output"));
        });
    }

    [Test]
    public void EditablePrompt_StripsTypedAttachmentEnvelope() {
        var wrapped = AttachmentBlockFormatter.BuildTypedAttachmentBlock("file", "notes.txt", "context") +
                      "\n\nPlease finish the change";
        var state = CreateState(prompt: wrapped, attachments: [new FollowUpAttachment("", "notes", null)]);

        Assert.That(
            InterruptedPromptRecoveryPresentation.BuildEditablePrompt(state),
            Is.EqualTo("Please finish the change"));
    }

    [Test]
    public void Store_RoundTripsDurableRecoveryAndClearsIt() {
        var store = new InterruptedPromptRecoveryStore(_tempDirectory);
        var state = CreateState(
            InterruptedPromptExecutionKind.NamedAgentDirect,
            prompt: "Continue implementation",
            targetAgentHandle: "shuri",
            targetAgentLabel: "Shuri",
            activeAgentLabels: ["Shuri"],
            attachments: [new FollowUpAttachment("abc123", "Relevant commit", "original")],
            failureCount: 2);
        state = state with {
            FailureTitle = "Provider request timed out",
            FailureCategory = ProviderFailureCategory.Timeout.ToString(),
            OriginalTurnStartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActiveAgentThreadIds = ["thread-shuri"],
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.Multiple(() => {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.RecoveryId, Is.EqualTo(state.RecoveryId));
            Assert.That(loaded.ExecutionKind, Is.EqualTo(InterruptedPromptExecutionKind.NamedAgentDirect));
            Assert.That(loaded.TargetAgentHandle, Is.EqualTo("shuri"));
            Assert.That(loaded.Attachments, Has.Count.EqualTo(1));
            Assert.That(loaded.FailureCount, Is.EqualTo(2));
            Assert.That(loaded.FailureTitle, Is.EqualTo("Provider request timed out"));
            Assert.That(loaded.FailureCategory, Is.EqualTo(ProviderFailureCategory.Timeout.ToString()));
            Assert.That(loaded.OriginalTurnStartedAt, Is.EqualTo(state.OriginalTurnStartedAt));
            Assert.That(loaded.ActiveAgentThreadIds, Is.EqualTo(new[] { "thread-shuri" }));
            Assert.That(File.Exists(store.PathForDiagnostics), Is.True);
        });

        store.Clear("test");

        Assert.Multiple(() => {
            Assert.That(store.Load(), Is.Null);
            Assert.That(File.Exists(store.PathForDiagnostics), Is.False);
        });
    }

    [Test]
    public void Detector_RecoversLegacyTimedOutTurnAndInfersDirectAgent() {
        var startedAt = new DateTimeOffset(2026, 9, 9, 19, 31, 23, TimeSpan.FromHours(-4));
        var completedAt = startedAt.AddMinutes(10).AddSeconds(19);
        var prompt = "Confirm via probe check, then implement 4+3";
        var turn = new TranscriptTurnRecord(
            startedAt,
            completedAt,
            prompt,
            string.Empty,
            "[error] The prompt stopped before Squad returned a complete answer.",
            false,
            Array.Empty<TranscriptToolRecord>());
        var thread = new TranscriptThreadRecord(
            ThreadId: "thread-1",
            Title: "Shuri",
            AgentId: null,
            ToolCallId: "tool-1",
            AgentName: "shuri",
            AgentDisplayName: "Shuri",
            AgentDescription: null,
            AgentType: null,
            AgentCardKey: "shuri",
            Prompt: prompt,
            LatestResponse: null,
            LastCoordinatorAnnouncedResponse: null,
            LatestIntent: "Implementing",
            RecentActivity: Array.Empty<string>(),
            ErrorText: null,
            StatusText: "Interrupted",
            DetailText: string.Empty,
            StartedAt: startedAt.AddMilliseconds(100),
            CompletedAt: completedAt,
            Turns: Array.Empty<TranscriptTurnRecord>());
        var conversation = new WorkspaceConversationState(
            "session-1",
            completedAt,
            null,
            Array.Empty<string>(),
            [turn],
            [thread]);
        File.WriteAllText(
            Path.Combine(_tempDirectory, "trace.log"),
            $"{completedAt:yyyy-MM-dd HH:mm:ss.fff zzz} [Bridge] Prompt inactivity timeout reached after 600s. lastActivityAt=ignored. Restarting bridge process.{Environment.NewLine}");

        var detected = InterruptedPromptRecoveryDetector.TryDetect(conversation, _tempDirectory);

        Assert.Multiple(() => {
            Assert.That(detected, Is.Not.Null);
            Assert.That(detected!.ExecutionKind, Is.EqualTo(InterruptedPromptExecutionKind.NamedAgentDirect));
            Assert.That(detected.TargetAgentHandle, Is.EqualTo("shuri"));
            Assert.That(detected.TargetAgentLabel, Is.EqualTo("Shuri"));
            Assert.That(detected.InactivitySeconds, Is.EqualTo(600));
            Assert.That(InterruptedPromptRecoveryPresentation.BuildPrimaryActionLabel(detected), Is.EqualTo("Continue with Shuri"));
        });
    }

    private static InterruptedPromptRecoveryState CreateState(
        InterruptedPromptExecutionKind kind = InterruptedPromptExecutionKind.Coordinator,
        string prompt = "Original request",
        string? targetAgentHandle = null,
        string? targetAgentLabel = null,
        IReadOnlyList<string>? activeAgentLabels = null,
        string? coordinatorLabel = "Squad",
        IReadOnlyList<FollowUpAttachment>? attachments = null,
        InterruptedPromptQueueContext? queueContext = null,
        int failureCount = 1) =>
        new(
            InterruptedPromptRecoveryState.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            kind,
            prompt,
            prompt,
            attachments ?? Array.Empty<FollowUpAttachment>(),
            targetAgentHandle,
            targetAgentLabel,
            "handoff context",
            "session-1",
            activeAgentLabels ?? Array.Empty<string>(),
            coordinatorLabel,
            queueContext,
            failureCount,
            "Timed out waiting for Squad to finish responding after 600 seconds without bridge activity.",
            600);

    private static TranscriptTurnRecord CreateTurn(
        DateTimeOffset startedAt,
        string prompt,
        string response) =>
        new(
            startedAt,
            startedAt.AddSeconds(1),
            prompt,
            string.Empty,
            response,
            false,
            Array.Empty<TranscriptToolRecord>());

    private static WorkspaceConversationState CreateConversation(
        IReadOnlyList<TranscriptTurnRecord> turns) =>
        new(
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            Array.Empty<string>(),
            turns);
}
