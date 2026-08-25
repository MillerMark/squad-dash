namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceUpgradeServiceTests
{
    [Test]
    public void ParseDryRunOutput_ClassifiesRepresentativeLinesIntoAllThreeCategories()
    {
        var output = """
            Dry run — previewing upgrade from 0.11.0 → 0.12.0
            ⚠️ squad.agent.md has local customizations — would back up to squad.agent.md.local-backup
            Would overwrite 3 squad-owned file(s):
              create  templates/session-init-reference.md
              overwrite  templates/charter.md
            generated  .squad/casting/registry.json
            """;

        var result = WorkspaceUpgradeService.ParseDryRunOutput(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.SafeAdditionsOrRefreshes, Is.EquivalentTo(new[]
            {
                "templates\\session-init-reference.md",
                "templates\\charter.md"
            }));
            Assert.That(result.TrackedLocalChangesAtRisk, Is.EquivalentTo(new[]
            {
                ".github\\agents\\squad.agent.md"
            }));
            Assert.That(result.GeneratedFilesAtRisk, Is.EquivalentTo(new[]
            {
                ".squad\\casting\\registry.json"
            }));
        });
    }

    [Test]
    public void ParseDryRunOutput_MalformedOrEmptyOutput_DoesNotThrowAndReturnsEmpty()
    {
        Assert.DoesNotThrow(() =>
        {
            var result = WorkspaceUpgradeService.ParseDryRunOutput(
                null,
                string.Empty,
                " \r\n\t",
                "This line is unrelated output with no parseable file markers.");

            Assert.Multiple(() =>
            {
                Assert.That(result.SafeAdditionsOrRefreshes, Is.Empty);
                Assert.That(result.TrackedLocalChangesAtRisk, Is.Empty);
                Assert.That(result.GeneratedFilesAtRisk, Is.Empty);
            });
        });
    }

    [Test]
    public void ParseDryRunOutput_CleanWorkspaceDryRun_HasEmptyRiskLists()
    {
        var output = """
            Dry run — previewing upgrade from 0.11.0 → 0.12.0
            squad.agent.md is unmodified — would refresh with latest template
            Would overwrite 2 squad-owned file(s):
              overwrite  templates/history.md
              create  templates/skill.md
            Run without --dry-run to apply.
            """;

        var result = WorkspaceUpgradeService.ParseDryRunOutput(output);

        Assert.Multiple(() =>
        {
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item(".github\\agents\\squad.agent.md"));
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item("templates\\history.md"));
            Assert.That(result.SafeAdditionsOrRefreshes, Contains.Item("templates\\skill.md"));
            Assert.That(result.TrackedLocalChangesAtRisk, Is.Empty);
            Assert.That(result.GeneratedFilesAtRisk, Is.Empty);
        });
    }
}
