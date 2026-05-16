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
}
