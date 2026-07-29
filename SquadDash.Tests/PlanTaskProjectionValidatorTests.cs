using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskProjectionValidatorTests
{
    [Test]
    public void TryGetValidatedItems_ExactCompletedProjection_Succeeds()
    {
        var plan = MakePlan();
        var parsed = MakeParseResult(
            completedItems: [MakeItem("PLAN-001-001", isChecked: true), MakeItem("PLAN-001-002", isChecked: true)]);

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            plan, parsed, plan.PlanId, requireAllComplete: true, out var items, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.True, error);
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(error, Is.Null);
        });
    }

    [Test]
    public void TryGetValidatedItems_MissingProjection_Fails()
    {
        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), null, "PLAN-001", requireAllComplete: false, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("missing"));
        });
    }

    [Test]
    public void TryGetValidatedItems_ParserErrors_Fails()
    {
        var parsed = MakeParseResult(errors: ["bad routing metadata"]);

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), parsed, "PLAN-001", requireAllComplete: false, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("bad routing metadata"));
        });
    }

    [Test]
    public void TryGetValidatedItems_RevisionMismatch_Fails()
    {
        var parsed = MakeParseResult(revision: "rev2");

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), parsed, "PLAN-001", requireAllComplete: false, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("revision changed"));
        });
    }

    [Test]
    public void TryGetValidatedItems_MissingTaskStatus_Fails()
    {
        var parsed = MakeParseResult(openItems: [MakeItem("PLAN-001-001")]);

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), parsed, "PLAN-001", requireAllComplete: false, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("statuses do not match"));
        });
    }

    [Test]
    public void TryGetValidatedItems_DuplicateTaskStatus_Fails()
    {
        var parsed = MakeParseResult(openItems:
            [MakeItem("PLAN-001-001"), MakeItem("PLAN-001-001")]);

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), parsed, "PLAN-001", requireAllComplete: false, out _, out var error);

        Assert.That(valid, Is.False, error);
    }

    [Test]
    public void TryGetValidatedItems_FinalStateWithPendingTask_Fails()
    {
        var parsed = MakeParseResult(openItems:
            [MakeItem("PLAN-001-001", isChecked: true), MakeItem("PLAN-001-002")]);

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), parsed, "PLAN-001", requireAllComplete: true, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("remain unfinished"));
        });
    }

    [Test]
    public void TryGetValidatedItems_RevisedTaskDefinition_Fails()
    {
        var parsed = MakeParseResult(definitionTaskIds: ["PLAN-001-001", "PLAN-001-003"]);

        var valid = PlanTaskProjectionValidator.TryGetValidatedItems(
            MakePlan(), parsed, "PLAN-001", requireAllComplete: false, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("definitions do not match"));
        });
    }

    private static Plan MakePlan() => new(
        PlanId: "PLAN-001",
        Revision: "rev1",
        Source: PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Executing,
        Title: "Plan",
        Branch: "feature/plan",
        Summary: "Summary",
        Tasks:
        [
            MakePlanTask("PLAN-001-001"),
            MakePlanTask("PLAN-001-002"),
        ],
        ApprovalGates: [],
        Progress: new PlanProgress(0, 2, "PLAN-001-001"),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow),
        HostRevision: "rev1");

    private static PlanTask MakePlanTask(string taskId) => new(
        TaskId: taskId,
        Title: taskId,
        Description: taskId,
        DependsOn: [],
        Priority: "mid",
        Status: PlanTaskStatus.Pending);

    private static TaskItem MakeItem(string taskId, bool isChecked = false) => new(
        Text: taskId,
        Owner: null,
        IsUserOwned: false,
        IsChecked: isChecked,
        Emoji: "🟡",
        RawLine: $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** {taskId}",
        DecomposeGroupId: "PLAN-001",
        TaskId: taskId);

    private static TaskParseResult MakeParseResult(
        IReadOnlyList<TaskItem>? openItems = null,
        IReadOnlyList<TaskItem>? completedItems = null,
        IReadOnlyList<string>? definitionTaskIds = null,
        IReadOnlyList<string>? errors = null,
        string revision = "rev1")
    {
        var taskIds = definitionTaskIds ?? ["PLAN-001-001", "PLAN-001-002"];
        var group = new DecomposedTaskGroup(
            "PLAN-001",
            "Plan",
            "feature/plan",
            "Summary",
            taskIds.Select(id => new DecomposedSubTask(id, id, [], "mid", id)).ToArray(),
            HostRevision: revision);
        var priorityGroup = new TaskPriorityGroup("🟡", "Mid", "PLAN-001", "Plan", "feature/plan");
        priorityGroup.Items.AddRange(openItems ?? (completedItems is null
            ? [MakeItem("PLAN-001-001"), MakeItem("PLAN-001-002")]
            : []));
        return new TaskParseResult(
            [priorityGroup],
            completedItems ?? [],
            new Dictionary<string, DecomposedTaskGroup>(StringComparer.Ordinal) { ["PLAN-001"] = group },
            errors);
    }
}
