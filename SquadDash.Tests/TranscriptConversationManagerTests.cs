using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptConversationManagerTests {

    // ── ResetHistoryNavigation ────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ResetHistoryNavigation_ClearsIndexAndDraft() {
        var manager = MakeManager();
        manager.HistoryIndex = 1;
        manager.HistoryDraft = "saved draft text";

        manager.ResetHistoryNavigation();

        Assert.Multiple(() => {
            Assert.That(manager.HistoryIndex, Is.Null);
            Assert.That(manager.HistoryDraft, Is.Null);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ResetHistoryNavigation_IsIdempotent_WhenAlreadyReset() {
        var manager = MakeManager();

        // Should not throw when called on an already-reset manager.
        Assert.DoesNotThrow(() => {
            manager.ResetHistoryNavigation();
            manager.ResetHistoryNavigation();
        });

        Assert.Multiple(() => {
            Assert.That(manager.HistoryIndex, Is.Null);
            Assert.That(manager.HistoryDraft, Is.Null);
        });
    }

    // ── NavigateHistory ───────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void NavigateHistory_Backward_MovesToLastHistoryEntry() {
        var promptText = "current draft";
        var manager = MakeManager(getPromptText: () => promptText, setPromptText: (t, _, _, _) => promptText = t);

        manager.AddPromptToHistory("first entry");
        manager.AddPromptToHistory("second entry");

        manager.NavigateHistory(-1);

        Assert.That(promptText, Is.EqualTo("second entry"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void NavigateHistory_BackwardThenForward_RestoresDraft() {
        var promptText = "my draft";
        var manager = MakeManager(getPromptText: () => promptText, setPromptText: (t, _, _, _) => promptText = t);

        manager.AddPromptToHistory("history entry");

        manager.NavigateHistory(-1); // go back to "history entry"
        manager.NavigateHistory(1);  // go forward → restore draft

        Assert.That(promptText, Is.EqualTo("my draft"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void NavigateHistory_NoHistory_DoesNotChangePrompt() {
        var promptText = "unchanged";
        var manager = MakeManager(getPromptText: () => promptText, setPromptText: (t, _, _, _) => promptText = t);

        manager.NavigateHistory(-1);

        Assert.That(promptText, Is.EqualTo("unchanged"));
    }

    // ── CurrentSessionId ─────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void CurrentSessionId_CanBeSetAndRead() {
        var manager = MakeManager();

        manager.CurrentSessionId = "session-42";

        Assert.That(manager.CurrentSessionId, Is.EqualTo("session-42"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CurrentSessionId_IsNullByDefault() {
        var manager = MakeManager();

        Assert.That(manager.CurrentSessionId, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CurrentSessionId_TracksRecentSessionIdsMostRecentFirst() {
        var manager = MakeManager();

        manager.CurrentSessionId = "session-1";
        manager.CurrentSessionId = "session-2";
        manager.CurrentSessionId = "session-1";

        Assert.That(manager.GetKnownSessionIds(), Is.EqualTo(new[] { "session-1", "session-2" }));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void StartFreshSessionPreservingTranscript_ClearsCurrentSessionButKeepsItRemembered() {
        var manager = MakeManager();
        manager.CurrentSessionId = "session-42";

        var detached = manager.StartFreshSessionPreservingTranscript();

        Assert.Multiple(() => {
            Assert.That(detached, Is.EqualTo("session-42"));
            Assert.That(manager.CurrentSessionId, Is.Null);
            Assert.That(manager.GetKnownSessionIds(), Is.EqualTo(new[] { "session-42" }));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryResumeSession_MatchesUniquePrefixAndMovesSelectionToFront() {
        var manager = MakeManager();
        manager.CurrentSessionId = "abc123-session";
        manager.CurrentSessionId = "def456-session";
        manager.StartFreshSessionPreservingTranscript();

        var result = manager.TryResumeSession("abc");

        Assert.Multiple(() => {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.SelectedSessionId, Is.EqualTo("abc123-session"));
            Assert.That(manager.CurrentSessionId, Is.EqualTo("abc123-session"));
            Assert.That(manager.GetKnownSessionIds(), Is.EqualTo(new[] { "abc123-session", "def456-session" }));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryResumeSession_RejectsAmbiguousPrefix() {
        var manager = MakeManager();
        manager.CurrentSessionId = "abc123-session";
        manager.CurrentSessionId = "abc999-session";
        manager.StartFreshSessionPreservingTranscript();

        var result = manager.TryResumeSession("abc");

        Assert.Multiple(() => {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("ambiguous"));
            Assert.That(manager.CurrentSessionId, Is.Null);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CapturePromptContextDiagnostics_SummarizesCoordinatorAndAgentTranscriptState() {
        var manager = MakeManager();
        manager.CurrentSessionId = "session-42";
        manager.AddPromptToHistory("First prompt");
        manager.AddPromptToHistory("Second prompt");
        manager.ConversationState = new WorkspaceConversationState(
            SessionId: "session-42",
            SessionUpdatedAt: new DateTimeOffset(2026, 4, 22, 17, 0, 0, TimeSpan.Zero),
            PromptDraft: null,
            PromptHistory: manager.PromptHistory.ToArray(),
            Turns: [
                new TranscriptTurnRecord(
                    new DateTimeOffset(2026, 4, 22, 16, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 4, 22, 16, 1, 0, TimeSpan.Zero),
                    "Fix the UI",
                    "Thinking",
                    "Coordinator answer",
                    true,
                    Array.Empty<TranscriptToolRecord>())
            ],
            Threads: [
                new TranscriptThreadRecord(
                    ThreadId: "thread-1",
                    Title: "Lyra",
                    AgentId: "agent-1",
                    ToolCallId: "tool-1",
                    AgentName: "lyra-morn",
                    AgentDisplayName: "Lyra Morn",
                    AgentDescription: "WPF specialist",
                    AgentType: "agent",
                    AgentCardKey: "lyra",
                    Prompt: "Please help",
                    LatestResponse: "I will take it",
                    LastCoordinatorAnnouncedResponse: null,
                    LatestIntent: "Implement UI",
                    RecentActivity: Array.Empty<string>(),
                    ErrorText: null,
                    StatusText: "Completed",
                    DetailText: string.Empty,
                    StartedAt: new DateTimeOffset(2026, 4, 22, 16, 2, 0, TimeSpan.Zero),
                    CompletedAt: new DateTimeOffset(2026, 4, 22, 16, 3, 0, TimeSpan.Zero),
                    Turns: [
                        new TranscriptTurnRecord(
                            new DateTimeOffset(2026, 4, 22, 16, 2, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 4, 22, 16, 3, 0, TimeSpan.Zero),
                            "Implement it",
                            "Inspecting XAML",
                            "Done",
                            true,
                            Array.Empty<TranscriptToolRecord>())
                    ])
            ],
            RecentSessionIds: ["session-42", "session-41"]);

        var diagnostics = manager.CapturePromptContextDiagnostics();

        Assert.Multiple(() => {
            Assert.That(diagnostics.SessionId, Is.EqualTo("session-42"));
            Assert.That(diagnostics.CoordinatorTurnCount, Is.EqualTo(1));
            Assert.That(diagnostics.AgentThreadCount, Is.EqualTo(1));
            Assert.That(diagnostics.AgentThreadTurnCount, Is.EqualTo(1));
            Assert.That(diagnostics.PromptHistoryCount, Is.EqualTo(2));
            Assert.That(diagnostics.RecentSessionCount, Is.EqualTo(2));
            Assert.That(diagnostics.CoordinatorPromptChars, Is.EqualTo("Fix the UI".Length));
            Assert.That(diagnostics.CoordinatorResponseChars, Is.EqualTo("Coordinator answer".Length));
            Assert.That(diagnostics.AgentResponseChars, Is.EqualTo("Done".Length));
            Assert.That(diagnostics.TranscriptStartedAt, Is.EqualTo(new DateTimeOffset(2026, 4, 22, 16, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void FindLastInteractiveTurnIndex_IgnoresTrailingSessionBoundary() {
        var first = MakeTurn("First response");
        var second = MakeTurn("Second response");
        var boundary = MakeTurn(string.Empty) with { IsSessionBoundary = true };

        var index = TranscriptConversationManager.FindLastInteractiveTurnIndex([first, second, boundary]);

        Assert.That(index, Is.EqualTo(1));
    }

    [Test]
    public void FindLastInteractiveTurnIndex_ReturnsMinusOne_WhenOnlySessionBoundariesExist() {
        var boundary = MakeTurn(string.Empty) with { IsSessionBoundary = true };

        var index = TranscriptConversationManager.FindLastInteractiveTurnIndex([boundary]);

        Assert.That(index, Is.EqualTo(-1));
    }

    [Test]
    public void ApplyPendingSessionBoundary_AppendsAfterInteractiveTurn() {
        var turn = MakeTurn("Ready.");
        var boundary = MakeBoundary(
            new DateTimeOffset(2026, 5, 16, 18, 5, 30, TimeSpan.Zero),
            TimeSpan.FromSeconds(3),
            new DateTimeOffset(2026, 5, 16, 18, 5, 33, TimeSpan.Zero));

        var result = TranscriptConversationManager.ApplyPendingSessionBoundary([turn], boundary, out var replaced);

        Assert.Multiple(() => {
            Assert.That(replaced, Is.False);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[1].SessionBoundaryShutdownTime, Is.EqualTo(boundary.SessionBoundaryShutdownTime));
        });
    }

    [Test]
    public void ApplyPendingSessionBoundary_ReplacesTailBoundaryWithLatestRestart() {
        var turn = MakeTurn("Ready.");
        var oldBoundary = MakeBoundary(
            new DateTimeOffset(2026, 5, 16, 17, 54, 28, TimeSpan.Zero),
            TimeSpan.FromSeconds(2.5),
            new DateTimeOffset(2026, 5, 16, 17, 54, 31, TimeSpan.Zero));
        var latestBoundary = MakeBoundary(
            new DateTimeOffset(2026, 5, 16, 18, 5, 30, TimeSpan.Zero),
            TimeSpan.FromSeconds(2.9),
            new DateTimeOffset(2026, 5, 16, 18, 5, 33, TimeSpan.Zero));

        var result = TranscriptConversationManager.ApplyPendingSessionBoundary(
            [turn, oldBoundary],
            latestBoundary,
            out var replaced);

        Assert.Multiple(() => {
            Assert.That(replaced, Is.True);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[1].SessionBoundaryShutdownTime, Is.EqualTo(latestBoundary.SessionBoundaryShutdownTime));
            Assert.That(result[1].SessionBoundaryStartupTime, Is.EqualTo(latestBoundary.SessionBoundaryStartupTime));
            Assert.That(result[1].SessionBoundaryOfflineDuration, Is.EqualTo(latestBoundary.SessionBoundaryOfflineDuration));
        });
    }

    [Test]
    public void ApplyPendingSessionBoundary_EmptyTurnList_AppendsAsSingleBoundary() {
        var boundary = MakeBoundary(
            new DateTimeOffset(2026, 5, 16, 18, 5, 30, TimeSpan.Zero),
            TimeSpan.FromSeconds(3),
            new DateTimeOffset(2026, 5, 16, 18, 5, 33, TimeSpan.Zero));

        var result = TranscriptConversationManager.ApplyPendingSessionBoundary(
            [], boundary, out var replaced);

        Assert.Multiple(() => {
            Assert.That(replaced, Is.False,
                "An empty list has no tail boundary to replace.");
            Assert.That(result, Has.Count.EqualTo(1),
                "Result should contain only the new boundary.");
            Assert.That(result[0].IsSessionBoundary, Is.True,
                "The single result entry should be the boundary.");
            Assert.That(result[0].SessionBoundaryShutdownTime,
                Is.EqualTo(boundary.SessionBoundaryShutdownTime));
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TranscriptTurnRecord MakeTurn(string response) =>
        new(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            "prompt",
            string.Empty,
            response,
            true,
            Array.Empty<TranscriptToolRecord>());

    private static TranscriptTurnRecord MakeBoundary(
        DateTimeOffset shutdownTime,
        TimeSpan offlineDuration,
        DateTimeOffset startupTime) =>
        new(
            shutdownTime,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            Array.Empty<TranscriptToolRecord>()) {
            IsSessionBoundary              = true,
            SessionBoundaryShutdownTime    = shutdownTime,
            SessionBoundaryOfflineDuration = offlineDuration,
            SessionBoundaryStartupTime     = startupTime,
            SessionBoundaryAppVersion      = "test"
        };

    private static AgentThreadRegistry MakeRegistry() =>
        new AgentThreadRegistry(
            beginTranscriptTurn:              (_, _) => { },
            finalizeCurrentTurnResponse:      _ => { },
            collapseCurrentTurnThinking:      _ => { },
            renderToolEntry:                  _ => { },
            updateToolSpinnerState:           () => { },
            syncActiveToolName:               () => { },
            syncThreadChip:                   _ => { },
            syncTaskToolTranscriptLink:       _ => { },
            appendText:                       (_, _) => { },
            syncAgentCards:                   () => { },
            syncAgentCardsWithThreads:        () => { },
            getKnownTeamAgentDescriptors:     () => Array.Empty<TeamAgentDescriptor>(),
            updateTranscriptThreadBadge:      () => { },
            isThreadActiveForDisplay:         _ => false,
            observeBackgroundAgentActivity:   (_, _) => { },
            renderConversationHistory:        (_, _) => Task.CompletedTask,
            resolveBackgroundAgentDisplayLabel: _ => string.Empty,
            buildAgentLabel:                  _ => string.Empty);

    private static TranscriptConversationManager MakeManager(
        Func<string>? getPromptText = null,
        Action<string, int, int, int>? setPromptText = null) {
        var promptText = string.Empty;
        getPromptText ??= () => promptText;
        setPromptText ??= (t, _, _, _) => promptText = t;

        return new TranscriptConversationManager(
            getWorkspace:             () => null,
            getPromptText:            getPromptText,
            setPromptText:            setPromptText,
            getPromptCaretState:      () => (0, 0, 0),
            isClosing:                () => false,
            renderPersistedTurn:      (_, _, _) => { },
            coordinatorThread:        () => throw new InvalidOperationException("not needed for this test"),
            selectedThread:           () => null,
            maybePublishRoutingIssue: _ => { },
            syncAgentCardsWithThreads: () => { },
            dispatcher:               Dispatcher.CurrentDispatcher,
            scrollOutputToEnd:        _ => { },
            agentThreadRegistry:      MakeRegistry(),
            getToolEntries:           () => new Dictionary<string, ToolTranscriptEntry>(),
            getCurrentTurn:           () => null,
            setCurrentTurnNull:       () => { },
            beginBulkDocumentLoad:    _ => { },
            endBulkDocumentLoad:      _ => { },
            prependTurnsBatch:        (_, _) => { },
            getScrollableHeight:      () => 0.0,
            getVerticalOffset:        () => 0.0,
            scrollToAbsoluteOffset:   _ => { },
            updateScrollLayout:       () => { });
    }
}
