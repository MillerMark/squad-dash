namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentThreadIdentityPolicyTests {
    [Test]
    public void NormalizeAgentCardKey_ClearsStaleRosterBindingForGenericBackgroundThread() {
        var roster = new[] {
            new TeamAgentDescriptor("Mira Quill", "mira-quill", "Documentation")
        };

        var normalized = AgentThreadIdentityPolicy.NormalizeAgentCardKey(
            new AgentThreadIdentitySnapshot(
                Title: "Architect",
                AgentId: "build-run-tests",
                AgentName: "general-purpose",
                AgentDisplayName: "Architect",
                AgentCardKey: "mira-quill",
                IsPlaceholderThread: false),
            roster);

        Assert.That(normalized, Is.Null);
    }

    [Test]
    public void NormalizeAgentCardKey_ReattachesRosterThreadWhenDisplayNameMatchesRoster() {
        var roster = new[] {
            new TeamAgentDescriptor("Mira Quill", "mira-quill", "Documentation")
        };

        var normalized = AgentThreadIdentityPolicy.NormalizeAgentCardKey(
            new AgentThreadIdentitySnapshot(
                Title: "Mira Quill",
                AgentId: null,
                AgentName: null,
                AgentDisplayName: "Mira Quill",
                AgentCardKey: null,
                IsPlaceholderThread: false),
            roster);

        Assert.That(normalized, Is.EqualTo("mira-quill"));
    }

    [Test]
    public void ResolveExpectedAgentCardKey_MatchesRosterFromSpecificPrefixToken() {
        var roster = new[] {
            new TeamAgentDescriptor("Lyra Morn", "lyra-morn", "WPF specialist")
        };

        var normalized = AgentThreadIdentityPolicy.ResolveExpectedAgentCardKey(
            agentId: "lyra-space4-resize",
            agentName: "Squad",
            agentDisplayName: "Squad",
            roster);

        Assert.That(normalized, Is.EqualTo("lyra-morn"));
    }

    [Test]
    public void CanReuseByAgentName_RequiresExpectedRosterIdentity() {
        Assert.Multiple(() => {
            Assert.That(
                AgentThreadIdentityPolicy.CanReuseByAgentName("general-purpose", expectedAgentCardKey: null),
                Is.False);
            Assert.That(
                AgentThreadIdentityPolicy.CanReuseByAgentName("vesper-knox", expectedAgentCardKey: "vesper-knox"),
                Is.True);
        });
    }
}
