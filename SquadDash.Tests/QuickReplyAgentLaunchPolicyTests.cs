namespace SquadDash.Tests;

[TestFixture]
internal sealed class QuickReplyAgentLaunchPolicyTests {
    [Test]
    public void RequiresObservedNamedAgentLaunch_ReturnsTrue_ForNamedAgentQuickReply() {
        var result = QuickReplyAgentLaunchPolicy.RequiresObservedNamedAgentLaunch(
            "start_named_agent",
            "lyra-morn");

        Assert.That(result, Is.True);
    }

    [Test]
    public void RequiresObservedNamedAgentLaunch_ReturnsFalse_WhenHandleMissing() {
        var result = QuickReplyAgentLaunchPolicy.RequiresObservedNamedAgentLaunch(
            "start_named_agent",
            null);

        Assert.That(result, Is.False);
    }

    [Test]
    public void MatchesExpectedAgent_AcceptsHandleAndDisplayNameMatches() {
        var byHandle = new SquadSdkEvent {
            AgentName = "kaiamercer"
        };
        var byDisplayName = new SquadSdkEvent {
            AgentDisplayName = "Kaia Mercer"
        };

        Assert.Multiple(() => {
            Assert.That(
                QuickReplyAgentLaunchPolicy.MatchesExpectedAgent("kaiamercer", "Kaia Mercer", byHandle),
                Is.True);
            Assert.That(
                QuickReplyAgentLaunchPolicy.MatchesExpectedAgent("kaiamercer", "Kaia Mercer", byDisplayName),
                Is.True);
        });
    }

    [Test]
    public void BuildLaunchFailureMessage_NamesExpectedAgentAndSelectedOption() {
        var message = QuickReplyAgentLaunchPolicy.BuildLaunchFailureMessage(
            "Start Kaia Mercer",
            "Kaia Mercer",
            "kaiamercer");

        Assert.Multiple(() => {
            Assert.That(message, Does.Contain("Kaia Mercer"));
            Assert.That(message, Does.Contain("\"Start Kaia Mercer\""));
            Assert.That(message, Does.Contain("no matching agent launch was observed"));
        });
    }
}
