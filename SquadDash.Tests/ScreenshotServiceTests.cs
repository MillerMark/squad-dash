using System;
using System.IO;
using NUnit.Framework;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
public sealed class ScreenshotServiceTests
{
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── ExtractDocImageDescription ─────────────────────────────────────────────

    [Test]
    public void ExtractDocImageDescription_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var result = ScreenshotService.ExtractDocImageDescription(
            Path.Combine(_workspace.RootPath, "nonexistent.md"),
            "images/screenshot.png");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ExtractDocImageDescription_ReturnsBlockquoteDescription_WhenPresentAfterImageLine()
    {
        _workspace.CreateFile("doc.md",
            "Some text\n" +
            "![My Alt](images/screenshot.png)\n" +
            "> 📸 *Screenshot needed: the main dashboard view*\n" +
            "More text\n");

        var result = ScreenshotService.ExtractDocImageDescription(
            _workspace.GetPath("doc.md"),
            "images/screenshot.png");

        Assert.That(result, Is.EqualTo("the main dashboard view"));
    }

    [Test]
    public void ExtractDocImageDescription_FallsBackToAltText_WhenNoBlockquote()
    {
        _workspace.CreateFile("doc.md",
            "Some text\n" +
            "![Main Dashboard](images/screenshot.png)\n" +
            "More text\n");

        var result = ScreenshotService.ExtractDocImageDescription(
            _workspace.GetPath("doc.md"),
            "images/screenshot.png");

        Assert.That(result, Is.EqualTo("Main Dashboard"));
    }

    [Test]
    public void ExtractDocImageDescription_ReturnsEmpty_WhenImagePathNotFound()
    {
        _workspace.CreateFile("doc.md",
            "![Alt](images/other.png)\n");

        var result = ScreenshotService.ExtractDocImageDescription(
            _workspace.GetPath("doc.md"),
            "images/screenshot.png");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ExtractDocImageDescription_HandlesBackslashImagePath()
    {
        _workspace.CreateFile("doc.md",
            "![Windows Path](images/screenshot.png)\n" +
            "> 📸 *Screenshot needed: windows path test*\n");

        // Pass with backslash — should still match the forward-slash in markdown.
        var result = ScreenshotService.ExtractDocImageDescription(
            _workspace.GetPath("doc.md"),
            @"images\screenshot.png");

        Assert.That(result, Is.EqualTo("windows path test"));
    }

    // ── Constructor guard clauses ──────────────────────────────────────────────

    [Test]
    public void Constructor_ThrowsArgumentNull_WhenWorkspacePathsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ScreenshotService(
                null!,
                new Screenshots.UiActionReplayRegistry(),
                new Screenshots.FixtureLoaderRegistry(),
                new FakeLiveElementLocator()));
    }

    [Test]
    public void Constructor_ThrowsArgumentNull_WhenElementLocatorIsNull()
    {
        var paths = new WorkspacePathsProvider(_workspace.RootPath);
        Assert.Throws<ArgumentNullException>(() =>
            new ScreenshotService(
                paths,
                new Screenshots.UiActionReplayRegistry(),
                new Screenshots.FixtureLoaderRegistry(),
                null!));
    }

    // ── WarmDefinitionRegistryCacheAsync ───────────────────────────────────────

    [Test]
    public async Task WarmDefinitionRegistryCacheAsync_PopulatesRegistry_WhenDirectoryExists()
    {
        // Ensure ScreenshotsDirectory exists (LoadAsync creates it if absent, but must be resolvable)
        Directory.CreateDirectory(_workspace.GetPath("docs", "screenshots"));

        var paths = new WorkspacePathsProviderStub(_workspace.RootPath);
        var service = new ScreenshotService(
            paths,
            new Screenshots.UiActionReplayRegistry(),
            new Screenshots.FixtureLoaderRegistry(),
            new FakeLiveElementLocator());

        await service.WarmDefinitionRegistryCacheAsync();

        Assert.That(service.CachedDefinitionRegistry, Is.Not.Null);
    }

    [Test]
    public async Task WarmDefinitionRegistryCacheAsync_PopulatesHealthChecker()
    {
        Directory.CreateDirectory(_workspace.GetPath("docs", "screenshots"));

        var paths = new WorkspacePathsProviderStub(_workspace.RootPath);
        var service = new ScreenshotService(
            paths,
            new Screenshots.UiActionReplayRegistry(),
            new Screenshots.FixtureLoaderRegistry(),
            new FakeLiveElementLocator());

        await service.WarmDefinitionRegistryCacheAsync();

        Assert.That(service.HealthChecker, Is.Not.Null);
    }

    // ── Stubs ──────────────────────────────────────────────────────────────────

    private sealed class FakeLiveElementLocator : Screenshots.ILiveElementLocator
    {
        public System.Windows.FrameworkElement? FindByName(string name) => null;
        public System.Windows.Rect GetBoundsRelativeToWindow(System.Windows.FrameworkElement element)
            => System.Windows.Rect.Empty;
        public bool IsVisible(System.Windows.FrameworkElement element) => false;
    }

    /// <summary>Maps ScreenshotsDirectory to docs/screenshots under the test root.</summary>
    private sealed class WorkspacePathsProviderStub : IWorkspacePaths
    {
        private readonly string _root;
        public WorkspacePathsProviderStub(string root) => _root = root;
        public string ApplicationRoot         => _root;
        public string SquadSdkDirectory       => Path.Combine(_root, "Squad.SDK");
        public string RunRootDirectory        => Path.Combine(_root, "Run");
        public string AgentImageAssetsDirectory => Path.Combine(_root, "Assets");
        public string RoleIconAssetsDirectory => Path.Combine(_root, "Assets", "Roles");
        public string ScreenshotsDirectory    => Path.Combine(_root, "docs", "screenshots");
    }
}
