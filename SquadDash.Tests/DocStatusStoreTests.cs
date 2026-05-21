namespace SquadDash.Tests;

[TestFixture]
internal sealed class DocStatusStoreTests {
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private string CreateDocFile(string relativePath, string content = "# Title") {
        var fullPath = Path.Combine(_workspace.RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Test]
    public void Load_FileNotPresent_ReturnsStoreWithNeedsReviewStatus() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        var status = store.GetStatus(filePath);

        Assert.That(status, Is.EqualTo(DocApprovalStatus.NeedsReview));
    }

    [Test]
    public void Load_CorruptJson_ReturnsEmptyStore() {
        File.WriteAllText(Path.Combine(_workspace.RootPath, ".doc-status.json"), "not json {{{{");
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        var status = store.GetStatus(filePath);

        Assert.That(status, Is.EqualTo(DocApprovalStatus.NeedsReview));
    }

    [Test]
    public void SetApproved_ThenGetStatus_ReturnsApproved() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        store.SetApproved(filePath);

        Assert.That(store.GetStatus(filePath), Is.EqualTo(DocApprovalStatus.Approved));
    }

    [Test]
    public void SetNeedsReview_ThenGetStatus_ReturnsNeedsReview() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        store.SetNeedsReview(filePath);

        Assert.That(store.GetStatus(filePath), Is.EqualTo(DocApprovalStatus.NeedsReview));
    }

    [Test]
    public void SetApproved_ThenSetNeedsReview_StatusIsNeedsReview() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        store.SetApproved(filePath);
        store.SetNeedsReview(filePath);

        Assert.That(store.GetStatus(filePath), Is.EqualTo(DocApprovalStatus.NeedsReview));
    }

    [Test]
    public void HasBeenTracked_NeverSet_ReturnsFalse() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        Assert.That(store.HasBeenTracked(filePath), Is.False);
    }

    [Test]
    public void HasBeenTracked_AfterSetApproved_ReturnsTrue() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        store.SetApproved(filePath);

        Assert.That(store.HasBeenTracked(filePath), Is.True);
    }

    [Test]
    public void HasBeenTracked_AfterSetNeedsReview_ReturnsTrue() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        store.SetNeedsReview(filePath);

        Assert.That(store.HasBeenTracked(filePath), Is.True);
    }

    [Test]
    public void GetKey_IsCaseInsensitive() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("SubDir\\Guide.md");

        store.SetApproved(filePath);

        var differentCasingPath = Path.Combine(_workspace.RootPath, "subdir", "guide.md");
        Assert.That(store.GetStatus(differentCasingPath), Is.EqualTo(DocApprovalStatus.Approved));
    }

    [Test]
    public void SetApproved_PersistsToJson() {
        var store = DocStatusStore.Load(_workspace.RootPath);
        var filePath = CreateDocFile("guide.md");

        store.SetApproved(filePath);

        Assert.That(File.Exists(Path.Combine(_workspace.RootPath, ".doc-status.json")), Is.True);
    }

    [Test]
    public void Load_ReloadAfterSave_RestoresApprovedStatus() {
        var filePath = CreateDocFile("guide.md");
        var store1 = DocStatusStore.Load(_workspace.RootPath);
        store1.SetApproved(filePath);

        var store2 = DocStatusStore.Load(_workspace.RootPath);

        Assert.That(store2.GetStatus(filePath), Is.EqualTo(DocApprovalStatus.Approved));
    }

    [Test]
    public void HasScreenshotPlaceholders_NoPlaceholders_ReturnsFalse() {
        var filePath = CreateDocFile("guide.md", "# Guide\n\nSome content here.\n");

        Assert.That(DocStatusStore.HasScreenshotPlaceholders(filePath), Is.False);
    }

    [Test]
    public void HasScreenshotPlaceholders_WithPlaceholder_ReturnsTrue() {
        var filePath = CreateDocFile("guide.md", "# Guide\n\n![Screenshot: main window](placeholder)\n");

        Assert.That(DocStatusStore.HasScreenshotPlaceholders(filePath), Is.True);
    }

    [Test]
    public void HasScreenshotPlaceholders_FileNotFound_ReturnsFalse() {
        var missingPath = Path.Combine(_workspace.RootPath, "does-not-exist.md");

        Assert.That(DocStatusStore.HasScreenshotPlaceholders(missingPath), Is.False);
    }

    // ── Save() silent-catch contract ──────────────────────────────────────────

    [Test]
    public void SetApproved_WhenSaveFails_DoesNotThrow() {
        // Reproduce the Save() silent-catch contract: an I/O failure must never
        // propagate out of SetApproved / SetNeedsReview.
        var store = DocStatusStore.Load(_workspace.RootPath);
        var firstFile = CreateDocFile("first.md");

        // First save succeeds and writes .doc-status.json.
        store.SetApproved(firstFile);

        // Make the JSON file read-only so the next write throws UnauthorizedAccessException.
        var jsonPath = Path.Combine(_workspace.RootPath, ".doc-status.json");
        File.SetAttributes(jsonPath, FileAttributes.ReadOnly);

        var secondFile = CreateDocFile("second.md");

        // The catch block must absorb the failure — no exception should escape.
        Assert.DoesNotThrow(() => store.SetApproved(secondFile));

        // Restore so TearDown can delete the temp directory.
        File.SetAttributes(jsonPath, FileAttributes.Normal);
    }
}
