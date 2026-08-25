using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanGateResponseParserTests
{
    [Test]
    public void LegacyResponse_PreviouslyUnconsumedAmendmentPayload_ParsesSuccessfully()
    {
        const string text = """
            PLAN_GATE_RESPONSE_JSON:
            {"planId":"SQUADUPDATE-20260821","gateId":"SQUADUPDATE-20260821-005-HUMAN-PROOF","revision":"8cdbf104a480c150","requestVersion":3,"disposition":"add-amendment","taskIds":["SQUADUPDATE-20260821-005"],"title":"Add simulation fixtures for T005 human-review checkpoints","instructions":"Add a Developer → Simulation menu item labelled 'Simulate Workspace Upgrade Button Flow' (or similarly clear label) under the existing Simulation submenu in MainWindow. When clicked, this simulation must open WorkspaceUpgradeSummaryPanel through the real MainWindow orchestration path, wired to fake upgrade dependencies so it never invokes the real Squad CLI, modifies workspace files, or creates commits."}
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out var response), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(response!.Disposition, Is.EqualTo(PlanGateResponseDisposition.AddAmendment));
            Assert.That(response.RequestVersion, Is.EqualTo(3));
            Assert.That(response.TaskIds, Is.EqualTo(new[] { "SQUADUPDATE-20260821-005" }));
        });
    }

    [Test]
    public void UnifiedResponse_ExactFailedAmendmentCorrelation_IsAcceptedWithoutExitDiagnostic()
    {
        const string text = """
            PLAN_REVIEW_RESPONSE_JSON:
            {"planId":"SQUADUPDATE-20260821","gateId":"SQUADUPDATE-20260821-005-HUMAN-PROOF","revision":"8cdbf104a480c150","requestVersion":3,"action":"add-amendment","taskIds":["SQUADUPDATE-20260821-005"],"title":"Add simulation fixtures for T005 human-review checkpoints","instructions":"Add a Developer → Simulation menu item labelled 'Simulate Workspace Upgrade Button Flow' under the existing Simulation submenu. Exercise the production action orchestration through fake upgrade dependencies, persist Later through the real pending-state store, show representative Git changes after simulated Apply, and display the unchanged HEAD before and after without invoking the real CLI, modifying workspace files, or creating commits."}
            """;
        var task = new PlanTask(
            "SQUADUPDATE-20260821-005", "Wire actions", "Work", [], "high", PlanTaskStatus.Complete);
        var gate = new PlanApprovalGate(
            "SQUADUPDATE-20260821-005-HUMAN-PROOF", "Review", [task.TaskId], [],
            PlanGateStatus.AwaitingApproval);
        var plan = new Plan(
            "SQUADUPDATE-20260821", "8cdbf104a480c150", PlanSource.Manual,
            PlanLifecycleStatus.AwaitingApproval, "Plan", "feature/plan", "Summary",
            [task], [gate], new PlanProgress(1, 1), new PlanTimestamps(DateTimeOffset.UtcNow));
        var token = new ApprovalClickToken(plan.PlanId, plan.Revision, 3, [gate.GateId]);

        Assert.That(PlanReviewResponseParser.TryParse(text, out var response, out var parseError),
            Is.True, parseError);
        var correlation = PlanReviewResponseCorrelation.Validate(response!, plan, gate, token);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Action, Is.EqualTo(PlanReviewResponseAction.AddAmendment));
            Assert.That(correlation.Success, Is.True, correlation.Diagnostic);
            Assert.That(correlation.Diagnostic, Is.EqualTo("correlation accepted"));
        });
    }

    [Test]
    public void UnifiedResponse_InvalidPayload_ReturnsActionableSchemaErrors()
    {
        const string text = """
            PLAN_REVIEW_RESPONSE_JSON:
            {"planId":"PLAN-1","gateId":"GATE-1","revision":"rev","requestVersion":3,"action":"add-amendment"}
            """;

        Assert.That(PlanReviewResponseParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("instructions are required for an amendment"));
    }

    [Test]
    public void UnifiedResponse_CorrelationReportsExactFailedExit()
    {
        var response = new PlanReviewResponse(
            "PLAN-1", "GATE-1", "rev", 4, PlanReviewResponseAction.AddAmendment,
            ["TASK-1"], Title: "Amend", Instructions: "Work");
        var task = new PlanTask("TASK-1", "Task", "Work", [], "high", PlanTaskStatus.Complete);
        var gate = new PlanApprovalGate("GATE-1", "Review", [task.TaskId], [], PlanGateStatus.AwaitingApproval);
        var plan = new Plan(
            "PLAN-1", "rev", PlanSource.Manual, PlanLifecycleStatus.AwaitingApproval,
            "Plan", "feature/plan", "Summary", [task], [gate],
            new PlanProgress(1, 1), new PlanTimestamps(DateTimeOffset.UtcNow));

        var result = PlanReviewResponseCorrelation.Validate(
            response, plan, gate, new ApprovalClickToken("PLAN-1", "rev", 3, ["GATE-1"]));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostic, Is.EqualTo("request version mismatch expected=3 actual=4"));
    }

    [Test]
    public void TryParse_RequestRework_RequiresTasksAndInstructions()
    {
        var text = """
            PLAN_GATE_RESPONSE_JSON:
            {
              "planId": "PLAN-1",
              "gateId": "GATE-1",
              "revision": "rev",
              "requestVersion": 3,
              "disposition": "request-rework",
              "taskIds": ["TASK-1"],
              "instructions": "Use the shared theme resources."
            }
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out var response), Is.True);
        Assert.That(response!.TaskIds, Is.EqualTo(new[] { "TASK-1" }));
    }

    [Test]
    public void TryParse_RequestReworkTasksCompatibilityShape_NormalizesToCanonicalResponse()
    {
        const string text = """
            PLAN_GATE_RESPONSE_JSON:
            {
              "planId": "PLAN-1",
              "gateId": "GATE-1",
              "revision": "rev",
              "requestVersion": 3,
              "disposition": "request-rework",
              "reworkTasks": [
                { "taskId": "TASK-1", "instructions": "Wire the presenter into the live viewer." }
              ]
            }
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out var response), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(response!.TaskIds, Is.EqualTo(new[] { "TASK-1" }));
            Assert.That(response.Instructions, Is.EqualTo("Wire the presenter into the live viewer."));
        });
    }

    [Test]
    public void BuildClassificationInstruction_IncludesExactCanonicalReworkSchema()
    {
        var plan = new Plan(
            "PLAN-1", "rev", PlanSource.Manual, PlanLifecycleStatus.AwaitingApproval,
            "Plan", "feature/plan", "Summary",
            [new PlanTask("TASK-1", "Task", "Work", [], "high", PlanTaskStatus.Complete)],
            [new PlanApprovalGate("GATE-1", "Review", ["TASK-1"], [], PlanGateStatus.AwaitingApproval)],
            new PlanProgress(1, 1),
            new PlanTimestamps(DateTimeOffset.UtcNow));
        var token = new ApprovalClickToken("PLAN-1", "rev", 3, ["GATE-1"]);

        var instruction = PlanGateResponseParser.BuildClassificationInstruction(
            plan, plan.ApprovalGates[0], token);

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("\"taskIds\":[\"TASK-1\"]"));
            Assert.That(instruction, Does.Contain("\"instructions\":"));
            Assert.That(instruction, Does.Contain("Never emit `reworkTasks`"));
            Assert.That(instruction, Does.Contain("`add-amendment`"));
            Assert.That(instruction, Does.Contain("completed reviewed tasks should remain complete"));
            Assert.That(instruction, Does.Contain("Do not choose `request-rework` merely because"));
        });
    }

    [Test]
    public void TryParse_AddAmendment_PreservesRelatedTasksTitleAndInstructions()
    {
        const string text = """
            PLAN_GATE_RESPONSE_JSON:
            {
              "planId": "PLAN-1",
              "gateId": "GATE-1",
              "revision": "rev",
              "requestVersion": 3,
              "disposition": "add-amendment",
              "taskIds": ["TASK-1", "TASK-2"],
              "title": "Add safe tour cleanup",
              "instructions": "Clean up simulated notes on every tour exit path."
            }
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out var response), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(response!.Disposition, Is.EqualTo(PlanGateResponseDisposition.AddAmendment));
            Assert.That(response.TaskIds, Is.EqualTo(new[] { "TASK-1", "TASK-2" }));
            Assert.That(response.Title, Is.EqualTo("Add safe tour cleanup"));
            Assert.That(response.Instructions, Is.EqualTo("Clean up simulated notes on every tour exit path."));
        });
    }

    [Test]
    public void TryParse_AddAmendment_AllowsWholeBoundaryAndSuppliesFallbackTitle()
    {
        const string text = """
            PLAN_GATE_RESPONSE_JSON:
            {"planId":"PLAN-1","gateId":"GATE-1","revision":"rev","requestVersion":2,"disposition":"add-amendment","instructions":"Add the joined-result integration test."}
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out var response), Is.True);
        Assert.That(response!.TaskIds, Is.Null);
        Assert.That(response.Title, Is.EqualTo("Apply requested changes before approval"));
    }

    [TestCase("unrelated")]
    [TestCase("clarification")]
    public void TryParse_NonRework_DoesNotRequireTasks(string disposition)
    {
        var text = $$"""
            PLAN_GATE_RESPONSE_JSON:
            {"planId":"PLAN-1","gateId":"GATE-1","revision":"rev","requestVersion":2,"disposition":"{{disposition}}"}
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out _), Is.True);
    }

    [Test]
    public void TryParse_RequestReworkWithoutInstructions_IsRejected()
    {
        const string text = """
            PLAN_GATE_RESPONSE_JSON:
            {"planId":"PLAN-1","gateId":"GATE-1","revision":"rev","requestVersion":2,"disposition":"request-rework","taskIds":["TASK-1"]}
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out _), Is.False);
    }

    [Test]
    public void TryParse_UnknownDisposition_IsRejected()
    {
        const string text = """
            PLAN_GATE_RESPONSE_JSON:
            {"planId":"PLAN-1","gateId":"GATE-1","revision":"rev","requestVersion":2,"disposition":"reject"}
            """;

        Assert.That(PlanGateResponseParser.TryParse(text, out _), Is.False);
    }
}
