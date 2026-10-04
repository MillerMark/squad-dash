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
            isCompact: true,
            isUtilityAgent: true);

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.True);
    }

    [Test]
    public void ShouldShow_ReturnsTrue_ForFactCheckerUtilityAgent() {
        var agent = new AgentStatusCard(
            "Verity Cross",
            "V",
            "Fact Checker",
            "Ready",
            string.Empty,
            string.Empty,
            "#FF4472C4",
            "fact-checker",
            charterPath: @"C:\Repo\.squad\agents\fact-checker\charter.md",
            folderPath: @"C:\Repo\.squad\agents\fact-checker",
            isCompact: true,
            isUtilityAgent: true);

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.True);
    }

    [Test]
    public void ShouldShow_ReturnsTrue_ForBuiltInRoryUtilityAgent() {
        var agent = new AgentStatusCard(
            "Rory",
            "R",
            "Planning Architect",
            "Ready",
            string.Empty,
            string.Empty,
            "#FF4472C4",
            "rory",
            charterPath: @"C:\Repo\.squad\agents\rory\charter.md",
            folderPath: @"C:\Repo\.squad\agents\rory",
            isCompact: true,
            isUtilityAgent: true);

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.True);
    }

    [TestCase("Fact Checker", null)]
    [TestCase("Verity Cross", null)]
    [TestCase("Other Name", @"C:\Repo\.squad\agents\FACT-CHECKER\")]
    public void IsVisibleUtilityAgent_RecognizesVerifierAliases(string name, string? folder) {
        Assert.That(AgentRosterVisibilityPolicy.IsVisibleUtilityAgent(name, folder), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LoadedRoster_ShowsOneVerifierAndPreservesOtherUtilityVisibility(bool withTeamFile) {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/legacy-verifier/charter.md", "# Fact Checker");
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", "# Verity Cross");
        workspace.CreateFile(".squad/agents/scribe/charter.md", "# Scribe");
        workspace.CreateFile(".squad/agents/ralph/charter.md", "# Ralph");
        workspace.CreateFile(".squad/agents/Rai/charter.md", "# Rai");
        workspace.CreateFile(".squad/agents/rory/charter.md", "# Rory");
        if (withTeamFile) {
            workspace.CreateFile(".squad/team.md", """
                ## Members
                | Name | Role | Charter | Status |
                |------|------|---------|--------|
                | Fact Checker | Verifier | agents/legacy-verifier/charter.md | Active |
                | Verity Cross | Fact Checker | agents/fact-checker/charter.md | Active |
                """);
        }

        var visible = new SquadTeamRosterLoader().Load(workspace.RootPath)
            .Select(member => new AgentStatusCard(
                member.Name, member.Name[..1], member.Role, member.Status,
                string.Empty, string.Empty, "#FF4472C4", member.AccentKey,
                charterPath: member.CharterPath,
                folderPath: member.FolderPath,
                isUtilityAgent: member.IsUtilityAgent))
            .Where(AgentRosterVisibilityPolicy.ShouldShow)
            .Select(agent => agent.Name)
            .ToArray();

        Assert.That(visible, Is.EquivalentTo(new[] { "Verity Cross", "Scribe", "Rory" }));
    }

    [TestCase("Ralph", "ralph")]
    [TestCase("Rai", "Rai")]
    public void ShouldShow_ReturnsFalse_ForOtherUtilityAgents(string name, string folder) {
        var agent = new AgentStatusCard(
            name,
            "R",
            "Work Monitor",
            "Ready",
            string.Empty,
            string.Empty,
            "#FF4472C4",
            folder,
            charterPath: $@"C:\Repo\.squad\agents\{folder}\charter.md",
            folderPath: $@"C:\Repo\.squad\agents\{folder}",
            isCompact: true,
            isUtilityAgent: true);

        Assert.That(AgentRosterVisibilityPolicy.ShouldShow(agent), Is.False);
    }
}
