using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Disposable self-hosted cohesion proof: exercises a complete plan lifecycle with
/// a PlanRowStatusFormatter scenario. A 2-step plan where:
///   Step 1: Introduce PlanRowStatusFormatter helper (unit tests, no production caller)
///   Step 2: Integrate the formatter into plan-row rendering surface
///
/// Proves:
///   - Disconnected helper acceptance preserves commit but blocks advancement
///   - Incomplete integration evidence is rejected; host holds the step (no rerun)
///   - Repaired wiring evidence passes validation and unblocks downstream
///   - Restart mid-flow preserves all state (serialize/deserialize round-trip)
///   - Plan completion with all validations passed
///   - ValidationShieldPresenter states verified at each transition
///   - PendingDecomposePlanAdapter durable conversion round-trips
///
/// Limitations documented (end of file):
///   - No actual UI rendering (Plans panel) — shield states are verified via DeriveVisualState
///   - No real file system interaction — all state is in-memory
///   - Cohesion advisory heuristics are text-pattern-based (not AST-aware)
/// </summary>
[TestFixture]
internal sealed class DisposableLivePlanCohesionProofTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Plan construction: PlanRowStatusFormatter scenario (2 tasks + 1 validation)
    // ═══════════════════════════════════════════════════════════════════════════

    private static Plan MakeFormatterPlan()
    {
        var stepOne = new PlanTask(
            "COHESION-001", "Create PlanRowStatusFormatter",
            "Introduce a reusable PlanRowStatusFormatter helper that converts plan lifecycle " +
            "statuses into display strings suitable for plan-row surfaces.",
            [], "high", PlanTaskStatus.Pending,
            Outputs: [new PlanTaskOutput("status-formatter", "PlanRowStatusFormatter utility class")]);

        var stepTwo = new PlanTask(
            "COHESION-002", "Integrate formatter into plan-row rendering",
            "Wire PlanRowStatusFormatter into the PlanRowPresenter so that plan-row " +
            "status labels are driven by the shared formatter in production.",
            ["COHESION-001"], "high", PlanTaskStatus.Pending,
            Inputs: ["status-formatter"]);

        var validation = new PlanValidationNode(
            "V-FMT", "Verify PlanRowStatusFormatter Wiring",
            "Verify that PlanRowStatusFormatter output is consumed by a host-visible " +
            "plan-row surface (not just unit tests).",
            ["COHESION-001"], ["COHESION-002"],
            ["PlanRowStatusFormatter is called from a production plan-row rendering path."],
            ["status-formatter"],
            "evidence", ["dotnet build"], true,
            PlanValidationStatus.Pending);

        return new Plan(
            "COHESION-PROOF", "rev-cohesion-proof", PlanSource.TasksJson,
            PlanLifecycleStatus.Executing,
            "Disposable Cohesion Proof — PlanRowStatusFormatter",
            "feature/plan-cohesion-acceptance",
            "Prove self-hosted cohesion: a disconnected formatter blocks until wired.",
            [stepOne, stepTwo],
            [],
            new PlanProgress(0, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 1: Plan generation with cohesion-aware validation node
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase1_PlanGenerated_WithCohesionValidationNode()
    {
        var plan = MakeFormatterPlan();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Has.Count.EqualTo(2));
            Assert.That(plan.Validations, Has.Count.EqualTo(1));
            Assert.That(plan.Validations![0].ValidationId, Is.EqualTo("V-FMT"));
            Assert.That(plan.Validations![0].AfterTaskIds, Is.EqualTo(new[] { "COHESION-001" }));
            Assert.That(plan.Validations![0].BeforeTaskIds, Is.EqualTo(new[] { "COHESION-002" }));
            Assert.That(plan.Validations![0].RevalidateAtCompletion, Is.True);
            Assert.That(plan.Validations![0].Assertions[0],
                Does.Contain("production plan-row rendering"));
            Assert.That(plan.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(plan.Tasks[0].Outputs![0].OutputId, Is.EqualTo("status-formatter"));
            Assert.That(plan.Tasks[1].Inputs, Is.EqualTo(new[] { "status-formatter" }));
        });
    }

    [Test]
    public void Phase1_CohesionAdvisory_Step1IsArtifactOnly()
    {
        const string formatterDesc =
            "Add a helper class for plan-row status formatting utilities.";

        Assert.Multiple(() =>
        {
            Assert.That(PlanCohesionValidator.IsArtifactOnly(formatterDesc), Is.True,
                "Step 1 is a helper without declared production consumer");
            Assert.That(PlanCohesionValidator.HasProductionConsumer(formatterDesc), Is.False);
        });
    }

    [Test]
    public void Phase1_CohesionAdvisory_Step2HasProductionConsumer()
    {
        const string integrationDesc =
            "PlanRowPresenter calls PlanRowStatusFormatter.Format and renders results.";

        Assert.That(PlanCohesionValidator.HasProductionConsumer(integrationDesc), Is.True,
            "Step 2 description declares a production consumer");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 2: Scheduling — Step 2 blocked, Step 1 available
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase2_ValidationBlocks_Step2_NotStep1()
    {
        var plan = MakeFormatterPlan();
        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Does.Contain("COHESION-002"),
                "Step 2 must be blocked by V-FMT");
            Assert.That(blocked, Does.Not.Contain("COHESION-001"),
                "Step 1 (upstream) must not be blocked");
        });
    }

    [Test]
    public void Phase2_NoValidationSchedulable_WhilePrereqsPending()
    {
        var plan = MakeFormatterPlan();
        var next = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(next, Is.Null, "No validation schedulable while prereqs not done");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 3: Step 1 accepted — disconnected helper with tests but no caller
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase3_Step1Accepted_CommitPreserved_ValidationBecomesReady()
    {
        var plan = MakeFormatterPlan();
        plan = AcceptStep1(plan);

        Assert.Multiple(() =>
        {
            var step1 = plan.Tasks.First(t => t.TaskId == "COHESION-001");
            Assert.That(step1.Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(step1.Commit, Is.EqualTo("fmt0001"),
                "Commit from disconnected formatter must be preserved");

            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready),
                "V-FMT becomes Ready after Step 1 completes");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 4: Validation scheduling and prompt generation
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase4_ValidationScheduled_PromptIncludesAssertions()
    {
        var plan = AcceptStep1(MakeFormatterPlan());

        var next = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(next, Is.Not.Null);
        Assert.That(next!.ValidationId, Is.EqualTo("V-FMT"));

        var prompt = PlanValidationPromptBuilder.Build(plan, next, "fmt0001");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("PlanRowStatusFormatter"));
            Assert.That(prompt, Does.Contain("production plan-row rendering"));
            Assert.That(prompt, Does.Contain("fmt0001"));
            Assert.That(prompt, Does.Contain("status-formatter"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 5: Validation FAILS — incomplete integration evidence
    // Host holds the step (commit preserved), does NOT rerun Step 1
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase5_IncompleteEvidence_ValidationFails_CommitPreserved_StepHeld()
    {
        var plan = AcceptStep1(MakeFormatterPlan());
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");

        // Deliberately incomplete evidence: formatter exists but no production caller
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V-FMT", passed: false,
            "PlanRowStatusFormatter has unit tests but no production caller in plan-row rendering.",
            ["Only test references found; no PlanRowPresenter usage."],
            validatedCommit: null);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked),
                "Plan blocks after validation failure");

            // Commit preserved — host holds the step, does NOT rerun
            var step1 = plan.Tasks.First(t => t.TaskId == "COHESION-001");
            Assert.That(step1.Commit, Is.EqualTo("fmt0001"),
                "Step 1 commit MUST be preserved (held, not rerun)");
            Assert.That(step1.Status, Is.EqualTo(PlanTaskStatus.Complete),
                "Step 1 status remains Complete (valuable work preserved)");

            // Step 2 still blocked
            var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
            Assert.That(blocked, Does.Contain("COHESION-002"));

            Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.True);
        });
    }

    [Test]
    public void Phase5_ResultParser_ParsesIncompleteEvidence()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V-FMT",
              "planId": "COHESION-PROOF",
              "passed": false,
              "summary": "PlanRowStatusFormatter has unit tests but no production caller.",
              "assertionEvidence": [
                {
                  "assertion": "PlanRowStatusFormatter is called from a production plan-row rendering path.",
                  "passed": false,
                  "evidence": "Only test references found; no PlanRowPresenter usage."
                }
              ]
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out var result, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Passed, Is.False);
            Assert.That(result.Summary, Does.Contain("no production caller"));
            Assert.That(result.AssertionEvidence[0].Passed, Is.False);
            Assert.That(result.ValidatedCommit, Is.Null);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 6: Evidence repair — retry transitions back to Ready
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase6_EvidenceRepair_RetryUnblocksPlan_CommitPreserved()
    {
        var plan = MakePlanAfterValidationFailed();

        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V-FMT");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready),
                "V-FMT transitions back to Ready for re-validation");
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing),
                "Plan unblocks from Blocked → Executing after retry");

            var step1 = plan.Tasks.First(t => t.TaskId == "COHESION-001");
            Assert.That(step1.Commit, Is.EqualTo("fmt0001"),
                "Commit preserved through evidence repair");

            Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.False);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 7: Real production wiring evidence — validation passes
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase7_RealWiringEvidence_ValidationPasses_Step2Unblocks()
    {
        var plan = MakePlanAfterValidationFailed();
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V-FMT");
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");

        // Production wiring now exists
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V-FMT", passed: true,
            "PlanRowStatusFormatter is consumed by PlanRowPresenter.RenderStatus in production.",
            ["PlanRowPresenter.cs line 87 calls PlanRowStatusFormatter.Format()."],
            validatedCommit: "fmt0001");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
            Assert.That(plan.Validations![0].ValidatedCommit, Is.EqualTo("fmt0001"));

            var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
            Assert.That(blocked, Does.Not.Contain("COHESION-002"),
                "Step 2 unblocked after validation passes");

            Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.False);
        });
    }

    [Test]
    public void Phase7_ResultParser_ParsesWiringEvidence()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V-FMT",
              "planId": "COHESION-PROOF",
              "passed": true,
              "summary": "PlanRowStatusFormatter consumed by PlanRowPresenter.RenderStatus.",
              "assertionEvidence": [
                {
                  "assertion": "PlanRowStatusFormatter is called from a production plan-row rendering path.",
                  "passed": true,
                  "evidence": "PlanRowPresenter.cs line 87 calls PlanRowStatusFormatter.Format()."
                }
              ],
              "validatedCommit": "fmt0001"
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out var result, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Passed, Is.True);
            Assert.That(result.ValidatedCommit, Is.EqualTo("fmt0001"));
            Assert.That(result.AssertionEvidence[0].Passed, Is.True);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 8: Step 2 accepted — plan completes
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase8_Step2Accepted_PlanCompletes_AllValidationsPassed()
    {
        var plan = MakePlanAfterValidationPassed();

        var items = new TaskItem[]
        {
            new("Create PlanRowStatusFormatter", null, false, true, "✅",
                "- [x] Create PlanRowStatusFormatter", TaskId: "COHESION-001"),
            new("Integrate formatter into plan-row rendering", null, false, true, "✅",
                "- [x] Integrate formatter", TaskId: "COHESION-002"),
        };
        var acceptedResult = new DecomposeStepResult(
            "COHESION-PROOF", "COHESION-002", "rev-cohesion-proof", "complete",
            "fmt0002", "Formatter wired into PlanRowPresenter.", null, null);

        var result = PlanStoreUpdater.ApplyStepAccepted(
            plan, items, nextExecutingTaskId: null,
            acceptedResult: acceptedResult);

        Assert.Multiple(() =>
        {
            var step2 = result.Tasks.First(t => t.TaskId == "COHESION-002");
            Assert.That(step2.Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(step2.Commit, Is.EqualTo("fmt0002"));

            Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(result), Is.True,
                "All required validations passed → completion gate satisfied");
        });

        var completed = PlanStoreUpdater.RepairInconsistentState(result);
        Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed),
            "Plan completes via RepairInconsistentState when all work done");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 9: Restart simulation — serialize/deserialize mid-flow
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase9_Restart_AfterFailure_StatePreserved()
    {
        var plan = MakePlanAfterValidationFailed();
        AssertRestartRoundTrip(plan, PlanValidationStatus.Failed, PlanLifecycleStatus.Blocked);
    }

    [Test]
    public void Phase9_Restart_AfterRetry_StatePreserved()
    {
        var plan = MakePlanAfterValidationFailed();
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V-FMT");
        AssertRestartRoundTrip(plan, PlanValidationStatus.Ready, PlanLifecycleStatus.Executing);
    }

    [Test]
    public void Phase9_Restart_AfterCompletion_StatePreserved()
    {
        var plan = MakePlanAfterValidationPassed();
        plan = plan with
        {
            Tasks = plan.Tasks.Select(t => t with
            {
                Status = PlanTaskStatus.Complete,
                Commit = t.TaskId == "COHESION-001" ? "fmt0001" : "fmt0002",
            }).ToArray(),
            Progress = new PlanProgress(2, 2),
            LifecycleStatus = PlanLifecycleStatus.Completed,
        };
        AssertRestartRoundTrip(plan, PlanValidationStatus.Passed, PlanLifecycleStatus.Completed);
    }

    [Test]
    public void Phase9_Restart_InProgressValidation_RecoveredByBoundaryPolicy()
    {
        var plan = AcceptStep1(MakeFormatterPlan());
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;

        var selected = PlanExecutionBoundaryPolicy.SelectValidation(restored,
            activeValidationId: "V-FMT");
        Assert.That(selected?.ValidationId, Is.EqualTo("V-FMT"),
            "Boundary policy recovers in-progress validation after restart");
    }

    [Test]
    public void Phase9_Restart_BlockedTaskIds_Deterministic()
    {
        var plan = MakePlanAfterValidationFailed();

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;

        var blockedBefore = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        var blockedAfter = PlanValidationScheduler.ComputeBlockedTaskIds(restored);

        Assert.That(blockedAfter, Is.EquivalentTo(blockedBefore),
            "Blocked set must be deterministic across restart");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 10: Durable conversion round-trip (PendingDecomposePlanAdapter)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase10_DurableConversion_FromPlan_PreservesValidations()
    {
        var plan = MakeFormatterPlan();
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);

        Assert.Multiple(() =>
        {
            Assert.That(pending.Group.Tasks, Has.Count.EqualTo(2));
            Assert.That(pending.Group.Tasks[0].Id, Is.EqualTo("COHESION-001"));
            Assert.That(pending.Group.Tasks[1].Id, Is.EqualTo("COHESION-002"));
            Assert.That(pending.Group.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(pending.Group.Tasks[0].Outputs![0].OutputId, Is.EqualTo("status-formatter"));
        });
    }

    [Test]
    public void Phase10_DurableConversion_RoundTrip_PreservesIntegrity()
    {
        var plan = MakeFormatterPlan();
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);
        var roundTripped = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped.Tasks, Has.Count.EqualTo(plan.Tasks.Count));
            Assert.That(roundTripped.Tasks[0].TaskId, Is.EqualTo("COHESION-001"));
            Assert.That(roundTripped.Tasks[1].TaskId, Is.EqualTo("COHESION-002"));
            Assert.That(roundTripped.Tasks[0].Outputs![0].OutputId, Is.EqualTo("status-formatter"));
            Assert.That(roundTripped.Tasks[1].Inputs, Is.EqualTo(new[] { "status-formatter" }));
            Assert.That(roundTripped.Validations, Has.Count.EqualTo(1));
            Assert.That(roundTripped.Validations![0].ValidationId, Is.EqualTo("V-FMT"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 11: ValidationShieldPresenter states at each transition
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase11_Shield_Pending()
    {
        var plan = MakeFormatterPlan();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Pending));
    }

    [Test]
    public void Phase11_Shield_Ready()
    {
        var plan = AcceptStep1(MakeFormatterPlan());
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Ready));
    }

    [Test]
    public void Phase11_Shield_Validating()
    {
        var plan = AcceptStep1(MakeFormatterPlan());
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Validating));
    }

    [Test]
    public void Phase11_Shield_Failed()
    {
        var plan = MakePlanAfterValidationFailed();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Failed));
    }

    [Test]
    public void Phase11_Shield_Passed()
    {
        var plan = MakePlanAfterValidationPassed();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Passed));
    }

    [Test]
    public void Phase11_Shield_TooltipContent_FailedState()
    {
        var plan = MakePlanAfterValidationFailed();
        var content = ValidationShieldPresenter.BuildTooltipContent(
            plan.Validations![0], plan.Tasks.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Verify PlanRowStatusFormatter Wiring"));
            Assert.That(content.Assertions, Has.Count.EqualTo(1));
            Assert.That(content.Assertions[0], Does.Contain("production plan-row rendering"));
            Assert.That(content.PrerequisiteLabels, Has.Count.EqualTo(1));
            Assert.That(content.PrerequisiteLabels[0], Does.Contain("PlanRowStatusFormatter"));
            Assert.That(content.BlockedLabels, Has.Count.EqualTo(1));
            Assert.That(content.BlockedLabels[0], Does.Contain("formatter"));
        });
    }

    [Test]
    public void Phase11_Shield_HighlightedTasks()
    {
        var plan = MakePlanAfterValidationFailed();
        var result = ValidationShieldPresenter.ComputeHighlightedTasks(
            plan.Validations![0], plan.Tasks.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(result.PrerequisiteTaskIds, Does.Contain("COHESION-001"));
            Assert.That(result.BlockedTaskIds, Does.Contain("COHESION-002"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 12: Staleness — re-acceptance invalidates passed validation
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase12_Staleness_ReacceptStep1_InvalidatesPassedValidation()
    {
        var plan = MakePlanAfterValidationPassed();

        var changedIds = new HashSet<string>(StringComparer.Ordinal) { "COHESION-001" };
        plan = PlanStoreUpdater.InvalidateCoveredValidations(plan, changedIds);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));
            Assert.That(plan.Validations![0].Summary, Does.Contain("Covered output changed"));
        });
    }

    [Test]
    public void Phase12_Staleness_UnrelatedChange_DoesNotInvalidate()
    {
        var plan = MakePlanAfterValidationPassed();

        var changedIds = new HashSet<string>(StringComparer.Ordinal) { "UNRELATED-999" };
        plan = PlanStoreUpdater.InvalidateCoveredValidations(plan, changedIds);

        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed),
            "Unrelated task change must not invalidate validation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Full end-to-end lifecycle (single comprehensive test)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FullLifecycle_DisposableCohesionProof_EndToEnd()
    {
        // --- 1. Plan generation ---
        var plan = MakeFormatterPlan();
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Pending));
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Pending));

        // --- 2. Scheduling: Step 2 blocked ---
        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        Assert.That(blocked, Does.Contain("COHESION-002"));
        Assert.That(blocked, Does.Not.Contain("COHESION-001"));

        // --- 3. Step 1 accepted (disconnected helper) ---
        plan = AcceptStep1(plan);
        Assert.That(plan.Tasks[0].Commit, Is.EqualTo("fmt0001"));
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Ready));

        // --- 4. Validation scheduling ---
        var nextV = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(nextV!.ValidationId, Is.EqualTo("V-FMT"));
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Validating));

        // --- 5. Deliberately incomplete evidence → FAILS ---
        plan = PlanStoreUpdater.ApplyValidationResult(plan, "V-FMT", passed: false,
            "PlanRowStatusFormatter not consumed in production.",
            ["Only test references."], validatedCommit: null);
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(plan.Tasks[0].Commit, Is.EqualTo("fmt0001"), "Commit preserved");
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Failed));

        // --- 6. Restart simulation mid-failure ---
        var failedJson = JsonSerializer.Serialize(plan, WriteOptions);
        var restoredFailed = JsonSerializer.Deserialize<Plan>(failedJson, ReadOptions)!;
        Assert.That(restoredFailed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(restoredFailed.Tasks[0].Commit, Is.EqualTo("fmt0001"));
        Assert.That(restoredFailed.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));
        plan = restoredFailed; // continue from restored state

        // --- 7. Evidence repair (retry) ---
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V-FMT");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(plan.Tasks[0].Commit, Is.EqualTo("fmt0001"), "Still preserved");

        // --- 8. Real production wiring → PASSES ---
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");
        plan = PlanStoreUpdater.ApplyValidationResult(plan, "V-FMT", passed: true,
            "PlanRowStatusFormatter consumed by PlanRowPresenter.RenderStatus.",
            ["PlanRowPresenter.cs:87 calls Format()."],
            validatedCommit: "fmt0001");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Passed));
        blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        Assert.That(blocked, Does.Not.Contain("COHESION-002"), "Step 2 unblocked");

        // --- 9. Step 2 accepted → plan completes ---
        var items = new TaskItem[]
        {
            new("Create PlanRowStatusFormatter", null, false, true, "✅",
                "- [x] Create PlanRowStatusFormatter", TaskId: "COHESION-001"),
            new("Integrate formatter", null, false, true, "✅",
                "- [x] Integrate formatter", TaskId: "COHESION-002"),
        };
        var acceptedB = new DecomposeStepResult(
            "COHESION-PROOF", "COHESION-002", "rev-cohesion-proof", "complete",
            "fmt0002", "Wiring complete.", null, null);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items,
            nextExecutingTaskId: null, acceptedResult: acceptedB);
        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.True);
        plan = PlanStoreUpdater.RepairInconsistentState(plan);
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));

        // --- 10. Final restart round-trip ---
        var finalJson = JsonSerializer.Serialize(plan, WriteOptions);
        var finalRestored = JsonSerializer.Deserialize<Plan>(finalJson, ReadOptions)!;
        Assert.That(finalRestored.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(finalRestored.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
        Assert.That(finalRestored.Tasks[0].Commit, Is.EqualTo("fmt0001"));
        Assert.That(finalRestored.Tasks[1].Commit, Is.EqualTo("fmt0002"));

        // --- Durable conversion ---
        var pending = PendingDecomposePlanAdapter.FromPlan(finalRestored);
        Assert.That(pending.Group.Tasks, Has.Count.EqualTo(2));
        var reconverted = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        Assert.That(reconverted.Validations, Has.Count.EqualTo(1));
        Assert.That(reconverted.Validations![0].ValidationId, Is.EqualTo("V-FMT"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers — build plan states at various lifecycle phases
    // ═══════════════════════════════════════════════════════════════════════════

    private static Plan AcceptStep1(Plan plan)
    {
        var items = new TaskItem[]
        {
            new("Create PlanRowStatusFormatter", null, false, true, "✅",
                "- [x] Create PlanRowStatusFormatter", TaskId: "COHESION-001"),
            new("Integrate formatter into plan-row rendering", null, false, false, "⬜",
                "- [ ] Integrate formatter", TaskId: "COHESION-002"),
        };
        var acceptedResult = new DecomposeStepResult(
            "COHESION-PROOF", "COHESION-001", "rev-cohesion-proof", "complete",
            "fmt0001", "PlanRowStatusFormatter created with unit tests.", null, null);

        return PlanStoreUpdater.ApplyStepAccepted(
            plan, items, nextExecutingTaskId: "COHESION-002",
            acceptedResult: acceptedResult);
    }

    private static Plan MakePlanAfterValidationFailed()
    {
        var plan = AcceptStep1(MakeFormatterPlan());
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");
        return PlanStoreUpdater.ApplyValidationResult(
            plan, "V-FMT", passed: false,
            "PlanRowStatusFormatter has unit tests but no production caller.",
            ["Only test references found; no PlanRowPresenter usage."],
            validatedCommit: null);
    }

    private static Plan MakePlanAfterValidationPassed()
    {
        var plan = MakePlanAfterValidationFailed();
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V-FMT");
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V-FMT");
        return PlanStoreUpdater.ApplyValidationResult(
            plan, "V-FMT", passed: true,
            "PlanRowStatusFormatter consumed by PlanRowPresenter.RenderStatus.",
            ["PlanRowPresenter.cs:87 calls PlanRowStatusFormatter.Format()."],
            validatedCommit: "fmt0001");
    }

    private static void AssertRestartRoundTrip(
        Plan plan, string expectedValidationStatus, string expectedLifecycleStatus)
    {
        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(restored.PlanId, Is.EqualTo(plan.PlanId));
            Assert.That(restored.LifecycleStatus, Is.EqualTo(expectedLifecycleStatus));
            Assert.That(restored.Validations, Has.Count.EqualTo(1));
            Assert.That(restored.Validations![0].Status, Is.EqualTo(expectedValidationStatus));
            Assert.That(restored.Tasks[0].Commit, Is.EqualTo("fmt0001"),
                "Step 1 commit must survive serialization round-trip");
            Assert.That(restored.Tasks, Has.Count.EqualTo(plan.Tasks.Count));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Outcomes & Limitations (documented as test evidence)
    //
    // OUTCOMES PROVED:
    // ✅ Disconnected helper acceptance preserves commit, validation becomes Ready
    // ✅ Incomplete integration evidence → validation fails, plan blocks, commit held
    // ✅ Evidence repair (retry) transitions back to Ready without losing work
    // ✅ Real production wiring evidence passes validation, unblocks downstream
    // ✅ Step 2 acceptance completes plan via RepairInconsistentState
    // ✅ Restart at any point preserves full state (serialization round-trip)
    // ✅ BoundaryPolicy recovers in-progress validation after restart
    // ✅ Blocked task IDs are deterministic across restarts
    // ✅ PendingDecomposePlanAdapter round-trips preserve all structure
    // ✅ ValidationShieldPresenter reports correct visual state at each transition
    // ✅ Staleness invalidation works for covered outputs, ignores unrelated
    // ✅ PlanValidationResultParser handles both pass and fail envelopes
    //
    // LIMITATIONS:
    // ⚠️ No actual Plans panel rendering — shield states verified via DeriveVisualState API
    // ⚠️ No actual transcript synchronization — verified through state consistency
    // ⚠️ Cohesion advisory (IsArtifactOnly/HasProductionConsumer) is text-heuristic-based
    // ⚠️ No real file system or git interaction — all state is in-memory records
    // ⚠️ TasksJsonParser.TryParse not exercised directly (plan built via record ctor)
    //    because TASKS_JSON format is validated by separate parser-specific tests
    // ═══════════════════════════════════════════════════════════════════════════
}
