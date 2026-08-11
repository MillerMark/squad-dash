namespace SquadDash.Tests;

[TestFixture]
internal sealed class AssessedPlanContinuationPolicyTests
{
    [Test]
    public void Resolve_NonBlockingAwaitingGateWithIndependentTask_StartsExecution()
    {
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Executing,
            gateStatus: PlanGateStatus.AwaitingApproval);

        var result = AssessedPlanContinuationPolicy.Resolve(
            plan,
            nextTaskId: "PLAN-007",
            nextValidation: null);

        Assert.That(result, Is.EqualTo(AssessedPlanContinuationAction.StartExecution));
    }

    [Test]
    public void Resolve_BlockingApprovalBoundary_RemainsStopped()
    {
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval,
            gateStatus: PlanGateStatus.AwaitingApproval);

        var result = AssessedPlanContinuationPolicy.Resolve(
            plan,
            nextTaskId: "PLAN-007",
            nextValidation: null);

        Assert.That(result, Is.EqualTo(AssessedPlanContinuationAction.RemainStopped));
    }

    [Test]
    public void Resolve_AllBoundariesSatisfied_CompletesPlan()
    {
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Executing,
            gateStatus: PlanGateStatus.Approved);

        var result = AssessedPlanContinuationPolicy.Resolve(
            plan,
            nextTaskId: null,
            nextValidation: null);

        Assert.That(result, Is.EqualTo(AssessedPlanContinuationAction.Complete));
    }

    private static Plan MakePlan(string lifecycleStatus, string gateStatus) => new(
        PlanId: "PLAN",
        Revision: "rev-1",
        Source: "test",
        LifecycleStatus: lifecycleStatus,
        Title: "Plan",
        Branch: "feature/test",
        Summary: "Test plan",
        Tasks:
        [
            new PlanTask(
                TaskId: "PLAN-006",
                Title: "Recovered task",
                Description: "Recovered",
                DependsOn: [],
                Priority: "mid",
                Status: PlanTaskStatus.Complete),
            new PlanTask(
                TaskId: "PLAN-007",
                Title: "Independent task",
                Description: "Continue while approval is open",
                DependsOn: [],
                Priority: "mid",
                Status: PlanTaskStatus.Pending),
        ],
        ApprovalGates:
        [
            new PlanApprovalGate(
                GateId: "PLAN-006-PROOF",
                Message: "Approve task 6",
                AfterTaskIds: ["PLAN-006"],
                BeforeTaskIds: ["PLAN-008"],
                Status: gateStatus),
        ],
        Progress: new PlanProgress(1, 2, "PLAN-007"),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));
}
