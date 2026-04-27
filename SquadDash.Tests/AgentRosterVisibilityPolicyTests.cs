using NUnit.Framework;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
public class AgentRosterVisibilityPolicyTests {
    [Test]
    public void ShouldShow_ReturnsTrue_ForNonUtilityAgents() {
        var agent = new AgentStatusCard(
            "Lyra Morn",
            "L",
            "UI",
            "Ready",
            string.Empty,
            string.Empty,
            "#FF4472C4",
            "lyra-morn",
            charterPath: @"C:\Repo\.squad\agents\lyra-morn\charter.md");

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.True);
    }

    [Test]
    public void ShouldShow_ReturnsTrue_ForScribeUtilityAgent() {
        var agent = new AgentStatusCard(
            "Scribe",
            "S",
            "Session Logger",
            "Ready",
            string.Empty,
            string.Empty,
            "#FF4472C4",
            "scribe",
            charterPath: @"C:\Repo\.squad\agents\scribe\charter.md",
            folderPath: @"C:\Repo\.squad\agents\scribe",
            isCompact: true);

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.True);
    }

    [Test]
    public void ShouldShow_ReturnsFalse_ForOtherUtilityAgents() {
        var agent = new AgentStatusCard(
            "Ralph",
            "R",
            "Work Monitor",
            "Ready",
            string.Empty,
            string.Empty,
            "#FF4472C4",
            "ralph",
            charterPath: @"C:\Repo\.squad\agents\ralph\charter.md",
            folderPath: @"C:\Repo\.squad\agents\ralph",
            isCompact: true);

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.False);
    }
}
