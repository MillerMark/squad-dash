namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanApprovalControlLockPolicyTests
{
    private static PlanTask Task(string id, string status, params string[] dependsOn) =>
        new(id, id, id, dependsOn, "mid", status);

    private static Plan BuildPlan(
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<PlanApprovalGate>? gates = null,
        string lifecycleStatus = "executing") =>
        new("PLAN-001", "rev1", "manual", lifecycleStatus, "Test Plan", "main", "A test plan",
            tasks, gates ?? [], new PlanProgress(0, tasks.Count),
            new PlanTimestamps(DateTimeOffset.UtcNow));

    // ── PlanHasExecutionContext ────────────────────────────────────────────────

    [TestCase("staged", false)]
    [TestCase("approved", false)]
    [TestCase("executing", true)]
    [TestCase("awaiting-approval", true)]
    [TestCase("interrupted", true)]
    [TestCase("blocked", true)]
    [TestCase("completed", true)]
    [TestCase("stopped", true)]
    public void PlanHasExecutionContext_RespectsLifecycleStatus(string status, bool expected)
    {
        var plan = BuildPlan([Task("A", PlanTaskStatus.Pending)], lifecycleStatus: status);
        Assert.That(PlanApprovalControlLockPolicy.PlanHasExecutionContext(plan), Is.EqualTo(expected));
    }

    [Test]
    public void PlanHasExecutionContext_NullPlan_ReturnsFalse()
    {
        Assert.That(PlanApprovalControlLockPolicy.PlanHasExecutionContext(null), Is.False);
    }

    // ── IsTaskEntryLocked ─────────────────────────────────────────────────────

    [Test]
    public void IsTaskEntryLocked_PendingTask_NotLocked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Pending, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "B"), Is.False);
    }

    [Test]
    public void IsTaskEntryLocked_ExecutingTask_Locked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Executing, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "B"), Is.True);
    }

    [Test]
    public void IsTaskEntryLocked_CompletedTask_Locked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "B"), Is.True);
    }

    // ── IsTaskExitLocked ──────────────────────────────────────────────────────

    [Test]
    public void IsTaskExitLocked_PendingTask_NotLocked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Pending),
            Task("B", PlanTaskStatus.Pending, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.False);
    }

    [Test]
    public void IsTaskExitLocked_ExecutingTask_NotLocked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Executing),
            Task("B", PlanTaskStatus.Pending, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.False);
    }

    [Test]
    public void IsTaskExitLocked_CompletedTask_Locked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Pending, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.True);
    }

    [Test]
    public void IsTaskExitLocked_FailedTask_Locked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Failed),
            Task("B", PlanTaskStatus.Pending, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.True);
    }

    // ── Mixed completed/pending branches (parallel lanes) ─────────────────────

    [Test]
    public void ParallelLanes_OneLaneCompleted_OnlyThatLaneLocked()
    {
        // A → B (completed), A → C (pending) — B's exit locked, C's exit not locked
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete, "A"),
            Task("C", PlanTaskStatus.Pending, "A"),
            Task("D", PlanTaskStatus.Pending, "B", "C"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "B"), Is.True);
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "C"), Is.False);
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "D"), Is.False);
        });
    }

    // ── ALL join locked only after all inbound complete ────────────────────────

    [Test]
    public void AllJoin_NotAllInboundComplete_NotLocked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Pending),
            Task("C", PlanTaskStatus.Pending, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsAllJoinLocked(
            plan, ["A", "B"], ["C"]), Is.False);
    }

    [Test]
    public void AllJoin_AllInboundComplete_Locked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete),
            Task("C", PlanTaskStatus.Pending, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsAllJoinLocked(
            plan, ["A", "B"], ["C"]), Is.True);
    }

    [Test]
    public void AllJoin_TraversedGate_Locked()
    {
        PlanApprovalGate[] gates =
        [
            new("G1", "Review", ["A", "B"], ["C"], PlanGateStatus.Approved),
        ];
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete),
            Task("C", PlanTaskStatus.Executing, "A", "B"),
        ], gates);
        Assert.That(PlanApprovalControlLockPolicy.IsAllJoinLocked(
            plan, ["A", "B"], ["C"]), Is.True);
    }

    // ── Stage milestone locking ───────────────────────────────────────────────

    [Test]
    public void StageMilestone_UpstreamPending_NotLocked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Pending),
            Task("B", PlanTaskStatus.Pending),
            Task("C", PlanTaskStatus.Pending, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(
            plan, ["A", "B"], ["C"]), Is.False);
    }

    [Test]
    public void StageMilestone_UpstreamCompletedDownstreamStarted_Locked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete),
            Task("C", PlanTaskStatus.Executing, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(
            plan, ["A", "B"], ["C"]), Is.True);
    }

    [Test]
    public void StageMilestone_UpstreamCompletedDownstreamNotStarted_NotLocked()
    {
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete),
            Task("C", PlanTaskStatus.Pending, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(
            plan, ["A", "B"], ["C"]), Is.False);
    }

    [Test]
    public void StageMilestone_TraversedGate_Locked()
    {
        PlanApprovalGate[] gates =
        [
            new("G1", "Review", ["A"], ["B"], PlanGateStatus.Approved),
        ];
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Pending, "A"),
        ], gates);
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(
            plan, ["A"], ["B"]), Is.True);
    }

    // ── Restart scenario: Completed → Executing ───────────────────────────────

    [Test]
    public void Restart_PlanBackToExecuting_PreviouslyCompletedStillLocked()
    {
        // Simulates a plan going from Completed back to Executing (restart)
        // Tasks A and B were previously completed; C is now re-executing
        var plan = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete, "A"),
            Task("C", PlanTaskStatus.Executing, "B"),
            Task("D", PlanTaskStatus.Pending, "C"),
        ], lifecycleStatus: PlanLifecycleStatus.Executing);

        Assert.Multiple(() =>
        {
            // Previously completed work remains locked
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.True);
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "B"), Is.True);
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "B"), Is.True);
            // Currently executing — entry locked, exit not locked
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "C"), Is.True);
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "C"), Is.False);
            // Future pending — still editable
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "D"), Is.False);
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "C"), Is.False);
        });
    }

    // ── Live transition: editable → locked when progress updates ──────────────

    [Test]
    public void LiveTransition_GateBecomeLockedWhenTaskCompletes()
    {
        // Before: task B is pending, so its entry is editable
        var planBefore = BuildPlan([
            Task("A", PlanTaskStatus.Executing),
            Task("B", PlanTaskStatus.Pending, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(planBefore, "B"), Is.False);

        // After: task A completed and B started — entry is now locked
        var planAfter = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Executing, "A"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(planAfter, "B"), Is.True);
    }

    [Test]
    public void LiveTransition_AllJoinBecomesLockedWhenLastPathCompletes()
    {
        // Before: only path A is complete
        var planBefore = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Executing),
            Task("C", PlanTaskStatus.Pending, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsAllJoinLocked(
            planBefore, ["A", "B"], ["C"]), Is.False);

        // After: B also completed — ALL join is now locked
        var planAfter = BuildPlan([
            Task("A", PlanTaskStatus.Complete),
            Task("B", PlanTaskStatus.Complete),
            Task("C", PlanTaskStatus.Pending, "A", "B"),
        ]);
        Assert.That(PlanApprovalControlLockPolicy.IsAllJoinLocked(
            planAfter, ["A", "B"], ["C"]), Is.True);
    }

    // ── Unknown task ID ───────────────────────────────────────────────────────

    [Test]
    public void IsTaskEntryLocked_UnknownTaskId_NotLocked()
    {
        var plan = BuildPlan([Task("A", PlanTaskStatus.Complete)]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "UNKNOWN"), Is.False);
    }

    [Test]
    public void IsTaskExitLocked_UnknownTaskId_NotLocked()
    {
        var plan = BuildPlan([Task("A", PlanTaskStatus.Complete)]);
        Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "UNKNOWN"), Is.False);
    }
}
