namespace SquadDash.Tests;

[TestFixture]
internal sealed class GitBranchTrackingTests
{
    [Test]
    public void SameNamedOriginUpstream_UsesExplicitSafeDestination()
    {
        var tracking = new GitBranchTracking(
            "new-aigen-models",
            "origin/new-aigen-models",
            new GitUpstreamCounts(0, 4));

        Assert.Multiple(() =>
        {
            Assert.That(tracking.HasMismatchedUpstream, Is.False);
            Assert.That(tracking.IndicatorText, Is.EqualTo("origin/new-aigen-models"));
            Assert.That(tracking.PushLabel, Is.EqualTo("Push to origin/new-aigen-models"));
            Assert.That(tracking.PushArguments,
                Is.EqualTo("push origin \"refs/heads/new-aigen-models:refs/heads/new-aigen-models\""));
        });
    }

    [Test]
    public void FeatureBranchTrackingMaster_IsExposedAndRepairedByPush()
    {
        var tracking = new GitBranchTracking(
            "new-aigen-models",
            "origin/master",
            new GitUpstreamCounts(7, 3));

        Assert.Multiple(() =>
        {
            Assert.That(tracking.HasMismatchedUpstream, Is.True);
            Assert.That(tracking.IndicatorText, Is.EqualTo("origin/master"));
            Assert.That(tracking.TrackingDescription,
                Does.Contain("Tracking: origin/master")
                    .And.Contain("Warning: Pushes should go to origin/new-aigen-models."));
            Assert.That(tracking.PullLabel, Is.EqualTo("Pull from origin/master (7 behind)"));
            Assert.That(tracking.PushLabel,
                Is.EqualTo("Push to origin/new-aigen-models and repair tracking"));
            Assert.That(tracking.PushArguments,
                Is.EqualTo("push --set-upstream origin \"refs/heads/new-aigen-models:refs/heads/new-aigen-models\""));
            Assert.That(tracking.RepairConfirmation,
                Does.Contain("currently tracks 'origin/master'")
                    .And.Contain("change its tracking branch"));
        });
    }

    [Test]
    public void MissingUpstream_IsVisibleAndEstablishedByPush()
    {
        var tracking = new GitBranchTracking("feature/models", null, null);

        Assert.Multiple(() =>
        {
            Assert.That(tracking.HasMismatchedUpstream, Is.False);
            Assert.That(tracking.IndicatorText, Is.EqualTo("no upstream"));
            Assert.That(tracking.PushLabel,
                Is.EqualTo("Push to origin/feature/models and set tracking"));
            Assert.That(tracking.PushArguments,
                Is.EqualTo("push --set-upstream origin \"refs/heads/feature/models:refs/heads/feature/models\""));
        });
    }
}
