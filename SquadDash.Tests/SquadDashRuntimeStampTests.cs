namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadDashRuntimeStampTests {
    [Test]
    public void BridgeMode_IsPersistent() {
        Assert.That(SquadDashRuntimeStamp.BridgeMode, Is.EqualTo("persistent"));
    }

    [Test]
    public void BridgeFeatures_IsNotNullOrEmpty() {
        Assert.That(SquadDashRuntimeStamp.BridgeFeatures, Is.Not.Null.And.Not.Empty);
    }

    [TestCase("request-ids")]
    [TestCase("background-task-cache")]
    [TestCase("subagent-events")]
    [TestCase("agent-threads")]
    [TestCase("completion-notices")]
    [TestCase("thread-cards")]
    [TestCase("background-report-handoff")]
    public void BridgeFeatures_ContainsExpectedCapability(string capability) {
        var bridgeFeatures = SquadDashRuntimeStamp.BridgeFeatures;
        Assert.That(bridgeFeatures, Does.Contain(capability));
    }

    [Test]
    public void BuildBridgeStamp_IncludesBridgeMode() {
        var stamp = SquadDashRuntimeStamp.BuildBridgeStamp();

        Assert.That(stamp, Does.Contain($"bridgeMode={SquadDashRuntimeStamp.BridgeMode}"));
    }

    [Test]
    public void BuildBridgeStamp_IncludesBridgeFeatures() {
        var stamp = SquadDashRuntimeStamp.BuildBridgeStamp();

        Assert.That(stamp, Does.Contain($"bridgeFeatures={SquadDashRuntimeStamp.BridgeFeatures}"));
    }

    [Test]
    public void BuildBridgeStamp_IsNotNullOrEmpty() {
        Assert.That(SquadDashRuntimeStamp.BuildBridgeStamp(), Is.Not.Null.And.Not.Empty);
    }
}
