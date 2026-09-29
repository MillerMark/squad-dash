namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanningSpecialistInstructionsTests
{
    [Test]
    public void LoadCharter_DefinesCreationRevisionAndNonImplementationContracts()
    {
        var charter = PlanningSpecialistInstructions.LoadCharter().ReplaceLineEndings("\n");

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
            Assert.That(charter, Does.Contain("only `tasks[].description` as presentation Markdown"));
            Assert.That(charter, Does.Contain("Keep every other plan property plain text"));
            Assert.That(charter, Does.Contain("Never add backticks or fences to structured"));
            Assert.That(charter, Does.Contain("least-complex complete outcome"));
            Assert.That(charter, Does.Contain("minimum\n  viable implementation relative to the full request"));
            Assert.That(charter, Does.Contain("deterministic host behavior"));
            Assert.That(charter, Does.Contain("hypothetical future platform problem"));
            Assert.That(charter, Does.Contain("Specialist availability alone decides neither outcome"));
            Assert.That(charter, Does.Contain("functionality, implementation\n  integrity, safety"));
            Assert.That(charter, Does.Contain("browser or UI automation"));
            Assert.That(charter, Does.Contain("normal product or current workspace already provides sufficient\n  test state"));
            Assert.That(charter, Does.Contain("Use `contractExamples` proactively"));
            Assert.That(charter, Does.Contain("plausible wrong interpretation"));
            Assert.That(charter, Does.Contain("not reserved only for strict syntax contracts"));
            Assert.That(charter, Does.Contain("absence alone is never a reason to reject or"));
            Assert.That(charter, Does.Contain("strong candidate for focused regression coverage"));
            Assert.That(charter, Does.Contain("does not itself justify another"));
            Assert.That(charter, Does.Contain("only meaningful outcome is a human observation"));
            Assert.That(charter, Does.Contain("Do not create a separate operator runbook"));
            Assert.That(charter, Does.Contain("group proofs that share setup, actions, and cleanup"));
            Assert.That(charter, Does.Contain("SquadDash Inbox"));
            Assert.That(charter, Does.Contain("chronologically in\n  the durable approval Inbox message"));
            Assert.That(charter, Does.Contain("source kit is internal staging"));
            Assert.That(charter, Does.Contain("authoritative opened-project workspace root"));
            Assert.That(charter, Does.Contain("never a SquadDash session, config, cache, or"));
            Assert.That(charter, Does.Contain("verify every link target before delivery"));
            Assert.That(charter, Does.Contain("absolute path"));
            Assert.That(charter, Does.Contain("exactly one accountable primary agent"));
            Assert.That(charter, Does.Contain("separate dependent"));
            Assert.That(charter, Does.Contain("Never hide required execution in prose"));
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
            Assert.That(guidance, Does.Contain("optional `contractExamples` field"));
            Assert.That(guidance, Does.Contain("Examples\n   are welcome even when the user did not supply them"));
            Assert.That(guidance, Does.Contain("absence\n   alone must not reject or halt an otherwise clear plan"));
            Assert.That(guidance, Does.Contain("strong candidates for focused regression coverage"));
            Assert.That(guidance, Does.Contain("extra task, validation node, approval gate, or planning halt"));
            Assert.That(guidance, Does.Contain("only meaningful outcome is a human observation"));
            Assert.That(guidance, Does.Contain("Do not create a separate operator runbook"));
            Assert.That(guidance, Does.Contain("Never merge several observations into one compound checkbox"));
            Assert.That(guidance, Does.Contain("renders the block with its standard copy button"));
            Assert.That(guidance, Does.Contain("durable approval Inbox message"));
            Assert.That(guidance, Does.Contain("Established regression checks may use the reviewer's familiar existing files"));
            Assert.That(guidance, Does.Contain("not a deterministic reason to reject or halt a plan"));
            Assert.That(guidance, Does.Contain("Compose the complete approval procedure"));
            Assert.That(guidance, Does.Contain("checkbox in the durable approval Inbox message"));
            Assert.That(guidance, Does.Contain("{{approval-check:requirement-id}}"));
            Assert.That(guidance, Does.Contain("authoritative opened-project"));
            Assert.That(guidance, Does.Contain("never a SquadDash session, config, cache, or state directory"));
            Assert.That(guidance, Does.Contain("executor must verify every resolved target exists before delivery"));
            Assert.That(guidance, Does.Contain("absolute path"));
        });
    }

    [Test]
    public void BuildDelegationGuidance_MakesCurrentBaselineAuthoritativeAndWorkspaceCharterAnOverlay()
    {
        const string hostPath = @"C:\host\hash\planning-specialist-charter.md";
        const string workspacePath = @"D:\repo\.squad\agents\rory\charter.md";

        var guidance = PlanningSpecialistInstructions.BuildDelegationGuidance(hostPath, workspacePath);

        Assert.Multiple(() =>
        {
            Assert.That(guidance, Does.Contain(hostPath));
            Assert.That(guidance, Does.Contain(workspacePath));
            Assert.That(guidance, Does.Contain("current immutable Planning Architect baseline"));
            Assert.That(guidance, Does.Contain("workspace customization overlay"));
            Assert.That(guidance, Does.Contain("cannot replace or weaken"));
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
