using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CommitApprovalStoreTests {
    private TestWorkspace _workspace = null!;
    private CommitApprovalStore _store = null!;

    [SetUp]
    public void SetUp() {
        _workspace = new TestWorkspace();
        _store = new CommitApprovalStore(_workspace.RootPath);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void Load_FileNotPresent_ReturnsEmptyList() {
        var result = _store.Load();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Load_EmptyJsonArray_ReturnsEmptyList() {
        File.WriteAllText(Path.Combine(_workspace.RootPath, "commit-approvals.json"), "[]");

        var result = _store.Load();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Load_CorruptJson_ReturnsEmptyList() {
        File.WriteAllText(Path.Combine(_workspace.RootPath, "commit-approvals.json"), "{ not valid json [[[");

        var result = _store.Load();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Load_ValidJson_ReturnsDeserializedItems() {
        var item = CommitApprovalItem.Create(
            "abc123", "https://github.com/sha/abc123",
            "Fix bug", DateTimeOffset.UtcNow, "some hint", null);
        _store.Save([item]);

        var result = _store.Load();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CommitSha, Is.EqualTo("abc123"));
            Assert.That(result[0].Description, Is.EqualTo("Fix bug"));
            Assert.That(result[0].TurnPromptHint, Is.EqualTo("some hint"));
            Assert.That(result[0].IsApproved, Is.False);
        });
    }

    [Test]
    public void Load_MoreThanMaxItems_CapsAtMaxKeepingNewest() {
        var baseTime = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(1, 201)
            .Select(i => CommitApprovalItem.Create(
                $"sha{i:D3}", null, $"commit {i}",
                baseTime.AddMinutes(-i), null, null))
            .ToList();
        _store.Save(items);

        var result = _store.Load();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(200));
            Assert.That(result.Any(r => r.CommitSha == "sha201"), Is.False);
            Assert.That(result.All(r => r.CommitSha != "sha201"), Is.True);
        });
    }

    [Test]
    public void Save_ThenLoad_RoundTripsItems() {
        var turnAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var item = new CommitApprovalItem(
            "idabc", "deadbeef", "https://example.com",
            "Refactor", turnAt, "hint text", IsApproved: true,
            "original prompt", IsRejected: false);

        _store.Save([item]);
        var result = _store.Load();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Id, Is.EqualTo("idabc"));
            Assert.That(result[0].CommitSha, Is.EqualTo("deadbeef"));
            Assert.That(result[0].CommitUrl, Is.EqualTo("https://example.com"));
            Assert.That(result[0].Description, Is.EqualTo("Refactor"));
            Assert.That(result[0].TurnStartedAt, Is.EqualTo(turnAt));
            Assert.That(result[0].TurnPromptHint, Is.EqualTo("hint text"));
            Assert.That(result[0].IsApproved, Is.True);
            Assert.That(result[0].OriginalPrompt, Is.EqualTo("original prompt"));
        });
    }

    [Test]
    public void Save_CreatesFileInStateDirectory() {
        var item = CommitApprovalItem.Create("sha1", null, "desc", DateTimeOffset.UtcNow, null, null);

        _store.Save([item]);

        Assert.That(File.Exists(Path.Combine(_workspace.RootPath, "commit-approvals.json")), Is.True);
    }
}
