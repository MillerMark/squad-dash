namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanReviewClarificationTests
{
    [Test]
    public void Parser_AcceptsNullOptionalCollections()
    {
        const string text = """
            PLAN_HUMAN_REVIEW_CLARIFICATION_JSON:
            {
              "planId": "PLAN-1",
              "revision": "rev-1",
              "gateId": "GATE-1",
              "itemId": "proof-3",
              "action": "remove",
              "userFacingAnswer": "This checks hidden implementation structure.",
              "reason": "A human cannot observe the gate count.",
              "replacementItems": null,
              "targetItemIds": null,
              "evidence": null
            }
            """;

        var parsed = PlanHumanReviewClarificationResponseParser.TryParse(
            text, out var response, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(response!.Action, Is.EqualTo(PlanHumanReviewClarificationAction.Remove));
            Assert.That(response.TargetItemIds, Is.Empty);
            Assert.That(response.Evidence, Is.Empty);
        });
    }

    [Test]
    public void Remove_PreservesOtherSelectionsAcrossStoreReload()
    {
        using var workspace = new TestWorkspace();
        var squadFolder = workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        var store = new PlanStore(squadFolder);
        var plan = MakePlan();
        var gate = plan.ApprovalGates.Single();
        var item = PlanHumanReviewChecklist.Build(plan, [gate])
            .Single(candidate => candidate.ItemId == "proof#3");
        var response = MakeResponse(PlanHumanReviewClarificationAction.Remove, item.ItemId);

        var updated = PlanStoreUpdater.ApplyHumanReviewClarification(
            plan, response, item, "reviewer", DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        store.Save(updated);
        var reloaded = store.Load(plan.PlanId)!;
        var remaining = PlanHumanReviewChecklist.Build(reloaded, [reloaded.ApprovalGates.Single()]);

        Assert.Multiple(() =>
        {
            Assert.That(remaining.Select(candidate => candidate.ItemId),
                Is.EqualTo(new[] { "proof#1", "proof#2" }));
            Assert.That(remaining.All(candidate => candidate.IsChecked), Is.True);
            Assert.That(reloaded.ApprovalGates.Single().HumanReviewDispositions, Has.Count.EqualTo(1));
            Assert.That(reloaded.ApprovalGates.Single().HumanReviewDispositions![0].Reason,
                Is.EqualTo("Not observable by a human."));
        });
    }

    [Test]
    public void Reword_CanInvalidateOnlyTargetSelection()
    {
        var plan = MakePlan();
        var gate = plan.ApprovalGates.Single();
        var item = PlanHumanReviewChecklist.Build(plan, [gate]).First();
        var response = MakeResponse(PlanHumanReviewClarificationAction.Reword, item.ItemId) with
        {
            ReplacementText = "Can the reviewer observe the renamed command?",
            PreserveCheckedSelections = false,
        };

        var updated = PlanStoreUpdater.ApplyHumanReviewClarification(
            plan, response, item, "reviewer");
        var checklist = PlanHumanReviewChecklist.Build(updated, [updated.ApprovalGates.Single()]);

        Assert.Multiple(() =>
        {
            Assert.That(checklist[0].Text, Is.EqualTo("Can the reviewer observe the renamed command?"));
            Assert.That(checklist[0].IsChecked, Is.False);
            Assert.That(checklist[1].IsChecked, Is.True);
        });
    }

    [Test]
    public void Automate_UsesExistingCompatibleValidationWithoutChangingProjection()
    {
        var plan = MakePlan();
        var gate = plan.ApprovalGates.Single();
        var existingValidation = new PlanValidationNode(
            "VAL-EXISTING", "Gate consolidation integrity",
            "Verify one durable gate owns the boundary.",
            gate.AfterTaskIds, gate.BeforeTaskIds,
            ["Exactly one gate represents the approval boundary."],
            OutputIds: null, Mode: "command",
            Commands: ["dotnet test --filter GateConsolidation"],
            RevalidateAtCompletion: false, Status: PlanValidationStatus.Pending);
        plan = plan with { Validations = [existingValidation] };
        var item = PlanHumanReviewChecklist.Build(plan, [gate]).Last();
        var response = MakeResponse(PlanHumanReviewClarificationAction.Automate, item.ItemId) with
        {
            ExistingValidationId = existingValidation.ValidationId,
        };

        var updated = PlanStoreUpdater.ApplyHumanReviewClarification(
            plan, response, item, "reviewer");

        Assert.Multiple(() =>
        {
            Assert.That(PlanHumanReviewChecklist.Build(updated, [updated.ApprovalGates.Single()]),
                Has.Count.EqualTo(2));
            Assert.That(updated.Validations, Has.Count.EqualTo(1));
            Assert.That(updated.Validations, Is.EqualTo(plan.Validations));
            Assert.That(updated.ApprovalGates[0].HumanReviewDispositions![0].AutomatedValidationId,
                Is.EqualTo(existingValidation.ValidationId));
        });
    }

    [Test]
    public void TranscriptSanitizer_HidesClarificationProtocol()
    {
        const string text = "Answer for the reviewer.\n\nPLAN_HUMAN_REVIEW_CLARIFICATION_JSON:\n" +
                            "{\"planId\":\"P\",\"revision\":\"R\"}";

        var sanitized = TranscriptTextUtilities.SanitizeResponseText(text);

        Assert.That(sanitized, Is.EqualTo("Answer for the reviewer."));
    }

    [TestCase(PlanHumanReviewClarificationAction.Remove, "Remove Requirement", HumanReviewClarificationChangeIcon.Remove)]
    [TestCase(PlanHumanReviewClarificationAction.Reword, "Apply Rewording", HumanReviewClarificationChangeIcon.Edit)]
    [TestCase(PlanHumanReviewClarificationAction.Automate, "Use Automated Validation", HumanReviewClarificationChangeIcon.Check)]
    public void ConfirmationPresentation_UsesExplicitActionLabelsAndIcons(
        string action,
        string expectedLabel,
        HumanReviewClarificationChangeIcon expectedIcon)
    {
        var plan = MakePlan();
        var item = PlanHumanReviewChecklist.Build(plan, plan.ApprovalGates).First();
        var response = MakeResponse(action, item.ItemId) with
        {
            ReplacementText = "Can the reviewer observe the updated command?",
            ExistingValidationId = "VAL-EXISTING",
        };

        var presentation = HumanReviewClarificationDialogPresentation.Create(response, item);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.PrimaryActionLabel, Is.EqualTo(expectedLabel));
            Assert.That(presentation.SecondaryActionLabel, Is.EqualTo("Keep Current Requirement"));
            Assert.That(presentation.ChangeIcon, Is.EqualTo(expectedIcon));
            Assert.That(presentation.CurrentRequirement, Is.EqualTo(item.Text));
            Assert.That(presentation.Reason, Is.EqualTo("Not observable by a human."));
        });
    }

    private static PlanHumanReviewClarificationResponse MakeResponse(string action, string itemId) => new(
        "PLAN-1", "rev-1", "GATE-1", itemId, action,
        "This requirement should change.", "Not observable by a human.");

    private static Plan MakePlan()
    {
        var task = new PlanTask(
            "TASK-1", "Implement editing", "Work", [], "high", PlanTaskStatus.Complete,
            Commit: "abcdef123456");
        var gate = new PlanApprovalGate(
            "GATE-1", "Review editing", [task.TaskId], ["TASK-2"],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "proof", "live-ui-observation", "Review the UI",
                    "Does the menu open? Does the selection persist? Is there only one durable gate?")
            ],
            HumanReviewSelections:
            [
                new PlanHumanReviewSelection("proof#1", true, "abcdef123456", DateTimeOffset.UtcNow),
                new PlanHumanReviewSelection("proof#2", true, "abcdef123456", DateTimeOffset.UtcNow),
            ]);
        return new Plan(
            "PLAN-1", "rev-1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, "Plan", "feature/plan", "Summary",
            [task, new PlanTask("TASK-2", "Continue", "Next", [task.TaskId], "mid", PlanTaskStatus.Pending)],
            [gate], new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: []);
    }
}
