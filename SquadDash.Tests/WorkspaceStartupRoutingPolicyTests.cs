using NUnit.Framework;
using SquadDash.Screenshots;

namespace SquadDash.Tests;

internal sealed class WorkspaceStartupRoutingPolicyTests {
    [Test]
    public void ShouldBypassSingleInstanceRouting_ReturnsFalse_ForInteractiveStartup() {
        Assert.That(
            WorkspaceStartupRoutingPolicy.ShouldBypassSingleInstanceRouting(ScreenshotRefreshOptions.None),
            Is.False);
    }

    [Test]
    public void ShouldBypassSingleInstanceRouting_ReturnsTrue_ForRefreshAllStartup() {
        var options = new ScreenshotRefreshOptions(ScreenshotRefreshMode.All, null);

        Assert.That(
            WorkspaceStartupRoutingPolicy.ShouldBypassSingleInstanceRouting(options),
            Is.True);
    }

    [Test]
    public void ShouldBypassSingleInstanceRouting_ReturnsTrue_ForNamedRefreshStartup() {
        var options = new ScreenshotRefreshOptions(ScreenshotRefreshMode.Named, "the-coordinator");

        Assert.That(
            WorkspaceStartupRoutingPolicy.ShouldBypassSingleInstanceRouting(options),
            Is.True);
    }
}
