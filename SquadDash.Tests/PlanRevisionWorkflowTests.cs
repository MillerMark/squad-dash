using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRevisionWorkflowTests
{
    [Test]
    public void TranscriptPresentation_UsesExplicitNoChangeAndAppliedSummaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PlanRevisionTranscriptPresentation.BuildNoChanges(0),
                Is.EqualTo("No changes found in the plan revision."));
            Assert.That(
                PlanRevisionTranscriptPresentation.BuildApplied(4, 6, 2),
                Is.EqualTo("✅ Plan updated to revision 4. Applied 6 downstream changes. Preserved 2 completed or active tasks."));
            Assert.That(
                PlanRevisionTranscriptPresentation.BuildApplied(4, 6, 0),
                Is.EqualTo("✅ Plan updated to revision 4. Applied 6 downstream changes."));
        });
    }

    [Test]
    public void PromptAttachment_RoundTripsPlanIdentityAndBaseRevision()
    {
        var plan = MakePlan();

        var attachment = PlanRevisionPromptContextParser.BuildAttachment(plan);

        Assert.That(PlanRevisionPromptContextParser.TryParse(attachment, out var context), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(context!.PlanId, Is.EqualTo(plan.PlanId));
            Assert.That(context.BaseRevision, Is.EqualTo(plan.Revision));
            Assert.That(attachment, Does.Contain("TASKS_JSON:"));
            Assert.That(attachment, Does.Contain("same groupId"));
        });
    }

    [Test]
    public void Apply_IdenticalDefinition_ReturnsNoChanges()
    {
        var plan = MakePlan();
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group;

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.NoChanges));
            Assert.That(result.AppliedChangeCount, Is.Zero);
            Assert.That(result.UpdatedPlan, Is.Null);
        });
    }

    [Test]
    public void Apply_ChangedPendingTask_IncrementsRevisionAndPreservesRuntimeState()
    {
        var plan = MakePlan() with { LifecycleStatus = PlanLifecycleStatus.Approved };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group;
        proposal = proposal with
        {
            Tasks = proposal.Tasks.Select(task => task.Id.EndsWith("003", StringComparison.Ordinal)
                ? task with { Description = "Revised downstream implementation" }
                : task).ToArray(),
        };
        var revisedAt = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, revisedAt);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied));
            Assert.That(result.AppliedChangeCount, Is.EqualTo(1));
            Assert.That(result.UpdatedPlan!.Revision, Is.Not.EqualTo(plan.Revision));
            Assert.That(result.UpdatedPlan.RevisionNumber, Is.EqualTo(2));
            Assert.That(result.UpdatedPlan.RevisedAt, Is.EqualTo(revisedAt));
            Assert.That(result.UpdatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(result.UpdatedPlan.Tasks[2].Description, Is.EqualTo("Revised downstream implementation"));
        });
    }

    [Test]
    public void Apply_DuringExecution_PreservesCompletedAndActiveTasksButRevisesDownstreamTask()
    {
        var original = MakePlan();
        var runtimeTasks = original.Tasks.Select((task, index) => index switch
        {
            0 => task with
            {
                Status = PlanTaskStatus.Complete,
                Commit = "abc123",
                CompletionSummary = "Completed safely",
            },
            1 => task with { Status = PlanTaskStatus.Executing },
            _ => task,
        }).ToArray();
        var plan = original with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = runtimeTasks,
            Progress = new PlanProgress(1, 3, runtimeTasks[1].TaskId),
        };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group with
        {
            Tasks = PendingDecomposePlanAdapter.FromPlan(plan).Group.Tasks.Select(task =>
                task with { Description = "AI changed " + task.Id }).ToArray(),
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied));
            Assert.That(result.PreservedLockedTaskCount, Is.EqualTo(2));
            Assert.That(result.UpdatedPlan!.Tasks[0].Description, Is.EqualTo(original.Tasks[0].Description));
            Assert.That(result.UpdatedPlan.Tasks[0].Commit, Is.EqualTo("abc123"));
            Assert.That(result.UpdatedPlan.Tasks[1].Description, Is.EqualTo(original.Tasks[1].Description));
            Assert.That(result.UpdatedPlan.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Executing));
            Assert.That(result.UpdatedPlan.Tasks[2].Description, Is.EqualTo("AI changed " + runtimeTasks[2].TaskId));
            Assert.That(result.UpdatedPlan.Progress.ExecutingTaskId, Is.EqualTo(runtimeTasks[1].TaskId));
        });
    }

    [Test]
    public void Apply_StaleBaseRevision_IsRejected()
    {
        var plan = MakePlan();
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group;

        var result = PlanRevisionApplier.Apply(plan, proposal, "old-revision", DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Stale));
        Assert.That(result.Error, Does.Contain("current plan"));
    }

    [Test]
    public void Apply_ArchivedUnstartedPlan_ReactivatesLatestRevisionAsStaged()
    {
        var plan = MakePlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Archived,
            Timestamps = MakePlan().Timestamps with { ArchivedAt = DateTimeOffset.UtcNow },
        };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group;
        proposal = proposal with { Summary = "A newly revised archived plan" };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied));
            Assert.That(result.UpdatedPlan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Staged));
            Assert.That(result.UpdatedPlan.Timestamps.ArchivedAt, Is.Null);
        });
    }

    [Test]
    public void ActiveExecution_Normalize_PreservesPendingFutureRevision()
    {
        var state = new ActiveLoopExecutionState(
            LoopPath: "loop.md",
            FilterText: "revision",
            DecomposeGroupId: "REVISION-20260806",
            DecomposeRevision: "revision-1",
            PendingDecomposeRevision: "revision-2");

        var normalized = ActiveLoopExecutionState.Normalize(state);

        Assert.That(normalized!.PendingDecomposeRevision, Is.EqualTo("revision-2"));
    }

    [Test]
    public void ArtifactResponse_IsResolvedAsTasksJson()
    {
        using var workspace = new TestWorkspace();
        var group = MakeGroup();
        var relativePath = Path.Combine(".squad", "tmp", "agent-artifacts", "revised-plan.json");
        workspace.CreateFile(relativePath, JsonSerializer.Serialize(group));
        var response = $$"""
            SQUADDASH_ARTIFACT_JSON:
            { "path": "{{relativePath.Replace('\\', '/')}}", "language": "json", "display": "code_block" }
            """;

        var resolved = TaskPlanResponseResolver.TryResolve(
            response, workspace.RootPath, out var parserInput, out var source);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(parserInput, Does.StartWith("TASKS_JSON:"));
            Assert.That(source, Is.EqualTo(relativePath));
            Assert.That(TasksJsonParser.TryParse(parserInput, out var parsed), Is.True);
            Assert.That(parsed!.GroupId, Is.EqualTo(group.GroupId));
        });
    }

    [Test]
    public void HistoryStore_SavesPreviousRevisionSnapshot()
    {
        using var workspace = new TestWorkspace();
        var plan = MakePlan();
        var store = new PlanRevisionHistoryStore(workspace.GetPath(".squad"));

        var path = store.SaveSnapshot(plan);
        var snapshots = store.LoadSnapshots(plan.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True);
            Assert.That(snapshots, Has.Count.EqualTo(1));
            Assert.That(snapshots[0].Revision, Is.EqualTo(plan.Revision));
        });
    }

    private static Plan MakePlan()
    {
        var group = MakeGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        return PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group),
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));
    }

    private static DecomposedTaskGroup MakeGroup() => new(
        GroupId: "REVISION-20260806",
        GroupTitle: "Revision workflow",
        Branch: "feature/revision-workflow",
        Summary: "Exercise plan revision behavior",
        Tasks:
        [
            MakeTask("001", "Create foundation", []),
            MakeTask("002", "Build feature", ["REVISION-20260806-001"]),
            MakeTask("003", "Verify behavior", ["REVISION-20260806-002"]),
        ]);

    private static DecomposedSubTask MakeTask(string suffix, string description, IReadOnlyList<string> dependsOn) =>
        new(
            Id: $"REVISION-20260806-{suffix}",
            Description: description,
            DependsOn: dependsOn,
            Priority: "high",
            Title: description);
}
