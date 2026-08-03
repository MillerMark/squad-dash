using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for validation scheduling, prompt building, result parsing, durable restart behavior,
/// and stale-attempt handling.
/// </summary>
[TestFixture]
internal sealed class PlanValidationSchedulingTests
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─── Prompt Building ─────────────────────────────────────────────────────────

    [Test]
    public void ValidationPrompt_IncludesAssertionsAndPlanObjective()
    {
        var plan = MakePlanWithReadyValidation();
        var validation = plan.Validations![0];

        var prompt = PlanValidationPromptBuilder.Build(plan, validation, "abc1234");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("Validation Assignment"));
            Assert.That(prompt, Does.Contain("Plan Objective"));
            Assert.That(prompt, Does.Contain(plan.Title));
            Assert.That(prompt, Does.Contain(plan.Summary));
            Assert.That(prompt, Does.Contain(validation.Title));
            Assert.That(prompt, Does.Contain(validation.ValidationId));
            Assert.That(prompt, Does.Contain("Assertions to Evaluate"));
            Assert.That(prompt, Does.Contain("A produces the declared output."));
            Assert.That(prompt, Does.Contain("abc1234"));
            Assert.That(prompt, Does.Contain(PlanValidationResultParser.Marker));
            Assert.That(prompt, Does.Contain("Do not create any commits"));
        });
    }

    [Test]
    public void ValidationPrompt_IncludesAcceptedTaskOutputs()
    {
        var plan = MakePlanWithReadyValidation();
        var validation = plan.Validations![0];

        var prompt = PlanValidationPromptBuilder.Build(plan, validation, "abc1234");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("Accepted Task Outputs"));
            Assert.That(prompt, Does.Contain("T1"));
            Assert.That(prompt, Does.Contain("contract-a"));
        });
    }

    [Test]
    public void ValidationPrompt_IncludesVerificationCommands()
    {
        var validation = new PlanValidationNode(
            "V1", "Validate", "Validate A", ["T1"], ["T2"],
            ["Works."], ["contract-a"], "evidence", ["dotnet test", "dotnet build"], true,
            PlanValidationStatus.Ready);
        var plan = MakePlan(validation);

        var prompt = PlanValidationPromptBuilder.Build(plan, validation, "abc1234");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("Verification Commands"));
            Assert.That(prompt, Does.Contain("`dotnet test`"));
            Assert.That(prompt, Does.Contain("`dotnet build`"));
        });
    }

    [Test]
    public void CompactPlanContext_IncludesCompletedTasksAndOutputs()
    {
        var plan = MakePlanWithReadyValidation();

        var context = PlanValidationPromptBuilder.BuildCompactPlanContext(plan);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Accepted Task Outputs"));
            Assert.That(context, Does.Contain("T1"));
            Assert.That(context, Does.Contain("Task A completed"));
            Assert.That(context, Does.Contain("contract-a"));
        });
    }

    [Test]
    public void CompactPlanContext_HandlesNoCompletedTasks()
    {
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary",
            [new PlanTask("T1", "A", "A", [], "high", PlanTaskStatus.Pending)],
            [],
            new PlanProgress(0, 1), new PlanTimestamps(DateTimeOffset.UtcNow));

        var context = PlanValidationPromptBuilder.BuildCompactPlanContext(plan);

        Assert.That(context, Does.Contain("No tasks have been completed yet"));
    }

    // ─── Result Parsing ──────────────────────────────────────────────────────────

    [Test]
    public void Parser_AcceptsValidPassedResult()
    {
        const string response = """
            Some preamble text.

            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "P1",
              "passed": true,
              "summary": "All assertions verified.",
              "assertionEvidence": [
                { "assertion": "A produces the declared output.", "passed": true, "evidence": "File exports.ts contains the expected interface." }
              ],
              "validatedCommit": "abc1234"
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out var result, out var error), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(result!.ValidationId, Is.EqualTo("V1"));
            Assert.That(result.PlanId, Is.EqualTo("P1"));
            Assert.That(result.Passed, Is.True);
            Assert.That(result.Summary, Is.EqualTo("All assertions verified."));
            Assert.That(result.AssertionEvidence, Has.Count.EqualTo(1));
            Assert.That(result.AssertionEvidence[0].Assertion, Is.EqualTo("A produces the declared output."));
            Assert.That(result.AssertionEvidence[0].Passed, Is.True);
            Assert.That(result.ValidatedCommit, Is.EqualTo("abc1234"));
        });
    }

    [Test]
    public void Parser_AcceptsFailedResult()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "P1",
              "passed": false,
              "summary": "Assertion failed.",
              "assertionEvidence": [
                { "assertion": "A produces output.", "passed": false, "evidence": "No export found." }
              ]
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out var result, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Passed, Is.False);
            Assert.That(result.ValidatedCommit, Is.Null);
            Assert.That(result.AssertionEvidence[0].Passed, Is.False);
        });
    }

    [Test]
    public void Parser_RejectsMissingAssertionEvidence()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "P1",
              "passed": true,
              "summary": "All good.",
              "assertionEvidence": []
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("assertion evidence"));
    }

    [Test]
    public void Parser_RejectsMissingMarker()
    {
        const string response = "Just some text without a validation result.";

        Assert.That(PlanValidationResultParser.TryParse(response, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("PLAN_VALIDATION_RESULT_JSON"));
    }

    [Test]
    public void Parser_RejectsMissingSummary()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "P1",
              "passed": true,
              "summary": "",
              "assertionEvidence": [
                { "assertion": "A", "passed": true, "evidence": "B" }
              ]
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("summary"));
    }

    [Test]
    public void Parser_AcceptsMultipleAssertions()
    {
        const string response = """
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "V1",
              "planId": "P1",
              "passed": true,
              "summary": "All verified.",
              "assertionEvidence": [
                { "assertion": "First assertion.", "passed": true, "evidence": "Evidence 1." },
                { "assertion": "Second assertion.", "passed": true, "evidence": "Evidence 2." },
                { "assertion": "Third assertion.", "passed": false, "evidence": "Failed evidence." }
              ],
              "validatedCommit": "def5678"
            }
            """;

        Assert.That(PlanValidationResultParser.TryParse(response, out var result, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result!.AssertionEvidence, Has.Count.EqualTo(3));
            Assert.That(result.AssertionEvidence[2].Passed, Is.False);
        });
    }

    // ─── Restart & Durable State ─────────────────────────────────────────────────

    [Test]
    public void ActiveLoopExecutionState_PersistsValidationFields()
    {
        var state = new ActiveLoopExecutionState(
            "/loop", "filter",
            DecomposeGroupId: "P1",
            DecomposeRevision: "rev1",
            ActiveValidationId: "V1",
            ValidationRepairCount: 1,
            ValidationRepairReason: " missing envelope ");

        var normalized = ActiveLoopExecutionState.Normalize(state);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Is.Not.Null);
            Assert.That(normalized!.ActiveValidationId, Is.EqualTo("V1"));
            Assert.That(normalized.ValidationRepairCount, Is.EqualTo(1));
            Assert.That(normalized.ValidationRepairReason, Is.EqualTo("missing envelope"));
        });
    }

    [Test]
    public void ActiveLoopExecutionState_NormalizesEmptyValidationId()
    {
        var state = new ActiveLoopExecutionState(
            "/loop", "filter",
            DecomposeGroupId: "P1",
            DecomposeRevision: "rev1",
            ActiveValidationId: "  ",
            ValidationRepairCount: 2);

        var normalized = ActiveLoopExecutionState.Normalize(state);

        Assert.Multiple(() =>
        {
            Assert.That(normalized!.ActiveValidationId, Is.Null);
            Assert.That(normalized.ValidationRepairCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void ActiveLoopExecutionState_RoundTripsValidationFields()
    {
        var state = new ActiveLoopExecutionState(
            "/loop", "filter",
            DecomposeGroupId: "P1",
            DecomposeRevision: "rev1",
            ActiveValidationId: "V1",
            ValidationRepairCount: 1,
            ValidationRepairReason: "missing envelope");

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<ActiveLoopExecutionState>(json, ReadOptions);
        var normalized = ActiveLoopExecutionState.Normalize(deserialized);

        Assert.Multiple(() =>
        {
            Assert.That(normalized!.ActiveValidationId, Is.EqualTo("V1"));
            Assert.That(normalized.ValidationRepairCount, Is.EqualTo(1));
            Assert.That(normalized.ValidationRepairReason, Is.EqualTo("missing envelope"));
            Assert.That(normalized.DecomposeGroupId, Is.EqualTo("P1"));
        });
    }

    [Test]
    public void ActiveLoopExecutionState_LegacyWithoutValidationFields_DeserializesWithDefaults()
    {
        // Simulate a persisted state from before validation fields were added
        const string json = """
            {
              "LoopPath": "/loop",
              "FilterText": "filter",
              "DecomposeGroupId": "P1",
              "DecomposeRevision": "rev1"
            }
            """;

        var deserialized = JsonSerializer.Deserialize<ActiveLoopExecutionState>(json, ReadOptions);
        var normalized = ActiveLoopExecutionState.Normalize(deserialized);

        Assert.Multiple(() =>
        {
            Assert.That(normalized!.ActiveValidationId, Is.Null);
            Assert.That(normalized.ValidationRepairCount, Is.EqualTo(0));
        });
    }

    // ─── Stale Attempt ───────────────────────────────────────────────────────────

    [Test]
    public void StaleValidation_CanBeReScheduled_AfterRereadiness()
    {
        // Start with a passed validation using task IDs that match the plan
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify A", ["T1"], ["T2"],
            ["A is correct."], [], "evidence", [], true,
            PlanValidationStatus.Passed,
            Summary: "All good.",
            Evidence: ["evidence1"],
            ValidatedCommit: "abc1234");
        var plan = MakePlan(validation);

        // Mark stale (e.g., prerequisite reworked)
        plan = PlanStoreUpdater.ApplyValidationStale(plan, "V1", "Prerequisite A was reworked.");
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Stale));

        // Stale can be re-readied (T1 is Complete, so readiness triggers)
        plan = PlanStoreUpdater.ApplyReadyValidations(plan);
        Assert.That(plan.Validations![0].Status, Is.EqualTo(PlanValidationStatus.Ready));

        // Re-ready validation can be scheduled
        var next = PlanValidationScheduler.SelectNextSchedulable(plan);
        Assert.That(next, Is.Not.Null);
        Assert.That(next!.ValidationId, Is.EqualTo("V1"));
    }

    [Test]
    public void StaleValidation_BlocksDownstream_UntilRepassed()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify A", ["T1"], ["T2"],
            ["A is correct."], [], "evidence", [], true,
            PlanValidationStatus.Stale);
        var plan = MakePlan(validation);

        var blocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);

        Assert.That(blocked, Is.Not.Empty);
        Assert.That(blocked, Does.Contain("T2"));
    }

    [Test]
    public void ValidationScheduler_NoReadyValidation_ReturnsNull()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify A", ["A"], ["B"],
            ["A is correct."], [], "evidence", [], true,
            PlanValidationStatus.Pending);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Pending),
                new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(0, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var next = PlanValidationScheduler.SelectNextSchedulable(plan);

        Assert.That(next, Is.Null);
    }

    [Test]
    public void ValidationScheduler_DetectsInProgressValidation()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify A", ["A"], ["B"],
            ["A is correct."], [], "evidence", [], true,
            PlanValidationStatus.Validating,
            StartedAt: DateTimeOffset.UtcNow);
        var plan = MakePlan(validation);

        Assert.Multiple(() =>
        {
            Assert.That(PlanValidationScheduler.IsValidationInProgress(plan), Is.True);
            var inProgress = PlanValidationScheduler.GetInProgressValidation(plan);
            Assert.That(inProgress, Is.Not.Null);
            Assert.That(inProgress!.ValidationId, Is.EqualTo("V1"));
        });
    }

    [Test]
    public void ValidationScheduler_NoValidation_InProgress_ReturnsFalse()
    {
        var plan = MakePlanWithReadyValidation();

        Assert.Multiple(() =>
        {
            Assert.That(PlanValidationScheduler.IsValidationInProgress(plan), Is.False);
            Assert.That(PlanValidationScheduler.GetInProgressValidation(plan), Is.Null);
        });
    }

    // ─── Validation Does Not Produce Commits ─────────────────────────────────────

    [Test]
    public void ValidationPrompt_ExplicitlyForbidsCommits()
    {
        var plan = MakePlanWithReadyValidation();
        var validation = plan.Validations![0];

        var prompt = PlanValidationPromptBuilder.Build(plan, validation, "abc1234");

        Assert.That(prompt, Does.Contain("must NOT create"));
    }

    // ─── Repair Prompt ───────────────────────────────────────────────────────────

    [Test]
    public void RepairPrompt_IncludesExpectedFields()
    {
        var prompt = PlanValidationRepairPrompt.Build("P1", "V1", "no result returned");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("P1"));
            Assert.That(prompt, Does.Contain("V1"));
            Assert.That(prompt, Does.Contain("no result returned"));
            Assert.That(prompt, Does.Contain(PlanValidationResultParser.Marker));
            Assert.That(prompt, Does.Contain("Do NOT create any commits"));
        });
    }

    // ─── Task Blocking Integration ───────────────────────────────────────────────

    [Test]
    public void ValidationBlocking_UnionsWithGateBlocking()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify", "Verify A", ["A"], ["C"],
            ["A is correct."], [], "evidence", [], true,
            PlanValidationStatus.Ready);
        var gate = new PlanApprovalGate(
            "G1", "Approve B", ["B"], ["D"],
            PlanGateStatus.Pending);
        var plan = new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary",
            [
                new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
                new PlanTask("B", "B", "B", [], "high", PlanTaskStatus.Complete),
                new PlanTask("C", "C", "C", ["A"], "high", PlanTaskStatus.Pending),
                new PlanTask("D", "D", "D", ["B"], "high", PlanTaskStatus.Pending),
            ],
            [gate],
            new PlanProgress(2, 4), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);

        var validationBlocked = PlanValidationScheduler.ComputeBlockedTaskIds(plan);
        var gateBlocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan);

        Assert.Multiple(() =>
        {
            Assert.That(validationBlocked, Does.Contain("C"));
            Assert.That(gateBlocked, Does.Contain("D"));
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static Plan MakePlanWithReadyValidation()
    {
        var validation = new PlanValidationNode(
            "V1", "Verify Contract", "Verify A produces what B consumes.",
            ["T1"], ["T2"],
            ["A produces the declared output."],
            ["contract-a"],
            "evidence", [], true,
            PlanValidationStatus.Ready);
        return MakePlan(validation);
    }

    private static Plan MakePlan(PlanValidationNode validation)
    {
        return new Plan(
            "P1", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Integration Plan", "feature/plan", "Verify cross-task integration contracts.",
            [
                new PlanTask("T1", "Task A", "Create A", [], "high", PlanTaskStatus.Complete,
                    CompletionSummary: "Task A completed",
                    Commit: "aaa1111",
                    Outputs: [new PlanTaskOutput("contract-a", "Public contract produced by A")]),
                new PlanTask("T2", "Task B", "Use A", ["T1"], "high", PlanTaskStatus.Pending,
                    Inputs: ["contract-a"]),
            ],
            [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: [validation]);
    }
}
