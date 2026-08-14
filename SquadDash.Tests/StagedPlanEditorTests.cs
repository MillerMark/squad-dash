using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class StagedPlanEditorTests
{
    private const string GroupId = "EDITPLAN-20260814";

    [Test]
    public void UpdateTask_ChangesIndividualPropertiesWithoutReplacingOtherEntities()
    {
        var original = MakeGroup();
        var updated = StagedPlanEditor.UpdateTask(
            original, Id(2), "New title", "New description", "high", [Id(1), Id(3)]);

        var task = updated.Tasks.Single(value => value.Id == Id(2));
        Assert.Multiple(() =>
        {
            Assert.That(task.Title, Is.EqualTo("New title"));
            Assert.That(task.Description, Is.EqualTo("New description"));
            Assert.That(task.Priority, Is.EqualTo("high"));
            Assert.That(task.DependsOn, Is.EqualTo(new[] { Id(1), Id(3) }));
            Assert.That(updated.Tasks.Single(value => value.Id == Id(1)),
                Is.EqualTo(original.Tasks.Single(value => value.Id == Id(1))));
            Assert.That(updated.Validations, Is.SameAs(original.Validations));
        });
    }

    [Test]
    public void UpdatePlanAndAdvancedTaskFields_ChangeOnlyEditableDefinitionData()
    {
        var group = StagedPlanEditor.UpdatePlan(
            MakeGroup(), "Renamed", "feature/renamed", "Revised summary", "squash merge");
        group = StagedPlanEditor.UpdateTaskAdvanced(
            group, Id(1),
            [new DecomposedAgentAssignment("rory", "Plan architecture", false)],
            "assigned", true, null, "isolated");
        var task = group.Tasks[0];
        var assignment = task.AgentAssignments!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(group.GroupTitle, Is.EqualTo("Renamed"));
            Assert.That(group.Branch, Is.EqualTo("feature/renamed"));
            Assert.That(group.Summary, Is.EqualTo("Revised summary"));
            Assert.That(group.Delivery, Is.EqualTo("squash merge"));
            Assert.That(task.AgentRoutingMode, Is.EqualTo("assigned"));
            Assert.That(assignment.AgentHandle, Is.EqualTo("rory"));
            Assert.That(assignment.AllowGenericChildren, Is.False);
            Assert.That(task.ParallelEligible, Is.True);
            Assert.That(task.ExecutionMode, Is.EqualTo("isolated"));
            Assert.That(group.Tasks[1].Title, Is.EqualTo("Second"));
            Assert.That(group.Tasks[1].Description, Is.EqualTo("Second result"));
        });
    }

    [Test]
    public void UpdateValidationAndGate_ChangeEveryInspectorProperty()
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [new DecomposedGate(
                GroupId + "-GATE-001", "Original gate", [Id(1)], [Id(2)])],
            Validations = [new DecomposedValidationNode(
                GroupId + "-VAL-001", "Original", "Original validation", [Id(1)], [Id(2)], ["Old assertion"])],
        };
        group = StagedPlanEditor.UpdateValidation(
            group, GroupId + "-VAL-001", "Updated", "Updated validation", "command",
            [Id(2)], [Id(3)], ["New assertion"], ["dotnet test"], false, ["output-1"]);
        group = StagedPlanEditor.UpdateGate(
            group, GroupId + "-GATE-001", "Updated gate", "Approve?", [Id(2)], [Id(3)],
            [new DecomposedTaskProofRequirement("proof-1", "test", "Run the focused tests")]);

        var validation = group.Validations!.Single();
        var gate = group.ApprovalGates!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(validation.Title, Is.EqualTo("Updated"));
            Assert.That(validation.Description, Is.EqualTo("Updated validation"));
            Assert.That(validation.Mode, Is.EqualTo("command"));
            Assert.That(validation.AfterTaskIds, Is.EqualTo(new[] { Id(2) }));
            Assert.That(validation.BeforeTaskIds, Is.EqualTo(new[] { Id(3) }));
            Assert.That(validation.Assertions, Is.EqualTo(new[] { "New assertion" }));
            Assert.That(validation.Commands, Is.EqualTo(new[] { "dotnet test" }));
            Assert.That(validation.OutputIds, Is.EqualTo(new[] { "output-1" }));
            Assert.That(validation.RevalidateAtCompletion, Is.False);
            Assert.That(gate.Message, Is.EqualTo("Updated gate"));
            Assert.That(gate.Question, Is.EqualTo("Approve?"));
            Assert.That(gate.ProofRequirements!.Single().RequirementId, Is.EqualTo("proof-1"));
        });
    }

    [Test]
    public void TryValidate_RejectsUnknownDependenciesAndCycles()
    {
        var unknown = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(2), "Second", "Second result", "mid", ["MISSING-001"]);
        var cycle = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(1), "First", "First result", "mid", [Id(3)]);

        Assert.Multiple(() =>
        {
            Assert.That(StagedPlanEditor.TryValidate(unknown, out _, out var unknownError), Is.False);
            Assert.That(unknownError, Is.Not.Empty);
            Assert.That(StagedPlanEditor.TryValidate(cycle, out _, out var cycleError), Is.False);
            Assert.That(cycleError, Does.Contain("cycle").IgnoreCase);
        });
    }

    [Test]
    public void AddValidation_UsesCompactIdAndTheImmediateTaskFrontier()
    {
        var updated = StagedPlanEditor.AddValidationAfterTask(MakeGroup(), Id(2));
        var validation = updated.Validations!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(validation.ValidationId, Is.EqualTo(GroupId + "-VAL-001"));
            Assert.That(validation.AfterTaskIds, Is.EqualTo(new[] { Id(2) }));
            Assert.That(validation.BeforeTaskIds, Is.EqualTo(new[] { Id(3) }));
            Assert.That(validation.Assertions, Has.Count.EqualTo(1));
            Assert.That(StagedPlanEditor.TryValidate(updated, out _, out var error), Is.True, error);
        });
    }

    [Test]
    public void InsertBefore_ReplacesAnchorDependenciesWithNewCompactTask()
    {
        var updated = StagedPlanEditor.InsertTask(MakeGroup(), Id(2), before: true);
        var inserted = updated.Tasks.Single(value => value.Id == Id(4));
        var anchor = updated.Tasks.Single(value => value.Id == Id(2));

        Assert.Multiple(() =>
        {
            Assert.That(inserted.DependsOn, Is.EqualTo(new[] { Id(1) }));
            Assert.That(anchor.DependsOn, Is.EqualTo(new[] { Id(4) }));
            Assert.That(StagedPlanEditor.TryValidate(updated, out _, out var error), Is.True, error);
        });
    }

    [Test]
    public void InsertAfter_RewiresEveryDirectDependentThroughNewTask()
    {
        var group = MakeGroup() with
        {
            Tasks = MakeGroup().Tasks.Append(
                new DecomposedSubTask(Id(4), "Parallel result", [Id(2)], "mid", "Parallel")).ToArray(),
        };
        var updated = StagedPlanEditor.InsertTask(group, Id(2), before: false);
        var insertedId = Id(5);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks.Single(value => value.Id == insertedId).DependsOn,
                Is.EqualTo(new[] { Id(2) }));
            Assert.That(updated.Tasks.Single(value => value.Id == Id(3)).DependsOn,
                Is.EqualTo(new[] { insertedId }));
            Assert.That(updated.Tasks.Single(value => value.Id == Id(4)).DependsOn,
                Is.EqualTo(new[] { insertedId }));
        });
    }

    [Test]
    public void DeleteTask_ReconnectsDependentsAndRemovesBoundaryReferences()
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [new DecomposedGate(
                GroupId + "-GATE-001",
                "Verify the second task's observable result and decide whether dependent work may proceed.",
                [Id(2)], [Id(3)],
                Question: "Does the second task meet its stated observable acceptance criteria?")],
            Validations = [new DecomposedValidationNode(
                GroupId + "-VAL-001", "Validate", "Validate result", [Id(2)], [Id(3)], ["It works"])],
        };
        var updated = StagedPlanEditor.DeleteTask(group, Id(2));

        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks.Select(value => value.Id), Does.Not.Contain(Id(2)));
            Assert.That(updated.Tasks.Single(value => value.Id == Id(3)).DependsOn,
                Is.EqualTo(new[] { Id(1) }));
            Assert.That(updated.ApprovalGates!.Single().AfterTaskIds, Is.EqualTo(new[] { Id(1) }));
            Assert.That(updated.Validations!.Single().AfterTaskIds, Is.EqualTo(new[] { Id(1) }));
            Assert.That(StagedPlanEditor.TryValidate(updated, out _, out var error), Is.True, error);
        });
    }

    [Test]
    public void DeleteValidationAndGate_RemoveOnlyTheSelectedEntities()
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [
                new DecomposedGate(GroupId + "-GATE-001", "One", [Id(1)], [Id(2)]),
                new DecomposedGate(GroupId + "-GATE-002", "Two", [Id(2)], [Id(3)]),
            ],
            Validations = [
                new DecomposedValidationNode(GroupId + "-VAL-001", "One", "One", [Id(1)], [Id(2)], ["One"]),
                new DecomposedValidationNode(GroupId + "-VAL-002", "Two", "Two", [Id(2)], [Id(3)], ["Two"]),
            ],
        };

        var updated = StagedPlanEditor.DeleteGate(
            StagedPlanEditor.DeleteValidation(group, GroupId + "-VAL-001"),
            GroupId + "-GATE-001");

        Assert.Multiple(() =>
        {
            Assert.That(updated.Validations!.Select(value => value.ValidationId),
                Is.EqualTo(new[] { GroupId + "-VAL-002" }));
            Assert.That(updated.ApprovalGates!.Select(value => value.GateId),
                Is.EqualTo(new[] { GroupId + "-GATE-002" }));
        });
    }

    [Test]
    public void EditHistory_SupportsUndoRedoAndClearsRedoAfterNewEdit()
    {
        var first = MakeGroup();
        var second = StagedPlanEditor.UpdateTask(first, Id(1), "Second", "Second", "mid", []);
        var third = StagedPlanEditor.UpdateTask(second, Id(1), "Third", "Third", "mid", []);
        var branch = StagedPlanEditor.UpdateTask(second, Id(1), "Branch", "Branch", "mid", []);
        var history = new StagedPlanEditHistory(first);

        history.Record(second);
        history.Record(third);
        Assert.That(history.Undo(), Is.EqualTo(second));
        Assert.That(history.Redo(), Is.EqualTo(third));
        Assert.That(history.Undo(), Is.EqualTo(second));
        history.Record(branch);

        Assert.Multiple(() =>
        {
            Assert.That(history.Current, Is.EqualTo(branch));
            Assert.That(history.CanUndo, Is.True);
            Assert.That(history.CanRedo, Is.False);
        });
    }

    private static string Id(int number) => $"{GroupId}-{number:000}";

    internal static DecomposedTaskGroup MakeGroup() => new(
        GroupId,
        "Editable plan",
        "feature/editable-plan",
        "Exercise staged plan editing.",
        [
            new DecomposedSubTask(Id(1), "First result", [], "mid", "First"),
            new DecomposedSubTask(Id(2), "Second result", [Id(1)], "mid", "Second"),
            new DecomposedSubTask(Id(3), "Third result", [Id(2)], "mid", "Third"),
        ]);
}
