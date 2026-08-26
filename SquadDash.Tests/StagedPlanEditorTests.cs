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
    public void UpdateTask_DependencyChangeSynchronizesTaskValidationBoundary()
    {
        var original = MakeGroup() with
        {
            Validations =
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001",
                    "Review first",
                    "Validate the first task before dependent work continues.",
                    [Id(1)],
                    [Id(2)],
                    ["First task output is correct."],
                    Mode: "evidence"),
            ],
        };

        var removed = StagedPlanEditor.UpdateTask(
            original, Id(2), "Second", "Second result", "mid", []);
        var added = StagedPlanEditor.UpdateTask(
            removed, Id(3), "Third", "Third result", "mid", [Id(1)]);

        Assert.Multiple(() =>
        {
            Assert.That(removed.Validations!.Single().BeforeTaskIds, Is.Empty);
            Assert.That(added.Validations!.Single().BeforeTaskIds,
                Is.EqualTo(new[] { Id(3) }));
        });
    }

    [Test]
    public void UpdateTask_DependencyChangeReplacesStaleApprovalAnchor()
    {
        var gateId = GroupId + "-GATE-001";
        var original = MakeGroup() with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    gateId,
                    "Review first output",
                    [Id(1)],
                    [Id(2)],
                    PresentationAnchor: "stage:1"),
            ],
        };

        var updated = StagedPlanEditor.UpdateTask(
            original, Id(3), "Third", "Third result", "mid", [Id(1)]);

        Assert.That(updated.ApprovalGates!.Single().PresentationAnchor,
            Is.EqualTo($"task-before:{Id(2)}"));
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
    public void AssignTaskToAgent_ReplacesPrimaryAndPreservesTheWorkRole()
    {
        var original = MakeGroup();
        original = StagedPlanEditor.UpdateTaskAdvanced(
            original,
            Id(2),
            [new DecomposedAgentAssignment("arjun-sen", "Build the backend contract", false)],
            "assigned",
            true,
            null,
            "implementation");

        var updated = StagedPlanEditor.AssignTaskToAgent(original, Id(2), "lyra-morn");
        var assignment = updated.Tasks.Single(task => task.Id == Id(2)).AgentAssignments!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(assignment.AgentHandle, Is.EqualTo("lyra-morn"));
            Assert.That(assignment.Role, Is.EqualTo("Build the backend contract"));
            Assert.That(assignment.AllowGenericChildren, Is.False);
            Assert.That(updated.Tasks.Single(task => task.Id == Id(2)).AgentRoutingMode, Is.EqualTo("assigned"));
            Assert.That(updated.Tasks.Single(task => task.Id == Id(1)),
                Is.EqualTo(original.Tasks.Single(task => task.Id == Id(1))));
        });
    }

    [Test]
    public void AssignTaskToAgent_ConvertsInsertedGenericTaskToAssignedRouting()
    {
        var inserted = StagedPlanEditor.InsertTask(MakeGroup(), Id(2), before: false);
        var insertedId = Id(4);

        var updated = StagedPlanEditor.AssignTaskToAgent(inserted, insertedId, "lyra-morn");
        var task = updated.Tasks.Single(candidate => candidate.Id == insertedId);
        var assignment = task.AgentAssignments!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(assignment.AgentHandle, Is.EqualTo("lyra-morn"));
            Assert.That(assignment.Role,
                Is.EqualTo("Primary implementation owner for this plan step."));
            Assert.That(assignment.AllowGenericChildren, Is.True);
            Assert.That(task.AgentRoutingMode, Is.EqualTo("assigned"));
            Assert.That(task.GenericAgentReason, Is.Null);
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
    public void TryValidate_RejectsGateWithMissingQuestion()
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [new DecomposedGate(
                GroupId + "-GATE-001",
                "Verify the second task's observable result before dependent work continues.",
                [Id(1)], [Id(2)])],   // Question is null (default)
        };

        Assert.Multiple(() =>
        {
            Assert.That(StagedPlanEditor.TryValidate(group, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(GroupId + "-GATE-001"));
            Assert.That(error, Does.Contain("verify or decide").IgnoreCase);
        });
    }

    [Test]
    public void TryValidate_AcceptsGateWithNonEmptyQuestion()
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [new DecomposedGate(
                GroupId + "-GATE-001",
                "Verify the second task's observable result before dependent work continues.",
                [Id(1)], [Id(2)],
                Question: "Does the second task's output meet its stated acceptance criteria?")],
        };

        Assert.That(StagedPlanEditor.TryValidate(group, out _, out var error), Is.True, error);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void TryValidate_RejectsGateWithEmptyOrWhitespaceQuestion(string question)
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [new DecomposedGate(
                GroupId + "-GATE-001",
                "Verify the second task's observable result before dependent work continues.",
                [Id(1)], [Id(2)],
                Question: question)],
        };

        Assert.That(StagedPlanEditor.TryValidate(group, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("verify or decide").IgnoreCase);
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

    [Test]
    public void UnlockedChangeValidation_RejectsChangesToCompletedAndExecutingTasks()
    {
        var current = MakeDurablePlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = MakeDurablePlan().Tasks.Select(task => task.TaskId switch
            {
                var id when id == Id(1) => task with { Status = PlanTaskStatus.Complete },
                var id when id == Id(2) => task with { Status = PlanTaskStatus.Executing },
                _ => task,
            }).ToArray(),
            Progress = new PlanProgress(1, 3, Id(2)),
        };
        var completedEdit = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(1), "Changed", "Changed", "mid", []);
        var executingEdit = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(2), "Changed", "Changed", "mid", [Id(1)]);

        Assert.Multiple(() =>
        {
            Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
                current, completedEdit, out var completedError), Is.False);
            Assert.That(completedError, Does.Contain(Id(1)));
            Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
                current, executingEdit, out var executingError), Is.False);
            Assert.That(executingError, Does.Contain(Id(2)));
        });
    }

    [Test]
    public void UnlockedChangeValidation_AllowsAChangeToFuturePendingWork()
    {
        var current = MakeDurablePlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Tasks = MakeDurablePlan().Tasks.Select(task => task.TaskId == Id(1)
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            Progress = new PlanProgress(1, 3),
        };
        var candidate = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(3), "Better future task", "Better future outcome", "high", [Id(2)]);

        Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
            current, candidate, out var error), Is.True, error);
    }

    [Test]
    public void UnlockedChangeValidation_RejectsDeletingCompletedTask()
    {
        var current = MakeDurablePlan() with
        {
            Tasks = MakeDurablePlan().Tasks.Select(task => task.TaskId == Id(1)
                ? task with { Status = PlanTaskStatus.Complete }
                : task).ToArray(),
            Progress = new PlanProgress(1, 3),
        };

        Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
            current, StagedPlanEditor.DeleteTask(MakeGroup(), Id(1)), out var error), Is.False);
        Assert.That(error, Does.Contain(Id(1)));
    }

    [Test]
    public void UnlockedChangeValidation_RejectsDependencyEditOnFailedTask()
    {
        var current = MakeDurablePlan() with
        {
            Tasks = MakeDurablePlan().Tasks.Select(task => task.TaskId == Id(2)
                ? task with { Status = PlanTaskStatus.Failed }
                : task).ToArray(),
            Progress = new PlanProgress(1, 3),
        };
        var candidate = StagedPlanEditor.UpdateTask(
            MakeGroup(), Id(2), "Second result", "Second", "mid", []);

        Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
            current, candidate, out var error), Is.False);
        Assert.That(error, Does.Contain(Id(2)));
    }

    [Test]
    public void UnlockedChangeValidation_RejectsResolvedGateAndCompletedValidationChanges()
    {
        var group = MakeGroup() with
        {
            ApprovalGates = [new DecomposedGate(
                GroupId + "-GATE-001", "Original gate", [Id(1)], [Id(2)])],
            Validations = [new DecomposedValidationNode(
                GroupId + "-VAL-001", "Original", "Original validation", [Id(1)], [Id(2)], ["Original assertion"])],
        };
        var projected = MakeDurablePlan(group);
        var current = projected with
        {
            ApprovalGates = projected.ApprovalGates.Select(gate => gate with
                { Status = PlanGateStatus.Approved }).ToArray(),
            Validations = projected.Validations!.Select(validation => validation with
                { Status = PlanValidationStatus.Passed }).ToArray(),
        };
        var gateEdit = StagedPlanEditor.UpdateGate(
            group, GroupId + "-GATE-001", "Changed gate", null, [Id(1)], [Id(2)]);
        var validationEdit = StagedPlanEditor.UpdateValidation(
            group, GroupId + "-VAL-001", "Changed", "Changed validation", "evidence",
            [Id(1)], [Id(2)], ["Changed assertion"], [], false);

        Assert.Multiple(() =>
        {
            Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
                current, gateEdit, out var gateError), Is.False);
            Assert.That(gateError, Does.Contain("GATE-001"));
            Assert.That(StagedPlanEditor.TryValidateUnlockedChanges(
                current, validationEdit, out var validationError), Is.False);
            Assert.That(validationError, Does.Contain("VAL-001"));
        });
    }

    [Test]
    public void DeleteTask_AuditValidationBoundaryReconciled_WhenDeletionChangesLeafSet()
    {
        // Diamond: 1 at top, 2 and 3 in the middle, 4 at the bottom.
        // 001 → 002, 001 → 003, 002 → 004.  Leaves: {003, 004}.
        // Audit: afterTaskIds=[003, 004], beforeTaskIds=[].
        // Delete task 002: task 004 is rewired to [001] (task 002's dependencies).
        // New task set: [001, 003, 004] where 004 depends on [001] and 003 depends on [001].
        // New leaves: {003, 004} — unchanged, so audit afterTaskIds stays {003, 004}.
        //
        // Delete task 003 (a leaf): remaining = [001, 002, 004].
        // Task 004 depends on [002]; task 002 depends on [001]. Non-leaves: {001, 002}. Leaves: {004}.
        // The raw RewireBoundary would set afterTaskIds=[001, 004] (replacing 003 with its dep [001]).
        // ReconcileAuditBoundary must correct this to {004} only.
        var group = new DecomposedTaskGroup(
            GroupId, "Audit diamond plan", "feature/audit-diamond", "Test audit reconcile.",
            [
                new DecomposedSubTask(Id(1), "Root result",   [],              "mid", "Root"),
                new DecomposedSubTask(Id(2), "Branch result", [Id(1)],         "mid", "Branch"),
                new DecomposedSubTask(Id(3), "Leaf A result", [Id(1)],         "mid", "Leaf A"),
                new DecomposedSubTask(Id(4), "Leaf B result", [Id(2)],         "mid", "Leaf B"),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001", "Final audit", "Audit all leaves.",
                    [Id(3), Id(4)], [],
                    ["All leaves completed."], Mode: "audit"),
            ]);

        var updated = StagedPlanEditor.DeleteTask(group, Id(3));

        var audit = updated.Validations!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(updated.Tasks.Select(t => t.Id), Does.Not.Contain(Id(3)));
            Assert.That(audit.AfterTaskIds, Is.EquivalentTo(new[] { Id(4) }),
                "Audit AfterTaskIds must be reconciled to the actual leaf set {004}, " +
                "not the raw rewired set {001, 004}");
        });
    }

    [Test]
    public void DeleteTask_AuditValidationOtherFieldsPreservedAfterReconcile()
    {
        // Verify that ReconcileAuditBoundary only changes AfterTaskIds and leaves all
        // other audit validation fields intact.
        var group = new DecomposedTaskGroup(
            GroupId, "Audit preserve plan", "feature/audit-preserve", "Preserve audit fields.",
            [
                new DecomposedSubTask(Id(1), "First result",  [],      "mid", "First"),
                new DecomposedSubTask(Id(2), "Second result", [Id(1)], "mid", "Second"),
                new DecomposedSubTask(Id(3), "Third result",  [Id(1)], "mid", "Third"),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001",
                    "Custom audit title",
                    "Custom audit description.",
                    [Id(2), Id(3)], [],
                    ["Assertion one.", "Assertion two."],
                    Mode: "audit",
                    Commands: ["dotnet test"],
                    RevalidateAtCompletion: false),
            ]);

        // Delete task 002; leaf set becomes {003} (003 still depends on 001, and 001 is non-leaf).
        var updated = StagedPlanEditor.DeleteTask(group, Id(2));

        var audit = updated.Validations!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(audit.ValidationId, Is.EqualTo(GroupId + "-VAL-001"));
            Assert.That(audit.Title, Is.EqualTo("Custom audit title"));
            Assert.That(audit.Description, Is.EqualTo("Custom audit description."));
            Assert.That(audit.Mode, Is.EqualTo("audit"));
            Assert.That(audit.BeforeTaskIds, Is.Empty);
            Assert.That(audit.Assertions, Is.EqualTo(new[] { "Assertion one.", "Assertion two." }));
            Assert.That(audit.Commands, Is.EqualTo(new[] { "dotnet test" }));
            Assert.That(audit.RevalidateAtCompletion, Is.False);
            Assert.That(audit.AfterTaskIds, Is.EquivalentTo(new[] { Id(3) }),
                "AfterTaskIds must be updated to the new leaf set after deletion");
        });
    }

    [Test]
    public void EditHistory_PeekDoesNotMoveTheUndoOrRedoCursor()
    {
        var first = MakeGroup();
        var second = StagedPlanEditor.UpdateTask(first, Id(3), "Changed", "Changed", "mid", [Id(2)]);
        var history = new StagedPlanEditHistory(first);
        history.Record(second);

        Assert.That(history.PeekUndo, Is.EqualTo(first));
        Assert.That(history.Current, Is.EqualTo(second));
        history.Undo();
        Assert.That(history.PeekRedo, Is.EqualTo(second));
        Assert.That(history.Current, Is.EqualTo(first));
    }

    [Test]
    public void EditHistory_UndoRedoRestoresDependencyEditTopology()
    {
        var original = MakeGroup();
        var dependencyEdited = StagedPlanEditor.UpdateTask(
            original, Id(3), "Third result", "Third", "mid", [Id(1)]);
        var history = new StagedPlanEditHistory(original);
        history.Record(dependencyEdited);

        var undone = history.Undo();
        var redone = history.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(undone!.Tasks.Single(task => task.Id == Id(3)).DependsOn,
                Is.EqualTo(new[] { Id(2) }));
            Assert.That(redone!.Tasks.Single(task => task.Id == Id(3)).DependsOn,
                Is.EqualTo(new[] { Id(1) }));
        });
    }

    private static string Id(int number) => $"{GroupId}-{number:000}";

    private static Plan MakeDurablePlan(DecomposedTaskGroup? group = null)
    {
        group ??= MakeGroup();
        var pending = new PendingDecomposePlan(
            PendingDecomposePlanStore.ComputeRevision(group), group, DateTimeOffset.UtcNow);
        return PendingDecomposePlanAdapter.ToPlan(pending, pending.CreatedAt!.Value);
    }

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
