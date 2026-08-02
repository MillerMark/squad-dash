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
