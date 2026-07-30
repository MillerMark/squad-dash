namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanApprovalSummaryBuilderTests
{
    private static Plan MakePlan(IReadOnlyList<PlanApprovalGate> gates) => new(
        "P", "r", PlanSource.Manual, PlanLifecycleStatus.Staged, "Plan", "branch", "summary",
        [
            new PlanTask("A", "Task A", "A", [], "mid", PlanTaskStatus.Pending),
            new PlanTask("B", "Task B", "B", ["A"], "mid", PlanTaskStatus.Pending),
            new PlanTask("C", "Task C", "C", ["B"], "mid", PlanTaskStatus.Pending),
        ], gates, new PlanProgress(0, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

    [Test]
    public void Build_CompressesCompleteSetOfStageMilestones()
    {
        var plan = MakePlan([
            new("G1", "one", ["A"], ["B"], PlanGateStatus.Pending, PresentationAnchor: "stage:1"),
            new("G2", "two", ["B"], ["C"], PlanGateStatus.Pending, PresentationAnchor: "stage:2"),
        ]);

        var result = PlanApprovalSummaryBuilder.Build(plan, new Dictionary<string, int>{{"A",0},{"B",1},{"C",2}});

        Assert.That(result.BetweenEveryStage, Is.True);
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public void Build_UsesPresentationAnchorToChooseTaskAndAllLanguage()
    {
        var plan = MakePlan([
            new("G1", "one", ["A"], ["B"], PlanGateStatus.Pending, PresentationAnchor: "task-before:B"),
            new("G2", "two", ["B"], ["C"], PlanGateStatus.Pending, PresentationAnchor: "all:C"),
        ]);

        var result = PlanApprovalSummaryBuilder.Build(plan, new Dictionary<string, int>{{"A",0},{"B",1},{"C",2}});

        Assert.That(result.Items.Select(item => item.Kind),
            Is.EqualTo(new[] { ApprovalSummaryKind.TaskBefore, ApprovalSummaryKind.All }));
        Assert.That(result.Items[0].TaskId, Is.EqualTo("B"));
    }
}
