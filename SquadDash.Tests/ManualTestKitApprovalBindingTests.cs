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
    public void TryBindExistingMessage_RecognizesApprovalMarkerWithoutManualTestWording()
    {
        var message = BuildMessage("{{approval-check:proof-one}}\n{{approval-check:proof-two}}") with
        {
            Subject = "Refreshed native target kit",
        };

        var bound = ManualTestKitApprovalBinding.TryBindExistingMessage(message, [BuildPlan()]);

        Assert.That(ManualTestKitApprovalBinding.Read(bound)?.GateIds, Is.EqualTo(new[] { "GATE-1" }));
    }

    [Test]
    public void ValidateSourceBody_ReportsAllMarkerErrorsForAutomaticRepair()
    {
        var error = ManualTestKitApprovalBinding.ValidateSourceBody(
            BuildPlan(),
            "TASK-1",
            "{{approval-check:proof-one}}\n{{approval-check:proof-one}}\n{{approval-check:stale-proof}}");

        Assert.That(error, Does.Contain("missing `{{approval-check:proof-two}}`")
            .And.Contain("unknown approval marker `stale-proof`")
            .And.Contain("approval marker `proof-one` appears 2 times")
            .And.Contain("Re-emit the corrected kit")
            .And.Contain("preserve the completed implementation work"));
    }

    [Test]
    public void ValidateSourceBody_RejectsProcedureThatDelegatesRequiredStepsToRunbook()
    {
        var body = """
            Follow the delivered Runbook.md Setup instructions.
            {{approval-check:proof-one}}
            {{approval-check:proof-two}}
            """;

        var error = ManualTestKitApprovalBinding.ValidateSourceBody(
            BuildPlan(),
            "TASK-1",
            body);

        Assert.That(error, Does.Contain("delegates work to another procedure"));
    }

    [Test]
    public void LooksLikeSourceForPlan_MatchesPartialSupersededKit()
    {
        var message = BuildMessage("{{approval-check:proof-one}}") with
        {
            Subject = "Refreshed native target kit",
        };

        Assert.That(ManualTestKitApprovalBinding.LooksLikeSourceForPlan(message, BuildPlan()), Is.True);
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
    public void Compose_UsesProofTitleForHeadingAndQuestionOnlyForCheckbox()
    {
        var body = """
            ## Does applying the response update the inactive Razor component?
            1. Open the prepared solution.
            2. Observe the inactive component.
            {{approval-check:native-inactive-target}}
            """;
        var item = BuildItem(
            "native-inactive-target",
            "Does applying the response update the inactive Razor component?",
            "Inactive Razor target routing");

        var result = ManualTestKitChecklistComposer.Compose(body, [item]);

        Assert.That(result, Does.StartWith("## Inactive Razor target routing\n")
            .And.Contain("- ☐ Does applying the response update the inactive Razor component?")
            .And.Not.Contain("## Does applying"));
    }

    [Test]
    public void Compose_PreservesSharedWorkflowHeadingAndSeparateAtomicCheckboxes()
    {
        var body = """
            ## Generate and inspect the result
            1. Open the prepared solution and invoke the feature once.
            **Observe:** The result appears in the editor.
            {{approval-check:proof-one}}
            **Observe:** The status indicator reports completion.
            {{approval-check:proof-two}}
            2. Clean up the shared fixture.
            """;

        var result = ManualTestKitChecklistComposer.Compose(
            body,
            [
                BuildItem("proof-one", "Does the result appear?", "Editor result"),
                BuildItem("proof-two", "Does the status report completion?", "Completion status"),
            ]);

        Assert.That(result, Does.StartWith("## Generate and inspect the result\n")
            .And.Contain("- ☐ Does the result appear?")
            .And.Contain("- ☐ Does the status report completion?")
            .And.Not.Contain("## Editor result")
            .And.Not.Contain("## Completion status"));
    }

    [Test]
    public void Compose_LegacyProofWithoutTitleHumanizesStableId()
    {
        var result = ManualTestKitChecklistComposer.Compose(
            "## Does it route correctly?\n{{approval-check:native-inactive-target}}",
            [BuildItem("native-inactive-target", "Does it route correctly?")]);

        Assert.That(result, Does.StartWith("## Native inactive target\n"));
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

    [Test]
    public void Compose_ReplacesUnmatchedProtocolMarkerWithBlockingSetupError()
    {
        var result = ManualTestKitChecklistComposer.Compose(
            "Open the sample.\n{{approval-check:native-inactive-target}}",
            []);

        Assert.That(result, Does.Contain("Open the sample.")
            .And.Contain(ManualTestKitChecklistComposer.SetupErrorMarker)
            .And.Contain("native-inactive-target")
            .And.Not.Contain("{{approval-check:"));
    }

    [Test]
    public void Compose_DoesNotGuessMissingPlacementWhenKitUsesExplicitMarkers()
    {
        var result = ManualTestKitChecklistComposer.Compose(
            "Unchanged proof-two.\n{{approval-check:proof-one}}",
            [BuildItem("proof-one", "Does one appear?"), BuildItem("proof-two", "Does two appear?")]);

        Assert.That(result, Does.Contain("- ☐ Does one appear?")
            .And.Contain(ManualTestKitChecklistComposer.SetupErrorMarker)
            .And.Contain("Does two appear?")
            .And.Not.Contain("- ☐ Does two appear?"));
    }

    private static InboxMessage BuildMessage(string body) => new()
    {
        Id = "message-1",
        Subject = "Manual Test Kit",
        Body = body,
        Attachments = [new InboxAttachment { Type = "file", Label = "Runbook", Path = "runbook.md" }],
    };

    private static PlanHumanReviewChecklistItem BuildItem(
        string requirementId,
        string question,
        string? title = null) =>
        new("GATE-1", requirementId, requirementId, question,
            PlanHumanReviewChecklist.UncommittedCandidate, false, false, null, title);

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
