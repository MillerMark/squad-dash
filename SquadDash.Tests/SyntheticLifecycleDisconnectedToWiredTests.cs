using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Host-controlled synthetic lifecycle runner: disconnected-to-wired production pipeline.
///
/// Scenario: Task A produces a helper with passing unit tests but NO production caller.
/// The host preserves the commit while refusing validation advancement.
/// After repair evidence connects the helper to the declared production entry point,
/// the validation passes and downstream work unblocks.
///
/// Exercises real production services end-to-end:
/// PlanStoreUpdater, PlanValidationScheduler, PlanValidationReadinessEvaluator,
/// PlanCohesionValidator, PlanValidationPromptBuilder, PlanValidationResultParser,
/// PlanExecutionBoundaryPolicy, ValidationShieldPresenter, TasksJsonParser.
/// </summary>
[TestFixture]
internal sealed class SyntheticLifecycleDisconnectedToWiredTests
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
    // Plan construction: TASKS_JSON proposal with 2 tasks + 1 validation node
    // ═══════════════════════════════════════════════════════════════════════════

    private static Plan MakeProposalPlan()
    {
        // Task A: produces "search-helper" output
        // Task B: consumes "search-helper" input (wiring A into production)
        // V1: gates B, asserts A's output is consumed in production
        var taskA = new PlanTask(
            "LIFECYCLE-001", "Create SearchHelper", "Create SearchHelper class with unit tests.",
            [], "high", PlanTaskStatus.Pending,
            Outputs: [new PlanTaskOutput("search-helper", "SearchHelper utility class")]);

        var taskB = new PlanTask(
            "LIFECYCLE-002", "Wire SearchHelper into SearchPanel",
            "Connect SearchHelper to the SearchPanel production entry point.",
            ["LIFECYCLE-001"], "high", PlanTaskStatus.Pending,
            Inputs: ["search-helper"]);

        var validation = new PlanValidationNode(
            "V1", "Verify SearchHelper Wiring",
            "Verify that SearchHelper output is consumed by a production caller.",
            ["LIFECYCLE-001"], ["LIFECYCLE-002"],
            ["SearchHelper output is consumed in production (not just tests)."],
            ["search-helper"],
            "evidence", ["dotnet build"], true,
            PlanValidationStatus.Pending);

        return new Plan(
            "LIFECYCLE-PLAN", "rev-lifecycle", PlanSource.TasksJson,
            PlanLifecycleStatus.Executing,
            "Disconnected-to-Wired Lifecycle",
            "feature/plan-cohesion-acceptance",
            "Prove that a disconnected helper blocks advancement until wired.",
            [taskA, taskB],
            [],
            new PlanProgress(0, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. Proposal Phase — TASKS_JSON parsing and plan construction
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase1_Proposal_ValidPlanWithValidationNode()
    {
        var plan = MakeProposalPlan();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Has.Count.EqualTo(2));
            Assert.That(plan.Validations, Has.Count.EqualTo(1));
            Assert.That(plan.Validations![0].AfterTaskIds, Is.EqualTo(new[] { "LIFECYCLE-001" }));
            Assert.That(plan.Validations![0].BeforeTaskIds, Is.EqualTo(new[] { "LIFECYCLE-002" }));
            Assert.That(plan.Validations![0].Assertions[0],
                Does.Contain("consumed in production"));
            Assert.That(plan.Tasks[0].Outputs, Has.Count.EqualTo(1));
            Assert.That(plan.Tasks[0].Outputs![0].OutputId, Is.EqualTo("search-helper"));
            Assert.That(plan.Tasks[1].Inputs, Is.EqualTo(new[] { "search-helper" }));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. Scheduling Phase — V1 blocks B, A is schedulable
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase2_Scheduling_V1BlocksB_AIsSchedulable()
    {
        var plan = MakeProposalPlan();

        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Does.Contain("LIFECYCLE-002"),
                "Task B must be blocked by V1");
            Assert.That(blocked, Does.Not.Contain("LIFECYCLE-001"),
                "Task A (upstream) must not be blocked");
        });
    }

    [Test]
    public void Phase2_Scheduling_NoValidationSchedulableWhilePending()
    {
        var plan = MakeProposalPlan();

        var next = PlanValidationScheduler.SelectNextSchedulable(plan);

        Assert.That(next, Is.Null,
            "No validation should be schedulable while in Pending state (prereqs not complete)");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. Task A acceptance (disconnected helper — has tests but no production caller)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase3_TaskAAccepted_CommitPreserved_ValidationBecomesReady()
    {
        var plan = MakeProposalPlan();

        // Simulate Task A acceptance with a commit
        var items = new TaskItem[]
        {
            new("Create SearchHelper", null, false, true, "✅",
                "- [x] Create SearchHelper", TaskId: "LIFECYCLE-001"),
            new("Wire SearchHelper into SearchPanel", null, false, false, "⬜",
                "- [ ] Wire SearchHelper", TaskId: "LIFECYCLE-002"),
        };

        var acceptedResult = new DecomposeStepResult(
            "LIFECYCLE-PLAN", "LIFECYCLE-001", "rev-lifecycle", "complete",
            "abc1111", "SearchHelper created with unit tests.", null, null);

        var result = PlanStoreUpdater.ApplyStepAccepted(
            plan, items, nextExecutingTaskId: "LIFECYCLE-002",
            acceptedResult: acceptedResult);

        Assert.Multiple(() =>
        {
            // Task A commit is preserved
            var taskA = result.Tasks.First(t => t.TaskId == "LIFECYCLE-001");
            Assert.That(taskA.Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(taskA.Commit, Is.EqualTo("abc1111"),
                "Commit from disconnected helper must be preserved");

            // Validation transitions to Ready (prereqs complete)
            Assert.That(result.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready),
                "V1 should become Ready after Task A completes");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. Validation scheduling — V1 selected, prompt includes assertions
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase4_ValidationScheduling_V1Selected_PromptCorrect()
    {
        var plan = MakePlanAfterTaskAAccepted();

        // Scheduler selects V1
        var next = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(next, Is.Not.Null);
        Assert.That(next!.ValidationId, Is.EqualTo("V1"));

        // Build validation prompt
        var prompt = PlanValidationPromptBuilder.Build(plan, next, "abc1111");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("SearchHelper output is consumed in production"));
            Assert.That(prompt, Does.Contain("search-helper"));
            Assert.That(prompt, Does.Contain("abc1111"));
            Assert.That(prompt, Does.Contain("Validation Assignment"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. Validation failure — missing wiring detected
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase5_ValidationFails_MissingWiring_CommitPreserved_BBlocked()
    {
        var plan = MakePlanAfterTaskAAccepted();

        // Start validation
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Validating));

        // Simulate failed validation result — wiring not found
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false,
            "SearchHelper has unit tests but no production caller found.",
            ["No production code references SearchHelper."], validatedCommit: null);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked),
                "Plan should be Blocked after validation failure");

            // Commit is preserved even though validation failed
            var taskA = plan.Tasks.First(t => t.TaskId == "LIFECYCLE-001");
            Assert.That(taskA.Commit, Is.EqualTo("abc1111"),
                "Task A commit must be preserved despite validation failure");
            Assert.That(taskA.Status, Is.EqualTo(PlanTaskStatus.Complete),
                "Task A status remains Complete");

            // B is still blocked
            var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
            Assert.That(blocked, Does.Contain("LIFECYCLE-002"),
                "Task B must remain blocked after validation failure");

            // Boundary policy detects the failure
            Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.True);
        });
    }

    [Test]
    public void Phase5_ValidationResultParser_ParsesFailedResponse()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "LIFECYCLE-PLAN",
              "passed": false,
              "summary": "SearchHelper has unit tests but no production caller found.",
              "assertionEvidence": [
                {
                  "assertion": "SearchHelper output is consumed in production (not just tests).",
                  "passed": false,
                  "evidence": "No production code references SearchHelper."
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
    // 6. Evidence repair — retry transitions back to Ready
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase6_EvidenceRepair_RetryTransitionsToReady_CommitPreserved()
    {
        var plan = MakePlanAfterValidationFailed();

        // Apply retry
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready),
                "V1 should transition back to Ready after retry");
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing),
                "Plan should unblock from Blocked → Executing after retry");

            // Task A commit still preserved
            var taskA = plan.Tasks.First(t => t.TaskId == "LIFECYCLE-001");
            Assert.That(taskA.Commit, Is.EqualTo("abc1111"),
                "Task A commit preserved through retry");

            // Boundary policy no longer sees failure
            Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.False);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. Wired evidence — passing validation, B unblocks
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase7_WiredEvidence_ValidationPasses_BUnblocks()
    {
        var plan = MakePlanAfterValidationFailed();
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");

        // Start validation again
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");

        // Submit passing result — wiring now exists
        plan = PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: true,
            "SearchHelper is consumed by SearchPanel.HandleQuery in production.",
            ["SearchPanel.cs line 42 calls SearchHelper.Execute()."],
            validatedCommit: "abc1111");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
            Assert.That(plan.Validations![0].ValidatedCommit, Is.EqualTo("abc1111"));

            // B is no longer blocked
            var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
            Assert.That(blocked, Does.Not.Contain("LIFECYCLE-002"),
                "Task B must be unblocked after validation passes");

            Assert.That(PlanExecutionBoundaryPolicy.HasFailedValidation(plan), Is.False);
        });
    }

    [Test]
    public void Phase7_ValidationResultParser_ParsesPassingResponse()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "LIFECYCLE-PLAN",
              "passed": true,
              "summary": "SearchHelper is consumed by SearchPanel.HandleQuery in production.",
              "assertionEvidence": [
                {
                  "assertion": "SearchHelper output is consumed in production (not just tests).",
                  "passed": true,
                  "evidence": "SearchPanel.cs line 42 calls SearchHelper.Execute()."
                }
              ],
              "validatedCommit": "abc1111"
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out var result, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Passed, Is.True);
            Assert.That(result.ValidatedCommit, Is.EqualTo("abc1111"));
            Assert.That(result.AssertionEvidence[0].Passed, Is.True);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 8. Task B acceptance and completion
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase8_TaskBAccepted_AllRequiredPassed_PlanCanComplete()
    {
        var plan = MakePlanAfterValidationPassed();

        // Accept Task B
        var items = new TaskItem[]
        {
            new("Create SearchHelper", null, false, true, "✅",
                "- [x] Create SearchHelper", TaskId: "LIFECYCLE-001"),
            new("Wire SearchHelper into SearchPanel", null, false, true, "✅",
                "- [x] Wire SearchHelper", TaskId: "LIFECYCLE-002"),
        };

        var acceptedResult = new DecomposeStepResult(
            "LIFECYCLE-PLAN", "LIFECYCLE-002", "rev-lifecycle", "complete",
            "def2222", "SearchHelper wired into SearchPanel.", null, null);

        var result = PlanStoreUpdater.ApplyStepAccepted(
            plan, items, nextExecutingTaskId: null,
            acceptedResult: acceptedResult);

        Assert.Multiple(() =>
        {
            var taskB = result.Tasks.First(t => t.TaskId == "LIFECYCLE-002");
            Assert.That(taskB.Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(taskB.Commit, Is.EqualTo("def2222"));

            // All validations passed — completion gate satisfied
            Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(result), Is.True,
                "AllRequiredPassed must be true after validation passed and all tasks done");
        });

        // Explicit completion (ApplyStepAccepted does not auto-complete per design)
        var completed = PlanStoreUpdater.RepairInconsistentState(result);
        Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed),
            "Plan completes via RepairInconsistentState when all tasks done and validations passed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 9. Restart safety — serialization round-trips at key states
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase9_RestartSafety_AfterFailure_RoundTrips()
    {
        var plan = MakePlanAfterValidationFailed();
        AssertRoundTrip(plan, PlanValidationStatus.Failed, PlanLifecycleStatus.Blocked);
    }

    [Test]
    public void Phase9_RestartSafety_AfterRetry_RoundTrips()
    {
        var plan = MakePlanAfterValidationFailed();
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");
        AssertRoundTrip(plan, PlanValidationStatus.Ready, PlanLifecycleStatus.Executing);
    }

    [Test]
    public void Phase9_RestartSafety_AfterCompletion_RoundTrips()
    {
        var plan = MakePlanAfterValidationPassed();
        // Complete task B
        plan = plan with
        {
            Tasks = plan.Tasks.Select(t => t with { Status = PlanTaskStatus.Complete,
                Commit = t.TaskId == "LIFECYCLE-001" ? "abc1111" : "def2222" }).ToArray(),
            Progress = new PlanProgress(2, 2),
            LifecycleStatus = PlanLifecycleStatus.Completed,
        };
        AssertRoundTrip(plan, PlanValidationStatus.Passed, PlanLifecycleStatus.Completed);
    }

    [Test]
    public void Phase9_RestartSafety_BlockedTaskIds_DeterministicAfterRestart()
    {
        var plan = MakePlanAfterValidationFailed();

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;

        var blockedBefore = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        var blockedAfter = PlanValidationScheduler.ComputeBlockedTaskIds(restored);

        Assert.That(blockedAfter, Is.EquivalentTo(blockedBefore),
            "Blocked task IDs must be deterministic after restart");
    }

    [Test]
    public void Phase9_RestartSafety_BoundaryPolicy_RecoversInProgressValidation()
    {
        var plan = MakePlanAfterTaskAAccepted();
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");

        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;

        var selected = PlanExecutionBoundaryPolicy.SelectValidation(restored,
            activeValidationId: "V1");
        Assert.That(selected?.ValidationId, Is.EqualTo("V1"),
            "Boundary policy must recover in-progress validation after restart");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 10. Shield presentation at each state transition
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Phase10_ShieldPresentation_PendingState()
    {
        var plan = MakeProposalPlan();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Pending));
    }

    [Test]
    public void Phase10_ShieldPresentation_ReadyState()
    {
        var plan = MakePlanAfterTaskAAccepted();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Ready));
    }

    [Test]
    public void Phase10_ShieldPresentation_ValidatingState()
    {
        var plan = MakePlanAfterTaskAAccepted();
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Validating));
    }

    [Test]
    public void Phase10_ShieldPresentation_FailedState()
    {
        var plan = MakePlanAfterValidationFailed();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Failed));
    }

    [Test]
    public void Phase10_ShieldPresentation_PassedState()
    {
        var plan = MakePlanAfterValidationPassed();
        var state = ValidationShieldPresenter.DeriveVisualState(plan.Validations![0].Status);
        Assert.That(state, Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Passed));
    }

    [Test]
    public void Phase10_ShieldPresentation_TooltipContent_FailedState()
    {
        var plan = MakePlanAfterValidationFailed();
        var content = ValidationShieldPresenter.BuildTooltipContent(
            plan.Validations![0], plan.Tasks.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Verify SearchHelper Wiring"));
            Assert.That(content.Assertions, Has.Count.EqualTo(1));
            Assert.That(content.Assertions[0], Does.Contain("consumed in production"));
            Assert.That(content.PrerequisiteLabels, Has.Count.EqualTo(1));
            Assert.That(content.PrerequisiteLabels[0], Does.Contain("SearchHelper"));
            Assert.That(content.BlockedLabels, Has.Count.EqualTo(1));
            Assert.That(content.BlockedLabels[0], Does.Contain("SearchHelper"));
        });
    }

    [Test]
    public void Phase10_ShieldPresentation_HighlightedTasks()
    {
        var plan = MakePlanAfterValidationFailed();
        var result = ValidationShieldPresenter.ComputeHighlightedTasks(
            plan.Validations![0], plan.Tasks.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(result.PrerequisiteTaskIds, Does.Contain("LIFECYCLE-001"),
                "Prerequisite task should be highlighted");
            Assert.That(result.BlockedTaskIds, Does.Contain("LIFECYCLE-002"),
                "Blocked task should be highlighted");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Cohesion validator advisory checks
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void CohesionAdvisory_TaskA_IsArtifactOnly_WithoutProductionConsumer()
    {
        // Task A description: a helper with unit tests but no production consumer
        const string disconnectedDescription = "Add a helper class for search utilities.";

        Assert.Multiple(() =>
        {
            Assert.That(PlanCohesionValidator.HasProductionConsumer(disconnectedDescription),
                Is.False, "Disconnected helper has no production consumer");
            Assert.That(PlanCohesionValidator.IsArtifactOnly(disconnectedDescription),
                Is.True, "Helper-only task is artifact-only");
        });
    }

    [Test]
    public void CohesionAdvisory_TaskB_HasProductionConsumer()
    {
        // Task B description: explicitly wires helper into a production caller
        const string wiredDescription =
            "SearchPanel calls SearchHelper.Execute and renders results.";

        Assert.That(PlanCohesionValidator.HasProductionConsumer(wiredDescription), Is.True,
            "Task B description declares a production consumer");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Full end-to-end lifecycle in a single test
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FullLifecycle_DisconnectedToWired_EndToEnd()
    {
        // 1. Proposal
        var plan = MakeProposalPlan();
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Pending));

        // 2. Scheduling — B blocked, A not
        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        Assert.That(blocked, Does.Contain("LIFECYCLE-002"));
        Assert.That(blocked, Does.Not.Contain("LIFECYCLE-001"));

        // 3. Task A accepted (disconnected helper)
        var items = new TaskItem[]
        {
            new("Create SearchHelper", null, false, true, "✅",
                "- [x] Create SearchHelper", TaskId: "LIFECYCLE-001"),
            new("Wire SearchHelper", null, false, false, "⬜",
                "- [ ] Wire SearchHelper", TaskId: "LIFECYCLE-002"),
        };
        var acceptedA = new DecomposeStepResult(
            "LIFECYCLE-PLAN", "LIFECYCLE-001", "rev-lifecycle", "complete",
            "abc1111", "SearchHelper created.", null, null);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items,
            nextExecutingTaskId: "LIFECYCLE-002", acceptedResult: acceptedA);
        Assert.That(plan.Tasks[0].Commit, Is.EqualTo("abc1111"));
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));

        // 4. Validation scheduling
        var nextV = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(nextV!.ValidationId, Is.EqualTo("V1"));
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");

        // 5. Validation fails — no production caller
        plan = PlanStoreUpdater.ApplyValidationResult(plan, "V1", passed: false,
            "No production caller for SearchHelper.",
            ["Only test references found."], validatedCommit: null);
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Failed));
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(plan.Tasks[0].Commit, Is.EqualTo("abc1111"), "Commit preserved");

        // 6. Evidence repair — retry
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(plan.Tasks[0].Commit, Is.EqualTo("abc1111"), "Commit preserved after retry");

        // 7. Wired evidence — pass
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");
        plan = PlanStoreUpdater.ApplyValidationResult(plan, "V1", passed: true,
            "SearchHelper consumed by SearchPanel.HandleQuery.",
            ["SearchPanel.cs:42 calls SearchHelper.Execute."],
            validatedCommit: "abc1111");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
        blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        Assert.That(blocked, Does.Not.Contain("LIFECYCLE-002"), "B unblocked after pass");

        // 8. Task B accepted — plan completes
        var itemsB = new TaskItem[]
        {
            new("Create SearchHelper", null, false, true, "✅",
                "- [x] Create SearchHelper", TaskId: "LIFECYCLE-001"),
            new("Wire SearchHelper", null, false, true, "✅",
                "- [x] Wire SearchHelper", TaskId: "LIFECYCLE-002"),
        };
        var acceptedB = new DecomposeStepResult(
            "LIFECYCLE-PLAN", "LIFECYCLE-002", "rev-lifecycle", "complete",
            "def2222", "Wiring complete.", null, null);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, itemsB,
            nextExecutingTaskId: null, acceptedResult: acceptedB);
        Assert.That(PlanValidationReadinessEvaluator.AllRequiredPassed(plan), Is.True);
        // ApplyStepAccepted does not auto-complete; use RepairInconsistentState
        plan = PlanStoreUpdater.RepairInconsistentState(plan);
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));

        // 9. Restart safety — final state round-trips
        var json = JsonSerializer.Serialize(plan, WriteOptions);
        var restored = JsonSerializer.Deserialize<Plan>(json, ReadOptions)!;
        Assert.That(restored.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(restored.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Passed));
        Assert.That(restored.Tasks[0].Commit, Is.EqualTo("abc1111"));
        Assert.That(restored.Tasks[1].Commit, Is.EqualTo("def2222"));

        // 10. Shield presentation (verify at final state)
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState(restored.Validations![0].Status),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Passed));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers — build plan states at various lifecycle phases
    // ═══════════════════════════════════════════════════════════════════════════

    private static Plan MakePlanAfterTaskAAccepted()
    {
        var plan = MakeProposalPlan();
        var items = new TaskItem[]
        {
            new("Create SearchHelper", null, false, true, "✅",
                "- [x] Create SearchHelper", TaskId: "LIFECYCLE-001"),
            new("Wire SearchHelper", null, false, false, "⬜",
                "- [ ] Wire SearchHelper", TaskId: "LIFECYCLE-002"),
        };
        var acceptedResult = new DecomposeStepResult(
            "LIFECYCLE-PLAN", "LIFECYCLE-001", "rev-lifecycle", "complete",
            "abc1111", "SearchHelper created.", null, null);
        return PlanStoreUpdater.ApplyStepAccepted(plan, items,
            nextExecutingTaskId: "LIFECYCLE-002", acceptedResult: acceptedResult);
    }

    private static Plan MakePlanAfterValidationFailed()
    {
        var plan = MakePlanAfterTaskAAccepted();
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");
        return PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: false,
            "SearchHelper has unit tests but no production caller found.",
            ["No production code references SearchHelper."], validatedCommit: null);
    }

    private static Plan MakePlanAfterValidationPassed()
    {
        var plan = MakePlanAfterValidationFailed();
        plan = PlanStoreUpdater.ApplyValidationRetry(plan, "V1");
        plan = PlanStoreUpdater.ApplyValidationStarted(plan, "V1");
        return PlanStoreUpdater.ApplyValidationResult(
            plan, "V1", passed: true,
            "SearchHelper is consumed by SearchPanel.HandleQuery.",
            ["SearchPanel.cs:42 calls SearchHelper.Execute."],
            validatedCommit: "abc1111");
    }

    private static void AssertRoundTrip(
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
            Assert.That(restored.Tasks[0].Commit, Is.EqualTo("abc1111"),
                "Task A commit must survive serialization round-trip");
            Assert.That(restored.Tasks, Has.Count.EqualTo(plan.Tasks.Count));
        });
    }
}
