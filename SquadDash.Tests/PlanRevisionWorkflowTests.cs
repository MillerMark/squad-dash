using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRevisionWorkflowTests
{
    [Test]
    public void ProposedPlanPreview_PreservesVisibleAgentAssignments()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "SquadDash", "MainWindow.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));
        var constructorStart = source.IndexOf(
            "var previewWindow = new PlanViewerWindow(",
            StringComparison.Ordinal);
        var constructorEnd = source.IndexOf(
            "previewWindow.Show();",
            constructorStart,
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(constructorStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(constructorEnd, Is.GreaterThan(constructorStart));
        });

        var proposedPreviewConstruction = source[constructorStart..constructorEnd];
        Assert.That(proposedPreviewConstruction,
            Does.Contain("resolveAgentAvatar: ResolveAgentAvatarForPlanViewer"),
            "The proposed-plan preview must render assignments for completed and remaining steps.");
    }

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
    public void ApprovalPresentation_CombinesInterruptedRecoveryWithCurrentSavedRevision()
    {
        var plan = MakePlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                "The step result was invalid after automatic repair.",
                PlanRecoveryState.PendingRecovery,
                3,
                InterruptedTaskId: "REVISION-20260806-002",
                AttentionCategory: PlanAttentionCategory.AutomaticWorkExhausted),
        };
        var pending = new PendingPlanRevisionProposal(
            "proposal-1",
            new DateTimeOffset(2026, 10, 12, 8, 46, 0, TimeSpan.FromHours(-4)),
            new PlanRevisionProposalPayload(
                plan.PlanId,
                plan.Revision,
                "Use the narrower supervised path.",
                null,
                Operations: []));

        var presentation = PlanRevisionApprovalPresentationBuilder.Build(plan, pending);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.OwnsInterruptedRecovery, Is.True);
            Assert.That(presentation.Title, Is.EqualTo("Plan stopped - saved revision ready"));
            Assert.That(presentation.Context, Does.Contain("Build feature"));
            Assert.That(presentation.RecordedReason, Does.Contain("invalid after automatic repair"));
            Assert.That(presentation.Recommendation, Does.Contain("saved on Oct 12, 2026"));
            Assert.That(presentation.Recommendation, Does.Contain("review the proposed plan, then approve it"));
            Assert.That(presentation.Status, Does.Contain("continue from the preserved interruption"));
        });
    }

    [Test]
    public void ApprovalPresentation_DoesNotReplaceRecoveryForStaleProposal()
    {
        var plan = MakePlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                "Stopped",
                PlanRecoveryState.PendingRecovery,
                1,
                InterruptedTaskId: "REVISION-20260806-002"),
        };
        var pending = new PendingPlanRevisionProposal(
            "proposal-stale",
            DateTimeOffset.UtcNow,
            new PlanRevisionProposalPayload(
                plan.PlanId,
                "older-revision",
                "Old proposal",
                null,
                Operations: []));

        var presentation = PlanRevisionApprovalPresentationBuilder.Build(plan, pending);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.OwnsInterruptedRecovery, Is.False);
            Assert.That(presentation.Title, Is.EqualTo("Plan Revision - Approval Required"));
            Assert.That(presentation.Context, Is.Null);
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
    public void Apply_ChangedApprovedGateDefinition_ResetsItToPending()
    {
        var plan = MakePlanWithTerminalGateAndValidation();
        var source = PendingDecomposePlanAdapter.FromPlan(plan).Group;
        var proposal = source with
        {
            ApprovalGates = source.ApprovalGates!.Select(gate => gate with
            {
                Message = "Review the revised production boundary",
                BeforeTaskIds = ["REVISION-20260806-003"],
            }).ToArray(),
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);
        var revised = result.UpdatedPlan!.ApprovalGates.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
            Assert.That(revised.Message, Is.EqualTo("Review the revised production boundary"));
            Assert.That(revised.Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(revised.ResolvedAt, Is.Null);
            Assert.That(revised.ResolvedBy, Is.Null);
            Assert.That(revised.ResolutionNote, Is.Null);
        });
    }

    [Test]
    public void Apply_ChangedAwaitingApprovalGateDefinition_LeavesPlanAwaitingReopenedGate()
    {
        var basePlan = MakePlan();
        var taskId = basePlan.Tasks[1].TaskId;
        var gate = new PlanApprovalGate(
            taskId + "-HUMAN-PROOF",
            "Confirm the old editor behavior.",
            [taskId],
            [basePlan.Tasks[2].TaskId],
            PlanGateStatus.AwaitingApproval,
            RequestedAt: DateTimeOffset.UtcNow,
            PlanRevision: basePlan.Revision,
            Question: "Is the old editor behavior correct?");
        var plan = basePlan with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = basePlan.Tasks.Select(task => task.TaskId == taskId
                ? task with { Status = PlanTaskStatus.Complete, Commit = "abc123" }
                : task).ToArray(),
            ApprovalGates = [gate],
            Progress = new PlanProgress(1, 3),
        };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    gate.GateId,
                    "Confirm the revised editor behavior.",
                    [taskId],
                    [basePlan.Tasks[2].TaskId],
                    null,
                    "Is the revised editor behavior correct?"),
            ],
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);
        var revisedGate = result.UpdatedPlan!.ApprovalGates.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
            Assert.That(result.UpdatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
            Assert.That(revisedGate.Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(revisedGate.Message, Is.EqualTo("Confirm the revised editor behavior."));
            Assert.That(revisedGate.Question, Is.EqualTo("Is the revised editor behavior correct?"));
            Assert.That(revisedGate.RequestedAt, Is.Null);
        });
    }

    [Test]
    public void Apply_ChangedFailedValidationDefinition_ResetsItToPending()
    {
        var plan = MakePlanWithTerminalGateAndValidation();
        var source = PendingDecomposePlanAdapter.FromPlan(plan).Group;
        var proposal = source with
        {
            Validations = source.Validations!.Select(validation => validation with
            {
                Title = "Revised production and commissioning boundary",
                AfterTaskIds = ["REVISION-20260806-001", "REVISION-20260806-002"],
            }).ToArray(),
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);
        var revised = result.UpdatedPlan!.Validations!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
            Assert.That(revised.Title, Is.EqualTo("Revised production and commissioning boundary"));
            Assert.That(revised.Status, Is.EqualTo(PlanValidationStatus.Pending));
            Assert.That(revised.StartedAt, Is.Null);
            Assert.That(revised.CompletedAt, Is.Null);
            Assert.That(revised.ValidatedCommit, Is.Null);
            Assert.That(revised.Summary, Is.Null);
            Assert.That(revised.Evidence, Is.Null);
        });
    }

    [Test]
    public void Apply_UnchangedTerminalGateAndValidation_PreservesTheirRuntimeHistory()
    {
        var plan = MakePlanWithTerminalGateAndValidation();
        var source = PendingDecomposePlanAdapter.FromPlan(plan).Group;
        var proposal = source with
        {
            Tasks = source.Tasks.Select(task => task.Id.EndsWith("003", StringComparison.Ordinal)
                ? task with { Description = "Revised downstream implementation" }
                : task).ToArray(),
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, DateTimeOffset.UtcNow);
        var gate = result.UpdatedPlan!.ApprovalGates.Single();
        var validation = result.UpdatedPlan.Validations!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
            Assert.That(gate.Status, Is.EqualTo(PlanGateStatus.Approved));
            Assert.That(gate.ResolvedBy, Is.EqualTo("Mark"));
            Assert.That(gate.ResolutionNote, Is.EqualTo("Approved original boundary"));
            Assert.That(validation.Status, Is.EqualTo(PlanValidationStatus.Failed));
            Assert.That(validation.ValidatedCommit, Is.EqualTo("abc123"));
            Assert.That(validation.Summary, Is.EqualTo("Original validation failed"));
            Assert.That(validation.Evidence, Is.EqualTo(new[] { "original evidence" }));
        });
    }

    [Test]
    public void Apply_AfterExecutionStarts_PreservesExistingLabelsAndOrigins()
    {
        var original = MakePlan();
        var addedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var origin = new PlanTaskExecutionOrigin(
            original.Tasks[2].TaskId, "Talia Rune", "A prerequisite was required.",
            addedAt, "attempt-1");
        var automatic = new PlanTask(
            "REVISION-20260806-AMD-001", "Prepare prerequisite", "Original prerequisite.",
            [original.Tasks[1].TaskId], "high", PlanTaskStatus.Pending,
            AgentRoutingMode: "generic",
            GenericAgentReason: "This prerequisite was added by the coordinator during execution and has no preapproved roster assignment.",
            DisplayStepLabel: "2a",
            ExecutionOrigin: origin,
            ResumeInstructions: "Resume Step 3 afterward.",
            AutomaticContinuationCount: 1);
        var plan = original with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = [original.Tasks[0], original.Tasks[1], automatic,
                original.Tasks[2] with { DependsOn = [automatic.TaskId] }],
            Progress = new PlanProgress(0, 4),
            Timestamps = original.Timestamps with { StartedAt = addedAt },
        };
        var proposal = PendingDecomposePlanAdapter.FromPlan(plan).Group with
        {
            Tasks = PendingDecomposePlanAdapter.FromPlan(plan).Group.Tasks.Select(task =>
                task.Id == automatic.TaskId
                    ? task with { Description = "Revised prerequisite." }
                    : task).ToArray(),
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, addedAt.AddMinutes(5));
        var revised = result.UpdatedPlan!.Tasks.Single(task => task.TaskId == automatic.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
            Assert.That(revised.Description, Is.EqualTo("Revised prerequisite."));
            Assert.That(revised.DisplayStepLabel, Is.EqualTo("2a"));
            Assert.That(revised.ExecutionOrigin, Is.EqualTo(origin));
            Assert.That(revised.ResumeInstructions, Is.EqualTo("Resume Step 3 afterward."));
            Assert.That(revised.AutomaticContinuationCount, Is.EqualTo(1));
            Assert.That(result.UpdatedPlan.Tasks.Single(task => task.TaskId == original.Tasks[2].TaskId)
                .DisplayStepLabel, Is.EqualTo("3"));
        });
    }

    [Test]
    public void Apply_AfterExecutionStarts_AssignsSuffixToNewTaskWithoutRenumberingExistingTasks()
    {
        var original = MakePlan();
        var startedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var plan = original with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Timestamps = original.Timestamps with { StartedAt = startedAt },
        };
        var group = PendingDecomposePlanAdapter.FromPlan(plan).Group;
        var inserted = new DecomposedSubTask(
            "REVISION-20260806-004", "Prepare verification", [group.Tasks[1].Id],
            "high", "Prepare verification");
        var proposal = group with
        {
            Tasks = [group.Tasks[0], group.Tasks[1], inserted,
                group.Tasks[2] with { DependsOn = [inserted.Id] }],
        };

        var result = PlanRevisionApplier.Apply(plan, proposal, plan.Revision, startedAt.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanRevisionApplyOutcome.Applied), result.Error);
            Assert.That(result.UpdatedPlan!.Tasks.Select(task => task.DisplayStepLabel),
                Is.EqualTo(new[] { "1", "2", "2a", "3" }));
            Assert.That(result.UpdatedPlan.Tasks.Single(task => task.TaskId == original.Tasks[2].TaskId)
                .DisplayStepLabel, Is.EqualTo("3"));
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
    public void HiddenPlanFileResponse_IsResolvedAsTasksJson()
    {
        using var workspace = new TestWorkspace();
        var group = MakeGroup();
        var relativePath = Path.Combine(".squad", "tmp", "agent-artifacts", "hidden-plan.json");
        workspace.CreateFile(relativePath, JsonSerializer.Serialize(group));
        var response = $$"""
            TASKS_JSON_FILE:
            { "path": "{{relativePath.Replace('\\', '/')}}" }
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
    public void ProtocolBlockFallback_IsResolvedWhenCombinedResponseOmitsPlan()
    {
        using var workspace = new TestWorkspace();
        var group = MakeGroup();
        var protocolBlock = new TranscriptProtocolJsonBlock(
            "TASKS_JSON",
            JsonSerializer.Serialize(group));

        var resolved = TaskPlanResponseResolver.TryResolve(
            "Submitting the plan proposal now.",
            responseSegments: ["Submitting the plan proposal now."],
            protocolJsonBlocks: [protocolBlock],
            workspace.RootPath,
            out var parserInput,
            out var source,
            out var diagnostic,
            out var payloadDetected);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(payloadDetected, Is.True);
            Assert.That(source, Is.EqualTo("protocol-block"));
            Assert.That(diagnostic, Is.Null);
            Assert.That(TasksJsonParser.TryParse(parserInput, out var parsed), Is.True);
            Assert.That(parsed!.GroupId, Is.EqualTo(group.GroupId));
        });
    }

    [Test]
    public void ResponseSegmentFallback_IsResolvedWhenCombinedResponseOmitsPlan()
    {
        using var workspace = new TestWorkspace();
        var group = MakeGroup();
        var segment = "TASKS_JSON:\n" + JsonSerializer.Serialize(group);

        var resolved = TaskPlanResponseResolver.TryResolve(
            "Submitting the plan proposal now.",
            responseSegments: [segment],
            protocolJsonBlocks: null,
            workspace.RootPath,
            out var parserInput,
            out var source,
            out var diagnostic,
            out var payloadDetected);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(payloadDetected, Is.True);
            Assert.That(source, Is.EqualTo("response-segments"));
            Assert.That(diagnostic, Is.Null);
            Assert.That(TasksJsonParser.TryParse(parserInput, out var parsed), Is.True);
            Assert.That(parsed!.GroupId, Is.EqualTo(group.GroupId));
        });
    }

    [Test]
    public void MissingArtifact_ReturnsActionableResolutionDiagnostic()
    {
        using var workspace = new TestWorkspace();
        const string response = """
            SQUADDASH_ARTIFACT_JSON:
            { "path": ".squad/tmp/agent-artifacts/missing-plan.json", "language": "json" }
            """;

        var resolved = TaskPlanResponseResolver.TryResolve(
            response,
            responseSegments: null,
            protocolJsonBlocks: null,
            workspace.RootPath,
            out _,
            out _,
            out var diagnostic,
            out var payloadDetected);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(payloadDetected, Is.True);
            Assert.That(diagnostic, Does.Contain("was not found"));
            Assert.That(diagnostic, Does.Contain("missing-plan.json"));
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

    [TestCase("taskId")]
    [TestCase("id")]
    public void ProposalParser_AcceptsCanonicalAndLegacyAddedTaskIdentity(string identityProperty)
    {
        var identity = "REVISION-20260806-010";
        var text = $$"""
            PLAN_REVISION_JSON:
            {
              "planId": "REVISION-20260806",
              "baseRevision": "base-1",
              "summary": "Add a follow-up task",
              "operations": [{
                "op": "addTask",
                "task": {
                  "{{identityProperty}}": "{{identity}}",
                  "title": "Follow up",
                  "description": "Complete the follow-up work.",
                  "dependsOn": [],
                  "priority": "high"
                }
              }]
            }
            """;

        var parsed = PlanRevisionProposalParser.TryParse(text, out var proposal, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(proposal!.Operations!.Single().Task!.Id, Is.EqualTo(identity));
        });
    }

    [Test]
    public void ProposalParser_ReportsConflictingTaskIdentityForRepair()
    {
        var text = """
            PLAN_REVISION_JSON:
            {
              "planId": "REVISION-20260806",
              "baseRevision": "base-1",
              "summary": "Add a follow-up task",
              "operations": [{
                "op": "addTask",
                "task": {
                  "taskId": "REVISION-20260806-010",
                  "id": "REVISION-20260806-011",
                  "description": "Complete the follow-up work.",
                  "dependsOn": [],
                  "priority": "high"
                }
              }]
            }
            """;

        var parsed = PlanRevisionProposalParser.TryParse(text, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("conflicting taskId"));
            Assert.That(error, Does.Contain("legacy id"));
        });
    }

    [Test]
    public void ProposalParser_ReportsMissingAddedTaskIdentityForRepair()
    {
        var text = """
            PLAN_REVISION_JSON:
            {
              "planId": "REVISION-20260806",
              "baseRevision": "base-1",
              "summary": "Add a follow-up task",
              "operations": [{
                "op": "addTask",
                "task": {
                  "description": "Complete the follow-up work.",
                  "dependsOn": [],
                  "priority": "high"
                }
              }]
            }
            """;

        var parsed = PlanRevisionProposalParser.TryParse(text, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("taskId is required"));
        });
    }

    [Test]
    public void ProposalParser_ReportsAllTopLevelSchemaErrorsTogether()
    {
        var text = """
            PLAN_REVISION_JSON:
            {
              "planId": "",
              "baseRevision": "",
              "summary": "",
              "operations": [null]
            }
            """;

        var parsed = PlanRevisionProposalParser.TryParse(text, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("planId is required"));
            Assert.That(error, Does.Contain("baseRevision is required"));
            Assert.That(error, Does.Contain("summary is required"));
            Assert.That(error, Does.Contain("operations cannot contain null entries"));
        });
    }

    [Test]
    public void ProposalParser_ReportsTopLevelAndOperationSchemaErrorsTogether()
    {
        var text = """
            PLAN_REVISION_JSON:
            {
              "planId": "REVISION-20260806",
              "baseRevision": "base-1",
              "summary": "",
              "operations": [{
                "op": "updateValidation",
                "targetId": "REVISION-20260806-VAL-001",
                "patch": { "status": "Pending" }
              }]
            }
            """;

        var parsed = PlanRevisionProposalParser.TryParse(text, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("summary is required"));
            Assert.That(error, Does.Contain("updateValidation cannot patch 'status'"));
        });
    }

    [Test]
    public void DecomposedTaskSerialization_WritesOnlyCanonicalTaskId()
    {
        var json = JsonSerializer.Serialize(new DecomposedSubTask(
            "REVISION-20260806-010", "Complete the follow-up work.", [], "high"));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"taskId\":\"REVISION-20260806-010\""));
            Assert.That(json, Does.Not.Contain("\"id\":"));
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
    public void ProposedPlanEditor_ValidatesUserEditAgainstMaterializedProposalBaseline()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "SquadDash", "MainWindow.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));
        var methodStart = source.IndexOf(
            "private PlanDefinitionEditResult SavePendingPlanRevisionEdit(",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private bool RefreshBridgeModelSettingsBeforeDispatch()",
            methodStart,
            StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));
        var method = source[methodStart..methodEnd];

        Assert.That(method, Does.Contain(
            "proposedPreview.UpdatedPlan, editedGroup, out var lockError"));
        Assert.That(method, Does.Not.Contain(
            "TryValidateUnlockedChanges(current, editedGroup"));
    }

    [Test]
    public void ProposalStore_ReplacePreservesProposalIdentityForUserEdits()
    {
        using var workspace = new TestWorkspace();
        var group = MakeGroup();
        var store = new PendingPlanRevisionProposalStore(workspace.GetPath(".squad"));
        var proposal = store.Save(new PlanRevisionProposalPayload(
            group.GroupId, "base-1", "AI proposal", null, group));

        var editedGroup = group with { Summary = "User-edited proposal" };
        store.Replace(proposal with
        {
            Payload = proposal.Payload with
            {
                Summary = "AI proposal (edited by user)",
                RevisedPlan = editedGroup,
                Operations = null,
            },
        });
        var loaded = store.Load(group.GroupId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded!.ProposalId, Is.EqualTo(proposal.ProposalId));
            Assert.That(loaded.Payload.Summary, Is.EqualTo("AI proposal (edited by user)"));
            Assert.That(loaded.Payload.RevisedPlan!.Summary, Is.EqualTo("User-edited proposal"));
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
            Assert.That(afterLoopYields, Does.Contain("editTaskText"));
            Assert.That(afterLoopYields, Does.Contain("oldText` that occurs exactly once"));
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
    public void ApprovedRevisionContinuation_ReopensAwaitingApprovalBoundaryBeforeResume()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "SquadDash", "MainWindow.xaml.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));
        var applyStart = source.IndexOf(
            "private async Task ApplyPendingPlanRevisionAsync(",
            StringComparison.Ordinal);
        var helperStart = source.IndexOf(
            "private async Task ContinueAfterApprovedPlanRevisionAsync(",
            StringComparison.Ordinal);
        var helperEnd = source.IndexOf(
            "private void AppendPlanRevisionApprovalCard(",
            helperStart,
            StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(applyStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperStart, Is.GreaterThan(applyStart));
            Assert.That(helperEnd, Is.GreaterThan(helperStart));
        });

        var applyMethod = source[applyStart..helperStart];
        var helperBlock = source[helperStart..helperEnd];
        Assert.Multiple(() =>
        {
            Assert.That(applyMethod, Does.Contain("await ContinueAfterApprovedPlanRevisionAsync(current, updated);"));
            Assert.That(helperBlock, Does.Contain("PlanLifecycleStatus.AwaitingApproval"));
            Assert.That(helperBlock, Does.Contain("HasReadyPendingApprovalGate(active)"));
            Assert.That(helperBlock, Does.Contain("PlanStoreUpdater.ApplyApprovalBoundaryRecovery(active)"));
            Assert.That(helperBlock, Does.Contain("await RestoreRevisionAwaitingApprovalGateUIAsync(active);"));
            Assert.That(helperBlock, Does.Contain("await RestoreAwaitingApprovalGateUIAsync(active.PlanId);"));
            Assert.That(helperBlock, Does.Contain("ProcessPlanApprovalsAfterStepAsync("));
            Assert.That(helperBlock, Does.Contain("StartOrResumeDurablePlanAsync(active)"));
        });
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
    public void DeltaMaterializer_ReopensCompletedTaskForPropertyLevelTextEdit()
    {
        var original = MakePlan();
        var completed = original.Tasks[1] with
        {
            Status = PlanTaskStatus.Complete,
            Description = "Implement the complete model profile workflow and preserve provider settings.",
        };
        var plan = original with
        {
            Tasks = original.Tasks.Select((task, index) => index == 1 ? completed : task).ToArray(),
        };
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Refine one completed-task clause",
            null,
            Operations:
            [
                new PlanRevisionOperation("reopenTask", completed.TaskId),
                new PlanRevisionOperation("editTaskText", completed.TaskId, Path: "/description",
                    OldText: "complete model profile workflow", NewText: "multi-provider model profile workflow"),
            ]);

        var materialized = PlanRevisionDeltaApplier.TryMaterialize(
            plan, payload, out var revised, out var reopened, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(materialized, Is.True, error);
            Assert.That(reopened, Does.Contain(completed.TaskId));
            Assert.That(revised!.Tasks[1].Description,
                Is.EqualTo("Implement the multi-provider model profile workflow and preserve provider settings."));
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

    [Test]
    public void DeltaMaterializer_ReportsAllSchemaErrorsAcrossOperations()
    {
        var plan = MakePlanWithTerminalGateAndValidation();
        using var gatePatch = JsonDocument.Parse("""{"status":"pending","resolvedAt":null,"resolvedBy":null}""");
        using var validationPatch = JsonDocument.Parse("""{"status":"pending","title":"Revised validation"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Invalid runtime mutations",
            null,
            Operations:
            [
                new PlanRevisionOperation(
                    "updateApprovalGate",
                    plan.ApprovalGates.Single().GateId,
                    gatePatch.RootElement.Clone()),
                new PlanRevisionOperation(
                    "updateValidation",
                    plan.Validations!.Single().ValidationId,
                    validationPatch.RootElement.Clone()),
            ]);

        var materialized = PlanRevisionDeltaApplier.TryMaterialize(
            plan, payload, out _, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(materialized, Is.False);
            Assert.That(error, Does.Contain("Plan revision schema errors:"));
            Assert.That(error, Does.Contain("updateApprovalGate cannot patch 'status'"));
            Assert.That(error, Does.Contain("updateApprovalGate cannot patch 'resolvedAt'"));
            Assert.That(error, Does.Contain("updateApprovalGate cannot patch 'resolvedBy'"));
            Assert.That(error, Does.Contain("updateValidation cannot patch 'status'"));
            Assert.That(error, Does.Not.Contain("Revised validation"),
                "Allowed values must not be misreported as schema errors.");
        });
    }

    [Test]
    public void RepairPrompt_RequiresSurgicalCorrectionAndListsPatchFields()
    {
        var plan = MakePlan();
        using var patch = JsonDocument.Parse("""{"summary":"Revised summary"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Preserve the intended change",
            null,
            Operations:
            [
                new PlanRevisionOperation("updatePlan", Patch: patch.RootElement.Clone()),
            ]);

        var prompt = PlanRevisionProposalParser.BuildRepairPrompt("bad patch", payload);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("Correct only the listed validation errors"));
            Assert.That(prompt, Does.Contain("Preserve every unaffected operation, field, and value"));
            Assert.That(prompt, Does.Contain("updateApprovalGate: message, afterTaskIds, beforeTaskIds, proofRequirements, question"));
            Assert.That(prompt, Does.Contain("updateValidation: title, description, afterTaskIds, beforeTaskIds, assertions, outputIds, mode, commands, revalidateAtCompletion"));
            Assert.That(prompt, Does.Contain("Runtime fields such as status"));
            Assert.That(prompt, Does.Contain("Do not set status to pending"));
            Assert.That(prompt, Does.Contain("SquadDash resets affected gates and validations itself"));
        });
    }

    [Test]
    public void RepairPrompt_CanEmbedSavedCandidateForRestartRecovery()
    {
        var plan = MakePlan();
        using var patch = JsonDocument.Parse("""{"status":"Pending"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Preserve the intended validation change",
            null,
            Operations:
            [
                new PlanRevisionOperation(
                    "updateValidation",
                    "REVISION-20260806-VAL-001",
                    patch.RootElement.Clone()),
            ]);

        var prompt = PlanRevisionProposalParser.BuildRepairPrompt(
            "updateValidation cannot patch 'status'.",
            payload,
            includeSavedCandidate: true);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("may no longer be present in recent conversation context"));
            Assert.That(prompt, Does.Contain(PlanRevisionProposalParser.Marker));
            Assert.That(prompt, Does.Contain("\"planId\": \"REVISION-20260806\""));
            Assert.That(prompt, Does.Contain("\"status\": \"Pending\""));
        });
    }

    [Test]
    public void RejectedRevisionRecovery_RecommendsSavedCandidateOnlyWhileBaseIsCurrent()
    {
        var plan = MakePlanWithTerminalGateAndValidation() with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
        };
        using var patch = JsonDocument.Parse("""{"title":"Revised validation"}""");
        var payload = new PlanRevisionProposalPayload(
            plan.PlanId,
            plan.Revision,
            "Introduce a supervised commissioning path",
            null,
            Operations:
            [
                new PlanRevisionOperation(
                    "updateValidation",
                    plan.Validations!.Single().ValidationId,
                    patch.RootElement.Clone()),
            ]);
        var recovery = new RejectedPlanRevisionRecoveryState(
            payload,
            "updateValidation cannot patch 'status'.",
            2,
            new DateTimeOffset(2026, 10, 11, 13, 54, 31, TimeSpan.Zero));

        var current = PlanRevisionRecoveryPresentationBuilder.Build(plan, recovery);
        var stale = PlanRevisionRecoveryPresentationBuilder.Build(
            plan with { Revision = "new-revision" }, recovery);
        var unsafeBoundary = PlanRevisionRecoveryPresentationBuilder.Build(
            plan with { LifecycleStatus = PlanLifecycleStatus.Archived }, recovery);

        Assert.Multiple(() =>
        {
            Assert.That(current.CanRetry, Is.True);
            Assert.That(current.Explanation, Does.Contain("later revision was proposed but rejected"));
            Assert.That(current.Explanation, Does.Contain("remains failed because the revision that changes it was never applied"));
            Assert.That(current.Explanation, Does.Not.Contain("Recommended:"));
            Assert.That(current.Recommendation, Does.StartWith("Recommended: repair and resubmit"));
            Assert.That(current.TechnicalDetails, Does.Contain("Operations preserved: 1"));
            Assert.That(stale.CanRetry, Is.False);
            Assert.That(stale.Recommendation, Does.Contain("automatic resubmission is unsafe"));
            Assert.That(unsafeBoundary.CanRetry, Is.False);
            Assert.That(unsafeBoundary.Recommendation, Does.Contain("not currently at a safe revision boundary"));
        });
    }

    [Test]
    public void RepairRetryPolicy_AllowsExactlyTwoAutomaticAttempts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlanRevisionRepairRetryPolicy.MaxAutomaticAttempts, Is.EqualTo(2));
            Assert.That(PlanRevisionRepairRetryPolicy.CanQueue(0), Is.True);
            Assert.That(PlanRevisionRepairRetryPolicy.CanQueue(1), Is.True);
            Assert.That(PlanRevisionRepairRetryPolicy.CanQueue(2), Is.False);
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

    private static Plan MakePlanWithTerminalGateAndValidation()
    {
        var group = MakeGroup() with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    "REVISION-20260806-GATE-001",
                    "Review the original boundary",
                    ["REVISION-20260806-001"],
                    ["REVISION-20260806-002"],
                    Question: "Is the original boundary acceptable?"),
            ],
            Validations =
            [
                new DecomposedValidationNode(
                    "REVISION-20260806-VAL-001",
                    "Original boundary validation",
                    "Validate the original boundary.",
                    ["REVISION-20260806-001"],
                    ["REVISION-20260806-003"],
                    ["The original boundary is valid."],
                    [],
                    "evidence",
                    null,
                    true),
            ],
        };
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var timestamp = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var plan = PendingDecomposePlanAdapter.ToPlan(new PendingDecomposePlan(revision, group), timestamp);

        return plan with
        {
            ApprovalGates =
            [
                plan.ApprovalGates.Single() with
                {
                    Status = PlanGateStatus.Approved,
                    RequestedAt = timestamp.AddMinutes(1),
                    ResolvedAt = timestamp.AddMinutes(2),
                    ResolutionNote = "Approved original boundary",
                    ResolvedBy = "Mark",
                },
            ],
            Validations =
            [
                plan.Validations!.Single() with
                {
                    Status = PlanValidationStatus.Failed,
                    StartedAt = timestamp.AddMinutes(3),
                    CompletedAt = timestamp.AddMinutes(4),
                    ValidatedCommit = "abc123",
                    Summary = "Original validation failed",
                    Evidence = ["original evidence"],
                },
            ],
        };
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
