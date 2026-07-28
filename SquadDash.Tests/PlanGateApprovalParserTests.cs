using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanGateApprovalParserTests
{
    private const string ValidPayload =
        "Some response text.\n\n" +
        "PLAN_GATE_APPROVAL_JSON:\n" +
        "{\n" +
        "  \"planId\": \"PLANS-20260101\",\n" +
        "  \"gateId\": \"PLANS-20260101-GATE-001\",\n" +
        "  \"revision\": \"abc123\"\n" +
        "}";

    [Test]
    public void TryParse_ValidPayload_ReturnsTrue()
    {
        var result = PlanGateApprovalParser.TryParse(ValidPayload, out var approval);

        Assert.That(result,            Is.True);
        Assert.That(approval,          Is.Not.Null);
        Assert.That(approval!.PlanId,  Is.EqualTo("PLANS-20260101"));
        Assert.That(approval.GateId,   Is.EqualTo("PLANS-20260101-GATE-001"));
        Assert.That(approval.Revision, Is.EqualTo("abc123"));
    }

    [Test]
    public void TryParse_MissingMarker_ReturnsFalse()
    {
        var text   = "{ \"planId\": \"P-001\", \"gateId\": \"G-001\", \"revision\": \"rev\" }";
        var result = PlanGateApprovalParser.TryParse(text, out var approval);

        Assert.That(result,   Is.False);
        Assert.That(approval, Is.Null);
    }

    [Test]
    public void TryParse_EmptyPlanId_ReturnsFalse()
    {
        var text =
            "PLAN_GATE_APPROVAL_JSON:\n" +
            "{ \"planId\": \"\", \"gateId\": \"PLANS-20260101-GATE-001\", \"revision\": \"abc123\" }";

        var result = PlanGateApprovalParser.TryParse(text, out var approval);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_EmptyGateId_ReturnsFalse()
    {
        var text =
            "PLAN_GATE_APPROVAL_JSON:\n" +
            "{ \"planId\": \"PLANS-20260101\", \"gateId\": \"\", \"revision\": \"abc123\" }";

        var result = PlanGateApprovalParser.TryParse(text, out var approval);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_EmptyRevision_ReturnsFalse()
    {
        var text =
            "PLAN_GATE_APPROVAL_JSON:\n" +
            "{ \"planId\": \"PLANS-20260101\", \"gateId\": \"PLANS-20260101-GATE-001\", \"revision\": \"\" }";

        var result = PlanGateApprovalParser.TryParse(text, out var approval);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_WithNote_ParsesNote()
    {
        var text =
            "PLAN_GATE_APPROVAL_JSON:\n" +
            "{\n" +
            "  \"planId\": \"PLANS-20260101\",\n" +
            "  \"gateId\": \"PLANS-20260101-GATE-001\",\n" +
            "  \"revision\": \"abc123\",\n" +
            "  \"note\": \"LGTM, proceed\"\n" +
            "}";

        var result = PlanGateApprovalParser.TryParse(text, out var approval);

        Assert.That(result,         Is.True);
        Assert.That(approval!.Note, Is.EqualTo("LGTM, proceed"));
    }

    [Test]
    public void TryParse_NullInput_ReturnsFalse()
    {
        var result = PlanGateApprovalParser.TryParse(null, out var approval);

        Assert.That(result,   Is.False);
        Assert.That(approval, Is.Null);
    }

    [Test]
    public void TryParse_UsesLastOccurrence()
    {
        var text =
            "PLAN_GATE_APPROVAL_JSON:\n" +
            "{ \"planId\": \"FIRST-00000000\", \"gateId\": \"FIRST-00000000-GATE-001\", \"revision\": \"first\" }\n\n" +
            "PLAN_GATE_APPROVAL_JSON:\n" +
            "{ \"planId\": \"LAST-99991231\", \"gateId\": \"LAST-99991231-GATE-002\", \"revision\": \"second\" }";

        var result = PlanGateApprovalParser.TryParse(text, out var approval);

        Assert.That(result,           Is.True);
        Assert.That(approval!.PlanId, Is.EqualTo("LAST-99991231"),
            "Parser must use the last occurrence of the marker.");
    }
}
