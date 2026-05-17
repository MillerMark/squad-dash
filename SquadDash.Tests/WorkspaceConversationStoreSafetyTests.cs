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
}
