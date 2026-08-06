using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStoreUpdaterTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeGroup(int taskCount = 3)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"GROUP-001-00{i}",
                Description: $"Task {i} description",
                DependsOn:   i == 1 ? [] : [$"GROUP-001-00{i - 1}"],
                Priority:    "mid",
                Title:       $"Task {i}"))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:    "GROUP-001",
            GroupTitle: "Test Plan",
            Branch:     "feature/test",
            Summary:    "A test plan",
            Tasks:      tasks);
    }

    private static DecomposedTaskGroup MakeApprovalWindowGroup() => new(
        GroupId: "GROUP-001",
        GroupTitle: "Approval window plan",
        Branch: "feature/approval-window",
        Summary: "Continue independent work, then stop at the approval frontier.",
        Tasks:
        [
            new DecomposedSubTask(
                "GROUP-001-001", "Create baseline", [], "high", "Create baseline"),
            new DecomposedSubTask(
                "GROUP-001-002", "Run independent work", [], "high", "Run independent work"),
            new DecomposedSubTask(
                "GROUP-001-003", "Cross approved boundary", ["GROUP-001-001"], "high",
                "Cross approved boundary"),
            new DecomposedSubTask(
                "GROUP-001-004", "Summarize", ["GROUP-001-002", "GROUP-001-003"], "mid",
                "Summarize"),
        ],
        ApprovalGates:
        [
            new DecomposedGate(
                "GROUP-001-G01",
                "Review the baseline before crossing the boundary.",
                ["GROUP-001-001"],
                ["GROUP-001-003"]),
        ]);

    private static TaskItem MakeItem(string taskId, bool isChecked = false, bool isFailed = false,
        bool isPartial = false, bool isSuperseded = false)
    {
        return new TaskItem(
            Text:             taskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        isChecked,
            Emoji:            "🟡",
            RawLine:          $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** description",
            DecomposeGroupId: "GROUP-001",
            TaskId:           taskId,
            IsFailed:         isFailed,
            IsPartial:        isPartial,
            IsSuperseded:     isSuperseded);
    }

    private static Plan MakeExecutingPlan(int completed, int total, string? executingTaskId = null)
    {
        var progress   = new PlanProgress(completed, total, executingTaskId);
        var timestamps = new PlanTimestamps(DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow);
        return new Plan(
            PlanId:          "GROUP-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        progress,
            Timestamps:      timestamps);
    }

    // ── ApplyExecutionStarted ─────────────────────────────────────────────────

    [Test]
    public void ApplyExecutionStarted_CreatesNewPlanWhenNoneExists()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001"),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "GROUP-001-001");

        Assert.That(plan.PlanId,          Is.EqualTo("GROUP-001"));
        Assert.That(plan.Revision,        Is.EqualTo("rev1"));
        Assert.That(plan.HostRevision,    Is.EqualTo("rev1"));
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(plan.Title,           Is.EqualTo("Test Plan"));
        Assert.That(plan.Branch,          Is.EqualTo("feature/test"));
        Assert.That(plan.Source,          Is.EqualTo(PlanSource.DecomposeDecision));
    }

    [Test]
    public void ApplyExecutionStarted_FreshApprovalPlan_PreservesGateAndApprovedRevision()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();

        var plan = PlanStoreUpdater.ApplyExecutionStarted(
            existing: null,
            group,
            revision,
            items,
            "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(plan.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(plan.ApprovalGates[0].GateId, Is.EqualTo("GROUP-001-G01"));
            Assert.That(plan.ApprovalGates[0].AfterTaskIds, Is.EqualTo(new[] { "GROUP-001-001" }));
            Assert.That(plan.ApprovalGates[0].BeforeTaskIds, Is.EqualTo(new[] { "GROUP-001-003" }));
            Assert.That(plan.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(plan.ApprovalGates[0].PlanRevision, Is.EqualTo(revision));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(plan), Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_FreshValidationPlan_PreservesExplicitEmptyCollectionsAndRevision()
    {
        var group = MakeGroup(2) with
        {
            Validations =
            [
                new DecomposedValidationNode(
                    "GROUP-001-VAL-001",
                    "Validate projection",
                    "Validate the durable projection.",
                    ["GROUP-001-001"],
                    ["GROUP-001-002"],
                    ["The projection remains intact."],
                    OutputIds: [],
                    Commands: []),
            ],
        };
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();

        var plan = PlanStoreUpdater.ApplyExecutionStarted(
            existing: null,
            group,
            revision,
            items,
            "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations, Has.Count.EqualTo(1));
            Assert.That(plan.Validations![0].OutputIds, Is.Empty);
            Assert.That(plan.Validations[0].Commands, Is.Empty);
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(plan), Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_ApprovalWindow_AllowsIndependentTaskThenBlocksFrontier()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToList();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, revision, items, "GROUP-001-001");

        items[0] = MakeItem("GROUP-001-001", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, "GROUP-001-002");
        var afterBaseline = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.Multiple(() =>
        {
            Assert.That(afterBaseline.Single().IsReady, Is.True);
            Assert.That(
                ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, afterBaseline),
                Is.EqualTo("GROUP-001-002"),
                "Independent work should continue while approval is available.");
            Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan, afterBaseline), Is.False);
        });

        items[1] = MakeItem("GROUP-001-002", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, nextExecutingTaskId: null);
        var afterIndependent = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.Multiple(() =>
        {
            Assert.That(ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, afterIndependent), Is.Null);
            Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan, afterIndependent), Is.True);
            Assert.That(
                PlanGateVisualizationPolicy.DownstreamTaskIds(plan.Tasks, plan.ApprovalGates),
                Does.Contain("GROUP-001-003"),
                "The durable gate must remain available to the plan viewer.");
        });
    }

    [Test]
    public void ApplyExecutionStarted_SameRevisionResume_PreservesGateRuntimeState()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var existing = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, revision, items, "GROUP-001-001");
        existing = existing with
        {
            ApprovalGates =
            [
                existing.ApprovalGates[0] with
                {
                    Status = PlanGateStatus.AwaitingApproval,
                    RequestedAt = new DateTimeOffset(2026, 7, 31, 18, 0, 0, TimeSpan.Zero),
                },
            ],
        };

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            existing, group, revision, items, "GROUP-001-002");

        Assert.Multiple(() =>
        {
            Assert.That(resumed.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(resumed.ApprovalGates[0].RequestedAt, Is.EqualTo(
                new DateTimeOffset(2026, 7, 31, 18, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void ApplyExecutionStarted_ResumePreservesPendingVerificationBoundary()
    {
        var group = MakeGroup(2);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var existing = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, "rev1", items, "GROUP-001-001");
        var candidate = new DecomposeStepResult(
            "GROUP-001", "GROUP-001-001", "rev1", "complete", "abcdef1",
            "Candidate work", null,
            new DecomposeStepVerification("passed", "dotnet test", "green"));
        existing = PlanStoreUpdater.ApplyTaskVerificationPending(
            existing, "GROUP-001-001", candidate, ["src/Candidate.cs"]);

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            existing, group, "rev1", items, "GROUP-001-001");
        var repaired = PlanStoreUpdater.RepairInconsistentState(resumed, items);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.VerificationPending));
            Assert.That(resumed.Tasks[0].Handoff?.ChangedFiles, Does.Contain("src/Candidate.cs"));
            Assert.That(repaired.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-001"));
            Assert.That(ApprovalGateReadinessEvaluator.SelectNextUngatedTask(resumed),
                Is.Null,
                "Pending verification must not be scheduled as fresh implementation work.");
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(resumed, "GROUP-001-001"),
                Is.True,
                "A task with candidate work awaiting verification has already crossed its editable entry boundary.");
        });
    }

    [Test]
    public void ApplyExecutionStarted_NewRevision_ReplacesStaleStagedGateDefinition()
    {
        var originalGroup = MakeApprovalWindowGroup();
        var originalRevision = PendingDecomposePlanStore.ComputeRevision(originalGroup);
        var items = originalGroup.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var existing = PlanStoreUpdater.ApplyExecutionStarted(
            null, originalGroup, originalRevision, items, "GROUP-001-001");
        var revisedGroup = originalGroup with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    "GROUP-001-G02",
                    "Review later.",
                    ["GROUP-001-002"],
                    ["GROUP-001-004"]),
            ],
        };
        var revisedRevision = PendingDecomposePlanStore.ComputeRevision(revisedGroup);

        var updated = PlanStoreUpdater.ApplyExecutionStarted(
            existing, revisedGroup, revisedRevision, items, "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(updated.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(updated.ApprovalGates[0].GateId, Is.EqualTo("GROUP-001-G02"));
            Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(updated.ApprovalGates[0].PlanRevision, Is.EqualTo(revisedRevision));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(updated), Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_SetsCorrectProgress()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "GROUP-001-002");

        Assert.That(plan.Progress.CompletedCount,  Is.EqualTo(1));
        Assert.That(plan.Progress.TotalCount,      Is.EqualTo(3));
        Assert.That(plan.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-002"));
    }

    [Test]
    public void ApplyExecutionStarted_MapsTaskStatuses()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002", isFailed: true),
            MakeItem("GROUP-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null);

        Assert.That(plan.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(plan.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Failed));
        Assert.That(plan.Tasks[2].Status, Is.EqualTo(PlanTaskStatus.Pending));
    }

    [Test]
    public void ApplyExecutionStarted_UpdatesExistingPlanToExecuting()
    {
        var existing = MakeExecutingPlan(0, 3) with { LifecycleStatus = PlanLifecycleStatus.Blocked };
        var group    = MakeGroup(3);
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyExecutionStarted(existing, group, "rev1", items, "GROUP-001-002");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(updated.InterruptionData, Is.Null);
    }

    [Test]
    public void ApplyExecutionStarted_PreservesStartedAtOnResume()
    {
        var startTime = DateTimeOffset.UtcNow.AddHours(-1);
        var existing  = MakeExecutingPlan(1, 3) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Timestamps = new PlanTimestamps(
                CreatedAt: startTime,
                StartedAt: startTime),
        };
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyExecutionStarted(existing, group, "rev1", items, "GROUP-001-002");

        Assert.That(updated.Timestamps.StartedAt, Is.EqualTo(startTime),
            "StartedAt must not be reset on resume.");
    }

    [Test]
    public void ApplyExecutionStarted_PreservesAcceptedResultProvenanceOnResume()
    {
        var completedAt = new DateTimeOffset(2026, 7, 29, 15, 21, 42, TimeSpan.Zero);
        var durableTask = new PlanTask(
            TaskId: "GROUP-001-001",
            Title: "Old title",
            Description: "Old description",
            DependsOn: [],
            Priority: "low",
            Status: PlanTaskStatus.Complete,
            Commit: "774a047",
            CompletedAt: completedAt,
            CompletionSummary: "Verified and adopted.");
        var existing = MakeExecutingPlan(1, 1) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Tasks = [durableTask],
        };
        var assignment = new DecomposedAgentAssignment("vesper-knox", "test verifier", false);
        var group = new DecomposedTaskGroup(
            "GROUP-001",
            "Test Plan",
            "feature/test",
            "A test plan",
            [new DecomposedSubTask(
                "GROUP-001-001",
                "Current description",
                [],
                "high",
                "Current title",
                AgentAssignments: [assignment],
                ParallelEligible: true,
                AgentRoutingMode: "assigned")]);

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            existing,
            group,
            "rev1",
            [MakeItem("GROUP-001-001", isChecked: true)],
            executingTaskId: null);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Tasks[0].Commit, Is.EqualTo("774a047"));
            Assert.That(resumed.Tasks[0].CompletedAt, Is.EqualTo(completedAt));
            Assert.That(resumed.Tasks[0].CompletionSummary, Is.EqualTo("Verified and adopted."));
            Assert.That(resumed.Tasks[0].Title, Is.EqualTo("Current title"));
            Assert.That(resumed.Tasks[0].AgentAssignments?.Single().AgentHandle,
                Is.EqualTo("vesper-knox"));
            Assert.That(resumed.Tasks[0].AgentRoutingMode, Is.EqualTo("assigned"));
            Assert.That(resumed.Tasks[0].ParallelEligible, Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_ReplacesStaleRoutingMetadataFromRevisedPlan()
    {
        var staleTask = new PlanTask(
            TaskId: "GROUP-001-001",
            Title: "Task",
            Description: "Assigned task",
            DependsOn: [],
            Priority: "high",
            Status: PlanTaskStatus.Pending,
            AgentAssignments: [new PlanAgentAssignment("old-agent", "old role", false)],
            AgentRoutingMode: "assigned");
        var existing = MakeExecutingPlan(0, 1) with { Tasks = [staleTask] };
        var revisedGroup = new DecomposedTaskGroup(
            "GROUP-001",
            "Revised Plan",
            "feature/revised",
            "A revised plan",
            [new DecomposedSubTask(
                "GROUP-001-001",
                "Explicit generic task",
                [],
                "high",
                "Task",
                AgentRoutingMode: "generic",
                GenericAgentReason: "No roster specialist is required.")]);

        var updated = PlanStoreUpdater.ApplyExecutionStarted(
            existing,
            revisedGroup,
            "rev2",
            [MakeItem("GROUP-001-001")],
            "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(updated.Revision, Is.EqualTo("rev2"));
            Assert.That(updated.HostRevision, Is.EqualTo("rev2"));
            Assert.That(updated.Title, Is.EqualTo("Revised Plan"));
            Assert.That(updated.Branch, Is.EqualTo("feature/revised"));
            Assert.That(updated.Summary, Is.EqualTo("A revised plan"));
            Assert.That(updated.Tasks[0].AgentAssignments, Is.Null);
            Assert.That(updated.Tasks[0].AgentRoutingMode, Is.EqualTo("generic"));
            Assert.That(updated.Tasks[0].GenericAgentReason,
                Is.EqualTo("No roster specialist is required."));
        });
    }

    // ── ApplyStepAccepted ─────────────────────────────────────────────────────

    [Test]
    public void ApplyStepAccepted_IncrementsCompletedCount()
    {
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002", isChecked: true),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyStepAccepted(existing, items, "GROUP-001-003");

        Assert.That(updated.Progress.CompletedCount,  Is.EqualTo(2));
        Assert.That(updated.Progress.TotalCount,      Is.EqualTo(3));
        Assert.That(updated.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-003"));
    }

    [Test]
    public void ApplyStepAccepted_ClearsExecutingTaskIdWhenNull()
    {
        var existing = MakeExecutingPlan(2, 3, "GROUP-001-003");
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002", isChecked: true),
            MakeItem("GROUP-001-003", isChecked: true),
        };

        var updated = PlanStoreUpdater.ApplyStepAccepted(existing, items, null);

        Assert.That(updated.Progress.CompletedCount,  Is.EqualTo(3));
        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyStepAccepted_PreservesLifecycleStatus()
    {
        var existing = MakeExecutingPlan(0, 3, "GROUP-001-001");
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyStepAccepted(existing, items, "GROUP-001-002");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
    }

    [Test]
    public void ApplyAssessedStepAccepted_RejoinsOrdinaryExecutingBoundary()
    {
        var group = MakeGroup(1);
        var started = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, "rev1", [MakeItem("GROUP-001-001")], "GROUP-001-001");
        var interrupted = PlanStoreUpdater.ApplyInterrupted(
            started, "Assessment required.", 2, "GROUP-001-001");
        var result = new DecomposeStepResult(
            "GROUP-001", "GROUP-001-001", "rev1", "complete", "abc1234",
            "Assessed work is complete.", [],
            new DecomposeStepVerification("passed", "dotnet test", "Tests passed."));

        var updated = PlanStoreUpdater.ApplyAssessedStepAccepted(
            interrupted,
            [MakeItem("GROUP-001-001", isChecked: true)],
            nextExecutingTaskId: null,
            result);

        Assert.Multiple(() =>
        {
            Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(updated.InterruptionData, Is.Null);
            Assert.That(updated.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
            Assert.That(updated.Tasks[0].Commit, Is.EqualTo("abc1234"));
        });
    }

    [Test]
    public void ApplyStepAccepted_RecordsAcceptedResultProvenance()
    {
        var group = MakeGroup(1);
        var started = PlanStoreUpdater.ApplyExecutionStarted(
            null,
            group,
            "rev1",
            [MakeItem("GROUP-001-001")],
            "GROUP-001-001");
        var result = new DecomposeStepResult(
            GroupId: "GROUP-001",
            TaskId: "GROUP-001-001",
            Revision: "rev1",
            Status: "complete",
            Commit: "8935e51",
            Summary: "Documented verified routing.",
            RemainingWork: [],
            Verification: new DecomposeStepVerification("passed", "path-check", "Paths verified."));
        var before = DateTimeOffset.UtcNow;

        var accepted = PlanStoreUpdater.ApplyStepAccepted(
            started,
            [MakeItem("GROUP-001-001", isChecked: true)],
            nextExecutingTaskId: null,
            acceptedResult: result);
        var completed = PlanStoreUpdater.ApplyCompleted(accepted);

        Assert.Multiple(() =>
        {
            Assert.That(completed.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(completed.Tasks[0].Commit, Is.EqualTo("8935e51"));
            Assert.That(completed.Tasks[0].CompletionSummary,
                Is.EqualTo("Documented verified routing."));
            Assert.That(completed.Tasks[0].CompletedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        });
    }

    // ── ApplyBlocked ──────────────────────────────────────────────────────────

    [Test]
    public void ApplyBlocked_SetsStatusToBlocked()
    {
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-002");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
    }

    [Test]
    public void ApplyBlocked_ClearsExecutingTaskId()
    {
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-002");

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyBlocked_SetsInterruptedAt()
    {
        var before   = DateTimeOffset.UtcNow;
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-002");

        Assert.That(updated.Timestamps.InterruptedAt, Is.Not.Null);
        Assert.That(updated.Timestamps.InterruptedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyBlocked_PreservesProgressCounts()
    {
        var existing = MakeExecutingPlan(2, 5, "GROUP-001-003");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-003");

        Assert.That(updated.Progress.CompletedCount, Is.EqualTo(2));
        Assert.That(updated.Progress.TotalCount,     Is.EqualTo(5));
    }

    // ── ApplyCompleted ────────────────────────────────────────────────────────

    [Test]
    public void ApplyCompleted_SetsStatusToCompleted()
    {
        var existing = MakeExecutingPlan(5, 5, null);
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
    }

    [Test]
    public void ApplyCompleted_SetsCompletedAt()
    {
        var before   = DateTimeOffset.UtcNow;
        var existing = MakeExecutingPlan(5, 5, null);
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.Timestamps.CompletedAt, Is.Not.Null);
        Assert.That(updated.Timestamps.CompletedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyCompleted_ClearsExecutingTaskId()
    {
        var existing = MakeExecutingPlan(4, 5, "GROUP-001-005");
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyCompleted_PreservesProgressCounts()
    {
        var existing = MakeExecutingPlan(5, 5, null);
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.Progress.CompletedCount, Is.EqualTo(5));
        Assert.That(updated.Progress.TotalCount,     Is.EqualTo(5));
    }

    // ── BuildProgress ─────────────────────────────────────────────────────────

    [Test]
    public void BuildProgress_CountsCheckedAndSuperseded()
    {
        var items = new List<TaskItem>
        {
            MakeItem("T1", isChecked: true),
            MakeItem("T2", isSuperseded: true),
            MakeItem("T3"),
            MakeItem("T4", isFailed: true),
        };

        var progress = PlanStoreUpdater.BuildProgress(items, "T3");

        Assert.That(progress.CompletedCount,  Is.EqualTo(2), "Only checked + superseded count as complete.");
        Assert.That(progress.TotalCount,      Is.EqualTo(4));
        Assert.That(progress.ExecutingTaskId, Is.EqualTo("T3"));
    }

    [Test]
    public void BuildProgress_ReturnsZeroForEmptyItems()
    {
        var progress = PlanStoreUpdater.BuildProgress(new List<TaskItem>(), "T1");

        Assert.That(progress.CompletedCount, Is.EqualTo(0));
        Assert.That(progress.TotalCount,     Is.EqualTo(0));
    }

    // ── Persisted vs live state consistency ───────────────────────────────────

    [Test]
    public void PlanLifecycle_StartedToCompletedTransitionIsConsistent()
    {
        var group      = MakeGroup(2);
        var allPending = new List<TaskItem>
        {
            MakeItem("GROUP-001-001"),
            MakeItem("GROUP-001-002"),
        };

        // 1. Start
        var started = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", allPending, "GROUP-001-001");
        Assert.That(started.LifecycleStatus,        Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(started.Progress.CompletedCount, Is.EqualTo(0));

        // 2. Step 1 accepted
        var afterStep1Items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
        };
        var afterStep1 = PlanStoreUpdater.ApplyStepAccepted(started, afterStep1Items, "GROUP-001-002");
        Assert.That(afterStep1.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(afterStep1.Progress.CompletedCount,  Is.EqualTo(1));
        Assert.That(afterStep1.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-002"));

        // 3. Plan completes
        var completed = PlanStoreUpdater.ApplyCompleted(afterStep1);
        Assert.That(completed.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(completed.Progress.ExecutingTaskId, Is.Null);
        Assert.That(completed.Timestamps.CompletedAt,   Is.Not.Null);
    }

    [Test]
    public void PlanLifecycle_StartedToBlockedTransitionIsConsistent()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001"),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var started = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "GROUP-001-001");
        var blocked  = PlanStoreUpdater.ApplyBlocked(started, "GROUP-001-001");

        Assert.That(blocked.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(blocked.Progress.ExecutingTaskId, Is.Null,
            "Blocked plan must not show a current step.");
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Blocked), Is.False,
            "Blocked is not terminal — plan can be recovered.");
    }

    [Test]
    public void PlanLifecycle_ResumedPlanPreservesStartedAt()
    {
        var originalStart = DateTimeOffset.UtcNow.AddDays(-1);
        var existing = new Plan(
            PlanId:          "GROUP-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(2, 5, null),
            Timestamps:      new PlanTimestamps(
                CreatedAt: originalStart,
                StartedAt: originalStart));

        var group = MakeGroup(5);
        var items = Enumerable.Range(1, 5)
            .Select(i => MakeItem($"GROUP-001-00{i}", isChecked: i <= 2))
            .ToList<TaskItem>();

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(existing, group, "rev1", items, "GROUP-001-003");

        Assert.That(resumed.Timestamps.StartedAt, Is.EqualTo(originalStart),
            "Resuming must preserve the original StartedAt.");
        Assert.That(resumed.LifecycleStatus,        Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(resumed.Progress.CompletedCount, Is.EqualTo(2));
    }

    [Test]
    public void ApplyGateReworkRequested_ReopensOnlyReviewedTaskAndArchivesAcceptedAttempt()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, revision, items, "GROUP-001-001");
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with
                {
                    Status = PlanTaskStatus.Complete,
                    Commit = "abc1234",
                    CompletedAt = DateTimeOffset.UtcNow,
                    CompletionSummary = "Original result",
                }
                : task).ToArray(),
            ApprovalGates =
            [
                plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval },
            ],
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Progress = new PlanProgress(1, 4),
            Validations =
            [
                new PlanValidationNode(
                    "GROUP-001-VAL-001",
                    "Reviewed output is coherent",
                    "Validate the reviewed task output.",
                    ["GROUP-001-001"],
                    ["GROUP-001-002"],
                    ["The output is coherent."],
                    null,
                    "ai",
                    null,
                    false,
                    PlanValidationStatus.Passed,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ValidatedCommit: "abc1234",
                    Summary: "Previously passed.",
                    Evidence: ["Old output passed."]),
            ],
        };

        var updated = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GROUP-001-G01", ["GROUP-001-001"], "Add the missing restart test.");
        var task = updated.Tasks.Single(candidate => candidate.TaskId == "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(task.Commit, Is.Null);
            Assert.That(task.AttemptHistory, Has.Count.EqualTo(1));
            Assert.That(task.AttemptHistory![0].Commit, Is.EqualTo("abc1234"));
            Assert.That(task.AttemptHistory[0].Disposition, Is.EqualTo("changes-requested"));
            Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(updated.ApprovalGates[0].ReworkCount, Is.EqualTo(1));
            Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(updated.Progress.CompletedCount, Is.Zero);
            Assert.That(updated.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));
            Assert.That(updated.Validations[0].Summary, Does.Contain("Covered output changed"));
        });
    }

    [Test]
    public void ApplyGateReworkRequested_DoesNotReopenApprovedBoundary()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, revision, items, "GROUP-001-001");
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with { Status = PlanTaskStatus.Complete, Commit = "abc1234" }
                : task).ToArray(),
            ApprovalGates =
            [
                plan.ApprovalGates[0] with { Status = PlanGateStatus.Approved },
            ],
        };

        var updated = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GROUP-001-G01", ["GROUP-001-001"], "Try again.");

        Assert.That(updated, Is.SameAs(plan));
        Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-001").Status,
            Is.EqualTo(PlanTaskStatus.Complete));
    }

    [Test]
    public void ApplyGateAmendmentRequested_PreservesCompletedTasksAndAddsReviewedWork()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, revision, items, "GROUP-001-001");
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with
                {
                    Status = PlanTaskStatus.Complete,
                    Commit = "abc1234",
                    CompletedAt = DateTimeOffset.UtcNow,
                    CompletionSummary = "Accepted original result",
                }
                : task).ToArray(),
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Progress = new PlanProgress(1, 4),
            Validations =
            [
                new PlanValidationNode(
                    "GROUP-001-VAL-001", "Joined work", "Validate joined work.",
                    ["GROUP-001-001"], ["GROUP-001-002"], ["It is integrated."],
                    null, "ai", null, false, PlanValidationStatus.Passed,
                    CompletedAt: DateTimeOffset.UtcNow, ValidatedCommit: "abc1234",
                    Summary: "Passed.", Evidence: ["Evidence"]),
            ],
        };

        var updated = PlanStoreUpdater.ApplyGateAmendmentRequested(
            plan,
            "GROUP-001-G01",
            ["GROUP-001-001"],
            "Add restart cleanup",
            "Run cleanup on every exit path.");
        var original = updated.Tasks.Single(task => task.TaskId == "GROUP-001-001");
        var amendment = updated.Tasks.Single(task => task.AmendmentGateId == "GROUP-001-G01");

        Assert.Multiple(() =>
        {
            Assert.That(original.Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(original.Commit, Is.EqualTo("abc1234"));
            Assert.That(original.AttemptHistory, Is.Null);
            Assert.That(amendment.Status, Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(amendment.DisplayStepLabel, Is.EqualTo("3"));
            Assert.That(amendment.DependsOn, Is.EqualTo(new[] { "GROUP-001-001" }));
            Assert.That(amendment.Description, Does.Contain("Run cleanup on every exit path."));
            Assert.That(updated.Tasks.Select(task => task.TaskId), Is.EqualTo(new[]
            {
                "GROUP-001-001", "GROUP-001-002", amendment.TaskId,
                "GROUP-001-003", "GROUP-001-004",
            }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-003").DependsOn,
                Is.EqualTo(new[] { amendment.TaskId }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-003").DisplayStepLabel,
                Is.EqualTo("4"));
            Assert.That(updated.ApprovalGates[0].AfterTaskIds, Does.Contain(amendment.TaskId));
            Assert.That(updated.ApprovalGates[0].PresentationAnchor,
                Is.EqualTo($"task-after:{amendment.TaskId}"));
            Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(updated.Validations![0].AfterTaskIds, Does.Contain(amendment.TaskId));
            Assert.That(updated.Validations[0].Status, Is.EqualTo(PlanValidationStatus.Pending));
            Assert.That(updated.Progress, Is.EqualTo(new PlanProgress(1, 5)));
            Assert.That(updated.Revision, Is.Not.EqualTo(revision));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(updated), Is.True);
        });
    }

    [Test]
    public void ApplyGateAmendmentRequested_RewiresAllJoinBranchesThroughInsertedBarrier()
    {
        var group = new DecomposedTaskGroup(
            "JOIN-001", "Joined approval", "feature/join", "Review joined work.",
            [
                new DecomposedSubTask("JOIN-001-001", "Left", [], "high", "Left"),
                new DecomposedSubTask("JOIN-001-002", "Right", [], "high", "Right"),
                new DecomposedSubTask("JOIN-001-003", "Continue left", ["JOIN-001-001"], "high", "Continue left"),
                new DecomposedSubTask("JOIN-001-004", "Continue right", ["JOIN-001-002"], "high", "Continue right"),
            ],
            ApprovalGates:
            [
                new DecomposedGate("JOIN-001-G01", "Review joined work.",
                    ["JOIN-001-001", "JOIN-001-002"], ["JOIN-001-003", "JOIN-001-004"]),
            ]);
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId is "JOIN-001-001" or "JOIN-001-002"
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
        };

        var updated = PlanStoreUpdater.ApplyGateAmendmentRequested(
            plan, "JOIN-001-G01", null, "Polish joined result", "Polish both branches together.");
        var amendment = updated.Tasks.Single(task => task.AmendmentGateId == "JOIN-001-G01");

        Assert.Multiple(() =>
        {
            Assert.That(amendment.DependsOn,
                Is.EquivalentTo(new[] { "JOIN-001-001", "JOIN-001-002" }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "JOIN-001-003").DependsOn,
                Is.EqualTo(new[] { amendment.TaskId }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "JOIN-001-004").DependsOn,
                Is.EqualTo(new[] { amendment.TaskId }));
            Assert.That(updated.Tasks.ToList().IndexOf(amendment), Is.EqualTo(2));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(updated), Is.True);
        });
    }

    [Test]
    public void ApplyGateAmendmentRequested_RefusesToRewriteStartedDownstreamHistory()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId switch
            {
                "GROUP-001-001" => task with { Status = PlanTaskStatus.Complete },
                "GROUP-001-003" => task with { Status = PlanTaskStatus.Executing },
                _ => task,
            }).ToArray(),
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
        };

        var updated = PlanStoreUpdater.ApplyGateAmendmentRequested(
            plan, "GROUP-001-G01", null, "Late amendment", "Do not rewrite history.");

        Assert.That(updated, Is.SameAs(plan));
    }

    [Test]
    public void ApplyGateAmendmentRequested_PreservesExecutedLaterLabelAndSuffixesPendingOverflow()
    {
        var group = new DecomposedTaskGroup(
            "LABEL-001", "Stable labels", "feature/labels", "Keep historical labels.",
            [
                new DecomposedSubTask("LABEL-001-001", "Reviewed", [], "high", "Reviewed"),
                new DecomposedSubTask("LABEL-001-002", "Future", ["LABEL-001-001"], "high", "Future"),
                new DecomposedSubTask("LABEL-001-003", "Completed parallel work", [], "high", "Completed parallel work"),
            ],
            ApprovalGates:
            [
                new DecomposedGate("LABEL-001-G01", "Review.", ["LABEL-001-001"], ["LABEL-001-002"]),
            ]);
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId is "LABEL-001-001" or "LABEL-001-003"
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
        };

        var updated = PlanStoreUpdater.ApplyGateAmendmentRequested(
            plan, "LABEL-001-G01", null, "Inserted", "Insert before future work.");
        var amendment = updated.Tasks.Single(task => task.AmendmentGateId == "LABEL-001-G01");

        Assert.Multiple(() =>
        {
            Assert.That(amendment.DisplayStepLabel, Is.EqualTo("2"));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "LABEL-001-002").DisplayStepLabel,
                Is.EqualTo("2A"));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "LABEL-001-003").DisplayStepLabel,
                Is.EqualTo("3"));
        });
    }

    [Test]
    public void ApplyGateAmendmentRequested_RepeatedAmendmentDependsOnLatestAmendmentOnly()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
        };
        var first = PlanStoreUpdater.ApplyGateAmendmentRequested(
            plan, "GROUP-001-G01", null, "First amendment", "First change.");
        var firstAmendment = first.Tasks.Single(task => task.AmendmentGateId == "GROUP-001-G01");
        first = first with
        {
            Tasks = first.Tasks.Select(task => task.TaskId == firstAmendment.TaskId
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            ApprovalGates = [first.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
        };

        var second = PlanStoreUpdater.ApplyGateAmendmentRequested(
            first, "GROUP-001-G01", null, "Second amendment", "Second change.");
        var secondAmendment = second.Tasks.Single(task => task.Title == "Second amendment");

        Assert.Multiple(() =>
        {
            Assert.That(secondAmendment.DependsOn, Is.EqualTo(new[] { firstAmendment.TaskId }));
            Assert.That(second.Tasks.Single(task => task.TaskId == "GROUP-001-003").DependsOn,
                Is.EqualTo(new[] { secondAmendment.TaskId }));
            Assert.That(second.ApprovalGates[0].PresentationAnchor,
                Is.EqualTo($"task-after:{secondAmendment.TaskId}"));
        });
    }

    [Test]
    public void ApplyTaskInserted_AfterPendingTask_RewiresFutureWhileUnrelatedTaskRuns()
    {
        var group = new DecomposedTaskGroup(
            "INSERT-001", "Dynamic insertion", "feature/insert", "Edit the future graph.",
            [
                new DecomposedSubTask("INSERT-001-001", "Running elsewhere", [], "high", "Running elsewhere"),
                new DecomposedSubTask("INSERT-001-002", "Future source", [], "high", "Future source"),
                new DecomposedSubTask("INSERT-001-003", "Future dependent", ["INSERT-001-002"], "high", "Future dependent"),
            ],
            ApprovalGates:
            [
                new DecomposedGate("INSERT-001-G01", "Review future source.",
                    ["INSERT-001-002"], ["INSERT-001-003"]),
            ]);
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        plan = plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = plan.Tasks.Select(task => task.TaskId == "INSERT-001-001"
                ? task with { Status = PlanTaskStatus.Executing }
                : task).ToArray(),
            ApprovalGates =
            [
                plan.ApprovalGates[0] with { PresentationAnchor = "task-after:INSERT-001-002" },
            ],
            Progress = new PlanProgress(0, 3, "INSERT-001-001"),
        };

        var updated = PlanStoreUpdater.ApplyTaskInserted(
            plan, "INSERT-001-002", insertAfter: true,
            "Inserted review preparation", "Prepare the future result for review.");
        var inserted = updated.Tasks.Single(task => task.TaskId.StartsWith("INSERT-001-INS-", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(inserted.DependsOn, Is.EqualTo(new[] { "INSERT-001-002" }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "INSERT-001-003").DependsOn,
                Is.EqualTo(new[] { inserted.TaskId }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "INSERT-001-001").DisplayStepLabel,
                Is.EqualTo("1"));
            Assert.That(updated.Progress.ExecutingTaskId, Is.EqualTo("INSERT-001-001"));
            Assert.That(updated.ApprovalGates[0].AfterTaskIds, Does.Contain(inserted.TaskId));
            Assert.That(updated.ApprovalGates[0].PresentationAnchor,
                Is.EqualTo($"task-after:{inserted.TaskId}"));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(updated), Is.True);
        });
    }

    [Test]
    public void ApplyTaskInserted_BeforePendingTask_InheritsPrerequisitesAndMovesBoundary()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);

        var updated = PlanStoreUpdater.ApplyTaskInserted(
            plan, "GROUP-001-003", insertAfter: false,
            "Prepare boundary", "Prepare before crossing.");
        var inserted = updated.Tasks.Single(task => task.TaskId.StartsWith("GROUP-001-INS-", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(inserted.DependsOn, Is.EqualTo(new[] { "GROUP-001-001" }));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-003").DependsOn,
                Is.EqualTo(new[] { inserted.TaskId }));
            Assert.That(updated.ApprovalGates[0].BeforeTaskIds, Is.EqualTo(new[] { inserted.TaskId }));
        });
    }

    [Test]
    public void ApplyTaskInserted_AfterTaskRefusesStartedImmediateDependent()
    {
        var group = MakeGroup(3);
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-002"
                ? task with { Status = PlanTaskStatus.Executing }
                : task).ToArray(),
        };

        var updated = PlanStoreUpdater.ApplyTaskInserted(
            plan, "GROUP-001-001", insertAfter: true, "Too late", "Do not insert.");

        Assert.That(updated, Is.SameAs(plan));
    }

    [Test]
    public void RepairInconsistentState_RepairsLegacyInterruptedAmendmentSiblingTopology()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group), DateTimeOffset.UtcNow);
        var amendment = new PlanTask(
            "GROUP-001-AMD-001", "Legacy amendment", "Legacy sibling shape.",
            ["GROUP-001-001"], "high", PlanTaskStatus.Executing,
            AmendmentGateId: "GROUP-001-G01",
            DisplayStepLabel: "5");
        plan = plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Tasks = plan.Tasks.Append(amendment).ToArray(),
            ApprovalGates =
            [
                plan.ApprovalGates[0] with
                {
                    AfterTaskIds = ["GROUP-001-001", amendment.TaskId],
                    PresentationAnchor = "all:GROUP-001-003",
                },
            ],
        };

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.Multiple(() =>
        {
            Assert.That(repaired.Tasks.Select(task => task.TaskId), Is.EqualTo(new[]
            {
                "GROUP-001-001", "GROUP-001-002", amendment.TaskId,
                "GROUP-001-003", "GROUP-001-004",
            }));
            Assert.That(repaired.Tasks.Single(task => task.TaskId == "GROUP-001-003").DependsOn,
                Is.EqualTo(new[] { amendment.TaskId }));
            Assert.That(repaired.ApprovalGates[0].PresentationAnchor,
                Is.EqualTo($"task-after:{amendment.TaskId}"));
            Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(repaired), Is.True);
        });
    }

    [Test]
    public void RepairInconsistentState_ResequencesLegacyAmendmentAndUnacceptedSuffix()
    {
        var tasks = new[]
        {
            new PlanTask("P-1", "One", "One", [], "high", PlanTaskStatus.Complete,
                DisplayStepLabel: "1"),
            new PlanTask("P-2", "Two", "Two", ["P-1"], "high", PlanTaskStatus.Complete,
                DisplayStepLabel: "2"),
            new PlanTask("P-3", "Three", "Three", ["P-2"], "high", PlanTaskStatus.Complete,
                DisplayStepLabel: "3"),
            new PlanTask("P-4", "Four", "Four", ["P-3"], "high", PlanTaskStatus.Complete,
                DisplayStepLabel: "4"),
            new PlanTask("P-AMD-001", "Amendment", "Amend", ["P-4"], "high", PlanTaskStatus.Complete,
                AmendmentGateId: "P-G", DisplayStepLabel: "8"),
            new PlanTask("P-5", "Five", "Five", ["P-AMD-001"], "high",
                PlanTaskStatus.HumanReviewRequired, DisplayStepLabel: "9"),
            new PlanTask("P-6", "Six", "Six", ["P-5"], "high", PlanTaskStatus.Pending,
                DisplayStepLabel: "10"),
            new PlanTask("P-7", "Seven", "Seven", ["P-6"], "high", PlanTaskStatus.Pending,
                DisplayStepLabel: "11"),
        };
        var plan = new Plan(
            "P", "old", PlanSource.Inbox, PlanLifecycleStatus.Interrupted, "Plan", "feature/p",
            "Summary", tasks,
            [new PlanApprovalGate("P-G", "Review", ["P-4", "P-AMD-001"], ["P-5"],
                PlanGateStatus.Approved, PlanRevision: "old")],
            new PlanProgress(5, 8), new PlanTimestamps(DateTimeOffset.UtcNow));

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.Multiple(() =>
        {
            Assert.That(repaired.Tasks.Select(task => task.DisplayStepLabel),
                Is.EqualTo(new[] { "1", "2", "3", "4", "5", "6", "7", "8" }));
            Assert.That(repaired.Revision, Is.Not.EqualTo("old"));
            Assert.That(repaired.ApprovalGates[0].PlanRevision, Is.EqualTo(repaired.Revision));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(repaired), Is.True);
        });
    }

    [Test]
    public void RepairInconsistentState_DoesNotRelabelAcceptedTaskAfterAmendment()
    {
        var tasks = new[]
        {
            new PlanTask("P-1", "One", "One", [], "high", PlanTaskStatus.Complete,
                DisplayStepLabel: "1"),
            new PlanTask("P-AMD-001", "Amendment", "Amend", ["P-1"], "high", PlanTaskStatus.Complete,
                AmendmentGateId: "P-G", DisplayStepLabel: "4"),
            new PlanTask("P-2", "Two", "Two", ["P-AMD-001"], "high", PlanTaskStatus.Complete,
                DisplayStepLabel: "5"),
        };
        var plan = new Plan(
            "P", "old", PlanSource.Inbox, PlanLifecycleStatus.Interrupted, "Plan", "feature/p",
            "Summary", tasks, [], new PlanProgress(3, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.Tasks.Select(task => task.DisplayStepLabel),
            Is.EqualTo(new[] { "1", "4", "5" }));
    }

    [Test]
    public void RepairInconsistentState_WithdrawsAiRecoveryAcceptanceThatContradictsVerification()
    {
        var accepted = new PlanTask(
            "P-2", "Second", "Second", ["P-1"], "high", PlanTaskStatus.Complete,
            Commit: "bbbbbbb",
            CompletedAt: DateTimeOffset.UtcNow,
            CompletionSummary: "AI-assessed recovery: task complete",
            Handoff: new PlanTaskHandoff(
                "bbbbbbb", "Candidate work", ["src/Work.cs"], null, DateTimeOffset.UtcNow),
            VerificationHistory:
            [
                new PlanTaskVerificationReport(
                    PlanTaskVerificationVerdict.HumanReviewRequired,
                    "Approval actions remain enabled.", [], ["Missing action guard"],
                    "Tests do not cover the production action.", ["Add action guard."],
                    "bbbbbbb", DateTimeOffset.UtcNow),
            ]);
        var tasks = new[]
        {
            new PlanTask("P-1", "First", "First", [], "high", PlanTaskStatus.Complete,
                Commit: "aaaaaaa", DisplayStepLabel: "1"),
            accepted with { DisplayStepLabel = "2" },
            new PlanTask("P-3", "Third", "Third", ["P-2"], "high", PlanTaskStatus.Pending,
                DisplayStepLabel: "3"),
        };
        var plan = new Plan(
            "P", "rev", PlanSource.Inbox, PlanLifecycleStatus.Interrupted,
            "Plan", "feature/p", "Summary", tasks, [], new PlanProgress(2, 3),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            InterruptionData: new PlanInterruptionData(
                "AI accepted task", PlanRecoveryState.PendingRecovery, 2,
                InterruptedTaskId: "P-3", LastCompletedTaskId: "P-2", LastCommit: "bbbbbbb"));

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);
        var repairedTask = repaired.Tasks.Single(task => task.TaskId == "P-2");

        Assert.Multiple(() =>
        {
            Assert.That(repairedTask.Status, Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
            Assert.That(repairedTask.Commit, Is.Null);
            Assert.That(repairedTask.Handoff?.Commit, Is.EqualTo("bbbbbbb"));
            Assert.That(repairedTask.VerificationHistory, Has.Count.EqualTo(1));
            Assert.That(repaired.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(repaired.InterruptionData?.InterruptedTaskId, Is.EqualTo("P-2"));
            Assert.That(repaired.InterruptionData?.LastCompletedTaskId, Is.EqualTo("P-1"));
            Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
        });
    }

    [Test]
    public void ConvertUnstartedGateReworkToAmendment_RestoresAcceptedAttemptBeforeAddingWork()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, revision, items, "GROUP-001-001");
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with
                {
                    Status = PlanTaskStatus.Complete,
                    Commit = "abc1234",
                    CompletedAt = DateTimeOffset.UtcNow,
                    CompletionSummary = "Accepted result",
                }
                : task).ToArray(),
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval }],
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
        };
        var misclassified = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GROUP-001-G01", ["GROUP-001-001"], "Add cleanup across the joined result.");
        misclassified = PlanStoreUpdater.ApplyInterrupted(
            misclassified, "Preflight blocked", 0, "GROUP-001-001");

        var repaired = PlanStoreUpdater.ConvertUnstartedGateReworkToAmendment(
            misclassified,
            "GROUP-001-G01",
            ["GROUP-001-001"],
            "Add joined cleanup",
            "Add cleanup across the joined result.");
        var original = repaired.Tasks.Single(task => task.TaskId == "GROUP-001-001");
        var amendment = repaired.Tasks.Single(task => task.AmendmentGateId == "GROUP-001-G01");

        Assert.Multiple(() =>
        {
            Assert.That(original.Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(original.Commit, Is.EqualTo("abc1234"));
            Assert.That(original.AttemptHistory, Is.Null);
            Assert.That(amendment.Status, Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(repaired.InterruptionData, Is.Null);
            Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(repaired), Is.True);
        });
    }

    [Test]
    public void ApplyTaskStarted_MarksAuthoritativeTaskAndProgressExecuting()
    {
        var group = MakeGroup(3);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, "rev1", items, "GROUP-001-001");

        var updated = PlanStoreUpdater.ApplyTaskStarted(plan, "GROUP-001-002");

        Assert.Multiple(() =>
        {
            Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(updated.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-002"));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-002").Status,
                Is.EqualTo(PlanTaskStatus.Executing));
            Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-001").Status,
                Is.EqualTo(PlanTaskStatus.Pending));
        });
    }

    [Test]
    public void ApplyArchived_PreservesHistoryAndHidesPlanAsTerminal()
    {
        var group = MakeGroup(2);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null) with
        {
            LifecycleStatus = PlanLifecycleStatus.Approved,
        };

        var archived = PlanStoreUpdater.ApplyArchived(plan);

        Assert.Multiple(() =>
        {
            Assert.That(archived.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Archived));
            Assert.That(archived.Timestamps.ArchivedAt, Is.Not.Null);
            Assert.That(archived.Tasks, Is.EqualTo(plan.Tasks));
            Assert.That(PlanLifecycleStatus.IsTerminal(archived.LifecycleStatus), Is.True);
        });
    }

    [Test]
    public void ApplyArchived_RefusesActivelyExecutingPlan()
    {
        var plan = MakeExecutingPlan(0, 1, "GROUP-001-001");

        Assert.That(PlanStoreUpdater.ApplyArchived(plan), Is.SameAs(plan));
    }

    [Test]
    public void ApplyRestoredForRevision_NeverRunArchivedPlan_ReturnsToApproved()
    {
        var group = MakeGroup(2);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var approved = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null) with
        {
            LifecycleStatus = PlanLifecycleStatus.Approved,
        };
        var plan = PlanStoreUpdater.ApplyArchived(approved);

        var restored = PlanStoreUpdater.ApplyRestoredForRevision(plan);

        Assert.Multiple(() =>
        {
            Assert.That(restored.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(restored.Timestamps.ArchivedAt, Is.Null);
            Assert.That(restored.PlanId, Is.EqualTo(plan.PlanId));
            Assert.That(restored.Revision, Is.EqualTo(plan.Revision));
        });
    }

    [Test]
    public void ApplyRestoredForRevision_CompletedPlan_PreservesCompletedState()
    {
        var completedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var group = MakeGroup(2);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var completed = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null) with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            Timestamps = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null).Timestamps with
            {
                CompletedAt = completedAt,
            },
        };
        var plan = PlanStoreUpdater.ApplyArchived(completed);

        var restored = PlanStoreUpdater.ApplyRestoredForRevision(plan);

        Assert.That(restored.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(restored.Timestamps.CompletedAt, Is.EqualTo(completedAt));
        Assert.That(restored.Timestamps.ArchivedAt, Is.Null);
    }
}
