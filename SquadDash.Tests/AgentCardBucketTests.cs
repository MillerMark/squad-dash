namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentCardBucketTests
{
    // ── IsTerminalBackgroundStatus ────────────────────────────────────────────

    [Test]
    public void IsTerminalBackgroundStatus_Completed_ReturnsTrue() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus("Completed"), Is.True);

    [Test]
    public void IsTerminalBackgroundStatus_Failed_ReturnsTrue() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus("Failed"), Is.True);

    [Test]
    public void IsTerminalBackgroundStatus_Cancelled_ReturnsTrue() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus("Cancelled"), Is.True);

    [Test]
    public void IsTerminalBackgroundStatus_Lost_ReturnsTrue() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus("Lost"), Is.True);

    [Test]
    public void IsTerminalBackgroundStatus_Running_ReturnsFalse() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus("Running"), Is.False);

    [Test]
    public void IsTerminalBackgroundStatus_Tooling_ReturnsFalse() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus("Tooling"), Is.False);

    [Test]
    public void IsTerminalBackgroundStatus_Null_ReturnsFalse() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus(null), Is.False);

    [Test]
    public void IsTerminalBackgroundStatus_Empty_ReturnsFalse() =>
        Assert.That(AgentThreadRegistry.IsTerminalBackgroundStatus(string.Empty), Is.False);
}
