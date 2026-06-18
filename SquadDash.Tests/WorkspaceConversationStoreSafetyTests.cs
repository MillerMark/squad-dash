using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceConversationStoreSafetyTests {
    [Test]
    public void Save_DoesNotOverwriteNonEmptyConversationWithEmptyState() {
        using var workspace = new TestWorkspace();
        var store = new WorkspaceConversationStore(workspace.GetPath("state"));
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var populated = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            "draft",
            new[] { "prompt" },
            new[] {
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow,
                    "prompt",
                    "thinking",
                    "response",
                    false,
                    Array.Empty<TranscriptToolRecord>())
            });

        store.Save(repo, populated);
        var preserved = store.Save(repo, WorkspaceConversationState.Empty);

        Assert.That(preserved.Turns, Has.Count.EqualTo(1));
        Assert.That(preserved.PromptDraft, Is.EqualTo("draft"));
    }

    [Test]
    public void Save_CreatesBackupWhenMeaningfulConversationExists() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var state = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            "draft",
            new[] { "prompt" },
            new[] {
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow,
                    "prompt",
                    "thinking",
                    "response",
                    false,
                    Array.Empty<TranscriptToolRecord>())
            });

        store.Save(repo, state);
        store.Save(repo, state with { PromptDraft = "updated draft" });

        var workspaceDirectory = Directory.GetDirectories(root).Single();
        var backupPath = Path.Combine(workspaceDirectory, "conversation.json.bak");

        Assert.That(File.Exists(backupPath), Is.True);

        var backup = JsonSerializer.Deserialize<WorkspaceConversationState>(File.ReadAllText(backupPath));
        Assert.That(backup, Is.Not.Null);
        Assert.That(backup!.Turns, Has.Count.EqualTo(1));
    }

    [Test]
    public void Save_ExplicitClearCreatesTimestampedRescueBackup() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var state = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            Array.Empty<string>(),
            new[] {
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow,
                    "prompt",
                    "thinking",
                    "response",
                    false,
                    Array.Empty<TranscriptToolRecord>())
            });

        store.Save(repo, state);
        var cleared = store.Save(
            repo,
            WorkspaceConversationState.Empty with {
                ClearedAt = DateTimeOffset.UtcNow
            });

        var workspaceDirectory = Directory.GetDirectories(root).Single();
        var rescueFiles = Directory.GetFiles(workspaceDirectory, "conversation.explicit-clear.*.json");
        var rescue = JsonSerializer.Deserialize<WorkspaceConversationState>(File.ReadAllText(rescueFiles.Single()));

        Assert.Multiple(() => {
            Assert.That(cleared.Turns, Is.Empty);
            Assert.That(cleared.ClearedAt, Is.Not.Null);
            Assert.That(rescueFiles, Has.Length.EqualTo(1));
            Assert.That(rescue, Is.Not.Null);
            Assert.That(rescue!.SessionId, Is.EqualTo("session-1"));
            Assert.That(rescue.Turns, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Save_DoesNotOverwriteRealTranscriptWithOnlySessionBoundary() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var populated = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            null,
            Array.Empty<string>(),
            new[] {
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow.AddMinutes(-2),
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    "prompt",
                    "thinking",
                    "response",
                    false,
                    Array.Empty<TranscriptToolRecord>())
            });
        var boundaryOnly = new WorkspaceConversationState(
            null,
            null,
            null,
            Array.Empty<string>(),
            new[] {
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    true,
                    Array.Empty<TranscriptToolRecord>()) {
                    IsSessionBoundary = true
                }
            });

        store.Save(repo, populated);
        var preserved = store.Save(repo, boundaryOnly);

        var workspaceDirectory = Directory.GetDirectories(root).Single();
        var rescueFiles = Directory.GetFiles(workspaceDirectory, "conversation.durable-drop.*.json");

        Assert.Multiple(() => {
            Assert.That(preserved.Turns, Has.Count.EqualTo(1));
            Assert.That(preserved.Turns[0].IsSessionBoundary, Is.False);
            Assert.That(rescueFiles, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void Load_RestoresAtomicReplaceTempWhenPrimaryConversationStateIsAccidentallyEmpty() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var workspaceDirectory = store.GetWorkspaceStateDirectory(repo);
        Directory.CreateDirectory(workspaceDirectory);
        var primaryPath = Path.Combine(workspaceDirectory, "conversation.json");
        var backupPath = primaryPath + ".bak";
        var replaceTempPath = Path.Combine(workspaceDirectory, "conversation.json~RF3b379b.TMP");

        var empty = WorkspaceConversationState.Empty;
        var lessCompleteBackup = new WorkspaceConversationState(
            null,
            null,
            null,
            new[] { "old prompt" },
            Array.Empty<TranscriptTurnRecord>());
        var recovered = CreateCrashRecoveryState("session-recovered", turnCount: 2, threadCount: 1);

        File.WriteAllText(primaryPath, JsonSerializer.Serialize(empty));
        File.WriteAllText(backupPath, JsonSerializer.Serialize(lessCompleteBackup));
        File.WriteAllText(replaceTempPath, JsonSerializer.Serialize(recovered));
        File.SetLastWriteTimeUtc(primaryPath, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(backupPath, DateTime.UtcNow.AddSeconds(-5));
        File.SetLastWriteTimeUtc(replaceTempPath, DateTime.UtcNow.AddSeconds(-10));

        var loaded = store.Load(repo);

        Assert.Multiple(() => {
            Assert.That(loaded.SessionId, Is.EqualTo("session-recovered"));
            Assert.That(loaded.Turns, Has.Count.EqualTo(2));
            Assert.That(loaded.GetThreads(), Has.Count.EqualTo(1));
            Assert.That(new FileInfo(primaryPath).Length, Is.GreaterThan(500));
        });
    }

    [Test]
    public void Save_BlocksEmptyOverwriteUsingAtomicReplaceTempRecoveryCandidate() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var workspaceDirectory = store.GetWorkspaceStateDirectory(repo);
        Directory.CreateDirectory(workspaceDirectory);
        var primaryPath = Path.Combine(workspaceDirectory, "conversation.json");
        var replaceTempPath = Path.Combine(workspaceDirectory, "conversation.json~RF3b379b.TMP");

        var recovered = CreateCrashRecoveryState("session-recovered", turnCount: 1, threadCount: 1);

        File.WriteAllText(primaryPath, JsonSerializer.Serialize(WorkspaceConversationState.Empty));
        File.WriteAllText(replaceTempPath, JsonSerializer.Serialize(recovered));
        File.SetLastWriteTimeUtc(primaryPath, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(replaceTempPath, DateTime.UtcNow.AddSeconds(-5));

        var preserved = store.Save(repo, WorkspaceConversationState.Empty);

        var primary = JsonSerializer.Deserialize<WorkspaceConversationState>(File.ReadAllText(primaryPath));
        Assert.Multiple(() => {
            Assert.That(preserved.SessionId, Is.EqualTo("session-recovered"));
            Assert.That(preserved.Turns, Has.Count.EqualTo(1));
            Assert.That(primary, Is.Not.Null);
            Assert.That(primary!.SessionId, Is.EqualTo("session-recovered"));
            Assert.That(Directory.GetFiles(workspaceDirectory, "conversation.empty-overwrite.*.json"), Has.Length.EqualTo(1));
        });
    }

    // ── Corrupt-JSON fallback (TryLoadState silent catch) ─────────────────────

    [Test]
    public void Load_WhenPrimaryJsonCorrupt_FallsBackToBakFile() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var turn = new TranscriptTurnRecord(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            "prompt",
            "thinking",
            "response",
            false,
            Array.Empty<TranscriptToolRecord>());

        var stateA = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            "original draft",
            new[] { "prompt" },
            new[] { turn });

        // First save creates conversation.json with stateA.
        store.Save(repo, stateA);
        // Second save promotes stateA to .bak and writes stateB to conversation.json.
        store.Save(repo, stateA with { PromptDraft = "updated draft" });

        // Corrupt the primary file.
        var workspaceDirectory = Directory.GetDirectories(root).Single();
        var primaryPath = Path.Combine(workspaceDirectory, "conversation.json");
        File.WriteAllText(primaryPath, "!! not valid json !!");

        var result = store.Load(repo);

        Assert.Multiple(() => {
            Assert.That(result.Turns, Has.Count.EqualTo(1));
            Assert.That(result.PromptDraft, Is.EqualTo("original draft"));
        });
    }

    [Test]
    public void Load_WhenPrimaryJsonCorrupt_AndNoBak_ReturnsEmpty() {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath("state");
        var store = new WorkspaceConversationStore(root);
        var repo = workspace.GetPath("repo");
        Directory.CreateDirectory(repo);

        var stateA = new WorkspaceConversationState(
            "session-1",
            DateTimeOffset.UtcNow,
            "draft",
            new[] { "prompt" },
            new[] {
                new TranscriptTurnRecord(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow,
                    "prompt",
                    "thinking",
                    "response",
                    false,
                    Array.Empty<TranscriptToolRecord>())
            });

        // Single save: produces conversation.json but no .bak.
        store.Save(repo, stateA);

        var workspaceDirectory = Directory.GetDirectories(root).Single();
        var primaryPath = Path.Combine(workspaceDirectory, "conversation.json");
        File.WriteAllText(primaryPath, "!! not valid json !!");

        var result = store.Load(repo);

        Assert.That(result.Turns, Is.Empty);
    }

    private static WorkspaceConversationState CreateCrashRecoveryState(
        string sessionId,
        int turnCount,
        int threadCount) {
        var now = DateTimeOffset.UtcNow;
        var turns = Enumerable.Range(0, turnCount)
            .Select(index => new TranscriptTurnRecord(
                now.AddMinutes(-10 + index),
                now.AddMinutes(-9 + index),
                $"prompt {index}",
                string.Empty,
                $"response {index}",
                false,
                Array.Empty<TranscriptToolRecord>()))
            .ToArray();

        var threads = Enumerable.Range(0, threadCount)
            .Select(index => new TranscriptThreadRecord(
                $"thread-{index}",
                $"Agent {index}",
                null,
                $"tool-{index}",
                "agent",
                $"Agent {index}",
                null,
                "general-purpose",
                $"agent-{index}",
                $"agent prompt {index}",
                null,
                null,
                null,
                Array.Empty<string>(),
                null,
                "Running",
                string.Empty,
                now.AddMinutes(-8 + index),
                null,
                new[] {
                    new TranscriptTurnRecord(
                        now.AddMinutes(-8 + index),
                        null,
                        $"agent prompt {index}",
                        string.Empty,
                        string.Empty,
                        false,
                        Array.Empty<TranscriptToolRecord>())
                }))
            .ToArray();

        return new WorkspaceConversationState(
            sessionId,
            now,
            null,
            new[] { "recent prompt" },
            turns,
            threads,
            new[] { sessionId });
    }
}
