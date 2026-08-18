using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SquadDash.Tests;

/// <summary>
/// Regression tests for the data-layer fixes landed in commit fe320eb:
/// <list type="bullet">
///   <item><see cref="DependencyEditRecomputer"/> strips orphaned <c>Inputs</c> after a dependency is removed.</item>
///   <item><see cref="StagedPlanEditor.DeleteTask"/> scrubs deleted task outputs from surviving task <c>Inputs</c> and from validation <c>OutputIds</c>.</item>
///   <item><see cref="DecomposedGate.PresentationAnchor"/> round-trips through <see cref="PendingDecomposePlanAdapter"/> and JSON serialization.</item>
/// </list>
/// </summary>
[TestFixture]
internal sealed class DependencyEditInputsRegressionTests
{
    private const string GroupId = "REGINPUTS-20260818";

    // ─── 1. Recompute strips orphaned inputs after dependency removal ──────────

    [Test]
    public void Recompute_RemovingProducerDependency_StripsOrphanedConsumerInputs()
    {
        // Task A produces "output-a-001"; Task B depends on A and consumes that output.
        // After the B→A dependency is removed, B's input becomes orphaned.
        var group = new DecomposedTaskGroup(
            GroupId, "Input strip plan", "main", "summary",
            [
                new DecomposedSubTask(
                    Id(1), "A result", [], "mid", "A",
                    Outputs: [new DecomposedTaskOutput("output-a-001", "A output")]),
                new DecomposedSubTask(
                    Id(2), "B result", [Id(1)], "mid", "B",
                    Inputs: ["output-a-001"]),
            ]);

        // Remove the B→A dependency so "output-a-001" is no longer reachable from B.
        var edited = StagedPlanEditor.UpdateTask(group, Id(2), "B", "B result", "mid", []);
        var result = DependencyEditRecomputer.Recompute(edited);

        var taskB = result.RecomputedGroup!.Tasks.Single(t => t.Id == Id(2));
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(taskB.Inputs, Is.Null.Or.Empty,
                "Orphaned input reference must be stripped when the producing dependency is removed");
        });
    }

    // ─── 2. Recompute preserves non-orphaned inputs when only one dep is removed ─

    [Test]
    public void Recompute_RemovingOneDependency_PreservesInputsFromRemainingProducers()
    {
        // Task A produces "output-a-001"; Task C produces "output-c-001".
        // Task B depends on both A and C and consumes both outputs.
        // After removing the B→A dependency, only A's output is orphaned; C's remains valid.
        var group = new DecomposedTaskGroup(
            GroupId, "Partial strip plan", "main", "summary",
            [
                new DecomposedSubTask(
                    Id(1), "A result", [], "mid", "A",
                    Outputs: [new DecomposedTaskOutput("output-a-001", "A output")]),
                new DecomposedSubTask(
                    Id(2), "C result", [], "mid", "C",
                    Outputs: [new DecomposedTaskOutput("output-c-001", "C output")]),
                new DecomposedSubTask(
                    Id(3), "B result", [Id(1), Id(2)], "mid", "B",
                    Inputs: ["output-a-001", "output-c-001"]),
            ]);

        // Remove B's dependency on A; keep B's dependency on C.
        var edited = StagedPlanEditor.UpdateTask(group, Id(3), "B", "B result", "mid", [Id(2)]);
        var result = DependencyEditRecomputer.Recompute(edited);

        var taskB = result.RecomputedGroup!.Tasks.Single(t => t.Id == Id(3));
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(taskB.Inputs, Does.Contain("output-c-001"),
                "C's output remains reachable and must be preserved");
            Assert.That(taskB.Inputs, Does.Not.Contain("output-a-001"),
                "A's output is no longer reachable and must be stripped");
        });
    }

    // ─── 3. DeleteTask scrubs downstream input references ─────────────────────

    [Test]
    public void DeleteTask_ProducerDeleted_StripsDownstreamInputReferences()
    {
        // Task A produces "output-a-001"; Task B depends on A and consumes that output.
        // After deleting A, B's Inputs list must no longer reference "output-a-001".
        var group = new DecomposedTaskGroup(
            GroupId, "Delete producer plan", "main", "summary",
            [
                new DecomposedSubTask(
                    Id(1), "A result", [], "mid", "A",
                    Outputs: [new DecomposedTaskOutput("output-a-001", "A output")]),
                new DecomposedSubTask(
                    Id(2), "B result", [Id(1)], "mid", "B",
                    Inputs: ["output-a-001"]),
            ]);

        var updated = StagedPlanEditor.DeleteTask(group, Id(1));

        var taskB = updated.Tasks.Single(t => t.Id == Id(2));
        Assert.Multiple(() =>
        {
            Assert.That(taskB.Inputs, Is.Null.Or.Empty,
                "Deleted producer's output must be scrubbed from surviving consumer's Inputs");
            Assert.That(StagedPlanEditor.TryValidate(updated, out _, out var error), Is.True, error);
        });
    }

    // ─── 4. DeleteTask scrubs validation OutputIds ────────────────────────────

    [Test]
    public void DeleteTask_ProducerDeleted_StripsOutputIdsFromValidations()
    {
        // Task A produces "output-a-001"; a validation references that output in OutputIds.
        // After deleting A, the validation's OutputIds must no longer contain "output-a-001".
        var group = new DecomposedTaskGroup(
            GroupId, "Delete validation output plan", "main", "summary",
            [
                new DecomposedSubTask(
                    Id(1), "A result", [], "mid", "A",
                    Outputs: [new DecomposedTaskOutput("output-a-001", "A output")]),
                new DecomposedSubTask(
                    Id(2), "B result", [Id(1)], "mid", "B"),
            ],
            Validations:
            [
                new DecomposedValidationNode(
                    GroupId + "-VAL-001",
                    "Check output",
                    "Validate A's output is present.",
                    [Id(1)],
                    [Id(2)],
                    ["A's output is observable."],
                    OutputIds: ["output-a-001"]),
            ]);

        var updated = StagedPlanEditor.DeleteTask(group, Id(1));

        var validation = updated.Validations!.Single();
        Assert.That(validation.OutputIds, Is.Null.Or.Empty,
            "Deleted task's output ID must be scrubbed from validation OutputIds");
    }

    // ─── 5. PresentationAnchor round-trips through PendingDecomposePlanAdapter ──

    [Test]
    public void PresentationAnchor_RoundTripsThroughPendingDecomposePlanAdapter()
    {
        const string anchor = "task-abc";

        var group = new DecomposedTaskGroup(
            GroupId, "Anchor round-trip plan", "feature/anchor-test", "summary",
            [
                new DecomposedSubTask(GroupId + "-001", "First step", [], "mid", "First"),
                new DecomposedSubTask(GroupId + "-002", "Second step", [GroupId + "-001"], "mid", "Second"),
            ],
            ApprovalGates:
            [
                new DecomposedGate(
                    GroupId + "-GATE-001",
                    "Review the first task.",
                    [GroupId + "-001"],
                    [GroupId + "-002"],
                    Question: "Does the first task meet its stated acceptance criteria?",
                    PresentationAnchor: anchor),
            ]);

        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var pending = new PendingDecomposePlan(revision, group, DateTimeOffset.UtcNow);

        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        var reconstructed = PendingDecomposePlanAdapter.FromPlan(plan);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ApprovalGates[0].PresentationAnchor, Is.EqualTo(anchor),
                "ToPlan must carry PresentationAnchor into the durable gate");
            Assert.That(reconstructed.Group.ApprovalGates![0].PresentationAnchor, Is.EqualTo(anchor),
                "FromPlan must restore PresentationAnchor on the reconstructed DecomposedGate");
        });
    }

    // ─── 6. PresentationAnchor JSON serialization round-trip ─────────────────

    [Test]
    public void PresentationAnchor_SerializesAndDeserializesCorrectly_WhenSet()
    {
        const string anchor = "task-xyz";
        var gate = new DecomposedGate(
            GroupId + "-GATE-001",
            "Review gate",
            Question: "Is the outcome correct?",
            PresentationAnchor: anchor);

        var json = JsonSerializer.Serialize(gate);
        var deserialized = JsonSerializer.Deserialize<DecomposedGate>(json);

        Assert.That(deserialized?.PresentationAnchor, Is.EqualTo(anchor),
            "PresentationAnchor must survive a JSON serialization round-trip");
    }

    [Test]
    public void PresentationAnchor_IsAbsentFromJson_WhenNull()
    {
        var gate = new DecomposedGate(
            GroupId + "-GATE-001",
            "Review gate",
            Question: "Is the outcome correct?",
            PresentationAnchor: null);

        var json = JsonSerializer.Serialize(gate);

        Assert.That(json, Does.Not.Contain("presentationAnchor"),
            "[JsonIgnore(WhenWritingNull)] must suppress the field when PresentationAnchor is null");
    }

    // ─── 7. TryValidate still rejects invalid input references ───────────────

    [Test]
    public void TryValidate_RejectsTask_WithInputFromNonAncestorProducer()
    {
        // Task A produces "output-a-001".
        // Task B does NOT depend on A but illegally references "output-a-001" in its Inputs.
        // TryValidate must return false with a diagnostic that names task B.
        var group = new DecomposedTaskGroup(
            GroupId, "Invalid input plan", "main", "summary",
            [
                new DecomposedSubTask(
                    Id(1), "A result", [], "mid", "A",
                    Outputs: [new DecomposedTaskOutput("output-a-001", "A output")]),
                new DecomposedSubTask(
                    Id(2), "B result", [], "mid", "B",
                    Inputs: ["output-a-001"]),
            ]);

        var valid = StagedPlanEditor.TryValidate(group, out _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.False,
                "A task that claims an input from a non-ancestor producer must fail validation");
            Assert.That(error, Does.Contain(Id(2)).IgnoreCase,
                "Validation error must identify the task with the invalid input reference");
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string Id(int number) => $"{GroupId}-{number:000}";
}
