using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SquadDash.Screenshots;

namespace SquadDash.Tests;

[TestFixture]
public class ScreenshotRefreshRunnerTests
{
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ScreenshotRefreshRunner MakeRunner(
        ScreenshotDefinitionRegistry registry, string screenshotsDir) =>
        new(registry, new UiActionReplayRegistry(), new FixtureLoaderRegistry(), screenshotsDir);

    private static async Task<ScreenshotDefinitionRegistry> EmptyRegistryAsync(string dir) =>
        await ScreenshotDefinitionRegistry.LoadAsync(dir);

    private static EdgeAnchorRecord EmptyAnchor(string edge) =>
        new(edge, [], NeedsName: true, 0, 0, 0, 0, 0);

    private static ScreenshotDefinition MakeDef(string name, string theme) =>
        new(name, "desc", theme, null, null,
            EmptyAnchor("Top"), EmptyAnchor("Right"),
            EmptyAnchor("Bottom"), EmptyAnchor("Left"),
            new CaptureBounds(0, 0, 100, 100, 1, 1));

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task RunAsync_ModeNone_CompletesWithoutRaisingCapture()
    {
        var registry = await EmptyRegistryAsync(_workspace.RootPath);
        var runner   = MakeRunner(registry, _workspace.RootPath);
        var captured = false;
        runner.CaptureRequested += (_, _) => captured = true;

        await runner.RunAsync(ScreenshotRefreshOptions.None);

        Assert.That(captured, Is.False, "CaptureRequested should not fire when Mode is None.");
    }

    [Test]
    public async Task RunAsync_AllMode_EmptyRegistry_CompletesWithoutRaisingCapture()
    {
        var registry = await EmptyRegistryAsync(_workspace.RootPath);
        var runner   = MakeRunner(registry, _workspace.RootPath);
        var captured = false;
        runner.CaptureRequested += (_, _) => captured = true;

        await runner.RunAsync(new ScreenshotRefreshOptions(ScreenshotRefreshMode.All, null));

        Assert.That(captured, Is.False,
            "CaptureRequested should not fire when the definition registry is empty.");
    }

    [Test]
    public async Task RunAsync_NamedMode_UnknownDefinition_CompletesWithoutRaisingCapture()
    {
        var registry = await EmptyRegistryAsync(_workspace.RootPath);
        var runner   = MakeRunner(registry, _workspace.RootPath);
        var captured = false;
        runner.CaptureRequested += (_, _) => captured = true;

        await runner.RunAsync(
            new ScreenshotRefreshOptions(ScreenshotRefreshMode.Named, "nonexistent-definition"));

        Assert.That(captured, Is.False,
            "CaptureRequested should not fire when the named definition is not registered.");
    }

    [Test]
    public async Task RunAsync_BothTheme_WithThemeSwitcher_CapturesTwiceWithCorrectPaths()
    {
        // Arrange
        var registry = await EmptyRegistryAsync(_workspace.RootPath);
        registry.AddOrUpdate(MakeDef("my-widget", "Both"));

        var appliedThemes = new List<string>();
        var capturedPaths = new List<string>();

        var runner = new ScreenshotRefreshRunner(
            registry,
            new UiActionReplayRegistry(),
            new FixtureLoaderRegistry(),
            _workspace.RootPath,
            applyThemeAsync: theme => { appliedThemes.Add(theme); return Task.CompletedTask; },
            getActiveTheme:  () => "Dark");

        runner.CaptureRequested += (_, args) =>
        {
            capturedPaths.Add(args.OutputPath);
            args.SignalSaved();
        };

        // Act
        await runner.RunAsync(new ScreenshotRefreshOptions(ScreenshotRefreshMode.All, null));

        // Assert — two captures, correct theme-suffixed paths, correct switcher call order
        Assert.That(capturedPaths, Has.Count.EqualTo(2),
            "Expected exactly two CaptureRequested events for a 'Both' definition.");
        Assert.That(capturedPaths[0], Does.EndWith("my-widget-light.png"),
            "First capture should be the Light pass.");
        Assert.That(capturedPaths[1], Does.EndWith("my-widget-dark.png"),
            "Second capture should be the Dark pass.");
        Assert.That(appliedThemes, Has.Count.EqualTo(4),
            "Expected 4 theme calls: apply Light, restore, apply Dark, restore.");
        Assert.That(appliedThemes[0], Is.EqualTo("Light"),
            "First theme switch should be to Light.");
        Assert.That(appliedThemes[2], Is.EqualTo("Dark"),
            "Third theme switch should be to Dark (second pass apply).");
    }
}
