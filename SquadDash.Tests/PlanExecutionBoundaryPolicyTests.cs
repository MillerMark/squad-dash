using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanExecutionBoundaryPolicyTests
{
    [Test]
    public void PreIteration_ReadyHumanGate_ActivatesApprovalInsteadOfGenericStop()
    {
        var plan = MakePlan(PlanValidationStatus.Passed, includeApprovalGate: true);

        Assert.That(
            PlanExecutionBoundaryPolicy.ResolvePreIteration(
                plan,
                DecomposeGroupExecutionState.AwaitingApproval),
            Is.EqualTo(PlanPreIterationBoundaryAction.ActivateApproval));
    }

    [Test]
    public void PreIteration_ValidationTakesPriority_ContinuesWithoutActivatingApproval()
    {
        var plan = MakePlan(PlanValidationStatus.Ready, includeApprovalGate: true);

        Assert.That(
            PlanExecutionBoundaryPolicy.ResolvePreIteration(
                plan,
                DecomposeGroupExecutionState.AwaitingApproval),
            Is.EqualTo(PlanPreIterationBoundaryAction.Continue));
    }

    [Test]
    public void PreIteration_BlockedGroup_UsesTerminalStop()
    {
        var plan = MakePlan(PlanValidationStatus.Passed);

        Assert.That(
            PlanExecutionBoundaryPolicy.ResolvePreIteration(
                plan,
                DecomposeGroupExecutionState.Blocked),
            Is.EqualTo(PlanPreIterationBoundaryAction.Stop));
    }

    [Test]
    public void ReadyValidation_WinsOverHumanApprovalBoundary()
    {
        var plan = MakePlan(PlanValidationStatus.Ready, includeApprovalGate: true);

        Assert.Multiple(() =>
        {
            Assert.That(PlanExecutionBoundaryPolicy.SelectValidation(plan)?.ValidationId,
                Is.EqualTo("PLAN-VAL-001"));
            Assert.That(PlanExecutionBoundaryPolicy.ShouldStopForHumanApproval(plan), Is.False);
        });
    }

    [Test]
    public void ReadyValidation_WaitsForHumanProofAtSameBoundary()
    {
        var plan = MakePlan(PlanValidationStatus.Ready, includeApprovalGate: true) with
        {
            ApprovalGates =
            [
                new PlanApprovalGate(
                    "GATE-1", "Observe A", ["A"], ["B"], PlanGateStatus.Pending,
                    ProofRequirements:
                    [
                        new PlanTaskProofRequirement(
                            "visible", "human-observation", "Observe the completed behavior."),
                    ]),
            ],
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlanExecutionBoundaryPolicy.SelectValidation(plan), Is.Null);
            Assert.That(PlanExecutionBoundaryPolicy.ShouldStopForHumanApproval(plan), Is.True);
        });
    }

    [Test]
    public void PassedHumanProof_ReleasesValidationAtSameBoundary()
    {
        var plan = MakePlan(PlanValidationStatus.Ready, includeApprovalGate: true) with
        {
            ApprovalGates =
            [
                new PlanApprovalGate(
                    "GATE-1", "Observe A", ["A"], ["B"], PlanGateStatus.Approved,
                    ProofRequirements:
                    [
                        new PlanTaskProofRequirement(
                            "visible", "human-observation", "Observe the completed behavior."),
                    ],
                    ProofEvidence:
                    [
                        new PlanTaskProofEvidence(
                            "visible", "human-observation", "Observed by Mark.",
                            ["squaddash://approval/PLAN/GATE-1"]),
                    ]),
            ],
        };

        Assert.That(PlanExecutionBoundaryPolicy.SelectValidation(plan)?.ValidationId,
            Is.EqualTo("PLAN-VAL-001"));
    }

    [Test]
    public void FinalValidation_WaitsForTerminalApprovalGate()
    {
        var plan = MakeFinalValidationPlan(PlanGateStatus.AwaitingApproval);

        Assert.Multiple(() =>
        {
            Assert.That(PlanValidationScheduler.SelectNextSchedulable(plan), Is.Null);
            Assert.That(PlanExecutionBoundaryPolicy.SelectValidation(plan), Is.Null);
            Assert.That(PlanExecutionBoundaryPolicy.ShouldStopForHumanApproval(plan), Is.True);
        });
    }

    [Test]
    public void ApprovedTerminalGate_ReleasesFinalValidation()
    {
        var plan = MakeFinalValidationPlan(PlanGateStatus.Approved);

        Assert.Multiple(() =>
        {
            Assert.That(PlanValidationScheduler.SelectNextSchedulable(plan)?.ValidationId,
                Is.EqualTo("PLAN-VAL-FINAL"));
            Assert.That(PlanExecutionBoundaryPolicy.SelectValidation(plan)?.ValidationId,
                Is.EqualTo("PLAN-VAL-FINAL"));
        });
    }

    [Test]
    public void InProgressValidation_IsRecoveredBeforeReadyValidation()
    {
        var plan = MakePlan(PlanValidationStatus.Validating) with
        {
            Validations =
            [
                MakeValidation("PLAN-VAL-001", PlanValidationStatus.Validating),
                MakeValidation("PLAN-VAL-002", PlanValidationStatus.Ready),
            ],
        };

        Assert.That(PlanExecutionBoundaryPolicy.SelectValidation(plan)?.ValidationId,
            Is.EqualTo("PLAN-VAL-001"));
    }

    [Test]
    public void ReadyApprovalWithoutValidation_StopsForHuman()
    {
        var plan = MakePlan(PlanValidationStatus.Passed, includeApprovalGate: true);

        Assert.That(PlanExecutionBoundaryPolicy.ShouldStopForHumanApproval(plan), Is.True);
    }

    [Test]
    public void InterruptedAfterPassedValidation_AtReadyApprovalBoundary_IsRecoverable()
    {
        var plan = MakePlan(PlanValidationStatus.Passed, includeApprovalGate: true) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                "Plan execution stopped before the current task was accepted.",
                "pending-recovery",
                0),
        };

        Assert.That(
            PlanExecutionBoundaryPolicy.ShouldRecoverInterruptedApprovalBoundary(plan),
            Is.True);
    }

    [Test]
    public void InterruptedWithActiveTask_IsNotReclassifiedAsApprovalBoundary()
    {
        var plan = MakePlan(PlanValidationStatus.Passed, includeApprovalGate: true) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Progress = new PlanProgress(1, 2, "B"),
            InterruptionData = new PlanInterruptionData(
                "Plan execution stopped before the current task was accepted.",
                "pending-recovery",
                0,
                InterruptedTaskId: "B"),
        };

        Assert.That(
            PlanExecutionBoundaryPolicy.ShouldRecoverInterruptedApprovalBoundary(plan),
            Is.False);
    }

    private static Plan MakePlan(string validationStatus, bool includeApprovalGate = false)
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
        };
        IReadOnlyList<PlanApprovalGate> gates = includeApprovalGate
            ? [new PlanApprovalGate("GATE-1", "Approve A", ["A"], ["B"], PlanGateStatus.Pending)]
            : [];
        return new Plan(
            "PLAN", "revision", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary", tasks, gates,
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [MakeValidation("PLAN-VAL-001", validationStatus)]);
    }

    private static PlanValidationNode MakeValidation(string id, string status) =>
        new(id, "Validate A", "Validate the contract", ["A"], ["B"],
            ["A is integrated."], [], "evidence", [], true, status);

    private static Plan MakeFinalValidationPlan(string gateStatus)
    {
        var plan = MakePlan(PlanValidationStatus.Ready);
        return plan with
        {
            Tasks =
            [
                plan.Tasks[0],
                plan.Tasks[1] with { Status = PlanTaskStatus.Complete },
            ],
            Progress = new PlanProgress(2, 2),
            ApprovalGates =
            [
                new PlanApprovalGate(
                    "GATE-FINAL", "Approve before validation", ["B"], [], gateStatus,
                    PresentationAnchor: "task-after:B"),
            ],
            Validations =
            [
                new PlanValidationNode(
                    "PLAN-VAL-FINAL", "Final validation", "Validate the completed plan",
                    ["B"], [], ["The completed plan works."], [], "evidence", [], true,
                    PlanValidationStatus.Ready),
            ],
        };
    }
}
