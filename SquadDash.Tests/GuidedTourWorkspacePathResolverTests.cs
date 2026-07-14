using SquadDash.GuidedTours;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class GuidedTourWorkspacePathResolverTests
{
    [Test]
    public void Resolve_CapturedPathAvailable_KeepsOriginalWorkspace()
    {
        var result = GuidedTourWorkspacePathResolver.Resolve("original", () => "current");

        Assert.That(result, Is.EqualTo("original"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_CapturedPathUnavailable_UsesCurrentWorkspace(string? capturedPath)
    {
        var result = GuidedTourWorkspacePathResolver.Resolve(capturedPath, () => "current");

        Assert.That(result, Is.EqualTo("current"));
    }

    [Test]
    public void Resolve_NoWorkspaceAvailable_ReturnsNull()
    {
        var result = GuidedTourWorkspacePathResolver.Resolve(null, () => null);

        Assert.That(result, Is.Null);
    }
}
