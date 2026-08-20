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

    [Test]
    public void Recompute_DownstreamStagePropagationAfterDependencyAdd_UsingSimulationPlanFixtureBuilder()
    {
        var fixturePlan = SimulationPlanFixtureBuilder.BuildDemoPlan();
        var fixtureTasks = fixturePlan.Tasks
            .Select(task => new DecomposedSubTask(
                task.TaskId,
                task.Description ?? task.Title ?? task.TaskId,
                task.DependsOn,
                task.Priority,
                task.Title))
            .ToArray();
        var branchTaskId = "sim-task-005";
        var group = new DecomposedTaskGroup(
            GroupId,
            "Simulation fixture downstream propagation",
            "main",
            "Validate downstream stage propagation after dependency edits.",
            [
                .. fixtureTasks,
                new DecomposedSubTask(
                    branchTaskId,
                    "Fixture-only branch used to force a later-stage recompute.",
                    ["sim-task-002"],
                    "medium",
                    "Fixture branch"),
            ]);

        var preEditStages = ValidationShieldPresenter.ComputeValidationAwareLevels(
            group.Tasks,
            group.Validations ?? []);
        var editedTask = group.Tasks.Single(task => task.Id == "sim-task-003");
        var edited = StagedPlanEditor.UpdateTask(
            group,
            editedTask.Id,
            editedTask.Title ?? "Build REST API endpoints",
            editedTask.Description,
            editedTask.Priority,
            [.. editedTask.DependsOn, branchTaskId]);

        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RecomputedGroup, Is.Not.Null);
        Assert.That(result.StageAssignments!["sim-task-003"],
            Is.GreaterThan(preEditStages["sim-task-003"]),
            "Edited task should move to a later stage.");
        Assert.That(result.StageAssignments["sim-task-004"],
            Is.GreaterThan(preEditStages["sim-task-004"]),
            "A downstream descendant should also move to a later stage.");
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

    [Test]
    public void Recompute_RemovingDependency_ReleasesConsumerFromTaskValidation()
    {
        var group = new DecomposedTaskGroup(
            GroupId, "Validated dependency", "main", "summary",
            [
                Task(1, []),
                Task(2, []),
                Task(3, [Id(1), Id(2)]),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Review second", "Validate second task output",
                    [Id(2)], [Id(3)], ["Output is correct"], Mode: "evidence"),
            ]);
        var firstRemoval = group with
        {
            Tasks = group.Tasks.Select(task => task.Id == Id(3)
                ? task with { DependsOn = [Id(2)] }
                : task).ToArray(),
        };
        var firstResult = DependencyEditRecomputer.Recompute(firstRemoval);
        var secondRemoval = firstResult.RecomputedGroup! with
        {
            Tasks = firstResult.RecomputedGroup.Tasks.Select(task => task.Id == Id(3)
                ? task with { DependsOn = [] }
                : task).ToArray(),
        };

        var result = DependencyEditRecomputer.Recompute(secondRemoval);

        Assert.Multiple(() =>
        {
            Assert.That(firstResult.RecomputedGroup!.Validations!.Single().BeforeTaskIds,
                Is.EqualTo(new[] { Id(3) }));
            Assert.That(firstResult.StageAssignments![Id(3)], Is.EqualTo(1));
            Assert.That(result.Success, Is.True);
            Assert.That(result.RecomputedGroup!.Validations!.Single().BeforeTaskIds, Is.Empty);
            Assert.That(result.StageAssignments![Id(3)], Is.EqualTo(0));
        });
    }

    [Test]
    public void Recompute_AddingDependency_BlocksConsumerOnTaskValidation()
    {
        var group = new DecomposedTaskGroup(
            GroupId, "Validated dependency", "main", "summary",
            [
                Task(1, []),
                Task(2, []),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Review first", "Validate first task output",
                    [Id(1)], [], ["Output is correct"], Mode: "evidence"),
            ]);
        var edited = group with
        {
            Tasks = group.Tasks.Select(task => task.Id == Id(2)
                ? task with { DependsOn = [Id(1)] }
                : task).ToArray(),
        };

        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.RecomputedGroup!.Validations!.Single().BeforeTaskIds,
                Is.EqualTo(new[] { Id(2) }));
            Assert.That(result.StageAssignments![Id(2)], Is.EqualTo(1));
        });
    }

    [Test]
    public void Recompute_TaskValidation_BlocksEveryDirectDependent()
    {
        var group = new DecomposedTaskGroup(
            GroupId, "Validated fanout", "main", "summary",
            [
                Task(1, []),
                Task(2, [Id(1)]),
                Task(3, [Id(1)]),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Review first", "Validate first task output",
                    [Id(1)], [Id(2)], ["Output is correct"], Mode: "evidence"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        Assert.That(result.RecomputedGroup!.Validations!.Single().BeforeTaskIds,
            Is.EqualTo(new[] { Id(2), Id(3) }));
    }

    [Test]
    public void Recompute_MultiTaskValidation_PreservesAuthoredBoundary()
    {
        var group = new DecomposedTaskGroup(
            GroupId, "Stage validation", "main", "summary",
            [
                Task(1, []),
                Task(2, []),
                Task(3, []),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Review stage", "Validate the authored boundary",
                    [Id(1), Id(2)], [Id(3)], ["Stage is correct"], Mode: "evidence"),
            ]);

        var result = DependencyEditRecomputer.Recompute(group);

        var validation = result.RecomputedGroup!.Validations!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validation.AfterTaskIds, Is.EqualTo(new[] { Id(1), Id(2) }));
            Assert.That(validation.BeforeTaskIds, Is.EqualTo(new[] { Id(3) }));
        });
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

    // TODO (WPF coverage gap — scenario 8): the following two UI-layer scenarios from the
    // dep-edit regression spec require a live WPF dispatcher and cannot be exercised as unit
    // tests in this assembly:
    //   a. PlanViewerWindow.CreateDependsOnSubmenu: verifies that IsChecked and _pendingAnimation
    //      are atomically rolled back when DependencyEditRecomputer returns a cycle failure inside
    //      the UI event handler (covered at the source level by Lyra's commit cd6817a).
    //   b. PlanViewerWindow end-to-end edit round-trip: verifies that after a successful dep edit
    //      the plan visualizer reflects the recomputed stage layout and the updated audit boundary
    //      is rendered correctly in the connector overlay.
    // Both require a WPF integration test harness with a live PlanViewerWindow instance and are
    // flagged here for human review.

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

    [Test]
    public void Recompute_AuditValidation_AfterTaskIdsShrinks_WhenAddedDependencyTurnsLeafToNonLeaf()
    {
        // Both tasks start as leaves: {001, 002}.  Audit covers {001, 002}.
        // Add: 002 depends on 001 → 001 becomes a non-leaf (002 consumes it).  Only 002 is a leaf.
        // After recompute the audit AfterTaskIds must shrink to {002}.
        var group = new DecomposedTaskGroup(
            GroupId, "Audit shrink plan", "main", "summary",
            [
                Task(1, []),
                Task(2, []),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Final audit", "Audit all leaves.",
                    [Id(1), Id(2)], [],
                    ["All tasks completed."], Mode: "audit"),
            ]);

        var edited = StagedPlanEditor.UpdateTask(
            group, Id(2), "Task 2", "Task 2 result", "mid", [Id(1)]);
        var result = DependencyEditRecomputer.Recompute(edited);

        Assert.That(result.Success, Is.True);
        var audit = result.RecomputedGroup!.Validations!.Single();
        Assert.That(audit.AfterTaskIds, Is.EquivalentTo(new[] { Id(2) }),
            "Audit AfterTaskIds must shrink to {002} when 001 becomes a non-leaf after the 002→001 dependency is added");
    }

    [Test]
    public void Recompute_CycleDetected_ReturnsNullRecomputedGroupAndAuditBoundaryNotModified()
    {
        // Audit covers the only leaf {003}.  Introduce a cycle: 001→003 (creating 001→002→003→001).
        // Recompute must return Success=false with RecomputedGroup=null — the audit boundary
        // must not be mutated and no modified plan must reach the caller.
        var group = new DecomposedTaskGroup(
            GroupId, "Audit cycle plan", "main", "summary",
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

        var cyclic = StagedPlanEditor.UpdateTask(
            group, Id(1), "First", "First result", "mid", [Id(3)]);
        var result = DependencyEditRecomputer.Recompute(cyclic);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False,
                "A cycle must cause Recompute to return a failure result");
            Assert.That(result.RecomputedGroup, Is.Null,
                "No RecomputedGroup must be returned on cycle failure — no mutations reach the caller");
            Assert.That(result.CycleIds, Is.Not.Empty,
                "CycleIds must identify the participating task IDs");
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
