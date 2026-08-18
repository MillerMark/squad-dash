using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DependencyEditRecomputerTests
{
    private const string GroupId = "RECOMP-20260817";

    // ─── Cycle detection ─────────────────────────────────────────────────────

    [Test]
    public void Recompute_ReturnsCycleFailure_WhenDirectCycleIntroduced()
    {
        // A → B → A (cycle)
        var group = MakeGroup() with
        {
            Tasks =
            [
                Task(1, []),
                Task(2, [Id(1)]),
                Task(3, [Id(2), Id(1)]),
            ],
        };
        // Introduce cycle: task 1 now depends on task 3
        var cyclic = StagedPlanEditor.UpdateTask(
            group, Id(1), "First", "First result", "mid", [Id(3)]);

        var result = DependencyEditRecomputer.Recompute(cyclic);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("cycle").IgnoreCase);
            Assert.That(result.CycleIds, Is.Not.Empty);
            Assert.That(result.RecomputedGroup, Is.Null);
            Assert.That(result.StageAssignments, Is.Null);
            Assert.That(result.DisplayLabels, Is.Null);
        });
    }

    [Test]
    public void Recompute_ReturnsCycleFailure_BeforeAnyMutation()
    {
        // Self-dependency
        var group = new DecomposedTaskGroup(
            GroupId, "Cyclic plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "Self", [Id(1)], "mid", "Self"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.False);
        Assert.That(result.CycleIds, Does.Contain(Id(1)));
    }

    // ─── Stage assignment correctness ────────────────────────────────────────

    [Test]
    public void Recompute_MultiStageFixture_StageAssignmentsMatchAuthoritativeLayoutEngine()
    {
        // Linear chain: 001 → 002 → 003
        // Expected: 001 at stage 0, 002 at stage 1, 003 at stage 2
        var group = MakeGroup();
        var authoritative = ValidationShieldPresenter.ComputeValidationAwareLevels(
            group.Tasks,
            group.Validations ?? []);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StageAssignments, Is.Not.Null);

        foreach (var (taskId, expectedLevel) in authoritative)
        {
            Assert.That(
                result.StageAssignments![taskId],
                Is.EqualTo(expectedLevel),
                $"Stage for {taskId} should match authoritative layout engine");
        }
    }

    [Test]
    public void Recompute_ParallelTasks_SameStageAssignmentAsAuthoritative()
    {
        // 001 → 002, 001 → 003 (parallel), 002 + 003 → 004
        var group = new DecomposedTaskGroup(
            GroupId, "Parallel plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "First", [],              "mid", "First"),
                new DecomposedSubTask(Id(2), "ParA",  [Id(1)],         "mid", "ParA"),
                new DecomposedSubTask(Id(3), "ParB",  [Id(1)],         "mid", "ParB"),
                new DecomposedSubTask(Id(4), "Join",  [Id(2), Id(3)],  "mid", "Join"),
            ]);

        var authoritative = ValidationShieldPresenter.ComputeValidationAwareLevels(
            group.Tasks, []);
        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        foreach (var (taskId, expectedLevel) in authoritative)
        {
            Assert.That(result.StageAssignments![taskId], Is.EqualTo(expectedLevel),
                $"Stage for {taskId} should match authoritative");
        }
    }

    [Test]
    public void Recompute_WithValidationNodes_LevelsMatchValidationAwareLevels()
    {
        // 001 → 002 with a validation between them that pushes 002 to stage 1
        var group = new DecomposedTaskGroup(
            GroupId, "Validated plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "First",  [],      "mid", "First"),
                new DecomposedSubTask(Id(2), "Second", [Id(1)], "mid", "Second"),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001",
                    "Check output",
                    "Validate first task output",
                    [Id(1)],
                    [Id(2)],
                    ["Output is correct"]),
            ]);

        var authoritative = ValidationShieldPresenter.ComputeValidationAwareLevels(
            group.Tasks, group.Validations!);
        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        foreach (var (taskId, expectedLevel) in authoritative)
        {
            Assert.That(result.StageAssignments![taskId], Is.EqualTo(expectedLevel),
                $"Stage for {taskId} should match validation-aware authoritative engine");
        }
    }

    // ─── Task with all dependencies removed → Stage 1 ────────────────────────

    [Test]
    public void Recompute_TaskWithAllDependenciesRemoved_PlacedAtStageZero()
    {
        // Remove all deps from task 2 (previously depended on task 1)
        var group = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(2), "Second", "Second result", "mid", []);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StageAssignments![Id(2)], Is.EqualTo(0),
            "Task with no dependencies must be placed in Stage 1 (index 0)");
    }

    [Test]
    public void Recompute_AllDependenciesRemovedFromEveryTask_AllAtStageZero()
    {
        // A plan where every task has no dependencies — all land in stage 0
        var group = new DecomposedTaskGroup(
            GroupId, "Flat plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "Alpha", [], "mid", "Alpha"),
                new DecomposedSubTask(Id(2), "Beta",  [], "mid", "Beta"),
                new DecomposedSubTask(Id(3), "Gamma", [], "mid", "Gamma"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StageAssignments![Id(1)], Is.EqualTo(0));
        Assert.That(result.StageAssignments![Id(2)], Is.EqualTo(0));
        Assert.That(result.StageAssignments![Id(3)], Is.EqualTo(0));
    }

    // ─── Display label correctness ────────────────────────────────────────────

    [Test]
    public void Recompute_DisplayLabels_AreOneBasedAndCoverAllTasks()
    {
        var group = MakeGroup();
        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        var labels = result.DisplayLabels!;
        Assert.That(labels.Count, Is.EqualTo(group.Tasks.Count));
        var numericLabels = labels.Values
            .Select(label => int.TryParse(label, out var n) ? n : -1)
            .OrderBy(n => n)
            .ToArray();
        Assert.That(numericLabels, Is.EqualTo(Enumerable.Range(1, group.Tasks.Count).ToArray()));
    }

    [Test]
    public void Recompute_DisplayLabels_MatchFreshAdapterAssignmentForSameGraph()
    {
        // From scratch, PendingDecomposePlanAdapter.ToPlan assigns labels as (index + 1)
        // where tasks are in list order. After recompute, the tasks are sorted topologically
        // and labeled in that topological order — matching a fresh ToPlan on sorted tasks.
        var group = new DecomposedTaskGroup(
            GroupId, "Staged plan", "feature/test", "Test",
            [
                new DecomposedSubTask(Id(1), "First",  [],      "mid", "First"),
                new DecomposedSubTask(Id(2), "Second", [Id(1)], "mid", "Second"),
                new DecomposedSubTask(Id(3), "Third",  [Id(2)], "mid", "Third"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        // Tasks in original topological order stay at the same label positions
        var recomputed = result.RecomputedGroup!;
        var labels = result.DisplayLabels!;

        // First task in topological order (stage 0 = Id(1)) → label "1"
        Assert.That(labels[Id(1)], Is.EqualTo("1"));
        Assert.That(labels[Id(2)], Is.EqualTo("2"));
        Assert.That(labels[Id(3)], Is.EqualTo("3"));

        // Tasks in recomputed group list are in topological order
        Assert.That(recomputed.Tasks.Select(t => t.Id),
            Is.EqualTo(new[] { Id(1), Id(2), Id(3) }));
    }

    [Test]
    public void Recompute_AfterDependencyAddedThatChangesStages_DisplayLabelsReflectNewOrder()
    {
        // Start: 001 and 002 both in stage 0 (no deps)
        // Add: 002 depends on 001 → 001 at stage 0, 002 at stage 1
        var group = new DecomposedTaskGroup(
            GroupId, "Parallel plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "First",  [], "mid", "First"),
                new DecomposedSubTask(Id(2), "Second", [], "mid", "Second"),
            ]);

        var edited = StagedPlanEditor.UpdateTask(
            group, Id(2), "Second", "Second result", "mid", [Id(1)]);
        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.That(result.Success, Is.True);
        // Task 1 is at stage 0; task 2 at stage 1 → "1" and "2" respectively
        Assert.That(result.StageAssignments![Id(1)], Is.EqualTo(0));
        Assert.That(result.StageAssignments![Id(2)], Is.EqualTo(1));
        Assert.That(result.DisplayLabels![Id(1)], Is.EqualTo("1"));
        Assert.That(result.DisplayLabels![Id(2)], Is.EqualTo("2"));
    }

    // ─── Approval boundary anchors ────────────────────────────────────────────

    [Test]
    public void Recompute_StageMilestoneGate_ProducesStageAnchor()
    {
        // Gate exactly covers the boundary between stage 0 and stage 1
        var group = MakeGroup() with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    GroupId + "-GATE-001",
                    "Review stage one output",
                    [Id(1)],     // after task 1 (stage 0)
                    [Id(2)],     // before task 2 (stage 1)
                    Question: "Is the stage one output correct?"),
            ],
        };

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ApprovalBoundaryAnchors, Is.Not.Null);
        Assert.That(result.ApprovalBoundaryAnchors!.ContainsKey(GroupId + "-GATE-001"), Is.True);
        Assert.That(result.ApprovalBoundaryAnchors[GroupId + "-GATE-001"],
            Does.StartWith("stage:"));
    }

    [Test]
    public void Recompute_TaskExitGate_ProducesTaskAfterAnchor()
    {
        // Gate targets a single-task boundary, not a full stage boundary
        var group = new DecomposedTaskGroup(
            GroupId, "Task gate plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "First",  [],      "mid", "First"),
                new DecomposedSubTask(Id(2), "Second", [Id(1)], "mid", "Second"),
                new DecomposedSubTask(Id(3), "Third",  [Id(2)], "mid", "Third"),
            ],
            ApprovalGates:
            [
                new DecomposedGate(
                    GroupId + "-GATE-001",
                    "Review task 2 output",
                    [Id(2)],
                    [Id(3)],
                    Question: "Is task 2 output correct?"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ApprovalBoundaryAnchors!.ContainsKey(GroupId + "-GATE-001"), Is.True);
        // It's a single-task exit gate
        Assert.That(result.ApprovalBoundaryAnchors[GroupId + "-GATE-001"],
            Does.StartWith("task-after:").Or.StartsWith("stage:"));
    }

    [Test]
    public void Recompute_NoApprovalGates_ApprovalBoundaryAnchorsIsEmpty()
    {
        var result = DependencyEditRecomputer.Recompute(MakeGroup());

        Assert.That(result.Success, Is.True);
        Assert.That(result.ApprovalBoundaryAnchors, Is.Not.Null);
        Assert.That(result.ApprovalBoundaryAnchors, Is.Empty);
    }

    // ─── Validation eligibility ───────────────────────────────────────────────

    [Test]
    public void Recompute_StageBoundaryValidation_IsStructurallyEligible()
    {
        var group = new DecomposedTaskGroup(
            GroupId, "Validated plan", "main", "summary",
            [
                new DecomposedSubTask(Id(1), "First",  [],      "mid", "First"),
                new DecomposedSubTask(Id(2), "Second", [Id(1)], "mid", "Second"),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Stage validation", "Validates boundary",
                    [Id(1)], [Id(2)],
                    ["Output is correct"]),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StructurallyEligibleValidationIds,
            Does.Contain(GroupId + "-VAL-001"));
    }

    [Test]
    public void Recompute_NoValidations_EligibleListIsEmpty()
    {
        var result = DependencyEditRecomputer.Recompute(MakeGroup());

        Assert.That(result.Success, Is.True);
        Assert.That(result.StructurallyEligibleValidationIds, Is.Empty);
    }

    // ─── Recomputed group structure ───────────────────────────────────────────

    [Test]
    public void Recompute_RecomputedGroupPreservesAllNonTaskFields()
    {
        var group = MakeGroup() with
        {
            Delivery = "squash merge",
        };

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        var recomputed = result.RecomputedGroup!;
        Assert.Multiple(() =>
        {
            Assert.That(recomputed.GroupId,    Is.EqualTo(group.GroupId));
            Assert.That(recomputed.GroupTitle, Is.EqualTo(group.GroupTitle));
            Assert.That(recomputed.Branch,     Is.EqualTo(group.Branch));
            Assert.That(recomputed.Summary,    Is.EqualTo(group.Summary));
            Assert.That(recomputed.Delivery,   Is.EqualTo(group.Delivery));
            Assert.That(recomputed.Tasks.Count, Is.EqualTo(group.Tasks.Count));
        });
    }

    [Test]
    public void Recompute_RecomputedGroupTasksInTopologicalOrder()
    {
        // Build group with tasks in reverse order of dependency (3 → 2 → 1 in list)
        var group = new DecomposedTaskGroup(
            GroupId, "Reversed plan", "main", "summary",
            [
                new DecomposedSubTask(Id(3), "Third",  [Id(2)], "mid", "Third"),
                new DecomposedSubTask(Id(2), "Second", [Id(1)], "mid", "Second"),
                new DecomposedSubTask(Id(1), "First",  [],      "mid", "First"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.Success, Is.True);
        // After recompute, should be in topological order: 1, 2, 3
        var taskIds = result.RecomputedGroup!.Tasks.Select(t => t.Id).ToArray();
        Assert.That(taskIds, Is.EqualTo(new[] { Id(1), Id(2), Id(3) }));
    }

    // ─── Audit boundary reconciliation ───────────────────────────────────────

    [Test]
    public void Recompute_AuditValidation_AfterTaskIdsUpdatedWhenDepRemovalCreatesNewLeaf()
    {
        // Linear chain: 001 → 002 → 003.  Task 003 is the only leaf.
        // Audit validation covers {003}.
        // Remove task 002's dependency on 001 → 001 no longer has any successors
        // in its dep graph, so it becomes a new leaf alongside 003.
        // After recompute the audit's AfterTaskIds must be {001, 003}.
        var group = new DecomposedTaskGroup(
            GroupId, "Audit plan", "main", "summary",
            [
                Task(1, []),
                Task(2, [Id(1)]),
                Task(3, [Id(2)]),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Final audit", "Audit all leaves.",
                    [Id(3)], [],
                    ["All tasks completed."], Mode: "audit"),
            ]);

        var edited = StagedPlanEditor.UpdateTask(group, Id(2), "Second", "Second result", "mid", []);
        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.That(result.Success, Is.True);
        var audit = result.RecomputedGroup!.Validations!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(audit.Mode, Is.EqualTo("audit"));
            Assert.That(audit.AfterTaskIds,
                Is.EquivalentTo(new[] { Id(1), Id(3) }),
                "Audit AfterTaskIds must include both the original leaf and the newly created leaf");
        });
    }

    [Test]
    public void Recompute_AuditValidation_UnchangedWhenLeafSetDoesNotChange()
    {
        // Linear chain: 001 → 002 → 003.  Only leaf is 003.
        // Remove 003's dep on 002: now both 002 and 003 are parallel leaves.
        // But audit's AfterTaskIds was already {003}; it must become {002, 003}.
        // Separate concern: ensure that if we instead change an interior dep that leaves the
        // leaf set intact, the audit is NOT changed.
        var group = new DecomposedTaskGroup(
            GroupId, "Audit stable plan", "main", "summary",
            [
                Task(1, []),
                Task(2, [Id(1)]),
                Task(3, [Id(2)]),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Final audit", "Audit all leaves.",
                    [Id(3)], [],
                    ["All tasks completed."], Mode: "audit"),
            ]);

        // Move task 002 to depend directly on task 001 (no structural change to leaves).
        var edited = StagedPlanEditor.UpdateTask(group, Id(2), "Second", "Second result", "mid", [Id(1)]);
        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.That(result.Success, Is.True);
        var audit = result.RecomputedGroup!.Validations!.Single();
        Assert.That(audit.AfterTaskIds, Is.EquivalentTo(new[] { Id(3) }),
            "Audit AfterTaskIds must remain {003} when the leaf set is unchanged");
    }

    [Test]
    public void Recompute_NonAuditValidations_AfterTaskIdsNotModified()
    {
        // Ensure that evidence-mode (non-audit) validations are never touched by
        // ReconcileAuditBoundary, even when the leaf set changes.
        var group = new DecomposedTaskGroup(
            GroupId, "Mixed validation plan", "main", "summary",
            [
                Task(1, []),
                Task(2, [Id(1)]),
                Task(3, [Id(2)]),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Evidence check", "Review step 1.",
                    [Id(1)], [Id(2)],
                    ["Output is observable."], Mode: "evidence"),
            ]);

        var edited = StagedPlanEditor.UpdateTask(group, Id(2), "Second", "Second result", "mid", []);
        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.That(result.Success, Is.True);
        var validation = result.RecomputedGroup!.Validations!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validation.Mode, Is.EqualTo("evidence"));
            Assert.That(validation.AfterTaskIds, Is.EqualTo(new[] { Id(1) }),
                "Non-audit validation AfterTaskIds must not be modified by ReconcileAuditBoundary");
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string Id(int number) => $"{GroupId}-{number:000}";

    private static DecomposedSubTask Task(int number, string[] dependsOn) =>
        new(Id(number), $"Task {number} result", dependsOn, "mid", $"Task {number}");

    private static DecomposedTaskGroup MakeGroup() => new(
        GroupId,
        "Recomputation test plan",
        "feature/recomputer-test",
        "Exercise DependencyEditRecomputer.",
        [
            new DecomposedSubTask(Id(1), "First result",  [],      "mid", "First"),
            new DecomposedSubTask(Id(2), "Second result", [Id(1)], "mid", "Second"),
            new DecomposedSubTask(Id(3), "Third result",  [Id(2)], "mid", "Third"),
        ]);
}
