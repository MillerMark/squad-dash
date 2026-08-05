using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanGateResponseParserTests
{
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
