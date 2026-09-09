namespace SquadDash.Tests;

[TestFixture]
internal sealed class ManualTestKitApprovalBindingTests
{
    [Test]
    public void BindForCompletedTask_AddsHostOwnedPlanAndGateIdentity()
    {
        var plan = BuildPlan();
        var message = BuildMessage("Observe proof-one and proof-two.");

        var bound = ManualTestKitApprovalBinding.BindForCompletedTask(message, plan, "TASK-1");
        var payload = ManualTestKitApprovalBinding.Read(bound);

        Assert.Multiple(() =>
        {
            Assert.That(bound.ResolvePlanGroupId(), Is.EqualTo("PLAN-1"));
            Assert.That(payload?.PlanRevision, Is.EqualTo("rev-1"));
            Assert.That(payload?.GateIds, Is.EqualTo(new[] { "GATE-1" }));
            Assert.That(bound.Attachments.Count(ManualTestKitApprovalBinding.IsBindingAttachment), Is.EqualTo(1));
        });
    }

    [Test]
    public void BindForCompletedTask_DoesNotBindMessageMissingAProofId()
    {
        var message = BuildMessage("Observe proof-one only.");

        var result = ManualTestKitApprovalBinding.BindForCompletedTask(message, BuildPlan(), "TASK-1");

        Assert.That(result, Is.SameAs(message));
    }

    [Test]
    public void RemoveUntrustedBinding_StripsAgentSuppliedHostAttachment()
    {
        var message = BuildMessage("Observe proof-one and proof-two.") with
        {
            Attachments =
            [
                new InboxAttachment { Type = ManualTestKitApprovalBinding.AttachmentType, Content = "{}" },
                new InboxAttachment { Type = "file", Path = "runbook.md" },
            ],
        };

        var sanitized = ManualTestKitApprovalBinding.RemoveUntrustedBinding(message);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized.Attachments, Has.Count.EqualTo(1));
            Assert.That(sanitized.Attachments[0].Type, Is.EqualTo("file"));
        });
    }

    [Test]
    public void TryBindExistingMessage_UpgradesUniqueAwaitingManualTestKit()
    {
        var message = BuildMessage("Observe proof-one and proof-two.");

        var bound = ManualTestKitApprovalBinding.TryBindExistingMessage(message, [BuildPlan()]);

        Assert.That(ManualTestKitApprovalBinding.Read(bound)?.GateIds, Is.EqualTo(new[] { "GATE-1" }));
    }

    [Test]
    public void Compose_ReplacesExplicitSlotImmediatelyWithPersistedQuestion()
    {
        var body = """
            ## Observe
            The result is visible.
            {{approval-check:proof-one}}
            ## Cleanup
            Undo it.
            """;

        var result = ManualTestKitChecklistComposer.Compose(body, [BuildItem("proof-one", "Does it appear?")]);

        Assert.That(result, Does.Contain("The result is visible.\n- ☐ Does it appear?\n## Cleanup"));
    }

    [Test]
    public void Compose_BackfillsExistingKitAfterItsPassFailObservation()
    {
        var body = """
            **1. Feature — proof `proof-one`**
            - Perform the action.
            - ✅ Pass: the result appears.
            - ❌ Fail: the result is missing.

            **2. Other — proof `proof-two`**
            - Perform the other action.
            """;

        var result = ManualTestKitChecklistComposer.Compose(body, [BuildItem("proof-one", "Does it appear?")]);

        Assert.That(result, Does.Contain(
            "- ❌ Fail: the result is missing.\n\n- ☐ Does it appear?\n\n\n**2. Other"));
    }

    private static InboxMessage BuildMessage(string body) => new()
    {
        Id = "message-1",
        Subject = "Manual Test Kit",
        Body = body,
        Attachments = [new InboxAttachment { Type = "file", Label = "Runbook", Path = "runbook.md" }],
    };

    private static PlanHumanReviewChecklistItem BuildItem(string requirementId, string question) =>
        new("GATE-1", requirementId, requirementId, question,
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null);

    private static Plan BuildPlan()
    {
        var requirements = new[]
        {
            new PlanTaskProofRequirement("proof-one", "live-ui-observation", "Observe one.", "Does one appear?"),
            new PlanTaskProofRequirement("proof-two", "live-ui-observation", "Observe two.", "Does two appear?"),
        };
        return new Plan(
            "PLAN-1",
            "rev-1",
            PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval,
            "Plan",
            "main",
            "Summary",
            [new PlanTask("TASK-1", "Prepare kit", "Prepare it", [], "high", PlanTaskStatus.Complete)],
            [new PlanApprovalGate(
                "GATE-1",
                "Review",
                ["TASK-1"],
                [],
                PlanGateStatus.AwaitingApproval,
                ProofRequirements: requirements)],
            new PlanProgress(1, 1),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }
}
