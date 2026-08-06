namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryPresentationTests
{
    [Test]
    public void BuildStatusMessage_WithCommitEvidence_LeadsWithCommittedWorkState()
    {
        Assert.That(
            PlanRecoveryPresentationBuilder.BuildStatusMessage(hasCommittedWork: true),
            Is.EqualTo("Plan execution stopped unexpectedly after producing committed work. Recovery is available."));
    }

    [Test]
    public void SummarizeReason_MissingScrutinyEnvelope_ProducesConcisePrimaryReason()
    {
        var reason =
            "Independent scrutiny requires human review. Scrutiny summary: Independent scrutiny could not produce a trustworthy structured verdict after one envelope repair. " +
            "Missing or overstated work: - Missing scrutiny result. Test assessment: Test adequacy could not be independently classified.";

        Assert.That(
            PlanRecoveryPresentationBuilder.SummarizeReason(reason),
            Is.EqualTo("Independent scrutiny did not return the required structured result after two attempts. " +
                       "Test adequacy could not be independently classified."));
    }

    [Test]
    public void SummarizeReason_LegacyAdditionalGenericWorker_ExplainsCorrectedPolicy()
    {
        var summary = PlanRecoveryPresentationBuilder.SummarizeReason(
            "Task PLAN-001 launched more than one generic primary worker.");

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("earlier SquadDash build"));
            Assert.That(summary, Does.Contain("preserved"));
            Assert.That(summary, Does.Contain("advisory rather than fatal"));
        });
    }

    [Test]
    public void BuildCompactTestSummary_OmitsCommandLine()
    {
        var verification = new DecomposeStepVerification(
            "passed",
            "dotnet test SquadDash.Tests --filter FullyQualifiedName~SimulationSessionManager",
            "11 tests passed");

        Assert.That(
            PlanRecoveryPresentationBuilder.BuildCompactTestSummary(verification),
            Is.EqualTo("Focused tests: 11 tests passed."));
    }

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
            Assert.That(presentation.Heading, Is.EqualTo("SquadDash could not confirm whether this task finished."));
            Assert.That(presentation.Explanation, Does.Contain("may include unrelated work"));
        });
    }

    [Test]
    public void Build_WithAmendmentEvidence_RecommendsOneCombinedReviewAndApproval()
    {
        var evidence = new PlanTaskCommitEvidence(
            "PLAN-001", "attempt-1", "baseline123", "commit456", "Amended.", null);
        var plan = TestPlan(evidence) with
        {
            Tasks =
            [
                new PlanTask("PLAN-001", "Amendment", "Amendment", [], "high",
                    PlanTaskStatus.Executing, AmendmentGateId: "PLAN-GATE-001"),
            ],
        };

        var presentation = PlanRecoveryPresentationBuilder.Build(
            plan, "PLAN-001", hasPreservedWork: false);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Heading, Does.StartWith("Amendment"));
            Assert.That(presentation.Recommendation, Does.Contain("accepting it also approves"));
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
