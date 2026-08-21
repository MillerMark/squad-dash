namespace SquadDash.Tests;

using System.Text.Json;

[TestFixture]
internal sealed class PlanTopologyNormalizerTests
{
    private static DecomposedSubTask Task(string id, params string[] dependsOn) =>
        new(id, id, dependsOn, "mid", id);

    private static DecomposedTaskGroup Group(
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyList<DecomposedGate>? gates = null) =>
        new("PLAN", "Plan", "main", "Summary", tasks, ApprovalGates: gates);

    [Test]
    public void Normalize_RemovesTransitiveTaskDependency_AndRedundantGateTarget()
    {
        var group = Group(
            [
                Task("T1"),
                Task("T2", "T1"),
                Task("T3", "T1"),
                Task("T4", "T1", "T2", "T3"),
                Task("T5", "T2", "T3"),
            ],
            [new DecomposedGate("G1", "Review", ["T1"], ["T2", "T3", "T4"], Question: "Approved?")]);

        var ok = PlanTopologyNormalizer.TryNormalize(group, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, error);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Group.Tasks.Single(task => task.Id == "T4").DependsOn,
                Is.EqualTo(new[] { "T2", "T3" }));
            Assert.That(result.Group.Tasks.Single(task => task.Id == "T5").DependsOn,
                Is.EqualTo(new[] { "T2", "T3" }));
            Assert.That(result.Group.ApprovalGates!.Single().BeforeTaskIds,
                Is.EqualTo(new[] { "T2", "T3" }));
            Assert.That(result.RemovedDependencyCount, Is.EqualTo(1));
            Assert.That(result.RemovedGateEndpointCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        var group = Group([Task("A"), Task("B", "A"), Task("C", "A", "B")]);
        Assert.That(PlanTopologyNormalizer.TryNormalize(group, out var first, out _), Is.True);
        Assert.That(PlanTopologyNormalizer.TryNormalize(first!.Group, out var second, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(first.HasChanges, Is.True);
            Assert.That(second!.HasChanges, Is.False);
            Assert.That(JsonSerializer.Serialize(second.Group),
                Is.EqualTo(JsonSerializer.Serialize(first.Group)));
        });
    }

    [Test]
    public void Normalize_PreservesDiamondDependencies()
    {
        var group = Group(
            [Task("A"), Task("B", "A"), Task("C", "A"), Task("D", "B", "C")]);

        Assert.That(PlanTopologyNormalizer.TryNormalize(group, out var result, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(result!.HasChanges, Is.False);
            Assert.That(result.Group.Tasks.Single(task => task.Id == "D").DependsOn,
                Is.EqualTo(new[] { "B", "C" }));
        });
    }

    [Test]
    public void Normalize_RejectsUnknownTaskId_ForAiRepair()
    {
        var group = Group([Task("A"), Task("B", "NOT-A-TASK")]);

        Assert.That(PlanTopologyNormalizer.TryNormalize(group, out var result, out var error), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(error, Does.Contain("unknown task ID 'NOT-A-TASK'"));
            Assert.That(error, Does.Contain("planning AI must correct"));
        });
    }

    [Test]
    public void Normalize_RejectsCycle()
    {
        var group = Group([Task("A", "B"), Task("B", "A")]);

        Assert.That(PlanTopologyNormalizer.TryNormalize(group, out var result, out var error), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(error, Does.Contain("cycle").IgnoreCase);
        });
    }

    [Test]
    public void TaskSpecificEntryGate_IsNotEquivalentToSharedAllBoundary()
    {
        var tasks = new[]
        {
            new PlanTask("T2", "T2", "", [], "mid", PlanTaskStatus.Pending),
            new PlanTask("T3", "T3", "", [], "mid", PlanTaskStatus.Pending),
            new PlanTask("T4", "T4", "", ["T2", "T3"], "mid", PlanTaskStatus.Pending),
            new PlanTask("T5", "T5", "", ["T2", "T3"], "mid", PlanTaskStatus.Pending),
        };

        Assert.That(PlanGateVisualizationPolicy.GraphEquivalent(
            tasks,
            ["T2", "T3"], ["T4"],
            ["T2", "T3"], ["T4", "T5"]), Is.False);
    }
}
