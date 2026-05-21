using System.Threading;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DocTopicsLoaderTests {
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private string CreateDocsFolder() {
        var docs = Path.Combine(_workspace.RootPath, "docs");
        Directory.CreateDirectory(docs);
        return docs;
    }

    private string CreateFile(string fullPath, string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // ── FindDocsFolderPath ──────────────────────────────────────────────────

    [Test]
    public void FindDocsFolderPath_WithWorkspaceFolder_DocsExists_ReturnsDocsPath() {
        var docs = CreateDocsFolder();

        var result = DocTopicsLoader.FindDocsFolderPath(_workspace.RootPath);

        Assert.That(result, Is.EqualTo(docs));
    }

    [Test]
    public void FindDocsFolderPath_WithWorkspaceFolder_DocsNotExists_ReturnsNull() {
        var result = DocTopicsLoader.FindDocsFolderPath(_workspace.RootPath);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindDocsFolderPath_NullWorkspaceFolder_ReturnsNullOrString() {
        string? result = null;
        Assert.DoesNotThrow(() => result = DocTopicsLoader.FindDocsFolderPath(null));
    }

    // ── ExtractMarkdownTitle ────────────────────────────────────────────────

    [Test]
    public void ExtractMarkdownTitle_FileWithH1_ReturnsTitle() {
        var path = CreateFile(Path.Combine(_workspace.RootPath, "doc.md"),
            "# Getting Started\n\nSome content.\n");

        var result = DocTopicsLoader.ExtractMarkdownTitle(path);

        Assert.That(result, Is.EqualTo("Getting Started"));
    }

    [Test]
    public void ExtractMarkdownTitle_FileWithNoH1_ReturnsNull() {
        var path = CreateFile(Path.Combine(_workspace.RootPath, "doc.md"),
            "## Section Two\n\nContent.\n");

        var result = DocTopicsLoader.ExtractMarkdownTitle(path);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractMarkdownTitle_EmptyFile_ReturnsNull() {
        var path = CreateFile(Path.Combine(_workspace.RootPath, "doc.md"), string.Empty);

        var result = DocTopicsLoader.ExtractMarkdownTitle(path);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractMarkdownTitle_FileNotFound_ReturnsNull() {
        var missing = Path.Combine(_workspace.RootPath, "nonexistent.md");

        var result = DocTopicsLoader.ExtractMarkdownTitle(missing);

        Assert.That(result, Is.Null);
    }

    // ── LoadTopics (WPF — STA) ──────────────────────────────────────────────

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoadTopics_WithSummaryMd_AddsExpectedItems() {
        var docs = CreateDocsFolder();
        CreateFile(Path.Combine(docs, "intro.md"), "# Introduction\n\nContent.\n");
        CreateFile(Path.Combine(docs, "guide.md"), "# User Guide\n\nContent.\n");
        CreateFile(Path.Combine(docs, "SUMMARY.md"),
            "* [Introduction](intro.md)\n  * [User Guide](guide.md)\n");

        var treeView = new TreeView();
        DocTopicsLoader.LoadTopics(treeView, out _, _workspace.RootPath);

        Assert.That(treeView.Items.Count, Is.GreaterThan(0));
        var topItem = (TreeViewItem)treeView.Items[0];
        Assert.That(topItem.Tag?.ToString(), Does.Contain("intro.md"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoadTopics_WithFolderScan_AddsExpectedItems() {
        var docs = CreateDocsFolder();
        var subDir = Path.Combine(docs, "getting-started");
        Directory.CreateDirectory(subDir);
        CreateFile(Path.Combine(subDir, "overview.md"), "# Overview\n\nContent.\n");
        CreateFile(Path.Combine(subDir, "setup.md"), "# Setup\n\nContent.\n");

        var treeView = new TreeView();
        DocTopicsLoader.LoadTopics(treeView, out var firstItem, _workspace.RootPath);

        Assert.Multiple(() => {
            Assert.That(treeView.Items.Count, Is.GreaterThan(0));
            var parentItem = (TreeViewItem)treeView.Items[0];
            Assert.That(parentItem.Items.Count, Is.EqualTo(2));
            Assert.That(firstItem, Is.Not.Null);
        });
    }
}
