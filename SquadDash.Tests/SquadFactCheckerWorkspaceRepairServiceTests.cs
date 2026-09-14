using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadFactCheckerWorkspaceRepairServiceTests
{
    [Test]
    public void Repair_CreatesVerityAsBuiltInFactCheckerWithoutEditingTeamRoster()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/team.md", "# Squad Team\n\n## Members\n");

        var result = SquadFactCheckerWorkspaceRepairService.Repair(workspace.RootPath);
        var charterPath = workspace.GetPath(".squad", "agents", "fact-checker", "charter.md");

        Assert.Multiple(() =>
        {
            Assert.That(result.Repaired, Is.True);
            Assert.That(result.CreatedCharter, Is.True);
            Assert.That(File.ReadAllText(charterPath), Does.Contain("# Verity Cross"));
            Assert.That(File.ReadAllText(charterPath), Does.Contain("optional customization overlay"));
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "team.md")),
                Is.EqualTo("# Squad Team\n\n## Members\n"));
        });

        var member = new SquadTeamRosterLoader().Load(workspace.RootPath).Single();
        Assert.Multiple(() =>
        {
            Assert.That(member.Name, Is.EqualTo(FactCheckerInstructions.DisplayName));
            Assert.That(member.AccentKey, Is.EqualTo(FactCheckerInstructions.Handle));
            Assert.That(member.IsUtilityAgent, Is.True);
        });
    }

    [Test]
    public void Repair_MigratesOnlyRecognizedGenericFactCheckerScaffold()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", """
            # Fact Checker

            - **Name:** Fact Checker
            - **Role:** Devil's Advocate & Verification Agent
            - **Casting:** Gets a universe name like any other agent (not exempt like Scribe/Ralph).
            """);

        var result = SquadFactCheckerWorkspaceRepairService.Repair(workspace.RootPath);
        var content = File.ReadAllText(workspace.GetPath(".squad", "agents", "fact-checker", "charter.md"));

        Assert.Multiple(() =>
        {
            Assert.That(result.MigratedLegacyCharter, Is.True);
            Assert.That(content, Does.Contain("# Verity Cross"));
            Assert.That(content, Does.Not.Contain("Gets a universe name"));
        });
    }

    [Test]
    public void Repair_PreservesWorkspaceCustomizationAndHostBaselineWrapsIt()
    {
        using var workspace = new TestWorkspace();
        const string custom = "# Verity Cross — Package verifier\n\nCheck generated Razor fixtures.";
        workspace.CreateFile(".squad/agents/fact-checker/charter.md", custom);
        workspace.CreateFile(".squad/agents/fact-checker/history.md", "# Custom history");

        var result = SquadFactCheckerWorkspaceRepairService.Repair(workspace.RootPath);
        var effective = FactCheckerInstructions.BuildEffectiveCharter(custom);

        Assert.Multiple(() =>
        {
            Assert.That(result.Repaired, Is.False);
            Assert.That(File.ReadAllText(workspace.GetPath(".squad", "agents", "fact-checker", "charter.md")),
                Is.EqualTo(custom));
            Assert.That(effective, Does.StartWith("# Verity Cross — Fact Checker"));
            Assert.That(effective, Does.Contain("<workspace-charter-overlay>"));
            Assert.That(effective, Does.Contain("Check generated Razor fixtures."));
        });
    }
}
