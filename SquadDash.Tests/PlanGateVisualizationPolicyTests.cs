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
        PlanTask[] tasks = [Task("A"), Task("B"), Task("C", "A", "B")];
        var sourceGate = new PlanApprovalGate("G", "Review", ["A"], ["C"], PlanGateStatus.Pending);

        Assert.That(PlanGateVisualizationPolicy.CompletelyCovers(
            tasks, sourceGate, ["A", "B"], ["C"]), Is.False);
    }

    [Test]
    public void CompletelyCovers_AllJoinInsideStageMilestone()
    {
        PlanTask[] tasks = [Task("A"), Task("B"), Task("C", "A", "B"), Task("D", "A", "B")];
        var milestone = new PlanApprovalGate(
            "G", "Review stage", ["A", "B"], ["C", "D"], PlanGateStatus.Pending,
            PresentationAnchor: "stage:3");

        Assert.That(PlanGateVisualizationPolicy.CompletelyCovers(
            tasks, milestone, ["A", "B"], ["D"]), Is.True);
    }

    [Test]
    public void CompletelyCovers_TaskExitWithLongEdgeThatRejoinsDownstream()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("B", "A"), Task("C", "A"),
            Task("JOIN", "B", "C"), Task("FINAL", "A", "JOIN"),
        ];
        var stageMilestone = new PlanApprovalGate(
            "G", "Review stage", ["A"], ["B", "C"], PlanGateStatus.Pending,
            PresentationAnchor: "stage:1");

        Assert.That(PlanGateVisualizationPolicy.CompletelyCovers(
            tasks, stageMilestone, ["A"], ["B", "C", "FINAL"]), Is.True);
    }

    [Test]
    public void CompletelyCovers_AllAndEntryWhenExtraPrerequisiteIsTransitivelyImplied()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("B", "A"), Task("C", "A"),
            Task("LEFT", "B", "C"), Task("RIGHT", "B", "C"),
            Task("FINAL", "A", "LEFT", "RIGHT"),
        ];
        var stageMilestone = new PlanApprovalGate(
            "G", "Review stage", ["LEFT", "RIGHT"], ["FINAL"], PlanGateStatus.Pending,
            PresentationAnchor: "stage:3");

        Assert.That(PlanGateVisualizationPolicy.CompletelyCovers(
            tasks, stageMilestone, ["A", "LEFT", "RIGHT"], ["FINAL"]), Is.True);
    }

    [Test]
    public void GraphEquivalent_RemovesTransitivelyImpliedAllPrerequisite()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("B", "A"), Task("C", "A"),
            Task("LEFT", "B", "C"), Task("RIGHT", "B", "C"),
            Task("FINAL", "A", "LEFT", "RIGHT"),
        ];

        Assert.That(PlanGateVisualizationPolicy.GraphEquivalent(
            tasks,
            ["LEFT", "RIGHT"], ["FINAL"],
            ["A", "LEFT", "RIGHT"], ["FINAL"]), Is.True);
    }

    [Test]
    public void GraphEquivalent_RejectsIndependentAdditionalAllPrerequisite()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("INDEPENDENT"), Task("LEFT", "A"),
            Task("RIGHT", "A"), Task("FINAL", "INDEPENDENT", "LEFT", "RIGHT"),
        ];

        Assert.That(PlanGateVisualizationPolicy.GraphEquivalent(
            tasks,
            ["LEFT", "RIGHT"], ["FINAL"],
            ["INDEPENDENT", "LEFT", "RIGHT"], ["FINAL"]), Is.False);
    }

    [Test]
    public void GraphEquivalent_RemovesLongEdgeTargetAlreadyDownstreamOfMilestone()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("B", "A"), Task("C", "A"),
            Task("JOIN", "B", "C"), Task("FINAL", "A", "JOIN"),
        ];

        Assert.That(PlanGateVisualizationPolicy.GraphEquivalent(
            tasks,
            ["A"], ["B", "C"],
            ["A"], ["B", "C", "FINAL"]), Is.True);
    }

    [Test]
    public void DashedEdges_OrPropagatesWhenAnyIncomingPathIsDashed()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("B"), Task("X", "A"),
            Task("JOIN", "X", "B"), Task("NEXT", "JOIN"),
        ];
        PlanApprovalGate[] gates =
        [new("G", "Review", ["A"], ["X"], PlanGateStatus.Pending)];

        var result = PlanGateVisualizationPolicy.DashedEdges(
            tasks, gates, requireEveryIncomingAtConvergence: false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(("X", "JOIN")));
            Assert.That(result, Does.Not.Contain(("B", "JOIN")));
            Assert.That(result, Does.Contain(("JOIN", "NEXT")));
        });
    }

    [Test]
    public void DashedEdges_AndStopsWhenOnlySomeIncomingPathsAreDashed()
    {
        PlanTask[] tasks =
        [
            Task("A"), Task("B"), Task("X", "A"),
            Task("JOIN", "X", "B"), Task("NEXT", "JOIN"),
        ];
        PlanApprovalGate[] gates =
        [new("G", "Review", ["A"], ["X"], PlanGateStatus.Pending)];

        var result = PlanGateVisualizationPolicy.DashedEdges(
            tasks, gates, requireEveryIncomingAtConvergence: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(("X", "JOIN")));
            Assert.That(result, Does.Not.Contain(("B", "JOIN")));
            Assert.That(result, Does.Not.Contain(("JOIN", "NEXT")));
        });
    }

    [Test]
    public void TaskExitIsCollectivelyCovered_WhenEveryExitEntersAnEnabledAllJoin()
    {
        PlanTask[] tasks =
        [
            Task("SOURCE"), Task("LEFT_PEER"), Task("RIGHT_PEER"),
            Task("LEFT", "SOURCE", "LEFT_PEER"),
            Task("RIGHT", "SOURCE", "RIGHT_PEER"),
        ];
        PlanApprovalGate[] enabledAllJoins =
        [
            new("LEFT-GATE", "Review left", ["SOURCE", "LEFT_PEER"], ["LEFT"], PlanGateStatus.Pending),
            new("RIGHT-GATE", "Review right", ["SOURCE", "RIGHT_PEER"], ["RIGHT"], PlanGateStatus.Pending),
        ];

        Assert.That(PlanGateVisualizationPolicy.TaskExitIsCollectivelyCovered(
            tasks, "SOURCE", enabledAllJoins), Is.True);
    }

    [Test]
    public void TaskExitIsNotCollectivelyCovered_WhenAnyExitBypassesEnabledAllJoins()
    {
        PlanTask[] tasks =
        [
            Task("SOURCE"), Task("PEER"),
            Task("COVERED", "SOURCE", "PEER"), Task("UNCOVERED", "SOURCE"),
        ];
        PlanApprovalGate[] enabledAllJoins =
        [new("GATE", "Review", ["SOURCE", "PEER"], ["COVERED"], PlanGateStatus.Pending)];

        Assert.That(PlanGateVisualizationPolicy.TaskExitIsCollectivelyCovered(
            tasks, "SOURCE", enabledAllJoins), Is.False);
    }

    [Test]
    public void BoundaryIsCollectivelyCovered_WhenEveryIncomingBranchHasItsOwnGate()
    {
        PlanApprovalGate[] taskExitGates =
        [
            new("A-GATE", "Review A", ["A"], ["TARGET"], PlanGateStatus.Pending),
            new("B-GATE", "Review B", ["B"], ["TARGET"], PlanGateStatus.Pending),
        ];

        Assert.That(PlanGateVisualizationPolicy.BoundaryIsCollectivelyCoveredByIncomingGates(
            ["A", "B"], ["TARGET"], taskExitGates), Is.True);
    }

    [Test]
    public void BoundaryIsNotCollectivelyCovered_WhenOneIncomingBranchHasNoGate()
    {
        PlanApprovalGate[] taskExitGates =
        [new("A-GATE", "Review A", ["A"], ["TARGET"], PlanGateStatus.Pending)];

        Assert.That(PlanGateVisualizationPolicy.BoundaryIsCollectivelyCoveredByIncomingGates(
            ["A", "B"], ["TARGET"], taskExitGates), Is.False);
    }
}
