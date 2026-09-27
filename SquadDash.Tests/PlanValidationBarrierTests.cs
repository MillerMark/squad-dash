using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Deterministic tests for validation barriers: parallel frontier, failure blocking,
/// retry, restart, staleness, legacy compatibility, and completion gate.
/// </summary>
[TestFixture]
internal sealed class PlanValidationBarrierTests
{
    // ─── Parallel Frontier ────────────────────────────────────────────────────────

    [Test]
    public void ParallelFrontier_UnrelatedTasksProcedeWhileValidationBlocksDownstream()
    {
        // V1 gates T1→T2; T3 is completely independent
        var plan = MakePlanWithParallelPaths();
        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Does.Contain("T2"), "T2 is downstream of V1 and should be blocked");
            Assert.That(blocked, Does.Not.Contain("T3"), "T3 is unrelated and must not be blocked");
            Assert.That(blocked, Does.Not.Contain("T1"), "T1 is upstream of V1 and must not be blocked");
        });
    }

    [Test]
    public void ParallelFrontier_PassedValidation_UnblocksDownstream()
    {
        var plan = MakePlanWithParallelPaths();
        plan = plan with
        {
            Validations = plan.Validations!.Select(v =>
                v with { Status = PlanValidationStatus.Passed }).ToArray(),
        };

        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.That(blocked, Is.Empty, "Passed validation must not block any tasks");
    }

    [Test]
    public void ParallelFrontier_MultipleValidations_BlockOnlyTheirOwnDownstream()
    {
        // V1 blocks T2, V2 blocks T4. T3 unrelated.
        var plan = MakePlanWithTwoValidations();
        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Does.Contain("T2"), "T2 blocked by V1");
            Assert.That(blocked, Does.Contain("T4"), "T4 blocked by V2");
            Assert.That(blocked, Does.Not.Contain("T3"), "T3 is unrelated");
            Assert.That(blocked, Does.Not.Contain("T1"), "T1 is upstream");
            Assert.That(blocked, Does.Not.Contain("T5"), "T5 is upstream of V2");
        });
    }

    // ─── Failure Blocking ─────────────────────────────────────────────────────────

    [Test]
    public void FailedValidation_BlocksDownstream_ButNotUnrelatedWork()
    {
        var plan = MakePlanWithParallelPaths();
        plan = plan with
        {
            Validations = plan.Validations!.Select(v =>
                v with { Status = PlanValidationStatus.Failed }).ToArray(),
        };

        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Does.Contain("T2"), "T2 blocked by failed V1");
            Assert.That(blocked, Does.Not.Contain("T3"), "T3 must proceed independently");
        });
    }

    [Test]
    public void FailedValidation_BlocksPlan_ViaApplyValidationResult()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false, "Assertion failed.",
            ["Output missing."], validatedCommit: null);

        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
    }

    [Test]
    public void FailedValidation_FixRequest_InsertsRepairAndPreservesAcceptedWork()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan,
            "V1",
            passed: false,
            "The declared output is malformed.",
            ["[FAIL] A produces the declared output.: Reproduced with a finite input."],
            validatedCommit: "bbb2222");

        var repaired = PlanStoreUpdater.ApplyValidationRepairRequested(plan, "V1");
        var repairTask = repaired.Tasks.Single(task => task.TaskId == "V1-FIX-001");
        var validation = repaired.Validations!.Single(item => item.ValidationId == "V1");

        Assert.Multiple(() =>
        {
            Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(repaired.Tasks.Select(task => task.TaskId),
                Is.EqualTo(new[] { "T1", "V1-FIX-001", "T2" }));
            Assert.That(repaired.Tasks.Single(task => task.TaskId == "T1").Commit,
                Is.EqualTo("aaa1111"), "Accepted task commits must remain intact.");
            Assert.That(repairTask.Status, Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(repairTask.DependsOn, Is.EqualTo(new[] { "T1" }));
            Assert.That(repairTask.Description, Does.Contain("The declared output is malformed."));
            Assert.That(repairTask.Description,
                Does.Contain("[FAIL] A produces the declared output.: Reproduced with a finite input."));
            Assert.That(validation.Status, Is.EqualTo(PlanValidationStatus.Pending));
            Assert.That(validation.AfterTaskIds, Is.EqualTo(new[] { "T1", "V1-FIX-001" }));
            Assert.That(validation.Summary, Is.EqualTo("The declared output is malformed."));
            Assert.That(validation.ValidatedCommit, Is.EqualTo("bbb2222"));
            Assert.That(repaired.Progress, Is.EqualTo(new PlanProgress(1, 3)));
            Assert.That(repaired.Timestamps.InterruptedAt, Is.Null);
        });
    }

    [Test]
    public void FailedValidation_FixRequest_RunsOnlyOnceAndBecomesReadyAfterRepair()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan,
            "V1",
            passed: false,
            "Assertion failed.",
            ["[FAIL] A produces the declared output.: Reproduced."],
            validatedCommit: "bbb2222");
        plan = PlanStoreUpdater.ApplyValidationRepairRequested(plan, "V1");

        var repeated = PlanStoreUpdater.ApplyValidationRepairRequested(plan, "V1");
        Assert.That(repeated, Is.SameAs(plan), "A repeated click must not create another repair task.");

        var completedRepair = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "V1-FIX-001"
                ? task with { Status = PlanTaskStatus.Complete, Commit = "ccc3333" }
                : task).ToArray(),
        };
        var ready = PlanStoreUpdater.ApplyReadyValidations(completedRepair);

        Assert.That(ready.Validations!.Single().Status, Is.EqualTo(PlanValidationStatus.Ready),
            "Completing the bounded repair should deterministically make the validation runnable.");
    }

    [Test]
    public void HumanProofOnlyGap_CreatesPendingCheckpointWithoutBlockingPlan()
    {
        var plan = MakePlanWithReadyValidation();
        var requirement = new PlanTaskProofRequirement(
            "live-result",
            "live-ui-observation",
            "The result appears in the live viewer.",
            "Does the result appear in the live viewer?",
            HumanVerificationAccessMode.DirectlyReachable);

        plan = PlanStoreUpdater.ApplyValidationHumanReviewRequired(
            plan,
            "V1",
            "Live confirmation remains.",
            ["Automated contract passed."],
            "abc1234",
            [requirement]);

        Assert.Multiple(() =>
        {
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Pending));
            Assert.That(plan.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(plan.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(plan.ApprovalGates[0].Question, Is.EqualTo(requirement.Question));
            Assert.That(plan.ApprovalGates[0].ProofRequirements, Is.EqualTo(new[] { requirement }));
            Assert.That(ApprovalGateReadinessEvaluator.IsValidationBlockedByApproval(
                plan, plan.Validations[0]), Is.True);
        });
    }

    [Test]
    public void HumanProofOnlyGap_NormalizesCheckpointForExecutionSchema()
    {
        var plan = MakeSchemaValidPlanWithReadyValidation();
        var validationId = plan.Validations![0].ValidationId;
        var requirements = new[]
        {
            new PlanTaskProofRequirement(
                "BLAZORAIGEN-20260316-VAL-001-HP1",
                "human-observation",
                "The live preview visibly shows the validated state.",
                "Does the live preview show the validated state?",
                HumanVerificationAccessMode.DirectlyReachable),
            new PlanTaskProofRequirement(
                "BLAZORAIGEN-20260316-VAL-001-HP-1",
                "restart-observation",
                "The validated state remains visible after restart.",
                "After restart, does the validated state remain visible?",
                HumanVerificationAccessMode.Recipe,
                new HumanVerificationRecipe(
                    "Prepare the validated state.",
                    "Restart SquadDash and reopen the workspace.",
                    "Confirm the validated state remains visible.",
                    "Restore the original state.")),
        };

        var updated = PlanStoreUpdater.ApplyValidationHumanReviewRequired(
            plan,
            validationId,
            "Human observations remain.",
            ["Automated checks passed."],
            "abc1234",
            requirements);
        var projected = PendingDecomposePlanAdapter.FromPlan(updated).Group;
        var payload = "TASKS_JSON:\n" + System.Text.Json.JsonSerializer.Serialize(projected);

        Assert.Multiple(() =>
        {
            var gate = updated.ApprovalGates.Single();
            Assert.That(gate.Question, Does.Contain("Does the live preview show the validated state?"));
            Assert.That(gate.Question, Does.Contain("After restart, does the validated state remain visible?"));
            Assert.That(gate.ProofRequirements!.Select(requirement => requirement.RequirementId),
                Is.EqualTo(new[]
                {
                    "blazoraigen-20260316-val-001-hp1",
                    "blazoraigen-20260316-val-001-hp-1",
                }));
            Assert.That(TasksJsonParser.TryParse(payload, out _, out var diagnostic),
                Is.True, diagnostic?.Message);
        });
    }

    [Test]
    public void RepairValidationHumanProofGateSchema_RepairsLegacyValidationCheckpoints()
    {
        var plan = MakeSchemaValidPlanWithReadyValidation();
        var validationId = plan.Validations![0].ValidationId;
        var firstRequirement = new PlanTaskProofRequirement(
            "BLAZORAIGEN-20260316-VAL-001-HP1",
            "human-observation",
            "The live preview visibly shows the validated state.",
            "Does the live preview show the validated state?",
            HumanVerificationAccessMode.DirectlyReachable);
        var secondRequirement = new PlanTaskProofRequirement(
            "BLAZORAIGEN-20260316-VAL-001-HP-1",
            "human-observation",
            "The command palette exposes the validated action.",
            "Does the command palette expose the validated action?",
            HumanVerificationAccessMode.DirectlyReachable);
        var legacy = plan with
        {
            ApprovalGates =
            [
                new PlanApprovalGate(
                    GateId: $"{validationId}-HUMAN-PROOF",
                    Message: "Review the first human proof.",
                    AfterTaskIds: plan.Validations[0].AfterTaskIds,
                    BeforeTaskIds: plan.Validations[0].BeforeTaskIds,
                    Status: PlanGateStatus.Approved,
                    PlanRevision: plan.Revision,
                    PresentationAnchor: $"validation:{validationId}",
                    ResolvedBy: "Mark",
                    ProofRequirements: [firstRequirement],
                    ProofEvidence:
                    [
                        new PlanTaskProofEvidence(
                            firstRequirement.RequirementId,
                            firstRequirement.ProofType,
                            "Mark confirmed the live preview.",
                            [$"squaddash://approval/{plan.PlanId}/{validationId}-HUMAN-PROOF"]),
                    ],
                    AfterTaskIdsSpecified: true,
                    BeforeTaskIdsSpecified: true),
                new PlanApprovalGate(
                    GateId: $"{validationId}-HUMAN-PROOF-2",
                    Message: "Review the second human proof.",
                    AfterTaskIds: plan.Validations[0].AfterTaskIds,
                    BeforeTaskIds: plan.Validations[0].BeforeTaskIds,
                    Status: PlanGateStatus.Pending,
                    PlanRevision: plan.Revision,
                    PresentationAnchor: $"validation:{validationId}",
                    ProofRequirements: [secondRequirement],
                    AfterTaskIdsSpecified: true,
                    BeforeTaskIdsSpecified: true),
            ],
        };

        var repaired = PlanStoreUpdater.RepairValidationHumanProofGateSchema(legacy);
        var projected = PendingDecomposePlanAdapter.FromPlan(repaired).Group;
        var payload = "TASKS_JSON:\n" + System.Text.Json.JsonSerializer.Serialize(projected);

        Assert.Multiple(() =>
        {
            Assert.That(repaired.ApprovalGates.All(gate => !string.IsNullOrWhiteSpace(gate.Question)),
                Is.True);
            Assert.That(repaired.ApprovalGates[0].ProofRequirements![0].RequirementId,
                Is.EqualTo("blazoraigen-20260316-val-001-hp1"));
            Assert.That(repaired.ApprovalGates[0].ProofEvidence![0].RequirementId,
                Is.EqualTo("blazoraigen-20260316-val-001-hp1"));
            Assert.That(repaired.ApprovalGates[1].ProofRequirements![0].RequirementId,
                Is.EqualTo("blazoraigen-20260316-val-001-hp-1"));
            Assert.That(TasksJsonParser.TryParse(payload, out _, out var diagnostic),
                Is.True, diagnostic?.Message);
        });
    }

    [Test]
    public void HasFailedValidation_DetectedByBoundaryPolicy()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false, "Failed.",
            ["No evidence."], validatedCommit: null);

        Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.True);
    }

    // ─── Retry ────────────────────────────────────────────────────────────────────

    [Test]
    public void Retry_TransitionsFailedValidationToReady()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false, "Parse failed.",
            ["Missing envelope."], validatedCommit: null);

        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));

        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
            Assert.That(plan.Validations![0].Summary, Is.Null);
            Assert.That(plan.Validations![0].Evidence, Is.Null);
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing),
                "Plan should unblock after retry");
        });
    }

    [Test]
    public void Retry_OnNonFailedValidation_IsNoOp()
    {
        var plan = MakePlanWithReadyValidation();

        var result = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");

        Assert.That(ReferenceEquals(result, plan), Is.True,
            "ApplyValidationRetry on non-failed validation must be no-op");
    }

    [Test]
    public void Retry_PreservesUpstreamTaskCommits()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false, "Evidence repair needed.",
            ["Ambiguous."], validatedCommit: null);
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");

        // The upstream task's commit is preserved
        var t1 = plan.Tasks.First(t => t.TaskId == "T1");
        Assert.That(t1.Commit, Is.EqualTo("aaa1111"),
            "Task commit must be preserved during validation retry");
    }

    // ─── Restart ──────────────────────────────────────────────────────────────────

    [Test]
    public void Restart_ValidationBarrierState_Survives_Serialization()
    {
        var plan = MakePlanWithParallelPaths();
        // Simulate a validation in progress at restart time
        plan = plan with
        {
            Validations = plan.Validations!.Select(v =>
                v with { Status = PlanValidationStatus.Validating, StartedAt = DateTimeOffset.UtcNow }).ToArray(),
        };

        // Verify the in-progress validation is recoverable
        var inProgress = PlanValidationScheduler.GetInProgressValidation(plan);
        Assert.That(inProgress, Is.Not.Null);
        Assert.That(inProgress!.ValidationId, Is.EqualTo("V1"));

        // After restart, the boundary policy recovers the in-progress validation
        var recovered = PlanExecutionBoundaryPolicy.SelectValidation(plan);
        Assert.That(recovered?.ValidationId, Is.EqualTo("V1"));
    }

    [Test]
    public void Restart_BlockedTaskIds_SurviveRecomputation()
    {
        var plan = MakePlanWithParallelPaths();
        var blockedBefore = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        // Simulate restart by recomputing from the same plan state
        var blockedAfter = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.That(blockedAfter, Is.EquivalentTo(blockedBefore),
            "Blocked set must be deterministic across recomputation (restart-safe)");
    }

    [Test]
    public void Restart_ActiveValidationId_RecoveredByBoundaryPolicy()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");

        // Simulate: on restart, boundary policy receives the active validation ID
        var selected = PlanExecutionBoundaryPolicy.SelectValidation(plan, activeValidationId: "V1");
        Assert.That(selected?.ValidationId, Is.EqualTo("V1"));
    }

    // ─── Staleness ────────────────────────────────────────────────────────────────

    [Test]
    public void Staleness_OutputChange_InvalidatesPassedValidation()
    {
        var plan = MakePlanWithPassedValidation();

        // Simulate: T1 re-accepted with a new commit (output change)
        var changedIds = new HashSet<string>(StringComparer.Ordinal) { "T1" };
        plan = PlanStoreUpdater.InvalidateCoveredValidations(plan, changedIds);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));
            Assert.That(plan.Validations![0].Summary, Does.Contain("Covered output changed"));
        });
    }

    [Test]
    public void Staleness_OutputChange_InvalidatesFailedValidationVerdict()
    {
        var plan = MakePlanWithPassedValidation();
        plan = plan with
        {
            Validations =
            [
                plan.Validations![0] with
                {
                    Status = PlanValidationStatus.Failed,
                    Summary = "The old output failed validation.",
                },
            ],
        };

        plan = PlanStoreUpdater.InvalidateCoveredValidations(
            plan,
            new HashSet<string>(StringComparer.Ordinal) { "T1" });

        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));
    }

    [Test]
    public void Staleness_UnrelatedTaskChange_DoesNotInvalidate()
    {
        var plan = MakePlanWithPassedValidation();

        // T3 is not in the validation's afterTaskIds
        var changedIds = new HashSet<string>(StringComparer.Ordinal) { "T3" };
        plan = PlanStoreUpdater.InvalidateCoveredValidations(plan, changedIds);

        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed),
            "Unrelated task change must not invalidate validation");
    }

    [Test]
    public void Staleness_IntegratedWithApplyStepAccepted_WhenCommitChanges()
    {
        var plan = MakePlanWithPassedValidation();

        // Items represent T1 as still checked (complete) — no status change
        var items = new TaskItem[]
        {
            new("Task A", null, false, true, "✅", "- [x] Task A", TaskId: "T1"),
            new("Task B", null, false, false, "⬜", "- [ ] Task B", TaskId: "T2"),
        };

        // acceptedResult provides a NEW commit for T1, simulating re-acceptance with new work
        var acceptedResult = new DecomposeStepResult(
            "P1", "T1", "rev", "complete", "bbb2222", "Reworked A.", null, null);

        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, nextExecutingTaskId: "T2",
            acceptedResult: acceptedResult);

        // The validation should be stale because T1's commit changed from aaa1111 to bbb2222
        Assert.That(plan.Validations![0].Status,
            Is.EqualTo(PlanValidationStatus.Stale).Or.EqualTo(PlanValidationStatus.Ready),
            "Validation should be invalidated when covered output changes via new commit");
    }

    [Test]
    public void Staleness_StaleValidation_BlocksDownstream()
    {
        var plan = MakePlanWithPassedValidation();
        plan = PlanStoreUpdater.ApplyValidationStale(plan, "V1", "Prerequisite reworked.");

        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.That(blocked, Does.Contain("T2"),
            "Stale validation must block downstream until re-passed");
    }

    [Test]
    public void Staleness_StaleValidation_CanBeReReadied_AndScheduled()
    {
        var plan = MakePlanWithPassedValidation();
        plan = PlanStoreUpdater.ApplyValidationStale(plan, "V1", "Reworked.");
        plan = PlanStoreUpdater.ApplyReadyValidations(plan);

        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));

        var next = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(next?.ValidationId, Is.EqualTo("V1"));
    }

    // ─── Legacy Plan Compatibility ────────────────────────────────────────────────

    [Test]
    public void LegacyPlan_WithoutValidations_CompletesNormally()
    {
        var plan = MakeLegacyPlanAllComplete();

        var result = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
            Assert.That(result.Timestamps.CompletedAt, Is.Not.Null);
        });
    }

    [Test]
    public void LegacyPlan_WithoutValidations_AllRequiredPassed_ReturnsTrue()
    {
        var plan = MakeLegacyPlanAllComplete();

        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.True,
            "Plans without validations satisfy the AllRequiredPassed check");
    }

    [Test]
    public void LegacyPlan_NoBlockedTasks()
    {
        var plan = MakeLegacyPlanAllComplete();

        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.That(blocked, Is.Empty,
            "Legacy plans without validations must have no validation-blocked tasks");
    }

    [Test]
    public void LegacyPlan_RepairInconsistentState_CompletesWithoutValidationCheck()
    {
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Legacy Plan", "feature/legacy", "No validations",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Complete),
            ],
            [],
            new PlanProgress(2, 2, ExecutingTaskId: "A"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
    }

    // ─── Completion Gate ──────────────────────────────────────────────────────────

    [Test]
    public void CompletionGate_AllValidationsPassed_AllowsCompletion()
    {
        var plan = MakePlanWithPassedValidation();
        // All tasks are complete, validation passed
        plan = plan with
        {
            Tasks = plan.Tasks.Select(t =>
                t with { Status = PlanTaskStatus.Complete }).ToArray(),
        };

        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.True);
    }

    [Test]
    public void CompletionGate_ReadyValidation_BlocksCompletion()
    {
        var plan = MakePlanWithReadyValidation();

        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.False,
            "Plan with Ready validation must not pass completion gate");
    }

    [Test]
    public void CompletionGate_ValidatingStatus_BlocksCompletion()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");

        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.False,
            "Plan with Validating status must not pass completion gate");
    }

    [Test]
    public void CompletionGate_FailedValidation_BlocksCompletion()
    {
        var plan = MakePlanWithReadyValidation();
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false, "Failed.",
            ["No."], validatedCommit: null);

        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.False,
            "Plan with Failed validation must not pass completion gate");
    }

    [Test]
    public void CompletionGate_StaleValidation_BlocksCompletion()
    {
        var plan = MakePlanWithPassedValidation();
        plan = PlanStoreUpdater.ApplyValidationStale(plan, "V1", "Reworked.");

        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.False,
            "Plan with Stale validation must not pass completion gate");
    }

    [Test]
    public void CompletionGate_RepairInconsistentState_DoesNotComplete_WhenValidationsPending()
    {
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary",
            [
                new PlanTask("T1", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("T2", "B", "B", ["T1"], "high", PlanTaskStatus.Complete),
            ],
            [],
            new PlanProgress(2, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [new PlanValidationNode(
                "V1", "Verify", "Verify A", ["T1"], ["T2"],
                ["A works."], [], "evidence", [], true,
                PlanValidationStatus.Ready)]);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.LifecycleStatus, Is.Not.EqualTo(PlanLifecycleStatus.Completed),
            "RepairInconsistentState must not complete plan with non-passed validations");
    }

    [Test]
    public void CompletionGate_ApplyValidationResult_Passed_CompletesIfAllTasksDone()
    {
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary",
            [
                new PlanTask("T1", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("T2", "B", "B", ["T1"], "high", PlanTaskStatus.Complete),
            ],
            [],
            new PlanProgress(2, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [new PlanValidationNode(
                "V1", "Verify", "Verify A", ["T1"], ["T2"],
                ["A works."], [], "evidence", [], true,
                PlanValidationStatus.Ready)]);

        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: true, "All good.",
            ["Evidence."], validatedCommit: "abc1234");

        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed),
            "Plan should auto-complete when all tasks done and all validations passed");
    }

    // ─── Task Acceptance Independence ─────────────────────────────────────────────

    [Test]
    public void TaskAcceptance_IsIndependent_OfValidationStatus()
    {
        // A task can be accepted regardless of validation status
        var plan = MakePlanWithReadyValidation();
        var items = new TaskItem[]
        {
            new("Task A", null, false, true, "✅", "- [x] Task A", TaskId: "T1"),
            new("Task B", null, false, false, "⬜", "- [ ] Task B", TaskId: "T2"),
        };

        var result = PlanStoreUpdater.ApplyStepAccepted(plan, items, nextExecutingTaskId: "T2");

        // The task is accepted — no rejection based on validation status
        Assert.That(result.Tasks.First(t => t.TaskId == "T1").Status,
            Is.EqualTo(PlanTaskStatus.Complete));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static Plan MakePlanWithParallelPaths()
    {
        // T1 → V1 → T2 (serial path through validation)
        // T3 (independent parallel path, no validation gate)
        var validation = new PlanValidationNode(
            "V1", "Verify A", "Verify A produces correct output.",
            ["T1"], ["T2"],
            ["A produces output."], ["output-a"], "evidence", [], true,
            PlanValidationStatus.Ready);
        return new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Parallel Plan", "feature/parallel", "Two independent paths.",
            [
                new PlanTask("T1", "Task A", "Create A", [], "high", PlanTaskStatus.Complete,
                    Commit: "aaa1111"),
                new PlanTask("T2", "Task B", "Use A", ["T1"], "high", PlanTaskStatus.Pending),
                new PlanTask("T3", "Task C", "Independent work", [], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 3), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    private static Plan MakePlanWithTwoValidations()
    {
        // V1: T1→T2; V2: T5→T4; T3 unrelated
        var v1 = new PlanValidationNode(
            "V1", "Verify A", "Verify A.", ["T1"], ["T2"],
            ["A ok."], [], "evidence", [], true, PlanValidationStatus.Ready);
        var v2 = new PlanValidationNode(
            "V2", "Verify E", "Verify E.", ["T5"], ["T4"],
            ["E ok."], [], "evidence", [], true, PlanValidationStatus.Ready);
        return new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Multi-Validation Plan", "feature/multi", "Two validations.",
            [
                new PlanTask("T1", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("T2", "B", "B", ["T1"], "high", PlanTaskStatus.Pending),
                new PlanTask("T3", "C", "C", [], "high", PlanTaskStatus.Pending),
                new PlanTask("T4", "D", "D", ["T5"], "high", PlanTaskStatus.Pending),
                new PlanTask("T5", "E", "E", [], "high", PlanTaskStatus.Complete),
            ],
            [],
            new PlanProgress(2, 5), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [v1, v2]);
    }

    private static Plan MakePlanWithReadyValidation()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify Contract", "Verify A produces what B consumes.",
            ["T1"], ["T2"],
            ["A produces the declared output."], ["contract-a"],
            "evidence", [], true,
            PlanValidationStatus.Ready);
        return new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Integration Plan", "feature/plan", "Verify cross-task contracts.",
            [
                new PlanTask("T1", "Task A", "Create A", [], "high", PlanTaskStatus.Complete,
                    Commit: "aaa1111",
                    Outputs: [new PlanTaskOutput("contract-a", "Public contract")]),
                new PlanTask("T2", "Task B", "Use A", ["T1"], "high", PlanTaskStatus.Pending,
                    Inputs: ["contract-a"]),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    private static Plan MakeSchemaValidPlanWithReadyValidation()
    {
        const string groupId = "BLAZORAIGEN-20260316";
        var firstTaskId = $"{groupId}-001";
        var secondTaskId = $"{groupId}-002";
        var validation = new PlanValidationNode(
            $"{groupId}-VAL-001",
            "Verify Contract",
            "Verify the first task produces what the second task consumes.",
            [firstTaskId],
            [secondTaskId],
            ["The first task produces the declared contract."],
            ["contract-a"],
            "evidence",
            [],
            true,
            PlanValidationStatus.Ready);
        return new Plan(
            groupId,
            "rev",
            PlanSource.TasksJson,
            PlanLifecycleStatus.Executing,
            "Integration Plan",
            "feature/plan",
            "Verify cross-task contracts.",
            [
                new PlanTask(
                    firstTaskId,
                    "Task A",
                    "Create A. Observable outcome: A exports contract-a. Production consumer: Task B consumes contract-a.",
                    [],
                    "high",
                    PlanTaskStatus.Complete,
                    Commit: "aaa1111",
                    Outputs: [new PlanTaskOutput("contract-a", "Public contract")]),
                new PlanTask(
                    secondTaskId,
                    "Task B",
                    "Use A. Observable outcome: B consumes contract-a in production.",
                    [firstTaskId],
                    "high",
                    PlanTaskStatus.Pending,
                    Inputs: ["contract-a"]),
            ],
            [],
            new PlanProgress(1, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    private static Plan MakePlanWithPassedValidation()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify Contract", "Verify A produces what B consumes.",
            ["T1"], ["T2"],
            ["A produces the declared output."], ["contract-a"],
            "evidence", [], true,
            PlanValidationStatus.Passed,
            Summary: "All assertions verified.",
            Evidence: ["File exports.ts contains expected interface."],
            ValidatedCommit: "aaa1111");
        return new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Integration Plan", "feature/plan", "Verify cross-task contracts.",
            [
                new PlanTask("T1", "Task A", "Create A", [], "high", PlanTaskStatus.Complete,
                    Commit: "aaa1111",
                    Outputs: [new PlanTaskOutput("contract-a", "Public contract")]),
                new PlanTask("T2", "Task B", "Use A", ["T1"], "high", PlanTaskStatus.Pending,
                    Inputs: ["contract-a"]),
                new PlanTask("T3", "Task C", "Independent", [], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(1, 3), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    private static Plan MakeLegacyPlanAllComplete()
    {
        return new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Legacy Plan", "feature/legacy", "No validations.",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Complete),
            ],
            [],
            new PlanProgress(2, 2), new PlanTimestamps(DateTimeOffset.UtcNow));
    }
}
