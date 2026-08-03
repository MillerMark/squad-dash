namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryPresentationTests
{
    [Test]
    public void Build_WithTaskScopedCommitEvidence_RecommendsReviewAndWarnsOnRetry()
    {
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-001",
            "attempt-1",
            "baseline123",
            "commit456",
            "Implemented durable replay.",
            new DecomposeStepVerification("passed", "dotnet test", "14 tests passed"));
        var plan = TestPlan(evidence);

        var presentation = PlanRecoveryPresentationBuilder.Build(
            plan, "PLAN-001", hasPreservedWork: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.CommitEvidence, Is.SameAs(evidence));
            Assert.That(presentation.Heading, Does.Contain("committed work"));
            Assert.That(presentation.RetryLabel, Is.EqualTo("Retry Task Anyway…"));
            Assert.That(presentation.RetryIsWarning, Is.True);
        });
    }

    [Test]
    public void Build_WithEvidenceForDifferentTask_DoesNotMisattributeCommit()
    {
        var plan = TestPlan(new PlanTaskCommitEvidence(
            "PLAN-OTHER", null, "baseline123", "commit456", "Other task", null));

        var presentation = PlanRecoveryPresentationBuilder.Build(
            plan, "PLAN-001", hasPreservedWork: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.CommitEvidence, Is.Null);
            Assert.That(presentation.Heading, Is.EqualTo("Task ownership needs review."));
            Assert.That(presentation.Explanation, Does.Contain("not capture definitive"));
        });
    }

    [Test]
    public void Build_WithPreservedFiles_RecommendsContinuationWithoutWarning()
    {
        var presentation = PlanRecoveryPresentationBuilder.Build(
            TestPlan(null), "PLAN-001", hasPreservedWork: true);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.RetryLabel, Is.EqualTo("Continue Preserved Work"));
            Assert.That(presentation.RetryIsWarning, Is.False);
        });
    }

    private static Plan TestPlan(PlanTaskCommitEvidence? evidence) => new(
        PlanId: "PLAN",
        Revision: "revision",
        Source: PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Interrupted,
        Title: "Plan",
        Branch: "feature/plan",
        Summary: "Summary",
        Tasks: [],
        ApprovalGates: [],
        Progress: new PlanProgress(0, 1, null),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow),
        InterruptionData: new PlanInterruptionData(
            "Stopped", PlanRecoveryState.PendingRecovery, 1,
            InterruptedTaskId: "PLAN-001",
            TaskCommitEvidence: evidence));
}
