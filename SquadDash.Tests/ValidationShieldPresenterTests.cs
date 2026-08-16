using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ValidationShieldPresenterTests
{
    [TestCase(PlanValidationStatus.Validating, true)]
    [TestCase(PlanValidationStatus.Ready, false)]
    [TestCase(PlanValidationStatus.Passed, false)]
    public void ShowsActivitySpinner_OnlyWhileValidationIsRunning(string status, bool expected)
    {
        Assert.That(ValidationShieldPresenter.ShowsActivitySpinner(status), Is.EqualTo(expected));
    }

    // ── Shield state derivation ───────────────────────────────────────────────

    [TestCase(null, ValidationShieldPresenter.ShieldVisualState.Pending)]
    [TestCase(PlanValidationStatus.Pending, ValidationShieldPresenter.ShieldVisualState.Pending)]
    [TestCase(PlanValidationStatus.Ready, ValidationShieldPresenter.ShieldVisualState.Ready)]
    [TestCase(PlanValidationStatus.Validating, ValidationShieldPresenter.ShieldVisualState.Validating)]
    [TestCase(PlanValidationStatus.Passed, ValidationShieldPresenter.ShieldVisualState.Passed)]
    [TestCase(PlanValidationStatus.Failed, ValidationShieldPresenter.ShieldVisualState.Failed)]
    [TestCase(PlanValidationStatus.Stale, ValidationShieldPresenter.ShieldVisualState.Stale)]
    public void DeriveVisualState_MapsStatusCorrectly(
        string? status, ValidationShieldPresenter.ShieldVisualState expected)
    {
        Assert.That(ValidationShieldPresenter.DeriveVisualState(status), Is.EqualTo(expected));
    }

    [Test]
    public void DeriveVisualState_UnrecognizedStatus_ReturnsPending()
    {
        Assert.That(
            ValidationShieldPresenter.DeriveVisualState("unknown"),
            Is.EqualTo(ValidationShieldPresenter.ShieldVisualState.Pending));
    }

    // ── Tooltip content generation ────────────────────────────────────────────

    [Test]
    public void BuildTooltipContent_PopulatesAllFields()
    {
        var tasks = new[]
        {
            new PlanTask("A", "Task A", "Does A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "Task B", "Does B", ["A"], "high", PlanTaskStatus.Pending),
        };
        var validation = new PlanValidationNode(
            "VAL-1", "Boundary Check", "Validates A → B contract",
            ["A"], ["B"],
            ["A output exists", "B can consume it"],
            [], "evidence", [], true, PlanValidationStatus.Ready,
            Summary: "Evidence collected",
            Evidence: ["File X confirmed"]);

        var content = ValidationShieldPresenter.BuildTooltipContent(validation, tasks);

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Boundary Check"));
            Assert.That(content.Description, Is.EqualTo("Validates A → B contract"));
            Assert.That(content.StatusLabel, Is.EqualTo("Ready to validate"));
            Assert.That(content.Assertions, Has.Count.EqualTo(2));
            Assert.That(content.Assertions[0], Is.EqualTo("A output exists"));
            Assert.That(content.PrerequisiteLabels, Has.Count.EqualTo(1));
            Assert.That(content.PrerequisiteLabels[0], Does.Contain("Task A"));
            Assert.That(content.BlockedLabels, Has.Count.EqualTo(1));
            Assert.That(content.BlockedLabels[0], Does.Contain("Task B"));
            Assert.That(content.Evidence, Has.Count.EqualTo(1));
            Assert.That(content.Summary, Is.EqualTo("Evidence collected"));
        });
    }

    [Test]
    public void BuildTooltipContent_UnknownTaskId_UsesRawId()
    {
        var tasks = Array.Empty<PlanTask>();
        var validation = new PlanValidationNode(
            "VAL-1", "T", "D", ["UNKNOWN"], ["ALSO-UNKNOWN"],
            ["Assert"], [], "evidence", [], false, PlanValidationStatus.Pending);

        var content = ValidationShieldPresenter.BuildTooltipContent(validation, tasks);

        Assert.Multiple(() =>
        {
            Assert.That(content.PrerequisiteLabels[0], Is.EqualTo("UNKNOWN"));
            Assert.That(content.BlockedLabels[0], Is.EqualTo("ALSO-UNKNOWN"));
        });
    }

    // ── Task highlighting ─────────────────────────────────────────────────────

    [Test]
    public void ComputeHighlightedTasks_IncludesPrerequisitesAndTransitiveBlocked()
    {
        // Graph: A → B → C → D (linear chain)
        // Validation sits after [A], before [B]
        // B → C → D are all transitively blocked.
        var tasks = new[]
        {
            new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
            new PlanTask("C", "C", "C", ["B"], "high", PlanTaskStatus.Pending),
            new PlanTask("D", "D", "D", ["C"], "high", PlanTaskStatus.Pending),
        };
        var validation = new PlanValidationNode(
            "VAL-1", "T", "D", ["A"], ["B"],
            ["Assert"], [], "evidence", [], false, PlanValidationStatus.Ready);

        var result = ValidationShieldPresenter.ComputeHighlightedTasks(validation, tasks);

        Assert.Multiple(() =>
        {
            Assert.That(result.PrerequisiteTaskIds, Is.EquivalentTo(new[] { "A" }));
            Assert.That(result.BlockedTaskIds, Is.EquivalentTo(new[] { "B", "C", "D" }));
        });
    }

    [Test]
    public void ComputeHighlightedTasks_MultiplePrerequisites()
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "B", [], "high", PlanTaskStatus.Complete),
            new PlanTask("C", "C", "C", ["A", "B"], "high", PlanTaskStatus.Pending),
        };
        var validation = new PlanValidationNode(
            "VAL-1", "T", "D", ["A", "B"], ["C"],
            ["Assert"], [], "evidence", [], false, PlanValidationStatus.Pending);

        var result = ValidationShieldPresenter.ComputeHighlightedTasks(validation, tasks);

        Assert.Multiple(() =>
        {
            Assert.That(result.PrerequisiteTaskIds, Is.EquivalentTo(new[] { "A", "B" }));
            Assert.That(result.BlockedTaskIds, Is.EquivalentTo(new[] { "C" }));
        });
    }

    [Test]
    public void ComputeHighlightedTasks_NoBlockedTasks_ReturnsEmptyBlockedSet()
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
        };
        var validation = new PlanValidationNode(
            "VAL-1", "T", "D", ["A"], [],
            ["Assert"], [], "evidence", [], false, PlanValidationStatus.Passed);

        var result = ValidationShieldPresenter.ComputeHighlightedTasks(validation, tasks);

        Assert.Multiple(() =>
        {
            Assert.That(result.PrerequisiteTaskIds, Is.EquivalentTo(new[] { "A" }));
            Assert.That(result.BlockedTaskIds, Is.Empty);
        });
    }

    [Test]
    public void ComputeValidationAwareLevels_MovesBlockedTaskAfterSameStagePrerequisite()
    {
        var tasks = new[]
        {
            new DecomposedSubTask("A", "Root", [], "high", "Root"),
            new DecomposedSubTask("B", "First branch", ["A"], "high", "First branch"),
            new DecomposedSubTask("C", "Blocked branch", ["A"], "high", "Blocked branch"),
            new DecomposedSubTask("D", "Downstream", ["C"], "high", "Downstream"),
        };
        var validation = new DecomposedValidationNode(
            "VAL", "Contract", "B must validate before C", ["B"], ["C"], ["B connects to C"]);

        var levels = ValidationShieldPresenter.ComputeValidationAwareLevels(tasks, [validation]);

        Assert.Multiple(() =>
        {
            Assert.That(levels["A"], Is.EqualTo(0));
            Assert.That(levels["B"], Is.EqualTo(1));
            Assert.That(levels["C"], Is.EqualTo(2));
            Assert.That(levels["D"], Is.EqualTo(3));
        });
    }

    [Test]
    public void ComputeValidationAwareLevels_MultiStagePlanPlacesAllThreeValidationRailsSemantically()
    {
        const string prefix = "SQUADUPDATE-20260815-";
        string Id(int value) => prefix + value.ToString("000");
        var tasks = new[]
        {
            new DecomposedSubTask(Id(1), "Commands", [], "high", "Commands"),
            new DecomposedSubTask(Id(2), "Parser", [Id(1)], "high", "Parser"),
            new DecomposedSubTask(Id(3), "Persistence", [], "high", "Persistence"),
            new DecomposedSubTask(Id(4), "Coordinator", [Id(1), Id(2), Id(3)], "high", "Coordinator"),
            new DecomposedSubTask(Id(5), "Dialog", [Id(2)], "high", "Dialog"),
            new DecomposedSubTask(Id(6), "Apply", [Id(4), Id(5)], "high", "Apply"),
            new DecomposedSubTask(Id(7), "Git view", [Id(6)], "high", "Git view"),
            new DecomposedSubTask(Id(8), "Indicator", [Id(3)], "high", "Indicator"),
            new DecomposedSubTask(Id(9), "Optional diff", [Id(2)], "low", "Optional diff"),
            new DecomposedSubTask(Id(10), "Tests", [Id(2), Id(3), Id(4), Id(5), Id(6)], "high", "Tests"),
            new DecomposedSubTask(Id(11), "End to end", [Id(7), Id(8), Id(10)], "high", "End to end"),
        };
        var validations = new[]
        {
            new DecomposedValidationNode("V1", "Dry run", "Contract", [Id(1), Id(2)], [Id(4), Id(5)], ["Connected"]),
            new DecomposedValidationNode("V2", "Build", "Build", [Id(4), Id(5), Id(6), Id(7), Id(8)], [Id(10)], ["Builds"]),
            new DecomposedValidationNode("V3", "Audit", "Audit", [Id(9), Id(11)], [], ["Complete"], Mode: "audit"),
        };

        var levels = ValidationShieldPresenter.ComputeValidationAwareLevels(tasks, validations);
        var stageCount = levels.Values.Max() + 1;
        int Anchor(DecomposedValidationNode validation) =>
            ValidationShieldPresenter.InferComplexValidationStageIndex(
                validation.AfterTaskIds.Select(id => levels[id]).ToArray(),
                validation.BeforeTaskIds.Select(id => levels[id]).ToArray(),
                stageCount);

        Assert.Multiple(() =>
        {
            Assert.That(Anchor(validations[0]), Is.EqualTo(1), "Dry-run validation belongs after Stage 2.");
            Assert.That(Anchor(validations[1]), Is.EqualTo(4), "Build validation belongs immediately before its delayed test stage.");
            Assert.That(Anchor(validations[2]), Is.EqualTo(6), "Final audit belongs on the final right-side rail.");
            Assert.That(levels[Id(10)], Is.EqualTo(5), "Validation constraints are display edges, separating tests from their prerequisites.");
            Assert.That(levels[Id(11)], Is.EqualTo(6));
        });
    }

    // ── Summary for Plans panel ───────────────────────────────────────────────

    [Test]
    public void Summarize_NoValidations_ReturnsNull()
    {
        var plan = MakePlan(validations: null);
        Assert.That(ValidationShieldPresenter.Summarize(plan), Is.Null);
    }

    [Test]
    public void Summarize_CountsCorrectly()
    {
        var validations = new[]
        {
            MakeValidation("V1", PlanValidationStatus.Passed),
            MakeValidation("V2", PlanValidationStatus.Failed),
            MakeValidation("V3", PlanValidationStatus.Stale),
            MakeValidation("V4", PlanValidationStatus.Validating),
            MakeValidation("V5", PlanValidationStatus.Ready),
            MakeValidation("V6", PlanValidationStatus.Pending),
        };
        var plan = MakePlan(validations: validations);

        var summary = ValidationShieldPresenter.Summarize(plan);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.Total, Is.EqualTo(6));
            Assert.That(summary.Passed, Is.EqualTo(1));
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(summary.Stale, Is.EqualTo(1));
            Assert.That(summary.Validating, Is.EqualTo(1));
            Assert.That(summary.Ready, Is.EqualTo(1));
            Assert.That(summary.Pending, Is.EqualTo(1));
        });
    }

    [Test]
    public void BuildSummaryLabel_Failed_ShowsFailureCount()
    {
        var summary = new ValidationShieldPresenter.ValidationSummary(3, 1, 2, 0, 0, 0, 0);
        Assert.That(ValidationShieldPresenter.BuildSummaryLabel(summary), Is.EqualTo("2 validations failed"));
    }

    [Test]
    public void BuildSummaryLabel_AllPassed_ShowsAllPassed()
    {
        var summary = new ValidationShieldPresenter.ValidationSummary(3, 3, 0, 0, 0, 0, 0);
        Assert.That(ValidationShieldPresenter.BuildSummaryLabel(summary),
            Is.EqualTo("All 3 validations passed"));
    }

    [Test]
    public void BuildSummaryLabel_SomePassed_ShowsFraction()
    {
        var summary = new ValidationShieldPresenter.ValidationSummary(4, 2, 0, 0, 0, 1, 1);
        Assert.That(ValidationShieldPresenter.BuildSummaryLabel(summary),
            Is.EqualTo("2/4 validations passed"));
    }

    [Test]
    public void BuildSummaryLabel_Validating_ShowsValidating()
    {
        var summary = new ValidationShieldPresenter.ValidationSummary(2, 0, 0, 0, 1, 1, 0);
        Assert.That(ValidationShieldPresenter.BuildSummaryLabel(summary),
            Is.EqualTo("Validating…"));
    }

    [Test]
    public void BuildSummaryLabel_Ready_ShowsReadyCount()
    {
        var summary = new ValidationShieldPresenter.ValidationSummary(3, 0, 0, 0, 0, 2, 1);
        Assert.That(ValidationShieldPresenter.BuildSummaryLabel(summary),
            Is.EqualTo("2 validations ready"));
    }

    [Test]
    public void BuildSummaryLabel_Null_ReturnsNull()
    {
        Assert.That(ValidationShieldPresenter.BuildSummaryLabel(null), Is.Null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PlanValidationNode MakeValidation(string id, string status) =>
        new(id, $"Validation {id}", "Desc", ["A"], ["B"],
            ["Assert"], [], "evidence", [], false, status);

    private static Plan MakePlan(IReadOnlyList<PlanValidationNode>? validations)
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "A", [], "high", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "B", ["A"], "high", PlanTaskStatus.Pending),
        };
        return new Plan(
            "PLAN", "rev", PlanSource.TasksJson, PlanLifecycleStatus.Executing,
            "Plan", "feature/x", "Summary", tasks, [],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow),
            Validations: validations);
    }
}
