namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryPresentationTests
{
    [Test]
    public void AssessedHumanReview_SuppressesDuplicatedGenericReason()
    {
        var assessment = new PlanRecoveryDecisionEvidence(
            "The evidence needs human review.",
            [new PlanEvidenceCommit("abcdef12", PlanRecoveryCommitRelation.Unknown, "Attribution is uncertain.")],
            DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryPresentationBuilder.ShouldShowGenericReason(assessment), Is.False);
            Assert.That(PlanRecoveryPresentationBuilder.AssessmentStoppedMessage,
                Is.EqualTo("Assessment finished — plan still stopped."));
        });
    }

    [Test]
    public void OrdinaryInterruption_KeepsGenericReason()
    {
        Assert.That(PlanRecoveryPresentationBuilder.ShouldShowGenericReason(null), Is.True);
    }

    [TestCase("5", "Task title", "Step 5")]
    [TestCase(" 5 ", "Task title", "Step 5")]
    [TestCase("Step 5", "Task title", "Step 5")]
    [TestCase(null, "Add profile grid", "Add profile grid")]
    public void FormatStepLabel_AddsStepPrefixToNumericLabels(
        string? displayLabel,
        string fallbackTitle,
        string expected)
    {
        Assert.That(
            PlanRecoveryPresentationBuilder.FormatStepLabel(displayLabel, fallbackTitle),
            Is.EqualTo(expected));
    }

    [Test]
    public void ExplicitStepAcceptance_DoesNotPromptForTheSameDecisionAgain()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlanRecoveryPresentationBuilder.ShouldPromptForCommitReview(true), Is.False);
            Assert.That(PlanRecoveryPresentationBuilder.ShouldPromptForCommitReview(false), Is.True);
        });
    }

    [Test]
    public void ResolveTaskEvidence_UsesRecoveryCommitsForActiveIncompleteStep()
    {
        var task = new PlanTask(
            "PLAN-001", "Active step", "Work", [], "high", PlanTaskStatus.Executing,
            DisplayStepLabel: "5");
        var commits = new[]
        {
            new PlanEvidenceCommit("commit-1", PlanRecoveryCommitRelation.Task, "Feature work."),
            new PlanEvidenceCommit("commit-2", PlanRecoveryCommitRelation.Unknown, "Attribution uncertain."),
        };
        var plan = TestPlan(null) with
        {
            Tasks = [task],
            InterruptionData = TestPlan(null).InterruptionData! with
            {
                RecoveryAssessment = new PlanRecoveryDecisionEvidence(
                    "Needs review.", commits, DateTimeOffset.UtcNow),
            },
        };

        var resolved = PlanRecoveryPresentationBuilder.ResolveTaskEvidence(plan, task);

        Assert.Multiple(() =>
        {
            Assert.That(task.Commit, Is.Null, "The active step must remain incomplete.");
            Assert.That(resolved.Select(commit => commit.Commit),
                Is.EqualTo(new[] { "commit-1", "commit-2" }));
        });
    }

    [Test]
    public void BuildStatusMessage_WithCommitEvidence_LeadsWithCommittedWorkState()
    {
        Assert.That(
            PlanRecoveryPresentationBuilder.BuildStatusMessage(hasCommittedWork: true),
            Is.EqualTo("Plan execution stopped unexpectedly after producing committed work. Recovery is available."));
    }

    [Test]
    public void SummarizeReason_MissingVerificationEnvelope_ProducesConcisePrimaryReason()
    {
        var reason =
            "Independent verification requires human review. Verification summary: Independent verification could not produce a trustworthy structured verdict after one envelope repair. " +
            "Missing or overstated work: - Missing verification result. Test assessment: Test adequacy could not be independently classified.";

        Assert.That(
            PlanRecoveryPresentationBuilder.SummarizeReason(reason),
            Is.EqualTo("Independent verification did not return the required structured result after two attempts. " +
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
    public void BuildHumanReviewCard_PresentsQuestionAndStructuredAiAnalysis()
    {
        var report = new PlanTaskVerificationReport(
            PlanTaskVerificationVerdict.HumanReviewRequired,
            "All code-verifiable claims are supported by the diff and current file state. " +
            "The build passes with 0 errors.",
            [],
            ["Runtime rendering still needs human observation."],
            "The build passes with 0 errors. AppendModelAttributionLine was removed.",
            [],
            "commit-1",
            DateTimeOffset.UtcNow);
        var task = new PlanTask(
            "PLAN-007",
            "Show model profile in completion footer",
            "Show the selected profile.",
            [],
            "high",
            PlanTaskStatus.HumanReviewRequired,
            VerificationHistory: [report],
            DisplayStepLabel: "7");
        var gate = new PlanApprovalGate(
            "GATE-007",
            "Confirm the completion footer.",
            [task.TaskId],
            [],
            PlanGateStatus.AwaitingApproval,
            Question: "Does the completed footer identify the selected model profile?");
        var plan = TestPlan(null) with
        {
            Tasks = [task],
            ApprovalGates = [gate],
        };

        var presentation = PlanRecoveryPresentationBuilder.BuildHumanReviewCard(plan, task.TaskId);

        Assert.Multiple(() =>
        {
            Assert.That(presentation, Is.Not.Null);
            Assert.That(presentation!.Title, Is.EqualTo("Human verification required"));
            Assert.That(presentation.Question,
                Is.EqualTo("Does the completed footer identify the selected model profile?"));
            Assert.That(presentation.AnalysisBullets, Does.Contain(
                "All code-verifiable claims are supported by the diff and current file state."));
            Assert.That(presentation.AnalysisBullets, Does.Contain(
                "Runtime rendering still needs human observation."));
            Assert.That(presentation.AnalysisBullets, Does.Contain(
                "AppendModelAttributionLine was removed."));
            Assert.That(presentation.AnalysisBullets.Count(bullet =>
                bullet.StartsWith("The build passes", StringComparison.Ordinal)), Is.EqualTo(1));
        });
    }

    [Test]
    public void SplitAnalysisSentences_PreservesCodeReferencesWhileSeparatingClaims()
    {
        var bullets = PlanRecoveryPresentationBuilder.SplitAnalysisSentences(
            "MainWindow.xaml.cs contains the handler. ResponseTextBuilder has no remaining attribution write.");

        Assert.That(bullets, Is.EqualTo(new[]
        {
            "MainWindow.xaml.cs contains the handler.",
            "ResponseTextBuilder has no remaining attribution write.",
        }));
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
