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
            Assert.That(attachment, Does.Contain("CURRENT_PLAN_JSON:"));
            Assert.That(attachment, Does.Contain("delta operations only"));
            Assert.That(attachment, Does.Contain("reference input only"));
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

    [Test]
    public void ProposalParser_ParsesDeltaOperations()
    {
        var group = MakeGroup();
        var json = JsonSerializer.Serialize(new
        {
            planId = group.GroupId,
            baseRevision = "base-1",
            summary = "Move attribution into the completion footer.",
            operations = new[]
            {
                new { op = "reopenTask", targetId = "REVISION-20260806-002" },
            },
        });

        var parsed = PlanRevisionProposalParser.TryParse(
            PlanRevisionProposalParser.Marker + "\n" + json,
            out var proposal,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(proposal!.PlanId, Is.EqualTo(group.GroupId));
            Assert.That(proposal.Operations, Has.Count.EqualTo(1));
            Assert.That(proposal.Operations![0].Op, Is.EqualTo("reopenTask"));
            Assert.That(proposal.Operations[0].TargetId, Is.EqualTo("REVISION-20260806-002"));
            Assert.That(proposal.RevisedPlan, Is.Null);
        });
    }

    [Test]
    public void ProposalParser_RejectsLegacyFullPlanResponse()
    {
        var group = MakeGroup();
        var json = JsonSerializer.Serialize(new
        {
            planId = group.GroupId,
            baseRevision = "base-1",
            summary = "Change it",
            revisedPlan = group,
        });

        var parsed = PlanRevisionProposalParser.TryParse(
            PlanRevisionProposalParser.Marker + "\n" + json,
            out _,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("operations"));
            Assert.That(error, Does.Contain("Do not return revisedPlan"));
        });
    }

    [Test]
    public void ProposalParser_RejectsMultipleRevisionObjects()
    {
        var group = MakeGroup();
        var json = JsonSerializer.Serialize(new
        {
            planId = group.GroupId,
            baseRevision = "base-1",
            summary = "Change it",
            operations = new[] { new { op = "updatePlan", patch = new { summary = "Changed" } } },
        });

        var parsed = PlanRevisionProposalParser.TryParse(
            $"{PlanRevisionProposalParser.Marker}\n{json}\n{PlanRevisionProposalParser.Marker}\n{json}",
            out _,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("exactly one"));
        });
    }

    [Test]
    public void ProposalStore_RoundTripsAndReplacesPriorProposalForPlan()
    {
        using var workspace = new TestWorkspace();
        var group = MakeGroup();
        var payload = new PlanRevisionProposalPayload(
            group.GroupId, "base-1", "First proposal", null, group);
        var store = new PendingPlanRevisionProposalStore(workspace.GetPath(".squad"));

        var first = store.Save(payload);
        var second = store.Save(payload with { Summary = "Replacement proposal" });
        var loaded = store.Load(group.GroupId);
        var all = store.LoadAll();

        Assert.Multiple(() =>
        {
            Assert.That(second.ProposalId, Is.Not.EqualTo(first.ProposalId));
            Assert.That(loaded!.ProposalId, Is.EqualTo(second.ProposalId));
            Assert.That(loaded.Payload.Summary, Is.EqualTo("Replacement proposal"));
            Assert.That(all.Select(item => item.ProposalId), Is.EqualTo(new[] { second.ProposalId }));
        });
    }

    [Test]
    public void PromptInjection_IncludesExecutingPlanOnlyAfterLoopYields()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        var plan = MakePlan() with { LifecycleStatus = PlanLifecycleStatus.Executing };
        new PlanStore(squadFolder).Save(plan);

        var whileLoopRuns = PlanRevisionPromptInjection.Build(squadFolder, includeExecutingPlan: false);
        var afterLoopYields = PlanRevisionPromptInjection.Build(squadFolder, includeExecutingPlan: true);

        Assert.Multiple(() =>
        {
            Assert.That(whileLoopRuns, Is.Empty);
            Assert.That(afterLoopYields, Does.Contain(plan.PlanId));
            Assert.That(afterLoopYields, Does.Contain(PlanRevisionProposalParser.Marker));
            Assert.That(afterLoopYields, Does.Contain("human must approve"));
        });
    }

    [Test]
    public void PromptInjection_IncludesAwaitingApprovalWithoutExecutingOverride()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        var plan = MakePlan() with { LifecycleStatus = PlanLifecycleStatus.AwaitingApproval };
        new PlanStore(squadFolder).Save(plan);

        var injection = PlanRevisionPromptInjection.Build(squadFolder, includeExecutingPlan: false);

        Assert.That(injection, Does.Contain(plan.PlanId));
    }

    [Test]
    public void DispatchPolicy_AllowsUserQueueItemAtRunningLoopBoundaryButNotPlanTurn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PromptExecutionController.ShouldAllowExecutingPlanRevision(true, dispatchedItem: null),
                Is.False);
            Assert.That(
                PromptExecutionController.ShouldAllowExecutingPlanRevision(
                    true,
                    new PromptQueueItem { Text = "Revise step 7", IsSystemInjected = false }),
                Is.True);
            Assert.That(
                PromptExecutionController.ShouldAllowExecutingPlanRevision(
                    true,
                    new PromptQueueItem { Text = "Continue plan", IsSystemInjected = true }),
                Is.False);
        });
    }

    [Test]
    public void Apply_ApprovedRevisionReopensChangedCompletedTaskAndPreservesPriorAttempt()
    {
        var original = MakePlan();
        var completedTask = original.Tasks[1] with
        {
            Status = PlanTaskStatus.Complete,
            Commit = "abc123",
            CompletedAt = DateTimeOffset.UtcNow,
            CompletionSummary = "Original implementation",
        };
        var plan = original with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = original.Tasks.Select((task, index) => index == 1 ? completedTask : task).ToArray(),
            Progress = new PlanProgress(1, 3),
        };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group with
        {
            Tasks = PendingDecomposePlanAdapter.FromPlan(plan).Group.Tasks.Select(task =>
                task.Id == completedTask.TaskId
                    ? task with { Description = "Revised completion-footer attribution" }
                    : task).ToArray(),
        };

        var result = PlanRevisionApplier.Apply(
            plan,
            proposal,
            plan.Revision,
            DateTimeOffset.UtcNow,
            new HashSet<string>(StringComparer.Ordinal) { completedTask.TaskId });

        var reopened = result.UpdatedPlan!.Tasks[1];
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied));
            Assert.That(result.UpdatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(reopened.Status, Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(reopened.Description, Is.EqualTo("Revised completion-footer attribution"));
            Assert.That(reopened.Commit, Is.Null);
            Assert.That(reopened.AttemptHistory, Has.Count.EqualTo(1));
            Assert.That(reopened.AttemptHistory![0].Commit, Is.EqualTo("abc123"));
            Assert.That(reopened.AttemptHistory[0].Disposition, Is.EqualTo("plan-revision"));
        });
    }

    [Test]
    public void Apply_ReopenedTaskUsesRevisedCheckpointDefinitionAndResetsItsRuntimeState()
    {
        var basePlan = MakePlan();
        var taskId = basePlan.Tasks[1].TaskId;
        var oldGate = new PlanApprovalGate(
            taskId + "-HUMAN-PROOF",
            "Confirm the old transcript attribution.",
            [taskId],
            [basePlan.Tasks[2].TaskId],
            PlanGateStatus.AwaitingApproval,
            RequestedAt: DateTimeOffset.UtcNow,
            PlanRevision: basePlan.Revision,
            Question: "Is the old attribution visible?");
        var plan = basePlan with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = basePlan.Tasks.Select(task => task.TaskId == taskId
                ? task with { Status = PlanTaskStatus.Complete, Commit = "abc123" }
                : task).ToArray(),
            ApprovalGates = [oldGate],
            Progress = new PlanProgress(1, 3),
        };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group with
        {
            Tasks = PendingDecomposePlanAdapter.FromPlan(plan).Group.Tasks.Select(task => task.Id == taskId
                ? task with { Description = "Show attribution in the completion footer." }
                : task).ToArray(),
            ApprovalGates =
            [
                new DecomposedGate(
                    oldGate.GateId,
                    "Confirm the revised completion footer.",
                    [taskId],
                    [basePlan.Tasks[2].TaskId],
                    null,
                    "Is the model profile shown in the completion footer?"),
            ],
        };

        var result = PlanRevisionApplier.Apply(
            plan, proposal, plan.Revision, DateTimeOffset.UtcNow,
            new HashSet<string>([taskId], StringComparer.Ordinal));

        Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
        var gate = result.UpdatedPlan!.ApprovalGates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(gate.Message, Is.EqualTo("Confirm the revised completion footer."));
            Assert.That(gate.Question, Is.EqualTo("Is the model profile shown in the completion footer?"));
            Assert.That(gate.RequestedAt, Is.Null);
        });
    }

    [Test]
    public void DeltaMaterializer_ReopensCompletedTaskAndChangesOnlyTargetedDefinitions()
    {
        var original = MakePlan();
        var completed = original.Tasks[1] with
        {
            Status = PlanTaskStatus.Complete,
            Commit = "abc123",
            CompletionSummary = "Original implementation",
        };
        var plan = original with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = original.Tasks.Select((task, index) => index == 1 ? completed : task).ToArray(),
            Progress = new PlanProgress(1, 3),
        };
        using var patchDocument = JsonDocument.Parse("""{"description":"Footer attribution"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Revise Step 2",
            null,
            Operations:
            [
                new PlanRevisionOperation("reopenTask", completed.TaskId),
                new PlanRevisionOperation("updateTask", completed.TaskId, patchDocument.RootElement.Clone()),
            ]);

        var materialized = PlanRevisionDeltaApplier.TryMaterialize(
            plan, payload, out var revised, out var reopened, out var error);
        var result = PlanRevisionApplier.Apply(
            plan, revised!, plan.Revision, DateTimeOffset.UtcNow, reopened);

        Assert.Multiple(() =>
        {
            Assert.That(materialized, Is.True, error);
            Assert.That(reopened, Does.Contain(completed.TaskId));
            Assert.That(revised!.Tasks[0].Description, Is.EqualTo(original.Tasks[0].Description));
            Assert.That(revised.Tasks[1].Description, Is.EqualTo("Footer attribution"));
            Assert.That(revised.Tasks[2].Description, Is.EqualTo(original.Tasks[2].Description));
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied));
            Assert.That(result.UpdatedPlan!.Tasks[1].AttemptHistory, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void DeltaMaterializer_RejectsCompletedTaskUpdateWithoutReopen()
    {
        var original = MakePlan();
        var completed = original.Tasks[1] with { Status = PlanTaskStatus.Complete };
        var plan = original with
        {
            Tasks = original.Tasks.Select((task, index) => index == 1 ? completed : task).ToArray(),
        };
        using var patchDocument = JsonDocument.Parse("""{"description":"Changed"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Invalid revision",
            null,
            Operations:
            [
                new PlanRevisionOperation("updateTask", completed.TaskId, patchDocument.RootElement.Clone()),
            ]);

        var materialized = PlanRevisionDeltaApplier.TryMaterialize(
            plan, payload, out _, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(materialized, Is.False);
            Assert.That(error, Does.Contain("must be reopened"));
        });
    }

    [Test]
    public void DeltaMaterializer_RejectsUnknownPatchField()
    {
        var plan = MakePlan();
        using var patchDocument = JsonDocument.Parse("""{"status":"complete"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Invalid runtime mutation",
            null,
            Operations:
            [
                new PlanRevisionOperation("updateTask", plan.Tasks[2].TaskId, patchDocument.RootElement.Clone()),
            ]);

        var materialized = PlanRevisionDeltaApplier.TryMaterialize(
            plan, payload, out _, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(materialized, Is.False);
            Assert.That(error, Does.Contain("cannot patch 'status'"));
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
