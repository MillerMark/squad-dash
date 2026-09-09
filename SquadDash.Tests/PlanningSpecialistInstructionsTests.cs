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
            Assert.That(charter, Does.Contain("unapproved staged proposal"));
            Assert.That(charter, Does.Contain("`STAGED_PLAN_REVISION_JSON`"));
            Assert.That(charter, Does.Contain("`STAGED_PLAN_REVISION_REQUEST_JSON`"));
            Assert.That(charter, Does.Contain("complete replacement `TASKS_JSON`"));
            Assert.That(charter, Does.Contain("accepted durable plan"));
            Assert.That(charter, Does.Contain("never use it to correct a staged proposal"));
            Assert.That(charter, Does.Contain("`.squad/team.md`"));
            Assert.That(charter, Does.Contain("`.squad/routing.md`"));
            Assert.That(charter, Does.Contain("Never implement source changes"));
            Assert.That(charter, Does.Contain("cheap, non-mutating final verification or audit"));
            Assert.That(charter, Does.Contain("never add both with substantially overlapping scope"));
            Assert.That(charter, Does.Contain("at most seven checks per checkpoint"));
            Assert.That(charter, Does.Contain("## Protocol Conformance Preflight"));
            Assert.That(charter, Does.Contain("Read the live host-owned planning or revision protocol directly"));
            Assert.That(charter, Does.Contain("Never treat a Coordinator-authored schema summary"));
            Assert.That(charter, Does.Contain("exact property names"));
            Assert.That(charter, Does.Contain("Never claim that the plan was staged"));
            Assert.That(charter, Does.Contain("Only SquadDash can"));
            Assert.That(charter, Does.Contain("SquadDash collects and validates that verified final response"));
            Assert.That(charter, Does.Contain("must not extract or re-emit it"));
            Assert.That(charter, Does.Contain("`staged_plan_revision_request` correlation metadata"));
            Assert.That(charter, Does.Contain("exact-match text-edit operations"));
            Assert.That(charter, Does.Contain("revision-locked outline"));
            Assert.That(charter, Does.Contain("`T1`, `G1`, and `V1`"));
            Assert.That(charter, Does.Contain("Use `targetRef`"));
            Assert.That(charter, Does.Contain("size-aware revision strategy"));
            Assert.That(charter, Does.Contain("Calibrate detail to the approval decision"));
            Assert.That(charter, Does.Contain("number of tests"));
            Assert.That(charter, Does.Contain("bounded execution contract"));
            Assert.That(charter, Does.Contain("only meaningful outcome is a human observation"));
            Assert.That(charter, Does.Contain("Markdown runbook"));
            Assert.That(charter, Does.Contain("SquadDash Inbox"));
            Assert.That(charter, Does.Contain("chronologically in\n  the durable approval Inbox message"));
            Assert.That(charter, Does.Contain("same persisted checkbox is shown in Inbox and transcript"));
            Assert.That(charter, Does.Contain("not from the Git repository root or shell directory"));
            Assert.That(charter, Does.Contain("verify every link target before delivery"));
            Assert.That(charter, Does.Contain("absolute path"));
        });
    }

    [Test]
    public void BuildDelegationGuidance_RequiresBuiltInRoryAndForbidsCoordinatorPlanning()
    {
        var guidance = PlanningSpecialistInstructions.BuildDelegationGuidance()
            .ReplaceLineEndings("\n");

        Assert.Multiple(() =>
        {
            Assert.That(guidance, Does.Contain("`.squad/team.md`"));
            Assert.That(guidance, Does.Contain("`.squad/routing.md`"));
            Assert.That(guidance, Does.Contain("`delegate_roster_agent`"));
            Assert.That(guidance, Does.Contain("built-in named Planning Architect, `rory`"));
            Assert.That(guidance, Does.Contain("Do not author even a small or mechanical plan delta"));
            Assert.That(guidance, Does.Contain("do not\n   replace Rory with a generic `task` worker"));
            Assert.That(guidance, Does.Contain("STAGED_PLAN_REVISION_REQUEST_JSON routing"));
            Assert.That(guidance, Does.Contain("`staged_plan_revision_request` argument"));
            Assert.That(guidance, Does.Contain("add no\n   invented operation schema"));
            Assert.That(guidance, Does.Match("exact-match text-edit\\s+operations"));
            Assert.That(guidance, Does.Contain("casual references such as \"this plan\""));
            Assert.That(guidance, Does.Contain("short T/G/V references"));
            Assert.That(guidance, Does.Contain("size-aware revisionStrategy"));
            Assert.That(guidance, Does.Contain("`PLAN_REVISION_JSON` only when revising an accepted durable\n   plan"));
            Assert.That(guidance, Does.Contain("Do not substitute one protocol\n   for another"));
            Assert.That(guidance, Does.Contain("collected, validated, and\n   applied directly by SquadDash"));
            Assert.That(guidance, Does.Contain("do not run shell extraction"));
            Assert.That(guidance, Does.Contain("required marker at the top level"));
            Assert.That(guidance, Does.Contain("Never drop the\n   structured-output requirement"));
            Assert.That(guidance, Does.Contain("rate-limit waits are not delegation unavailability"));
            Assert.That(guidance, Does.Contain("do not take over the specialist's work"));
            Assert.That(guidance, Does.Contain("read the live protocol directly"));
            Assert.That(guidance, Does.Contain("`{groupId}-NNN`"));
            Assert.That(guidance, Does.Contain("`{groupId}-VAL-NNN`"));
            Assert.That(guidance, Does.Contain("`validationId` field"));
            Assert.That(guidance, Does.Contain("`requirementId`, `proofType`, and `description`"));
            Assert.That(guidance, Does.Contain("rejected candidate"));
            Assert.That(guidance, Does.Contain("one complete corrected payload"));
            Assert.That(guidance, Does.Contain("Never claim that the plan"));
            Assert.That(guidance, Does.Contain("only the host may report those outcomes"));
            Assert.That(guidance, Does.Contain("Calibrate plan detail to the approval decision"));
            Assert.That(guidance, Does.Contain("only meaningful outcome is a human observation"));
            Assert.That(guidance, Does.Contain("Markdown runbook"));
            Assert.That(guidance, Does.Contain("SquadDash Inbox"));
            Assert.That(guidance, Does.Contain("Established regression checks may use the reviewer's familiar existing files"));
            Assert.That(guidance, Does.Contain("not a deterministic reason to reject or halt a plan"));
            Assert.That(guidance, Does.Contain("chronological live approval form"));
            Assert.That(guidance, Does.Contain("persisted\n   checkbox shown in both the Inbox and transcript"));
            Assert.That(guidance, Does.Contain("not from the Git repository root or shell directory"));
            Assert.That(guidance, Does.Contain("verify every\n   resolved target exists before delivery"));
            Assert.That(guidance, Does.Contain("absolute path"));
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
            Assert.That(universe, Does.Contain("correcting staged proposals with compact revision-anchored, property-level deltas"));
            Assert.That(profiles, Does.Contain("Staged-proposal correction using compact, revision-anchored property-level `STAGED_PLAN_REVISION_JSON` deltas"));
            Assert.That(profiles, Does.Contain("Accepted-plan revision using the separately injected live `PLAN_REVISION_JSON` protocol"));
        });
    }
}
