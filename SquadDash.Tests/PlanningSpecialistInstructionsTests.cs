namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanningSpecialistInstructionsTests
{
    [Test]
    public void LoadCharter_DefinesCreationRevisionAndNonImplementationContracts()
    {
        var charter = PlanningSpecialistInstructions.LoadCharter();

        Assert.Multiple(() =>
        {
            Assert.That(charter, Does.Contain("# Planning Architect"));
            Assert.That(charter, Does.Contain("`TASKS_JSON`"));
            Assert.That(charter, Does.Contain("`PLAN_REVISION_JSON`"));
            Assert.That(charter, Does.Contain("`.squad/team.md`"));
            Assert.That(charter, Does.Contain("`.squad/routing.md`"));
            Assert.That(charter, Does.Contain("Never implement source changes"));
            Assert.That(charter, Does.Contain("tailored end-to-end proof"));
            Assert.That(charter, Does.Contain("## Protocol Conformance Preflight"));
            Assert.That(charter, Does.Contain("Read the live host-owned planning or revision protocol directly"));
            Assert.That(charter, Does.Contain("Never treat a Coordinator-authored schema summary"));
            Assert.That(charter, Does.Contain("exact property names"));
            Assert.That(charter, Does.Contain("Never claim that the plan was staged"));
            Assert.That(charter, Does.Contain("Only SquadDash can"));
        });
    }

    [Test]
    public void BuildDelegationGuidance_PrefersRosterPlannerAndProvidesGenericFallback()
    {
        var guidance = PlanningSpecialistInstructions.BuildDelegationGuidance();

        Assert.Multiple(() =>
        {
            Assert.That(guidance, Does.Contain("`.squad/team.md`"));
            Assert.That(guidance, Does.Contain("`.squad/routing.md`"));
            Assert.That(guidance, Does.Contain("`delegate_roster_agent`"));
            Assert.That(guidance, Does.Contain("generic `task` tool"));
            Assert.That(guidance, Does.Contain("Do not give that worker Rory's name"));
            Assert.That(guidance, Does.Contain("`TASKS_JSON` for new plan creation"));
            Assert.That(guidance, Does.Contain("`PLAN_REVISION_JSON` for plan"));
            Assert.That(guidance, Does.Contain("same required marker at the top level"));
            Assert.That(guidance, Does.Contain("Never drop the\n   structured-output requirement"));
            Assert.That(guidance, Does.Contain("read the live protocol directly"));
            Assert.That(guidance, Does.Contain("`{groupId}-NNN`"));
            Assert.That(guidance, Does.Contain("`{groupId}-VAL-NNN`"));
            Assert.That(guidance, Does.Contain("`validationId` field"));
            Assert.That(guidance, Does.Contain("`requirementId`, `proofType`, and `description`"));
            Assert.That(guidance, Does.Contain("rejected candidate"));
            Assert.That(guidance, Does.Contain("one complete corrected payload"));
            Assert.That(guidance, Does.Contain("Never claim that the plan"));
            Assert.That(guidance, Does.Contain("only the host may report those outcomes"));
        });
    }

    [Test]
    public void BuildHireCharterGuidance_AppliesOnlyToSquadDashRory()
    {
        var rory = PlanningSpecialistInstructions.BuildHireCharterGuidance(
            SquadInstallerService.SquadDashUniverseName,
            "Rory");
        var otherAgent = PlanningSpecialistInstructions.BuildHireCharterGuidance(
            SquadInstallerService.SquadDashUniverseName,
            "Orion Vale");
        var otherUniverse = PlanningSpecialistInstructions.BuildHireCharterGuidance(
            "Another Universe",
            "Rory");

        Assert.Multiple(() =>
        {
            Assert.That(rory, Does.Contain("Rory's baseline role contract"));
            Assert.That(rory, Does.Contain("# Planning Architect"));
            Assert.That(otherAgent, Is.Empty);
            Assert.That(otherUniverse, Is.Empty);
        });
    }

    [Test]
    public void EmbeddedUniverse_AdvertisesRoryAsPlanningArchitect()
    {
        var universe = SquadInstallerService.LoadEmbeddedSquadDashMdPublic();
        var profiles = SquadInstallerService.LoadEmbeddedSquadDashProfilesMdPublic();

        Assert.Multiple(() =>
        {
            Assert.That(universe, Does.Contain("### Rory\n**Role:** Planning Architect"));
            Assert.That(profiles, Does.Contain("SquadDash plan creation using the live `TASKS_JSON` protocol"));
            Assert.That(profiles, Does.Contain("Safe plan revision using the live `PLAN_REVISION_JSON` protocol"));
        });
    }
}
