using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanProofCapabilityPolicyTests
{
    [TestCase("automated-test", PlanProofExecutorKind.Worker)]
    [TestCase("build", PlanProofExecutorKind.Worker)]
    [TestCase("host-recorded", PlanProofExecutorKind.Host)]
    [TestCase("live-ui-observation", PlanProofExecutorKind.Human)]
    [TestCase("restart-observation", PlanProofExecutorKind.Human)]
    [TestCase("invented-proof", PlanProofExecutorKind.Unsupported)]
    public void ProofTypesHaveExplicitExecutors(string proofType, PlanProofExecutorKind expected)
    {
        Assert.That(PlanProofCapabilityPolicy.Classify(proofType), Is.EqualTo(expected));
    }

    [Test]
    public void LegacyHumanProofGate_GetsCompatibilityQuestion()
    {
        var gate = new PlanApprovalGate(
            "GATE-001",
            "Confirm the human-observed proof.",
            ["T1"],
            [],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "visible",
                    "live-ui-observation",
                    "Clicking the item shows a selection highlight"),
            ]);

        Assert.That(
            PlanProofCapabilityPolicy.ResolveHumanQuestion(gate),
            Is.EqualTo("Did you observe the following behavior: Clicking the item shows a selection highlight?"));
    }

    [Test]
    public void HumanTaskProof_IsMovedToCheckpoint_WhileAutomatedProofRemainsOnWorker()
    {
        var root = Task(
            "CAPABILITY-20260804-001",
            proofRequirements:
            [
                new DecomposedTaskProofRequirement("tests", "automated-test", "Run the integration tests."),
                new DecomposedTaskProofRequirement(
                    "visible",
                    "live-ui-observation",
                    "Observe the live transition.",
                    "Does the live transition appear in the running window?"),
            ]);
        var child = Task("CAPABILITY-20260804-002", dependsOn: [root.Id]);
        var group = Group([root, child]);

        var routed = PlanProofCapabilityPolicy.RouteHumanProofsToApprovalGates(group);

        Assert.Multiple(() =>
        {
            Assert.That(routed.Tasks[0].ProofRequirements, Has.Count.EqualTo(1));
            Assert.That(routed.Tasks[0].ProofRequirements![0].RequirementId, Is.EqualTo("tests"));
            Assert.That(routed.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(routed.ApprovalGates![0].AfterTaskIds, Is.EqualTo(new[] { root.Id }));
            Assert.That(routed.ApprovalGates[0].BeforeTaskIds, Is.EqualTo(new[] { child.Id }));
            Assert.That(routed.ApprovalGates[0].ProofRequirements![0].RequirementId, Is.EqualTo("visible"));
            Assert.That(routed.ApprovalGates[0].Question,
                Is.EqualTo("Does the live transition appear in the running window?"));
        });
    }

    [Test]
    public void FinalHumanProofCheckpoint_StopsCompletionAndRecordsDurableAttestation()
    {
        var task = Task(
            "CAPABILITY-20260804-001",
            proofRequirements:
            [new DecomposedTaskProofRequirement("restart", "restart-observation", "Restart and confirm state.")]);
        var routed = PlanProofCapabilityPolicy.RouteHumanProofsToApprovalGates(Group([task]));
        var pending = new PendingDecomposePlan("rev", routed, DateTimeOffset.UtcNow);
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow) with
        {
            Tasks =
            [
                PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow).Tasks[0] with
                {
                    Status = PlanTaskStatus.Complete,
                    Commit = "abc1234",
                },
            ],
            Progress = new PlanProgress(1, 1),
            LifecycleStatus = PlanLifecycleStatus.Executing,
        };

        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.True);
        var ready = PlanStoreUpdater.ApplyGateReady(plan, plan.ApprovalGates[0].GateId);
        var approved = PlanStoreUpdater.ApplyGateApproved(ready, ready.ApprovalGates[0].GateId,
            "Observed after a real restart.", "Mark");

        Assert.Multiple(() =>
        {
            Assert.That(ApprovalGateReadinessEvaluator.AllRequiredApproved(approved), Is.True);
            var proofEvidence = approved.ApprovalGates[0].ProofEvidence!;
            Assert.That(proofEvidence, Has.Count.EqualTo(1));
            Assert.That(proofEvidence[0].Summary, Does.Contain("Mark confirmed"));
            Assert.That(proofEvidence[0].Artifacts![0],
                Does.StartWith("squaddash://approval/"));
        });
    }

    [Test]
    public void TasksJsonParser_RoutesHumanProofBeforeStaging()
    {
        var json = """
            TASKS_JSON:
            {
              "groupId":"CAPABILITY-20260804",
              "groupTitle":"Capability-aware proof",
              "branch":"feature/capability-proof",
              "summary":"Deliver a cohesive feature and require truthful proof from the executor capable of observing it.",
              "tasks":[
                {"id":"CAPABILITY-20260804-001","title":"Build the feature","description":"Implement the feature. Observable outcome: the feature builds. Production consumer: task 002 exercises it.","dependsOn":[],"priority":"high","agentRoutingMode":"generic","genericAgentReason":"fixture"},
                {"id":"CAPABILITY-20260804-002","title":"Observe the live feature","description":"Exercise the feature. Observable outcome: the running UI updates. Production consumer: the production window displays the update.","dependsOn":["CAPABILITY-20260804-001"],"priority":"high","agentRoutingMode":"generic","genericAgentReason":"fixture","proofRequirements":[{"requirementId":"live","proofType":"live-ui-observation","description":"Observe the running UI update.","question":"Does the running UI show the expected update?"}]}
              ],
              "validations":[{"validationId":"CAPABILITY-20260804-VAL-001","title":"Feature proven","description":"Audit the accepted implementation and human observation.","afterTaskIds":["CAPABILITY-20260804-002"],"beforeTaskIds":[],"assertions":["The feature was observed in the running UI."],"mode":"audit"}]
            }
            """;

        Assert.That(TasksJsonParser.TryParse(json, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Tasks[1].ProofRequirements, Is.Null);
            Assert.That(parsed.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(parsed.ApprovalGates![0].BeforeTaskIds, Is.Empty);
            Assert.That(parsed.ApprovalGates[0].ProofRequirements![0].ProofType,
                Is.EqualTo("live-ui-observation"));
            Assert.That(parsed.ApprovalGates[0].Question,
                Is.EqualTo("Does the running UI show the expected update?"));
        });
    }

    private static DecomposedSubTask Task(
        string id,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<DecomposedTaskProofRequirement>? proofRequirements = null) =>
        new(id, "Observable outcome and production consumer.", dependsOn ?? [], "high", "Task " + id,
            AgentRoutingMode: "generic", GenericAgentReason: "fixture", ProofRequirements: proofRequirements);

    private static DecomposedTaskGroup Group(IReadOnlyList<DecomposedSubTask> tasks) =>
        new("CAPABILITY-20260804", "Capability aware plan", "feature/capability-proof",
            "Keep proof truthful and route it to the executor capable of observing it.", tasks);
}
