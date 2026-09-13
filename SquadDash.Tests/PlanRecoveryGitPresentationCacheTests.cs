using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryGitPresentationCacheTests
{
    private const string Workspace = @"C:\Source\Repo";
    private const string OtherWorkspace = @"C:\Source\Other";
    private const string HeadA = "aaaaaaaa";
    private const string HeadB = "bbbbbbbb";

    [Test]
    public void OverlappingSurfaceThenCacheOnlyRefresh_LatestSuccessStillRequestsRendering()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        var previous = Snapshot(HeadA);
        Apply(cache, cache.BeginRefresh(Workspace, true), previous, true);
        cache.MarkRendered();

        var earlier = cache.BeginRefresh(Workspace, true);
        var latest = cache.BeginRefresh(Workspace, false);
        Assert.That(latest, Is.GreaterThan(earlier));
        Reject(cache, earlier, Snapshot(HeadB), previous);

        var current = Snapshot(HeadB);
        Apply(cache, latest, current, true);
        Assert.That(cache.Current, Is.SameAs(current));
        Reject(cache, earlier, Snapshot(HeadA), current);

        // Merely applying a result must not consume the request before the UI renders.
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(HeadB), true);
        cache.MarkRendered();
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot("cccccccc"), false);
    }

    [Test]
    public void RenderedAThenCacheOnlyB_LaterSurfaceRequestForUnchangedBStillRenders()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(HeadA), true);
        cache.MarkRendered();

        var cached = Snapshot(HeadB);
        Apply(cache, cache.BeginRefresh(Workspace, false), cached, false);
        Assert.That(cache.Current, Is.SameAs(cached));

        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(HeadB), true);
        cache.MarkRendered();
        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(HeadB), false);
    }

    [Test]
    public void FailedHead_PreservesPresentationAndPendingIntentUntilDefaultFalseSuccess()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        var previous = Snapshot(HeadA, ancestors: ["ancestor"]);
        Apply(cache, cache.BeginRefresh(Workspace, true), previous, true);
        cache.MarkRendered();

        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(null), false);
        Assert.That(cache.Current, Is.SameAs(previous));
        Assert.That(cache.Current!.IsAncestor("ancestor", HeadA), Is.True);
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(null), false);
        Assert.That(cache.Current, Is.SameAs(previous));

        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(HeadB), true);
        cache.MarkRendered();
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(HeadA), false);
    }

    [Test]
    public void FailedInitialHead_KeepsPendingIntentForFirstSuccessfulSnapshot()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(null), false);
        Assert.That(cache.Current!.Head, Is.Null);
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(HeadA), true);
    }

    [TestCase(OtherWorkspace)]
    [TestCase(null)]
    public void WorkspaceSwitch_DiscardsSnapshotPendingIntentAndRenderedIdentity(string? workspace)
    {
        var cache = new PlanRecoveryGitPresentationCache();
        Assert.That(cache.Current, Is.Null);
        var previous = Snapshot(HeadA, ancestors: ["ancestor"]);
        Apply(cache, cache.BeginRefresh(Workspace, true), previous, true);
        cache.MarkRendered();
        var oldVersion = cache.BeginRefresh(Workspace, true);

        var newVersion = cache.BeginRefresh(workspace, false);
        Assert.That(newVersion, Is.GreaterThan(oldVersion));
        Assert.That(cache.Current, Is.Null);
        Reject(cache, oldVersion, previous, null);
        Reject(cache, newVersion, previous, null);

        var failed = Snapshot(null, workspace);
        Apply(cache, newVersion, failed, false);
        Assert.That(cache.Current, Is.SameAs(failed), "Old workspace evidence must not survive a failed query.");

        var current = Snapshot(HeadA, workspace, ["ancestor"]);
        Apply(cache, cache.BeginRefresh(workspace, false), current, false);
        Reject(cache, oldVersion, previous, current);
        Apply(cache, cache.BeginRefresh(workspace, true),
            Snapshot(HeadA, workspace, ["ancestor"]), true);
    }

    [Test]
    public void WrongWorkspaceWithLatestVersion_CannotConsumePendingIntentOrReplaceCurrent()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        var previous = Snapshot(HeadA);
        Apply(cache, cache.BeginRefresh(Workspace, true), previous, true);
        cache.MarkRendered();
        var version = cache.BeginRefresh(Workspace, true);

        Reject(cache, version, Snapshot(HeadB, OtherWorkspace), previous);
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(HeadB), true);
    }

    [Test]
    public void SwitchingAwayAndBack_RejectsOldGenerationEvenWithMatchingWorkspace()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        var oldVersion = cache.BeginRefresh(Workspace, true);
        cache.BeginRefresh(OtherWorkspace, false);
        var currentVersion = cache.BeginRefresh(Workspace, false);

        Reject(cache, oldVersion, Snapshot(HeadA), null);
        Apply(cache, currentVersion, Snapshot(HeadB), false);
    }

    [Test]
    public void UnchangedRenderedSnapshot_AvoidsRedundantRenderAndClearsSatisfiedIntent()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        Apply(cache, cache.BeginRefresh(Workspace, true),
            Snapshot(HeadA, ancestors: ["one", "two"]), true);
        cache.MarkRendered();

        Apply(cache, cache.BeginRefresh(Workspace, true),
            Snapshot(HeadA.ToUpperInvariant(), ancestors: ["TWO", "ONE"]), false);
        Apply(cache, cache.BeginRefresh(Workspace, false), Snapshot(HeadB), false);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AncestorOnlyChange_RequestsRendering(bool addingAncestor)
    {
        var cache = new PlanRecoveryGitPresentationCache();
        var before = addingAncestor ? new[] { "one" } : new[] { "one", "two" };
        var after = addingAncestor ? new[] { "one", "two" } : new[] { "one" };
        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(HeadA, ancestors: before), true);
        cache.MarkRendered();

        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(HeadA, ancestors: after), true);
        cache.MarkRendered();
        Apply(cache, cache.BeginRefresh(Workspace, true), Snapshot(HeadA, ancestors: after), false);
    }

    [Test]
    public void WorkspaceCaseChange_PreservesPresentationAndPendingIntent()
    {
        var cache = new PlanRecoveryGitPresentationCache();
        var previous = Snapshot(HeadA);
        Apply(cache, cache.BeginRefresh(Workspace, true), previous, true);
        cache.MarkRendered();
        cache.BeginRefresh(Workspace, true);

        var version = cache.BeginRefresh(Workspace.ToUpperInvariant(), false);
        Assert.That(cache.Current, Is.SameAs(previous));
        Apply(cache, version, Snapshot(HeadB), true);
    }

    [TestCase(Workspace)]
    [TestCase(@"c:\source\repo")]
    public void SelectForPresentation_FailedHeadRetainsValidSameWorkspacePrior(string workspace)
    {
        var previous = Snapshot(HeadA, ancestors: ["ancestor"]);
        var selected = PlanRecoveryGitSnapshot.SelectForPresentation(previous, Snapshot(null, workspace));

        Assert.That(selected, Is.SameAs(previous));
        Assert.That(selected.IsAncestor("ancestor", HeadA), Is.True);
    }

    [TestCase(OtherWorkspace)]
    [TestCase(null)]
    public void SelectForPresentation_CrossWorkspaceFailureCannotLeakPriorEvidence(string? workspace)
    {
        var previous = Snapshot(HeadA, ancestors: ["ancestor"]);
        var candidate = Snapshot(null, workspace);
        var selected = PlanRecoveryGitSnapshot.SelectForPresentation(previous, candidate);

        Assert.That(selected, Is.SameAs(candidate));
        Assert.That(selected.Head, Is.Null);
        Assert.That(selected.IsAncestor("ancestor", HeadA), Is.False);
    }

    [TestCase(Workspace)]
    [TestCase(OtherWorkspace)]
    public void SelectForPresentation_SuccessfulNewSnapshotReplacesPrior(string workspace)
    {
        var previous = Snapshot(HeadA, ancestors: ["old-ancestor"]);
        var candidate = Snapshot(HeadB, workspace, ["new-ancestor"]);
        var selected = PlanRecoveryGitSnapshot.SelectForPresentation(previous, candidate);

        Assert.That(selected, Is.SameAs(candidate));
        Assert.That(selected.IsAncestor("new-ancestor", HeadB), Is.True);
        Assert.That(selected.IsAncestor("old-ancestor", HeadB), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SelectForPresentation_NoValidPriorUsesFailedCandidate(bool missingPrior)
    {
        var previous = missingPrior ? null : Snapshot(null);
        var candidate = Snapshot(null);

        Assert.That(PlanRecoveryGitSnapshot.SelectForPresentation(previous, candidate), Is.SameAs(candidate));
    }

    private static PlanRecoveryGitSnapshot Snapshot(
        string? head, string? workspace = Workspace, string[]? ancestors = null) =>
        new(workspace, head, new HashSet<string>(ancestors ?? [], StringComparer.OrdinalIgnoreCase));

    private static void Apply(
        PlanRecoveryGitPresentationCache cache, int version, PlanRecoveryGitSnapshot candidate, bool shouldRender)
    {
        Assert.That(cache.TryApply(version, candidate, out var refreshSurfaces), Is.True);
        Assert.That(refreshSurfaces, Is.EqualTo(shouldRender));
    }

    private static void Reject(
        PlanRecoveryGitPresentationCache cache, int version, PlanRecoveryGitSnapshot candidate,
        PlanRecoveryGitSnapshot? expectedCurrent)
    {
        Assert.That(cache.TryApply(version, candidate, out var refreshSurfaces), Is.False);
        Assert.That(refreshSurfaces, Is.False);
        Assert.That(cache.Current, Is.SameAs(expectedCurrent));
    }
}
