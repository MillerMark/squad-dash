using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class StagedPlanRevisionProposalTests
{
    [Test]
    public void Parser_ParsesCompactDeltaAndNormalizesIdentity()
    {
        var pending = MakePending();
        var text = $$"""
            STAGED_PLAN_REVISION_JSON:
            {
              "groupId": "  {{pending.Group.GroupId}}  ",
              "baseRevision": "  {{pending.Revision}}  ",
              "summary": "  Refine task wording  ",
              "operations": [
                { "op": " updateTask ", "targetId": " {{Id("003")}} ", "patch": { "description": "Revised" } }
              ]
            }
            """;

        var parsed = StagedPlanRevisionProposalParser.TryParse(text, out var proposal, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(proposal!.GroupId, Is.EqualTo(pending.Group.GroupId));
            Assert.That(proposal.BaseRevision, Is.EqualTo(pending.Revision));
            Assert.That(proposal.Summary, Is.EqualTo("Refine task wording"));
            Assert.That(proposal.Operations[0].Op, Is.EqualTo("updateTask"));
            Assert.That(proposal.Operations[0].TargetId, Is.EqualTo(Id("003")));
        });
    }

    [TestCase("")]
    [TestCase("STAGED_PLAN_REVISION_JSON: {}")]
    public void Parser_RejectsMissingRequiredContent(string text)
    {
        var parsed = StagedPlanRevisionProposalParser.TryParse(text, out var proposal, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(proposal, Is.Null);
            if (text.Length > 0) Assert.That(error, Is.Not.Empty);
        });
    }

    [Test]
    public void Parser_RejectsMultiplePayloads()
    {
        var text = "STAGED_PLAN_REVISION_JSON: {}\nSTAGED_PLAN_REVISION_JSON: {}";

        Assert.That(StagedPlanRevisionProposalParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("exactly one"));
    }

    [Test]
    public void Parser_RejectsReopenTaskForUnapprovedProposal()
    {
        var pending = MakePending();
        var text = Serialize(new StagedPlanRevisionProposalPayload(
            pending.Group.GroupId, pending.Revision, "Do not reopen", [new("reopenTask", Id("001"))]));

        Assert.That(StagedPlanRevisionProposalParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("no execution history"));
    }

    [Test]
    public void Parser_RejectsNullOperationWithoutThrowing()
    {
        var pending = MakePending();
        var text = $$"""
            STAGED_PLAN_REVISION_JSON:
            { "groupId": "{{pending.Group.GroupId}}", "baseRevision": "{{pending.Revision}}",
              "summary": "Invalid null operation", "operations": [null] }
            """;

        Assert.That(StagedPlanRevisionProposalParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("null entries"));
    }

    [TestCase("description", "{\"description\":\"Revised implementation contract\"}")]
    [TestCase("title", "{\"title\":\"Revised title\"}")]
    [TestCase("priority", "{\"priority\":\"medium\"}")]
    [TestCase("dependsOn", "{\"dependsOn\":[\"STAGEDREV-20260814-001\"]}")]
    [TestCase("parallelEligible", "{\"parallelEligible\":true}")]
    [TestCase("executionMode", "{\"executionMode\":\"implementation\"}")]
    public void Apply_UpdatesIndividualTaskPropertyWithoutReplacingTask(string property, string patchJson)
    {
        var pending = MakePending();

        var result = Apply(pending, new PlanRevisionOperation("updateTask", Id("003"), Patch(patchJson)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
            var revised = result.RevisedGroup!.Tasks.Single(task => task.Id == Id("003"));
            Assert.That(ReadProperty(revised, property), Is.Not.EqualTo(ReadProperty(pending.Group.Tasks[2], property)));
            Assert.That(revised.Id, Is.EqualTo(Id("003")));
            var untouched = result.RevisedGroup.Tasks.Single(task => task.Id == Id("002"));
            Assert.That(untouched.Description, Is.EqualTo(pending.Group.Tasks[1].Description));
            Assert.That(untouched.DependsOn, Is.EqualTo(pending.Group.Tasks[1].DependsOn));
        });
    }

    [Test]
    public void Apply_ReplacesArrayPropertyAtomically()
    {
        var pending = MakePending();

        var result = Apply(pending, new PlanRevisionOperation(
            "updateTask", Id("003"), Patch($$"""{"dependsOn":["{{Id("001")}}"]}""")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
        Assert.That(result.RevisedGroup!.Tasks.Single(task => task.Id == Id("003")).DependsOn,
            Is.EqualTo(new[] { Id("001") }));
    }

    [Test]
    public void Apply_UpdatesAssignedRoutingPropertiesTogether()
    {
        var pending = MakePending();
        var patch = Patch("""
            {
              "agentRoutingMode": "assigned",
              "agentAssignments": [
                { "agentHandle": "sam", "role": "Implement", "allowGenericChildren": false }
              ]
            }
            """);

        var result = Apply(pending, new PlanRevisionOperation("updateTask", Id("003"), patch));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
            var task = result.RevisedGroup!.Tasks.Single(candidate => candidate.Id == Id("003"));
            Assert.That(task.AgentRoutingMode, Is.EqualTo("assigned"));
            Assert.That(task.AgentAssignments!.Single().AgentHandle, Is.EqualTo("sam"));
        });
    }

    [Test]
    public void Apply_UpdatesPlanMetadataOnly()
    {
        var pending = MakePending();

        var result = Apply(pending, new PlanRevisionOperation(
            "updatePlan", Patch: Patch("""{"groupTitle":"Revised staged plan","branch":"feature/revised","summary":"Revised summary"}""")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
            Assert.That(result.RevisedGroup!.GroupTitle, Is.EqualTo("Revised staged plan"));
            Assert.That(result.RevisedGroup.Branch, Is.EqualTo("feature/revised"));
            Assert.That(result.RevisedGroup.Summary, Is.EqualTo("Revised summary"));
            Assert.That(result.RevisedGroup.Tasks.Select(task => task.Id),
                Is.EqualTo(pending.Group.Tasks.Select(task => task.Id)));
            Assert.That(result.RevisedGroup.Tasks.Select(task => task.Description),
                Is.EqualTo(pending.Group.Tasks.Select(task => task.Description)));
        });
    }

    [Test]
    public void Apply_AddsCompleteTask()
    {
        var pending = MakePending();
        var task = MakeTask("005", "Added follow-up", [Id("003")]);

        var result = Apply(pending, new PlanRevisionOperation("addTask", Task: task));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
        Assert.That(result.RevisedGroup!.Tasks, Has.Count.EqualTo(5));
        Assert.That(result.RevisedGroup.Tasks.Last().Id, Is.EqualTo(task.Id));
        Assert.That(result.RevisedGroup.Tasks.Last().Description, Is.EqualTo(task.Description));
        Assert.That(result.RevisedGroup.Tasks.Last().DependsOn, Is.EqualTo(task.DependsOn));
    }

    [Test]
    public void Apply_RemovesUnreferencedTask()
    {
        var pending = MakePending();

        var result = Apply(pending, new PlanRevisionOperation("removeTask", Id("004")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
        Assert.That(result.RevisedGroup!.Tasks.Select(task => task.Id), Does.Not.Contain(Id("004")));
    }

    [Test]
    public void Apply_AddsUpdatesAndRemovesApprovalGates()
    {
        var pending = MakePending();
        var addedGate = new DecomposedGate(
            "review-two", "Review second boundary", [Id("002")], [Id("003")],
            Question: "Does the second boundary look correct?");

        var added = Apply(pending, new PlanRevisionOperation("addApprovalGate", ApprovalGate: addedGate));
        var updated = Apply(pending, new PlanRevisionOperation(
            "updateApprovalGate", "review-one", Patch("""{"message":"Updated review message"}""")));
        var removed = Apply(pending, new PlanRevisionOperation("removeApprovalGate", "review-one"));

        Assert.Multiple(() =>
        {
            Assert.That(added.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), added.Error);
            Assert.That(added.RevisedGroup!.ApprovalGates, Has.Count.EqualTo(2));
            Assert.That(updated.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), updated.Error);
            Assert.That(updated.RevisedGroup!.ApprovalGates!.Single(gate => gate.GateId == "review-one").Message,
                Is.EqualTo("Updated review message"));
            Assert.That(removed.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), removed.Error);
            Assert.That(removed.RevisedGroup!.ApprovalGates, Is.Null);
        });
    }

    [Test]
    public void Apply_AddsUpdatesAndRemovesValidations()
    {
        var pending = MakePending();
        var validation = MakeValidation("002", "Added validation", [Id("003")], []);

        var added = Apply(pending, new PlanRevisionOperation("addValidation", Validation: validation));
        var updated = Apply(pending, new PlanRevisionOperation(
            "updateValidation", ValidationId("001"), Patch("""{"assertions":["Revised assertion"]}""")));
        var removed = Apply(pending, new PlanRevisionOperation("removeValidation", ValidationId("001")));

        Assert.Multiple(() =>
        {
            Assert.That(added.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), added.Error);
            Assert.That(added.RevisedGroup!.Validations, Has.Count.EqualTo(2));
            Assert.That(updated.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), updated.Error);
            Assert.That(updated.RevisedGroup!.Validations!.Single().Assertions,
                Is.EqualTo(new[] { "Revised assertion" }));
            Assert.That(removed.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), removed.Error);
            Assert.That(removed.RevisedGroup!.Validations, Is.Null);
        });
    }

    [Test]
    public void Apply_MaterializesMultipleIndependentOperationsAndPreservesDelivery()
    {
        var pending = MakePending() with { Group = MakePending().Group with { Delivery = "inbox" } };
        pending = pending with { Revision = PendingDecomposePlanStore.ComputeRevision(pending.Group) };
        var proposal = MakeProposal(pending,
            new PlanRevisionOperation("updateTask", Id("002"), Patch("""{"description":"Changed second task"}""")),
            new PlanRevisionOperation("removeTask", Id("004")),
            new PlanRevisionOperation("updateValidation", ValidationId("001"), Patch("""{"title":"Changed validation"}""")));

        var result = StagedPlanRevisionApplier.Apply(pending, proposal);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Applied), result.Error);
            Assert.That(result.RevisedGroup!.Tasks, Has.Count.EqualTo(3));
            Assert.That(result.RevisedGroup.Tasks[1].Description, Is.EqualTo("Changed second task"));
            Assert.That(result.RevisedGroup.Validations!.Single().Title, Is.EqualTo("Changed validation"));
            Assert.That(result.RevisedGroup.Delivery, Is.EqualTo("inbox"));
            Assert.That(result.Revision, Is.Not.EqualTo(pending.Revision));
        });
    }

    [Test]
    public void Apply_IdenticalPatchReturnsNoChanges()
    {
        var pending = MakePending();

        var result = Apply(pending, new PlanRevisionOperation(
            "updateTask", Id("001"), Patch("""{"description":"Create foundation"}""")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.NoChanges), result.Error);
        Assert.That(result.Revision, Is.EqualTo(pending.Revision));
    }

    [Test]
    public void Apply_RejectsStaleBaseRevision()
    {
        var pending = MakePending();
        var proposal = MakeProposal(pending,
            new PlanRevisionOperation("updateTask", Id("003"), Patch("""{"title":"New title"}"""))) with
        {
            BaseRevision = "stale-revision",
        };

        var result = StagedPlanRevisionApplier.Apply(pending, proposal);

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Stale));
        Assert.That(result.Error, Does.Contain(pending.Revision));
    }

    [Test]
    public void Apply_RejectsWrongGroupIdentity()
    {
        var pending = MakePending();
        var proposal = MakeProposal(pending,
            new PlanRevisionOperation("updateTask", Id("003"), Patch("""{"title":"New title"}"""))) with
        {
            GroupId = "OTHER-20260814",
        };

        var result = StagedPlanRevisionApplier.Apply(pending, proposal);

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain(pending.Group.GroupId));
    }

    [TestCase("updateTask", "missing-task")]
    [TestCase("updateApprovalGate", "missing-gate")]
    [TestCase("updateValidation", "missing-validation")]
    public void Apply_RejectsUnknownUpdateTarget(string operation, string targetId)
    {
        var pending = MakePending();
        var result = Apply(pending, new PlanRevisionOperation(operation, targetId, Patch("""{"title":"Changed"}""")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain("does not exist"));
    }

    [Test]
    public void Apply_RejectsUnknownPatchProperty()
    {
        var result = Apply(MakePending(), new PlanRevisionOperation(
            "updateTask", Id("003"), Patch("""{"status":"complete"}""")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain("cannot patch 'status'"));
    }

    [Test]
    public void Apply_RejectsDuplicateMutationOfSameEntity()
    {
        var pending = MakePending();
        var proposal = MakeProposal(pending,
            new PlanRevisionOperation("updateTask", Id("003"), Patch("""{"title":"First"}""")),
            new PlanRevisionOperation("updateTask", Id("003"), Patch("""{"description":"Second"}""")));

        var result = StagedPlanRevisionApplier.Apply(pending, proposal);

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain("more than once"));
    }

    [Test]
    public void Apply_RejectsRemovingReferencedTask()
    {
        var result = Apply(MakePending(), new PlanRevisionOperation("removeTask", Id("002")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain("unknown id"));
    }

    [Test]
    public void Apply_RejectsDependencyCycleAfterMaterialization()
    {
        var result = Apply(MakePending(), new PlanRevisionOperation(
            "updateTask", Id("001"), Patch($$"""{"dependsOn":["{{Id("003")}}"]}""")));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain("cycle").IgnoreCase);
    }

    [Test]
    public void Apply_RejectsAddedTaskWithNonconformingId()
    {
        var invalidTask = new DecomposedSubTask(
            "WRONG-001", "Invalid ID", [], "high", "Invalid ID");

        var result = Apply(MakePending(), new PlanRevisionOperation("addTask", Task: invalidTask));

        Assert.That(result.Outcome, Is.EqualTo(StagedPlanRevisionOutcome.Invalid));
        Assert.That(result.Error, Does.Contain("does not match"));
    }

    [Test]
    public void RepairPrompt_PreservesRevisionLockAndAllowedOperationContract()
    {
        var pending = MakePending();
        var proposal = MakeProposal(pending,
            new PlanRevisionOperation("updateTask", Id("003"), Patch("""{"title":"Changed"}""")));

        var prompt = StagedPlanRevisionProposalParser.BuildRepairPrompt("bad patch", proposal);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(pending.Group.GroupId));
            Assert.That(prompt, Does.Contain(pending.Revision));
            Assert.That(prompt, Does.Contain("updateTask"));
            Assert.That(prompt, Does.Contain("do not use reopenTask").IgnoreCase);
            Assert.That(prompt, Does.Not.Contain("complete revised plan"));
        });
    }

    [Test]
    public void CompletionPipeline_RoutesStagedDeltaBeforeAcceptedRevisionAndFullTasks()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var stagedIndex = source.IndexOf(
            "var hasStagedRevisionPayload = _stagedPlanRevisionTurnCollectionState.Handled ||",
            StringComparison.Ordinal);
        var acceptedIndex = source.IndexOf(
            "var hasAcceptedRevisionPayload = !hasStagedRevisionPayload &&",
            StringComparison.Ordinal);
        var tasksIndex = source.IndexOf(
            "if (!hasRevisionPayload)",
            StringComparison.Ordinal);
        var stagedRouting = stagedIndex >= 0 && acceptedIndex > stagedIndex
            ? source[stagedIndex..acceptedIndex]
            : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(stagedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(acceptedIndex, Is.GreaterThan(stagedIndex));
            Assert.That(tasksIndex, Is.GreaterThan(acceptedIndex));
            Assert.That(stagedRouting, Does.Contain("TryApplyStagedPlanRevisionFromResponse("));
            Assert.That(source, Does.Contain("if (StageValidatedDecomposeGroup(revised, ownerView, fullProposal))"));
            Assert.That(source, Does.Contain("RestorePendingDecomposeApprovalSurfaces();"));
        });
    }

    private static StagedPlanRevisionResult Apply(PendingDecomposePlan pending, params PlanRevisionOperation[] operations) =>
        StagedPlanRevisionApplier.Apply(pending, MakeProposal(pending, operations));

    private static StagedPlanRevisionProposalPayload MakeProposal(
        PendingDecomposePlan pending,
        params PlanRevisionOperation[] operations) =>
        new(pending.Group.GroupId, pending.Revision, "Apply requested corrections", operations);

    private static PendingDecomposePlan MakePending()
    {
        var group = new DecomposedTaskGroup(
            GroupId: "STAGEDREV-20260814",
            GroupTitle: "Staged revision integration",
            Branch: "feature/staged-revision",
            Summary: "Exercise compact staged plan corrections",
            Tasks:
            [
                MakeTask("001", "Create foundation", []),
                MakeTask("002", "Build feature", [Id("001")]),
                MakeTask("003", "Verify behavior", [Id("002")]),
                MakeTask("004", "Independent documentation", []),
            ],
            ApprovalGates:
            [
                new DecomposedGate(
                    "review-one", "Review the foundation", [Id("001")], [Id("002")],
                    Question: "Does the foundation meet the requested contract?")
            ],
            Validations:
            [
                MakeValidation("001", "Validate feature boundary", [Id("002")], [Id("003")])
            ]);
        return new PendingDecomposePlan(
            PendingDecomposePlanStore.ComputeRevision(group),
            group,
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
    }

    private static DecomposedSubTask MakeTask(
        string suffix,
        string description,
        IReadOnlyList<string> dependsOn) =>
        new(Id(suffix), description, dependsOn, "high", description);

    private static DecomposedValidationNode MakeValidation(
        string suffix,
        string title,
        IReadOnlyList<string> after,
        IReadOnlyList<string> before) =>
        new(ValidationId(suffix), title, title, after, before, [title + " passes"], Mode: "audit");

    private static string Id(string suffix) => $"STAGEDREV-20260814-{suffix}";

    private static string ValidationId(string suffix) => $"STAGEDREV-20260814-VAL-{suffix}";

    private static JsonElement Patch(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static object? ReadProperty(DecomposedSubTask task, string property) => property switch
    {
        "description" => task.Description,
        "title" => task.Title,
        "priority" => task.Priority,
        "dependsOn" => string.Join("|", task.DependsOn),
        "parallelEligible" => task.ParallelEligible,
        "executionMode" => task.ExecutionMode,
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static string Serialize(StagedPlanRevisionProposalPayload payload) =>
        StagedPlanRevisionProposalParser.Marker + "\n" + JsonSerializer.Serialize(payload);

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
