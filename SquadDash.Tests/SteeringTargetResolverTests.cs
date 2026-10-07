namespace SquadDash.Tests;

[TestFixture]
internal sealed class SteeringTargetResolverTests {
    private static readonly DateTimeOffset Baseline =
        new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void SelectDefault_QuietRunningOwnerWithSession_RemainsSelectable() {
        var owner = Candidate(
            id: "owner",
            sessionId: "owner-session",
            directLastActivityAt: null,
            isWaitingOnChild: true);

        var selected = SteeringTargetResolver.SelectDefault([owner]);

        Assert.Multiple(() => {
            Assert.That(owner.CanSteer, Is.True);
            Assert.That(selected, Is.SameAs(owner));
        });
    }

    [Test]
    public void SelectDefault_WaitingParentAndActiveChild_SelectsChild() {
        var parent = Candidate(
            id: "parent",
            sessionId: "parent-session",
            stableOrder: 0,
            directLastActivityAt: Baseline.AddMinutes(1),
            activeToolCount: 1,
            isWaitingOnChild: true);
        var child = Candidate(
            id: "child",
            sessionId: "child-session",
            parentId: parent.Id,
            stableOrder: 1,
            directLastActivityAt: Baseline,
            isWaitingOnChild: false);

        var selected = SteeringTargetResolver.SelectDefault([parent, child]);

        Assert.That(selected, Is.SameAs(child));
    }

    [Test]
    public void SelectDefault_DirectlyActiveSiblings_SelectsNewestActivity() {
        var older = Candidate(
            id: "older",
            sessionId: "older-session",
            stableOrder: 0,
            directLastActivityAt: Baseline);
        var newer = Candidate(
            id: "newer",
            sessionId: "newer-session",
            stableOrder: 1,
            directLastActivityAt: Baseline.AddSeconds(1));

        var selected = SteeringTargetResolver.SelectDefault([older, newer]);

        Assert.That(selected, Is.SameAs(newer));
    }

    [Test]
    public void OrderForDisplay_OrdersEachParentBeforeItsChildren() {
        var coordinator = Candidate(id: "coordinator", stableOrder: 30);
        var namedAgent = Candidate(
            id: "named",
            parentId: coordinator.Id,
            stableOrder: 20);
        var temporaryAgent = Candidate(
            id: "temporary",
            parentId: namedAgent.Id,
            stableOrder: 10);
        var sibling = Candidate(
            id: "sibling",
            parentId: coordinator.Id,
            stableOrder: 40);

        var ordered = SteeringTargetResolver.OrderForDisplay(
            [temporaryAgent, sibling, namedAgent, coordinator]);

        Assert.That(
            ordered.Select(candidate => candidate.Id),
            Is.EqualTo(new[] { "coordinator", "named", "temporary", "sibling" }));
    }

    [Test]
    public void SelectDefault_EquivalentCandidates_UsesStableOrderDeterministically() {
        var later = Candidate(
            id: "later",
            sessionId: "later-session",
            stableOrder: 20,
            directLastActivityAt: Baseline);
        var earlier = Candidate(
            id: "earlier",
            sessionId: "earlier-session",
            stableOrder: 10,
            directLastActivityAt: Baseline);

        var selectedInForwardOrder = SteeringTargetResolver.SelectDefault([later, earlier]);
        var selectedInReverseOrder = SteeringTargetResolver.SelectDefault([earlier, later]);

        Assert.Multiple(() => {
            Assert.That(selectedInForwardOrder, Is.SameAs(earlier));
            Assert.That(selectedInReverseOrder, Is.SameAs(earlier));
        });
    }

    [Test]
    public void HierarchyResolution_OrphanAndCycle_TerminatesAndReturnsEveryCandidate() {
        var orphan = Candidate(id: "orphan", parentId: "missing", stableOrder: 0);
        var cycleA = Candidate(id: "cycle-a", parentId: "cycle-b", stableOrder: 1);
        var cycleB = Candidate(id: "cycle-b", parentId: "cycle-a", stableOrder: 2);

        var ordered = SteeringTargetResolver.OrderForDisplay([cycleB, orphan, cycleA]);
        var cycleDepth = SteeringTargetResolver.ResolveDepth(
            cycleA,
            [cycleA, cycleB, orphan]);

        Assert.Multiple(() => {
            Assert.That(
                ordered.Select(candidate => candidate.Id),
                Is.EqualTo(new[] { "orphan", "cycle-a", "cycle-b" }));
            Assert.That(ordered.Select(candidate => candidate.Id).Distinct().ToArray(), Has.Length.EqualTo(3));
            Assert.That(cycleDepth, Is.EqualTo(1));
        });
    }

    [Test]
    public void SelectDefault_NonrunningSession_IsExcluded() {
        var stopped = Candidate(
            id: "stopped",
            sessionId: "stopped-session",
            stableOrder: 0,
            isRunning: false,
            directLastActivityAt: Baseline.AddMinutes(1));
        var running = Candidate(
            id: "running",
            sessionId: "running-session",
            stableOrder: 1,
            isRunning: true,
            directLastActivityAt: Baseline);

        var selected = SteeringTargetResolver.SelectDefault([stopped, running]);

        Assert.Multiple(() => {
            Assert.That(stopped.CanSteer, Is.False);
            Assert.That(selected, Is.SameAs(running));
        });
    }

    private static SteeringTargetCandidate Candidate(
        string id,
        string? sessionId = null,
        string? parentId = null,
        int stableOrder = 0,
        bool isRunning = true,
        DateTimeOffset? directLastActivityAt = null,
        int activeToolCount = 0,
        bool isWaitingOnChild = false) =>
        new(
            Id: id,
            DisplayName: id,
            SessionId: sessionId,
            ParentId: parentId,
            StableOrder: stableOrder,
            IsRunning: isRunning,
            DirectLastActivityAt: directLastActivityAt,
            ActiveToolCount: activeToolCount,
            IsWaitingOnChild: isWaitingOnChild);
}
