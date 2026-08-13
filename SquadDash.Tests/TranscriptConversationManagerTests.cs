using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
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

    [Test, Apartment(ApartmentState.STA)]
    public void ShouldRecoverMissingAgentReportForLatestCoordinatorTurn_ReturnsTrue_WhenThreadCompletedDuringLatestTurn() {
        var startedAt = new DateTimeOffset(2026, 7, 10, 14, 19, 36, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(11);
        var manager = MakeManager();
        manager.ConversationState = WorkspaceConversationState.Empty with {
            Turns = [
                new TranscriptTurnRecord(
                    startedAt,
                    completedAt.AddSeconds(15),
                    "Fix the callout",
                    string.Empty,
                    "Vesper is writing test cases in the background.",
                    true,
                    Array.Empty<TranscriptToolRecord>()),
                MakeBoundary(
                    completedAt.AddMinutes(24),
                    TimeSpan.FromSeconds(3),
                    completedAt.AddMinutes(24).AddSeconds(3))
            ]
        };
        var thread = new TranscriptThreadState(
            "call-vesper",
            TranscriptThreadKind.Agent,
            "Vesper Knox",
            startedAt.AddMinutes(5)) {
            CompletedAt = completedAt
        };

        Assert.That(manager.ShouldRecoverMissingAgentReportForLatestCoordinatorTurn(thread), Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ShouldRecoverMissingAgentReportForLatestCoordinatorTurn_ReturnsFalse_ForOlderThread() {
        var latestStartedAt = new DateTimeOffset(2026, 7, 10, 14, 19, 36, TimeSpan.Zero);
        var manager = MakeManager();
        manager.ConversationState = WorkspaceConversationState.Empty with {
            Turns = [
                new TranscriptTurnRecord(
                    latestStartedAt,
                    latestStartedAt.AddMinutes(11),
                    "Fix the callout",
                    string.Empty,
                    "Vesper is writing test cases in the background.",
                    true,
                    Array.Empty<TranscriptToolRecord>())
            ]
        };
        var thread = new TranscriptThreadState(
            "call-lyra",
            TranscriptThreadKind.Agent,
            "Lyra Morn",
            latestStartedAt.AddDays(-3)) {
            CompletedAt = latestStartedAt.AddDays(-3).AddMinutes(2)
        };

        Assert.That(manager.ShouldRecoverMissingAgentReportForLatestCoordinatorTurn(thread), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AppendAgentReportToCurrentOrLastTurn_AttachesActiveTurnAndPersistsFoldedTurn() {
        using var workspace = new TestWorkspace();
        var workspacePath = workspace.GetPath("workspace");
        Directory.CreateDirectory(workspacePath);

        var startedAt = new DateTimeOffset(2026, 7, 10, 14, 25, 11, TimeSpan.Zero);
        var coordinatorThread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Squad",
            startedAt);
        var activeTurn = new TranscriptTurnView(
            coordinatorThread,
            "Have Vesper write tests",
            startedAt,
            new Section(),
            []);
        activeTurn.ResponseTextBuilder.Append("Vesper is writing test cases in the background.");

        var manager = MakeManager(
            workspace: SessionWorkspace.Create(workspacePath),
            coordinatorThread: coordinatorThread,
            currentTurn: activeTurn);

        var attached = manager.AppendAgentReportToCurrentOrLastTurn(
            "Vesper Knox",
            @"C:\reports\vesper.md");

        var savedTurn = manager.ConversationState.Turns.Single();
        Assert.Multiple(() => {
            Assert.That(attached, Is.True);
            Assert.That(activeTurn.AgentReports, Has.Count.EqualTo(1));
            Assert.That(activeTurn.AgentReports[0].AgentLabel, Is.EqualTo("Vesper Knox"));
            Assert.That(savedTurn.AgentReports, Is.Not.Null);
            Assert.That(savedTurn.AgentReports!.Single().ReportPath, Is.EqualTo(@"C:\reports\vesper.md"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SaveCurrentTurnToConversation_PreservesPersistedAgentReports_WhenActiveTurnLacksMetadata() {
        using var workspace = new TestWorkspace();
        var workspacePath = workspace.GetPath("workspace");
        Directory.CreateDirectory(workspacePath);

        var startedAt = new DateTimeOffset(2026, 7, 10, 14, 25, 11, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(5);
        var prompt = "Have Vesper write tests";
        var report = new AgentReportInfo("Vesper Knox", @"C:\reports\vesper.md");
        var coordinatorThread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Squad",
            startedAt);
        var activeTurn = new TranscriptTurnView(
            coordinatorThread,
            prompt,
            startedAt,
            new Section(),
            []);
        activeTurn.ResponseTextBuilder.Append("Final coordinator response.");

        var manager = MakeManager(
            workspace: SessionWorkspace.Create(workspacePath),
            coordinatorThread: coordinatorThread,
            currentTurn: activeTurn);
        manager.ConversationState = WorkspaceConversationState.Empty with {
            Turns = [
                new TranscriptTurnRecord(
                    startedAt,
                    null,
                    prompt,
                    string.Empty,
                    "Partial coordinator response.",
                    true,
                    Array.Empty<TranscriptToolRecord>()) {
                    AgentReports = [report]
                }
            ]
        };

        manager.SaveCurrentTurnToConversation(completedAt, "execute-coordinator-finally");

        var savedTurn = manager.ConversationState.Turns.Single();
        Assert.Multiple(() => {
            Assert.That(savedTurn.ResponseText, Is.EqualTo("Final coordinator response."));
            Assert.That(savedTurn.AgentReports, Is.Not.Null);
            Assert.That(savedTurn.AgentReports!.Single(), Is.EqualTo(report));
        });
    }

    [Test]
    public void ShouldApplyPendingSessionBoundary_ExplicitClear_ReturnsFalse() {
        var state = WorkspaceConversationState.Empty with {
            ClearedAt = DateTimeOffset.UtcNow
        };

        Assert.That(TranscriptConversationManager.ShouldApplyPendingSessionBoundary(state), Is.False);
    }

    [Test]
    public void ShouldApplyPendingSessionBoundary_ActiveConversation_ReturnsTrue() {
        var state = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            Array.Empty<string>(),
            [MakeTurn("response")]);

        Assert.That(TranscriptConversationManager.ShouldApplyPendingSessionBoundary(state), Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_PreventsOlderBackgroundSaveFromRestoringDequeuedItem() {
        var manager = MakeManager();
        var staleState = WorkspaceConversationState.Empty with {
            QueuedPromptEntries = [new QueuedPromptEntry("queued prompt", IsDictated: true)]
        };
        manager.ConversationState = staleState;

        var staleVersion = InvokePrivate<long>(manager, "RegisterConversationSaveRequest");

        manager.UpdateQueuedPromptsState(Array.Empty<PromptQueueItem>());
        var clearedAt = manager.ConversationState.QueueLastChangedAt;
        InvokePrivate<object?>(manager, "ApplySavedConversationStateIfCurrent", staleVersion, staleState);

        Assert.Multiple(() => {
            Assert.That(manager.ConversationState.QueuedPromptEntries, Is.Null);
            Assert.That(manager.ConversationState.QueueLastChangedAt, Is.EqualTo(clearedAt));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_RecordsQueueLastChangedAtWhenPersistenceStateChanges() {
        var manager = MakeManager();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        manager.UpdateQueuedPromptsState([
            new PromptQueueItem { Text = "queued prompt", SequenceNumber = 1 }
        ]);

        Assert.That(manager.ConversationState.QueueLastChangedAt, Is.Not.Null);
        Assert.That(manager.ConversationState.QueueLastChangedAt!.Value, Is.GreaterThan(before));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_DuringHydration_CannotEraseSavedQueueState() {
        var manager = MakeManager();
        var savedEntries = Enumerable.Range(1, 20)
            .Select(index => new QueuedPromptEntry($"queued prompt {index}", IsDictated: false))
            .ToArray();
        manager.ConversationState = WorkspaceConversationState.Empty with {
            QueuedPromptEntries = savedEntries,
            QueueRightmostHeld = true,
            QueueActiveTabIndex = 7,
            LoopQueuedToDequeue = true,
        };

        manager.BeginQueuedPromptsStateHydration();
        manager.UpdateQueuedPromptsState(Array.Empty<PromptQueueItem>());

        Assert.Multiple(() => {
            Assert.That(manager.ConversationState.QueuedPromptEntries, Has.Count.EqualTo(20));
            Assert.That(manager.ConversationState.QueueRightmostHeld, Is.True);
            Assert.That(manager.ConversationState.QueueActiveTabIndex, Is.EqualTo(7));
            Assert.That(manager.ConversationState.LoopQueuedToDequeue, Is.True);
        });

        manager.CompleteQueuedPromptsStateHydration();
        manager.UpdateQueuedPromptsState(Array.Empty<PromptQueueItem>());
        Assert.That(manager.ConversationState.QueuedPromptEntries, Is.Null,
            "Explicit queue changes must persist normally after hydration completes.");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_PreservesQueuedEditorStateAndSelectedTab() {
        var manager = MakeManager();
        var item = new PromptQueueItem {
            Text = "select this text",
            SequenceNumber = 1,
            CaretIndex = 11,
            SelectionStart = 7,
            SelectionLength = 4,
        };

        manager.UpdateQueuedPromptsState([item], activeTabIndex: 0);

        var entry = manager.ConversationState.QueuedPromptEntries!.Single();
        Assert.Multiple(() => {
            Assert.That(manager.ConversationState.QueueActiveTabIndex, Is.Zero);
            Assert.That(entry.CaretIndex, Is.EqualTo(11));
            Assert.That(entry.SelectionStart, Is.EqualTo(7));
            Assert.That(entry.SelectionLength, Is.EqualTo(4));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void EnsureAgentThreadRenderedAsync_ConcurrentCallersAwaitOneLazyRender()
    {
        var renderedTurns = 0;
        var manager = MakeManager(renderPersistedTurn: (_, _, _) => renderedTurns++);
        var thread = new TranscriptThreadState(
            "agent-thread",
            TranscriptThreadKind.Agent,
            "Lyra Morn",
            DateTimeOffset.UtcNow);
        var turn = new TranscriptTurnRecord(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "prompt",
            string.Empty,
            "Model: Profile 2",
            false,
            Array.Empty<TranscriptToolRecord>());
        var pendingField = typeof(TranscriptConversationManager).GetField(
            "_pendingAgentRenders",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pending = (Dictionary<string, IReadOnlyList<TranscriptTurnRecord>>)pendingField.GetValue(manager)!;
        pending[thread.ThreadId] = new[] { turn };

        var first = manager.EnsureAgentThreadRenderedAsync(thread);
        var second = manager.EnsureAgentThreadRenderedAsync(thread);
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        _ = first.ContinueWith(
            _ => dispatcher.BeginInvoke(DispatcherPriority.Send, () => frame.Continue = false),
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        first.GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.IsCompletedSuccessfully, Is.True);
            Assert.That(renderedTurns, Is.EqualTo(1));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void WorkspaceTransitionSave_SealsQueueAndEditorStateBeforeWorkspaceChanges() {
        using var workspace = new TestWorkspace();
        var workspacePath = workspace.GetPath("workspace-transition");
        Directory.CreateDirectory(workspacePath);
        var promptText = "draft for the old workspace";
        var manager = MakeManager(
            getPromptText: () => promptText,
            workspace: SessionWorkspace.Create(workspacePath));
        var item = new PromptQueueItem {
            Text = "queued old-workspace prompt",
            SequenceNumber = 1,
            CaretIndex = 12,
            SelectionStart = 7,
            SelectionLength = 5,
        };
        manager.UpdateQueuedPromptsState([item], activeTabIndex: 0);

        var saved = manager.TrySaveWorkspaceStateForTransition("test");
        var reloaded = new WorkspaceConversationStore().Load(workspacePath);

        Assert.Multiple(() => {
            Assert.That(saved, Is.True);
            Assert.That(reloaded.PromptDraft, Is.EqualTo(promptText));
            Assert.That(reloaded.QueueActiveTabIndex, Is.Zero);
            Assert.That(reloaded.QueuedPromptEntries, Has.Count.EqualTo(1));
            Assert.That(reloaded.QueuedPromptEntries![0].Text, Is.EqualTo(item.Text));
            Assert.That(reloaded.QueuedPromptEntries[0].CaretIndex, Is.EqualTo(12));
            Assert.That(reloaded.QueuedPromptEntries[0].SelectionStart, Is.EqualTo(7));
            Assert.That(reloaded.QueuedPromptEntries[0].SelectionLength, Is.EqualTo(5));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_PreservesLockedHostPresentation()
    {
        var manager = MakeManager();

        manager.UpdateQueuedPromptsState([
            new PromptQueueItem
            {
                Text = "Continue plan P",
                SequenceNumber = 1,
                SourceTag = "plan-continuation",
                IsSystemInjected = true,
                IsLocked = true,
                DisplayLabel = "Plan Step 4",
                ReadOnlyDisplayText = "Read-only continuation details",
                IsEditing = true,
            }
        ]);

        var entry = manager.ConversationState.QueuedPromptEntries!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.IsLocked, Is.True);
            Assert.That(entry.DisplayLabel, Is.EqualTo("Plan Step 4"));
            Assert.That(entry.ReadOnlyDisplayText, Is.EqualTo("Read-only continuation details"));
            Assert.That(entry.IsEditing, Is.True);
        });
    }

    // ── UpdateQueuedPromptsState — image attachment preservation (411e34f) ────

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_PreservesImagePath_OnAttachment() {
        var manager = MakeManager();
        var item = new PromptQueueItem { Text = "prompt with image", SequenceNumber = 1 };
        var attachment = new FollowUpAttachment(
            CommitSha:    "abc123",
            Description:  "screenshot",
            OriginalPrompt: null,
            ImagePath:    @"C:\screenshots\shot.png");

        manager.UpdateQueuedPromptsState(
            [item],
            new Dictionary<string, List<FollowUpAttachment>> {
                [item.Id] = [attachment]
            });

        var dto = manager.ConversationState.QueuedPromptEntries![0].Attachments![0];
        Assert.That(dto.ImagePath, Is.EqualTo(@"C:\screenshots\shot.png"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_PreservesImageSubmittedAt_AsIso8601() {
        var manager = MakeManager();
        var item = new PromptQueueItem { Text = "prompt", SequenceNumber = 1 };
        var capturedAt = new DateTime(2026, 5, 19, 14, 0, 0, DateTimeKind.Utc);
        var attachment = new FollowUpAttachment(
            CommitSha:       "abc123",
            Description:     "screenshot",
            OriginalPrompt:  null,
            ImagePath:       @"C:\screenshots\shot.png",
            ImageSubmittedAt: capturedAt);

        manager.UpdateQueuedPromptsState(
            [item],
            new Dictionary<string, List<FollowUpAttachment>> {
                [item.Id] = [attachment]
            });

        var dto = manager.ConversationState.QueuedPromptEntries![0].Attachments![0];
        Assert.That(dto.ImageSubmittedAt, Is.EqualTo("2026-05-19T14:00:00.0000000Z"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_ImageSubmittedAt_IsNull_WhenAttachmentHasNoImage() {
        var manager = MakeManager();
        var item = new PromptQueueItem { Text = "prompt", SequenceNumber = 1 };
        var attachment = new FollowUpAttachment(
            CommitSha:    "abc123",
            Description:  "no image attachment",
            OriginalPrompt: null);

        manager.UpdateQueuedPromptsState(
            [item],
            new Dictionary<string, List<FollowUpAttachment>> {
                [item.Id] = [attachment]
            });

        var dto = manager.ConversationState.QueuedPromptEntries![0].Attachments![0];
        Assert.Multiple(() => {
            Assert.That(dto.ImagePath, Is.Null);
            Assert.That(dto.ImageSubmittedAt, Is.Null);
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UpdateQueuedPromptsState_PreservesAllFieldsAlongWithImagePath() {
        var manager = MakeManager();
        var item = new PromptQueueItem { Text = "prompt", SequenceNumber = 1 };
        var attachment = new FollowUpAttachment(
            CommitSha:       "sha-feed1",
            Description:     "full attachment",
            OriginalPrompt:  "original prompt text",
            TranscriptQuote: "quoted text",
            ContentBlock:    "code block",
            ImagePath:       @"C:\screenshots\shot.png",
            ImageSubmittedAt: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            RequiresRemovalConfirmation: true,
            RemovalWarning: "Required plan context");

        manager.UpdateQueuedPromptsState(
            [item],
            new Dictionary<string, List<FollowUpAttachment>> {
                [item.Id] = [attachment]
            });

        var dto = manager.ConversationState.QueuedPromptEntries![0].Attachments![0];
        Assert.Multiple(() => {
            Assert.That(dto.CommitSha,       Is.EqualTo("sha-feed1"));
            Assert.That(dto.Description,     Is.EqualTo("full attachment"));
            Assert.That(dto.OriginalPrompt,  Is.EqualTo("original prompt text"));
            Assert.That(dto.TranscriptQuote, Is.EqualTo("quoted text"));
            Assert.That(dto.ContentBlock,    Is.EqualTo("code block"));
            Assert.That(dto.ImagePath,       Is.EqualTo(@"C:\screenshots\shot.png"));
            Assert.That(dto.ImageSubmittedAt, Is.EqualTo("2026-05-01T12:00:00.0000000Z"));
            Assert.That(dto.RequiresRemovalConfirmation, Is.True);
            Assert.That(dto.RemovalWarning, Is.EqualTo("Required plan context"));
        });
    }

    // ── ClearAndPersist ───────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ClearAndPersist_MakesPendingSaveStale() {
        using var workspace = new TestWorkspace();
        var workspacePath = workspace.GetPath("test-workspace");
        Directory.CreateDirectory(workspacePath);

        var manager = MakeManager();

        // Inject a test-isolated store so ClearAndPersist doesn't write to production AppData.
        var testStore = new WorkspaceConversationStore(workspace.GetPath("store"));
        var storeField = typeof(TranscriptConversationManager)
            .GetField("_conversationStore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        storeField.SetValue(manager, testStore);

        // Capture the current save version, simulating a background save that is now queued.
        var staleVersion = InvokePrivate<long>(manager, "RegisterConversationSaveRequest");

        manager.ClearAndPersist(workspacePath);

        // ClearAndPersist bumps the version; the previously captured version must now be stale.
        var isStale = InvokePrivate<bool>(manager, "HasNewerConversationSaveRequest", staleVersion);
        Assert.That(isStale, Is.True,
            "Any save queued before ClearAndPersist should be considered stale after the call.");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildTranscriptTurnRecord_ExpandsDisplayArtifactSegmentsBeforePersisting() {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(
            Path.Combine(".squad", "tmp", "agent-artifacts", "guided-tour-data-loss-prompt.md"),
            "# Guided tour data loss\n\nPersisted artifact content.");

        var sessionWorkspace = SessionWorkspace.Create(workspace.RootPath);
        var thread = new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Squad",
            DateTimeOffset.UtcNow);
        var turn = new TranscriptTurnView(
            thread,
            "prompt",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            new Section(),
            Array.Empty<Block>());
        var response = new TranscriptResponseEntry(turn, 1, new Section());
        response.RawTextBuilder.Append("""
            Here is the investigation prompt.

            SQUADDASH_ARTIFACT_JSON:
            { "path": ".squad/tmp/agent-artifacts/guided-tour-data-loss-prompt.md", "language": "markdown", "display": "code_block", "label": "Investigation prompt" }
            """);
        turn.ResponseEntries.Add(response);

        var manager = MakeManager(workspace: sessionWorkspace, coordinatorThread: thread);

        var record = manager.BuildTranscriptTurnRecord(turn, DateTimeOffset.UtcNow);
        var segment = record.GetResponseSegments().Single();

        Assert.Multiple(() => {
            Assert.That(segment.Text, Does.Contain("Here is the investigation prompt."));
            Assert.That(segment.Text, Does.Contain("**Investigation prompt**"));
            Assert.That(segment.Text, Does.Contain("```markdown"));
            Assert.That(segment.Text, Does.Contain("Persisted artifact content."));
            Assert.That(segment.Text, Does.Not.Contain("SQUADDASH_ARTIFACT_JSON:"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ReplaceLatestCoordinatorResponse_ReplacesStaleRecoveryAndDropsProtocolButtons() {
        using var workspace = new TestWorkspace();
        var sessionWorkspace = SessionWorkspace.Create(workspace.RootPath);
        var manager = MakeManager(workspace: sessionWorkspace);
        var testStore = new WorkspaceConversationStore(workspace.GetPath("store"));
        typeof(TranscriptConversationManager)
            .GetField("_conversationStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, testStore);
        const string stale = "Plan execution stopped unexpectedly after producing committed work. Recovery is available.";
        const string outcome = "✓ Recovery completed. Reused committed work and continued.";
        var turn = MakeTurn(stale) with
        {
            ResponseSegments =
            [
                new TranscriptResponseSegmentRecord(stale)
                {
                    ProtocolJsonBlocks =
                    [
                        new TranscriptProtocolJsonBlock("DECOMPOSE_STEP_RESULT_JSON", "{}"),
                    ],
                },
            ],
        };
        manager.ConversationState = manager.ConversationState with { Turns = [turn] };

        var replaced = manager.ReplaceLatestCoordinatorResponse([stale], outcome);

        var updated = manager.ConversationState.Turns.Single();
        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.True);
            Assert.That(updated.ResponseText, Is.EqualTo(outcome));
            Assert.That(updated.GetResponseSegments().Single().ProtocolJsonBlocks, Is.Null);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T InvokePrivate<T>(TranscriptConversationManager manager, string methodName, params object?[] args) {
        var method = typeof(TranscriptConversationManager).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Expected private method {methodName} to exist.");
        return (T)method!.Invoke(manager, args)!;
    }

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
        Action<string, int, int, int>? setPromptText = null,
        SessionWorkspace? workspace = null,
        TranscriptThreadState? coordinatorThread = null,
        TranscriptTurnView? currentTurn = null,
        Action<TranscriptThreadState, TranscriptTurnRecord, bool>? renderPersistedTurn = null) {
        var promptText = string.Empty;
        getPromptText ??= () => promptText;
        setPromptText ??= (t, _, _, _) => promptText = t;
        coordinatorThread ??= new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Squad",
            DateTimeOffset.UtcNow);

        return new TranscriptConversationManager(
            getWorkspace:             () => workspace,
            getPromptText:            getPromptText,
            setPromptText:            setPromptText,
            getPromptCaretState:      () => (0, 0, 0),
            isClosing:                () => false,
            renderPersistedTurn:      renderPersistedTurn ?? ((_, _, _) => { }),
            coordinatorThread:        () => coordinatorThread,
            selectedThread:           () => null,
            maybePublishRoutingIssue: _ => { },
            syncAgentCardsWithThreads: () => { },
            dispatcher:               Dispatcher.CurrentDispatcher,
            scrollOutputToEnd:        _ => { },
            agentThreadRegistry:      MakeRegistry(),
            getToolEntries:           () => new Dictionary<string, ToolTranscriptEntry>(),
            getCurrentTurn:           () => currentTurn,
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
