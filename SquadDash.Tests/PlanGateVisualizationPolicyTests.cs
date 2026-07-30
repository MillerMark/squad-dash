namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanGateVisualizationPolicyTests
{
    private static PlanTask Task(string id, params string[] dependsOn) =>
        new(id, id, id, dependsOn, "mid", PlanTaskStatus.Pending);

    [Test]
    public void DownstreamTaskIds_PropagatesThroughEveryFollowingTask()
    {
        PlanTask[] tasks = [Task("A"), Task("B", "A"), Task("C", "B"), Task("D", "C")];
        PlanApprovalGate[] gates = [new("G", "Review", ["A"], ["B"], PlanGateStatus.Pending)];

        Assert.That(PlanGateVisualizationPolicy.DownstreamTaskIds(tasks, gates),
            Is.EquivalentTo(new[] { "B", "C", "D" }));
    }

    [Test]
    public void CompletelyCovers_RejectsEntryWithAnAdditionalIncomingDependency()
    {
        var sourceGate = new PlanApprovalGate("G", "Review", ["A"], ["C"], PlanGateStatus.Pending);

        Assert.That(PlanGateVisualizationPolicy.CompletelyCovers(sourceGate, ["A", "B"], ["C"]), Is.False);
    }

    [Test]
    public void CompletelyCovers_AllJoinInsideStageMilestone()
    {
        var milestone = new PlanApprovalGate(
            "G", "Review stage", ["A", "B"], ["C", "D"], PlanGateStatus.Pending,
            PresentationAnchor: "stage:3");

        Assert.That(PlanGateVisualizationPolicy.CompletelyCovers(
            milestone, ["A", "B"], ["D"]), Is.True);
    }
}
