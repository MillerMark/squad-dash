namespace SquadDash.Tests;

[TestFixture]
internal sealed class SilentBackgroundAgentPolicyTests {
    [TestCase("scribe", null, null)]
    [TestCase(null, "scribe", null)]
    [TestCase(null, null, "Scribe")]
    [TestCase("Scribe", null, null)]
    public void ShouldSuppressThread_ReturnsTrue_ForScribeIdentity(
        string? agentId,
        string? agentName,
        string? agentDisplayName) {
        Assert.That(
            SilentBackgroundAgentPolicy.ShouldSuppressThread(agentId, agentName, agentDisplayName),
            Is.True);
    }

    [Test]
    public void ShouldSuppressThread_ReturnsFalse_ForNormalAgents() {
        Assert.That(
            SilentBackgroundAgentPolicy.ShouldSuppressThread("orion-vale", "orion-vale", "Orion Vale"),
            Is.False);
    }
}
