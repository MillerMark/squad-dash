namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentReportStoreTests {
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── FormatReportTitle ────────────────────────────────────────────────────

    [Test]
    public void FormatReportTitle_KnownAgent_ReturnsPossessiveTitle() {
        Assert.That(AgentReportStore.FormatReportTitle("Orion Vale"), Is.EqualTo("Orion Vale's Report"));
    }

    [Test]
    public void FormatReportTitle_AdHocAgent_ReturnsPlainTitle() {
        Assert.That(AgentReportStore.FormatReportTitle("Explore Tinting"), Is.EqualTo("Explore Tinting Report"));
    }

    // ── IsKnownAgent ─────────────────────────────────────────────────────────

    [Test]
    public void IsKnownAgent_KnownName_ReturnsTrue() {
        Assert.That(AgentReportStore.IsKnownAgent("Vesper Knox"), Is.True);
    }

    [Test]
    public void IsKnownAgent_UnknownName_ReturnsFalse() {
        Assert.That(AgentReportStore.IsKnownAgent("Gandalf"), Is.False);
    }

    [Test]
    public void IsKnownAgent_MatchesCaseInsensitively() {
        Assert.That(AgentReportStore.IsKnownAgent("orion vale"), Is.True);
    }

    // ── Store (also exercises SanitizeForFileName) ───────────────────────────

    [Test]
    public void Store_WritesFileWithExpectedContent_WithHeader() {
        var ts = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var path = AgentReportStore.Store(
            reportsDir: _workspace.RootPath,
            agentLabel: "Orion Vale",
            header: "Summary line",
            body: "Body text.",
            timestamp: ts);

        var content = File.ReadAllText(path);
        Assert.Multiple(() => {
            Assert.That(content, Does.StartWith("# Orion Vale's Report"));
            Assert.That(content, Does.Contain("Summary line"));
            Assert.That(content, Does.Contain("---"));
            Assert.That(content, Does.Contain("Body text."));
        });
    }

    [Test]
    public void Store_WritesFileWithoutHeaderBlock_WhenHeaderIsEmpty() {
        var ts = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var path = AgentReportStore.Store(
            reportsDir: _workspace.RootPath,
            agentLabel: "Orion Vale",
            header: "",
            body: "Body only.",
            timestamp: ts);

        var content = File.ReadAllText(path);
        Assert.That(content, Does.Not.Contain("---"));
    }

    [Test]
    public void Store_ReturnsPath_WithSanitizedAgentLabel() {
        var ts = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var path = AgentReportStore.Store(
            reportsDir: _workspace.RootPath,
            agentLabel: "Foo / Bar",
            header: "",
            body: "x",
            timestamp: ts);

        var fileName = Path.GetFileName(path);
        // spaces → '-', '/' is an invalid file-name char → '_'
        Assert.That(fileName, Does.StartWith("Foo-_-Bar-"));
    }

    [Test]
    public void Store_TruncatesFileName_WhenLabelExceeds40Chars() {
        var ts    = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var label = new string('A', 60);

        var path     = AgentReportStore.Store(_workspace.RootPath, label, "", "x", ts);
        var fileName = Path.GetFileNameWithoutExtension(path);
        // Format is "{sanitized}-{unixMs}". The sanitized portion must be ≤ 40 chars.
        var sanitizedPart = fileName[..fileName.LastIndexOf('-')];
        Assert.That(sanitizedPart.Length, Is.LessThanOrEqualTo(40));
    }

    // ── PruneOld ─────────────────────────────────────────────────────────────

    [Test]
    public void PruneOld_DeletesExpiredFiles_KeepsRecentOnes() {
        var oldFile    = Path.Combine(_workspace.RootPath, "old.md");
        var recentFile = Path.Combine(_workspace.RootPath, "recent.md");
        File.WriteAllText(oldFile,    "old");
        File.WriteAllText(recentFile, "recent");

        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));

        AgentReportStore.PruneOld(_workspace.RootPath, maxAge: TimeSpan.FromDays(14));

        Assert.Multiple(() => {
            Assert.That(File.Exists(oldFile),    Is.False, "old file should be deleted");
            Assert.That(File.Exists(recentFile), Is.True,  "recent file should be kept");
        });
    }

    [Test]
    public void PruneOld_DirectoryMissing_DoesNotThrow() {
        var missing = Path.Combine(_workspace.RootPath, "nonexistent");
        Assert.DoesNotThrow(() => AgentReportStore.PruneOld(missing));
    }

    // ── ClearAll ─────────────────────────────────────────────────────────────

    [Test]
    public void ClearAll_RemovesAllMdFiles() {
        File.WriteAllText(Path.Combine(_workspace.RootPath, "a.md"), "a");
        File.WriteAllText(Path.Combine(_workspace.RootPath, "b.md"), "b");
        File.WriteAllText(Path.Combine(_workspace.RootPath, "c.md"), "c");

        AgentReportStore.ClearAll(_workspace.RootPath);

        Assert.That(Directory.EnumerateFiles(_workspace.RootPath, "*.md"), Is.Empty);
    }

    [Test]
    public void ClearAll_DirectoryMissing_DoesNotThrow() {
        var missing = Path.Combine(_workspace.RootPath, "nonexistent");
        Assert.DoesNotThrow(() => AgentReportStore.ClearAll(missing));
    }
}
