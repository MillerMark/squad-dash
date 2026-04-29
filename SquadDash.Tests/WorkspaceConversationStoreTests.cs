using System;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceConversationStoreTests {
    private string _rootPath = null!;
    private string _workspacePath = null!;
    private WorkspaceConversationStore _store = null!;

    [SetUp]
    public void SetUp() {
        _rootPath = Path.Combine(Path.GetTempPath(), "SquadDash.Tests", Guid.NewGuid().ToString("N"));
        _workspacePath = Path.Combine(_rootPath, "WpfCalc");
        Directory.CreateDirectory(_workspacePath);
        _store = new WorkspaceConversationStore(Path.Combine(_rootPath, "store"));
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }

    [Test]
    public void SaveAndLoad_RoundTripsConversationStateForWorkspace() {
        var startedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var finishedAt = startedAt.AddMinutes(2);
        var expectedConfigDirectory = _store.GetSessionConfigDirectory(_workspacePath);

        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                " session-123 ",
                finishedAt,
                "line one\r\nline two",
                ["status?", "who is on the team?"],
                [
                    new TranscriptTurnRecord(
                        startedAt,
                        finishedAt,
                        "  status?  ",
                        "  Looking around the repo.  ",
                        "Ready.\r\n",
                        true,
                        [
                            new TranscriptToolRecord(
                                "tool-1",
                                new ToolTranscriptDescriptor(
                                    " powershell ",
                                    " Check git status ",
                                    " git status ",
                                    DisplayText: " status "),
                                " { \"command\": \"git status\" } ",
                                startedAt,
                                finishedAt,
                                " Still running ",
                                " Clean ",
                                " details ",
                                IsCompleted: true,
                                Success: true) {
                                ThinkingBlockSequence = 2
                            }
                        ],
                        [
                            new TranscriptThoughtRecord(" Coordinator ", " Let me inspect the repo. ", TranscriptThoughtPlacement.BeforeTools) {
                                Sequence = 1
                            },
                            new TranscriptThoughtRecord(" Coordinator ", " I have enough context to summarize. ", TranscriptThoughtPlacement.AfterTools) {
                                Sequence = 3
                            }
                        ],
                        [
                            new TranscriptResponseSegmentRecord("First streamed response segment.") {
                                Sequence = 4
                            },
                            new TranscriptResponseSegmentRecord("Second streamed response segment.") {
                                Sequence = 6
                            }
                        ])
                ],
                RecentSessionIds: [" session-123 ", " session-456 ", "session-123"]));

        var loaded = _store.Load(_workspacePath);

        Assert.Multiple(() => {
            Assert.That(Directory.Exists(expectedConfigDirectory), Is.True);
            Assert.That(saved.SessionId, Is.EqualTo("session-123"));
            Assert.That(loaded.SessionId, Is.EqualTo("session-123"));
            Assert.That(loaded.GetRecentSessionIds(), Is.EqualTo(new[] { "session-123", "session-456" }));
            Assert.That(loaded.PromptDraft, Is.EqualTo("line one\nline two"));
            Assert.That(loaded.PromptHistory, Is.EqualTo(new[] { "status?", "who is on the team?" }));
            Assert.That(loaded.Turns, Has.Count.EqualTo(1));
            Assert.That(loaded.Turns[0].Prompt, Is.EqualTo("status?"));
            Assert.That(loaded.Turns[0].ThinkingText, Is.EqualTo("Looking around the repo."));
            Assert.That(loaded.Turns[0].Thoughts, Has.Count.EqualTo(2));
            Assert.That(loaded.Turns[0].GetThoughts()[0].Speaker, Is.EqualTo("Coordinator"));
            Assert.That(loaded.Turns[0].GetThoughts()[0].Text, Is.EqualTo("Let me inspect the repo."));
            Assert.That(loaded.Turns[0].GetThoughts()[0].Placement, Is.EqualTo(TranscriptThoughtPlacement.BeforeTools));
            Assert.That(loaded.Turns[0].GetThoughts()[0].Sequence, Is.EqualTo(1));
            Assert.That(loaded.Turns[0].GetThoughts()[1].Text, Is.EqualTo("I have enough context to summarize."));
            Assert.That(loaded.Turns[0].GetThoughts()[1].Placement, Is.EqualTo(TranscriptThoughtPlacement.AfterTools));
            Assert.That(loaded.Turns[0].GetThoughts()[1].Sequence, Is.EqualTo(3));
            Assert.That(loaded.Turns[0].ResponseText, Is.EqualTo("Ready."));
            Assert.That(loaded.Turns[0].GetResponseSegments(), Has.Count.EqualTo(2));
            Assert.That(loaded.Turns[0].GetResponseSegments()[0].Text, Is.EqualTo("First streamed response segment."));
            Assert.That(loaded.Turns[0].GetResponseSegments()[0].Sequence, Is.EqualTo(4));
            Assert.That(loaded.Turns[0].GetResponseSegments()[1].Text, Is.EqualTo("Second streamed response segment."));
            Assert.That(loaded.Turns[0].GetResponseSegments()[1].Sequence, Is.EqualTo(6));
            Assert.That(loaded.Turns[0].StartedAt, Is.EqualTo(startedAt.ToUniversalTime()));
            Assert.That(loaded.Turns[0].CompletedAt, Is.EqualTo(finishedAt.ToUniversalTime()));
            Assert.That(loaded.Turns[0].Tools, Has.Count.EqualTo(1));
            Assert.That(loaded.Turns[0].Tools[0].Descriptor.ToolName, Is.EqualTo("powershell"));
            Assert.That(loaded.Turns[0].Tools[0].Descriptor.Description, Is.EqualTo("Check git status"));
            Assert.That(loaded.Turns[0].Tools[0].Descriptor.Command, Is.EqualTo("git status"));
            Assert.That(loaded.Turns[0].Tools[0].Descriptor.DisplayText, Is.EqualTo("status"));
            Assert.That(loaded.Turns[0].Tools[0].ArgsJson, Is.EqualTo("{ \"command\": \"git status\" }"));
            Assert.That(loaded.Turns[0].Tools[0].OutputText, Is.EqualTo("Clean"));
            Assert.That(loaded.Turns[0].Tools[0].DetailContent, Is.EqualTo("details"));
            Assert.That(loaded.Turns[0].Tools[0].ThinkingBlockSequence, Is.EqualTo(2));
        });
    }

    [Test]
    public void Load_LegacyThinkingTextAppearsAsCoordinatorThoughtBeforeTools() {
        _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-123",
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                [
                    new TranscriptTurnRecord(
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow,
                        "status?",
                        "Legacy thinking text",
                        "Ready.",
                        false,
                        Array.Empty<TranscriptToolRecord>())
                ]));

        var loaded = _store.Load(_workspacePath);
        var thought = loaded.Turns.Single().GetThoughts().Single();

        Assert.Multiple(() => {
            Assert.That(thought.Speaker, Is.EqualTo("Coordinator"));
            Assert.That(thought.Text, Is.EqualTo("Legacy thinking text"));
            Assert.That(thought.Placement, Is.EqualTo(TranscriptThoughtPlacement.BeforeTools));
        });
    }

    [Test]
    public void Save_PrunesTurnsOlderThanRetentionAndKeepsNewestTwoHundred() {
        var now = DateTimeOffset.UtcNow;
        var turns = Enumerable.Range(0, 205)
            .Select(index => new TranscriptTurnRecord(
                now.AddMinutes(-205 + index),
                now.AddMinutes(-205 + index).AddSeconds(15),
                $"prompt-{index}",
                string.Empty,
                $"response-{index}",
                true,
                Array.Empty<TranscriptToolRecord>()))
            .Prepend(new TranscriptTurnRecord(
                now.AddDays(-20),
                now.AddDays(-20).AddMinutes(1),
                "stale",
                string.Empty,
                "stale",
                true,
                Array.Empty<TranscriptToolRecord>()))
            .ToArray();

        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState("session-123", now, null, Array.Empty<string>(), turns));

        var prompts = saved.Turns.Select(turn => turn.Prompt).ToArray();

        Assert.Multiple(() => {
            Assert.That(saved.Turns, Has.Count.EqualTo(200));
            Assert.That(prompts, Does.Not.Contain("stale"));
            Assert.That(prompts, Does.Not.Contain("prompt-0"));
            Assert.That(prompts, Does.Not.Contain("prompt-4"));
            Assert.That(prompts[0], Is.EqualTo("prompt-5"));
            Assert.That(prompts[^1], Is.EqualTo("prompt-204"));
        });
    }

    [Test]
    public void Save_SortsTurnsChronologicallyBeforePersisting() {
        var promptStartedAt = new DateTimeOffset(2026, 4, 16, 17, 45, 0, TimeSpan.FromHours(-4));
        var localCommandStartedAt = promptStartedAt.AddMinutes(1);
        var promptTurn = new TranscriptTurnRecord(
            promptStartedAt,
            promptStartedAt.AddMinutes(3),
            "Show me Lyra's full plan",
            string.Empty,
            "Here is the plan.",
            true,
            Array.Empty<TranscriptToolRecord>());
        var localCommandTurn = new TranscriptTurnRecord(
            localCommandStartedAt,
            localCommandStartedAt.AddSeconds(5),
            "/tasks",
            string.Empty,
            "Current turn:\n• Coordinator [Working] - Still responding to the current prompt.",
            true,
            Array.Empty<TranscriptToolRecord>());

        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-123",
                promptStartedAt.AddMinutes(3),
                null,
                Array.Empty<string>(),
                [localCommandTurn, promptTurn]));

        var loaded = _store.Load(_workspacePath);
        var savedPrompts = saved.Turns.Select(turn => turn.Prompt).ToArray();
        var loadedPrompts = loaded.Turns.Select(turn => turn.Prompt).ToArray();

        Assert.Multiple(() => {
            Assert.That(savedPrompts, Is.EqualTo(new[] { "Show me Lyra's full plan", "/tasks" }));
            Assert.That(loadedPrompts, Is.EqualTo(new[] { "Show me Lyra's full plan", "/tasks" }));
        });
    }

    [Test]
    public void Save_ClearsExpiredSessionIdWhenNoTurnsRemain() {
        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-123",
                DateTimeOffset.UtcNow.AddDays(-20),
                null,
                Array.Empty<string>(),
                Array.Empty<TranscriptTurnRecord>()));

        Assert.Multiple(() => {
            Assert.That(saved.SessionId, Is.Null);
            Assert.That(saved.SessionUpdatedAt, Is.Null);
            Assert.That(saved.Turns, Is.Empty);
        });
    }

    [Test]
    public void SaveAndLoad_RoundTripsPersistedAgentThreads() {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var completedAt = startedAt.AddMinutes(3);

        _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-123",
                completedAt,
                null,
                Array.Empty<string>(),
                Array.Empty<TranscriptTurnRecord>(),
                [
                    new TranscriptThreadRecord(
                        " thread-1 ",
                        " Wanda Maximoff ",
                        " wanda-review-3 ",
                        " tool-123 ",
                        " wanda-review-3 ",
                        " Wanda Maximoff ",
                        " Review options page changes ",
                        " general-purpose ",
                        " wanda-maximoff ",
                        " Review the model options page changes. ",
                        " Looks solid overall. \r\n",
                        " Looks solid overall. ",
                        " Review options page changes ",
                        [" Tool execution completed ", " Waiting on code review "],
                        "  ",
                        " Completed ",
                        " Looks solid overall. ",
                        startedAt,
                        completedAt,
                        [
                            new TranscriptTurnRecord(
                                startedAt,
                                completedAt,
                                "Review options page changes",
                                string.Empty,
                                "Looks solid overall.",
                                true,
                                Array.Empty<TranscriptToolRecord>())
                        ])
                ]));

        var loaded = _store.Load(_workspacePath);
        var thread = loaded.GetThreads().Single();

        Assert.Multiple(() => {
            Assert.That(loaded.GetThreads(), Has.Count.EqualTo(1));
            Assert.That(thread.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(thread.Title, Is.EqualTo("Wanda Maximoff"));
            Assert.That(thread.AgentId, Is.EqualTo("wanda-review-3"));
            Assert.That(thread.ToolCallId, Is.EqualTo("tool-123"));
            Assert.That(thread.AgentDisplayName, Is.EqualTo("Wanda Maximoff"));
            Assert.That(thread.AgentCardKey, Is.EqualTo("wanda-maximoff"));
            Assert.That(thread.Prompt, Is.EqualTo("Review the model options page changes."));
            Assert.That(thread.LatestResponse, Is.EqualTo(" Looks solid overall."));
            Assert.That(thread.LastCoordinatorAnnouncedResponse, Is.EqualTo(" Looks solid overall."));
            Assert.That(thread.LatestIntent, Is.EqualTo("Review options page changes"));
            Assert.That(thread.RecentActivity, Is.EqualTo(new[] { "Tool execution completed", "Waiting on code review" }));
            Assert.That(thread.StatusText, Is.EqualTo("Completed"));
            Assert.That(thread.DetailText, Is.EqualTo("Looks solid overall."));
            Assert.That(thread.StartedAt, Is.EqualTo(startedAt.ToUniversalTime()));
            Assert.That(thread.CompletedAt, Is.EqualTo(completedAt.ToUniversalTime()));
            Assert.That(thread.Turns, Has.Count.EqualTo(1));
            Assert.That(thread.Turns[0].ResponseText, Is.EqualTo("Looks solid overall."));
        });
    }

    [Test]
    public void Load_ReconcilesIncompleteToolEntriesForCompletedAgentThreads() {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var completedAt = startedAt.AddMinutes(1);

        _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-123",
                completedAt,
                null,
                Array.Empty<string>(),
                Array.Empty<TranscriptTurnRecord>(),
                [
                    new TranscriptThreadRecord(
                        "thread-2",
                        "R2-D2",
                        "r2d2-session-recovery",
                        "tool-456",
                        "r2d2-session-recovery",
                        "R2-D2",
                        "Fix stale session after idle",
                        "worker",
                        "r2-d2",
                        "Fix stale session after idle",
                        "All set.",
                        "All set.",
                        "Fix stale session after idle",
                        Array.Empty<string>(),
                        null,
                        "Completed",
                        "All set.",
                        startedAt,
                        completedAt,
                        [
                            new TranscriptTurnRecord(
                                startedAt,
                                completedAt,
                                "Fix stale session after idle",
                                string.Empty,
                                "All set.",
                                true,
                                [
                                    new TranscriptToolRecord(
                                        "tool-report-intent",
                                        new ToolTranscriptDescriptor("report_intent", Intent: "Fixing quick reply keyboard bug"),
                                        "{\n  \"intent\": \"Fixing quick reply keyboard bug\"\n}",
                                        startedAt,
                                        null,
                                        null,
                                        null,
                                        null,
                                        IsCompleted: false,
                                        Success: false)
                                ])
                        ])
                ]));

        var loaded = _store.Load(_workspacePath);
        var tool = loaded.GetThreads().Single().Turns.Single().Tools.Single();

        Assert.Multiple(() => {
            Assert.That(tool.IsCompleted, Is.True);
            Assert.That(tool.Success, Is.True);
            Assert.That(tool.FinishedAt, Is.EqualTo(completedAt.ToUniversalTime()));
            Assert.That(tool.DetailContent, Does.Contain("Status: Succeeded"));
            Assert.That(tool.DetailContent, Does.Not.Contain("Status: Running"));
        });
    }

    [Test]
    public void Load_RestoresBackupWhenPrimaryConversationFileIsEmpty() {
        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-restore",
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                [
                    new TranscriptTurnRecord(
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow,
                        "status?",
                        string.Empty,
                        "Ready.",
                        false,
                        Array.Empty<TranscriptToolRecord>())
                ]));

        var workspaceDirectory = Directory.GetDirectories(Path.Combine(_rootPath, "store")).Single();
        var conversationPath = Path.Combine(workspaceDirectory, "conversation.json");
        var backupPath = conversationPath + ".bak";

        _store.Save(_workspacePath, saved with { PromptDraft = "updated" });
        File.WriteAllText(conversationPath, string.Empty);

        var loaded = _store.Load(_workspacePath);

        Assert.Multiple(() => {
            Assert.That(File.Exists(backupPath), Is.True);
            Assert.That(loaded.SessionId, Is.EqualTo("session-restore"));
            Assert.That(loaded.Turns, Has.Count.EqualTo(1));
            Assert.That(new FileInfo(conversationPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Load_RestoresBackupWhenPrimaryConversationStateIsAccidentallyEmpty() {
        var meaningful = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-restore",
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                [
                    new TranscriptTurnRecord(
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow,
                        "status?",
                        string.Empty,
                        "Ready.",
                        false,
                        Array.Empty<TranscriptToolRecord>())
                ]));

        var workspaceDirectory = Directory.GetDirectories(Path.Combine(_rootPath, "store")).Single();
        var conversationPath = Path.Combine(workspaceDirectory, "conversation.json");

        _store.Save(_workspacePath, meaningful with { PromptDraft = "updated" });
        File.WriteAllText(conversationPath, """
            {
              "SessionId": null,
              "SessionUpdatedAt": null,
              "PromptDraft": "",
              "PromptHistory": [],
              "Turns": [],
              "Threads": [],
              "RecentSessionIds": []
            }
            """);

        var loaded = _store.Load(_workspacePath);

        Assert.Multiple(() => {
            Assert.That(loaded.SessionId, Is.EqualTo("session-restore"));
            Assert.That(loaded.Turns, Has.Count.EqualTo(1));
            Assert.That(new FileInfo(conversationPath).Length, Is.GreaterThan(200));
        });
    }

    [Test]
    public void Load_DoesNotRestoreBackupWhenPrimaryStateWasExplicitlyCleared() {
        var meaningful = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-restore",
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                [
                    new TranscriptTurnRecord(
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow,
                        "status?",
                        string.Empty,
                        "Ready.",
                        false,
                        Array.Empty<TranscriptToolRecord>())
                ]));

        var workspaceDirectory = Directory.GetDirectories(Path.Combine(_rootPath, "store")).Single();
        var conversationPath = Path.Combine(workspaceDirectory, "conversation.json");

        _store.Save(_workspacePath, meaningful with { PromptDraft = "updated" });
        File.WriteAllText(conversationPath, $$"""
            {
              "SessionId": null,
              "SessionUpdatedAt": null,
              "PromptDraft": "",
              "PromptHistory": [],
              "Turns": [],
              "Threads": [],
              "RecentSessionIds": [],
              "ClearedAt": "{{DateTimeOffset.UtcNow:O}}"
            }
            """);

        var loaded = _store.Load(_workspacePath);

        Assert.Multiple(() => {
            Assert.That(loaded.Turns, Is.Empty);
            Assert.That(loaded.SessionId, Is.Null);
            Assert.That(loaded.ClearedAt, Is.Not.Null);
        });
    }

    [Test]
    public void Save_PreservesRecentSessionIdsEvenWhenCurrentSessionIsCleared() {
        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                null,
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                Array.Empty<TranscriptTurnRecord>(),
                RecentSessionIds: [" session-a ", "session-b", "session-a"]));

        Assert.That(saved.GetRecentSessionIds(), Is.EqualTo(new[] { "session-a", "session-b" }));
    }

    // ── QueuedPromptEntries (dictation flag) ──────────────────────────────────

    [Test]
    public void SaveAndLoad_RoundTripsQueuedPromptEntriesWithDictationFlags() {
        _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-q",
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                Array.Empty<TranscriptTurnRecord>()) {
                QueuedPromptEntries = [
                    new QueuedPromptEntry("typed prompt",    IsDictated: false),
                    new QueuedPromptEntry("dictated prompt", IsDictated: true)
                ]
            });

        var loaded = _store.Load(_workspacePath);
        var entries = loaded.QueuedPromptEntries;

        Assert.Multiple(() => {
            Assert.That(entries, Is.Not.Null);
            Assert.That(entries!, Has.Count.EqualTo(2));
            Assert.That(entries![0].Text,       Is.EqualTo("typed prompt"));
            Assert.That(entries![0].IsDictated, Is.False);
            Assert.That(entries![1].Text,       Is.EqualTo("dictated prompt"));
            Assert.That(entries![1].IsDictated, Is.True);
        });
    }

    [Test]
    public void Save_NullsQueuedPromptEntriesWhenListIsEmpty() {
        var saved = _store.Save(
            _workspacePath,
            new WorkspaceConversationState(
                "session-q",
                DateTimeOffset.UtcNow,
                null,
                Array.Empty<string>(),
                Array.Empty<TranscriptTurnRecord>()) {
                QueuedPromptEntries = []
            });

        Assert.That(saved.QueuedPromptEntries, Is.Null);
    }
}
