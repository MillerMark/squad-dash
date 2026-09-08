using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadPlanningWorkspaceRepairServiceTests
{
    [Test]
    public void Repair_CreatesBuiltInRoryWithoutChangingEditableRoster()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", "# Squad Team\n\n## Members\n");

        var result = SquadPlanningWorkspaceRepairService.Repair(workspace.RootPath);
        var charterPath = workspace.GetPath(".squad", "agents", "rory", "charter.md");
        var historyPath = workspace.GetPath(".squad", "agents", "rory", "history.md");

        Assert.Multiple(() =>
        {
            Assert.That(result.Repaired, Is.True);
            Assert.That(result.CreatedCharter, Is.True);
            Assert.That(result.CreatedHistory, Is.True);
            Assert.That(File.ReadAllText(charterPath), Does.Contain("# Rory — Planning Architect"));
            Assert.That(File.ReadAllText(charterPath), Does.Contain("- **Name:** Rory"));
            Assert.That(File.ReadAllText(historyPath), Does.Contain("- **Role:** Planning Architect"));
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "team.md")),
                Is.EqualTo("# Squad Team\n\n## Members\n"));
        });

        var members = new SquadTeamRosterLoader().Load(workspace.RootPath);
        var rory = members.Single(member => member.Name == "Rory");
        Assert.Multiple(() =>
        {
            Assert.That(rory.Role, Is.EqualTo("Planning Architect"));
            Assert.That(rory.IsUtilityAgent, Is.True);
            Assert.That(rory.AccentKey, Is.EqualTo("rory"));
        });
    }

    [Test]
    public void Repair_PreservesCustomizedRoryCharterAndHistory()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/rory/charter.md", "# Rory — Custom Planner");
        workspace.CreateFile(".squad/agents/rory/history.md", "# Custom History");

        var result = SquadPlanningWorkspaceRepairService.Repair(workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Repaired, Is.False);
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "agents", "rory", "charter.md")),
                Is.EqualTo("# Rory — Custom Planner"));
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "agents", "rory", "history.md")),
                Is.EqualTo("# Custom History"));
        });
    }
}
