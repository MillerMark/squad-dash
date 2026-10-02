namespace SquadDash.Tests;

[TestFixture]
internal sealed class CompletedWorkReviewPresentationTests
{
    [Test]
    public void RecoveryReason_FormatsProtocolPayloadNameAsInlineCode()
    {
        var reason = PlanRecoveryPresentationBuilder.SummarizeReason(
            "The response did not contain a valid DECOMPOSE_STEP_RESULT_JSON payload.");

        Assert.That(reason, Is.EqualTo(
            "The response did not contain a valid `DECOMPOSE_STEP_RESULT_JSON` payload."));
    }

    [Test]
    public void ProtocolPayloadFormatting_DoesNotDoubleWrapExistingInlineCode()
    {
        var reason = PlanRecoveryPresentationBuilder.SummarizeReason(
            "The `PLAN_VALIDATION_RESULT_JSON` payload was invalid.");

        Assert.That(reason, Is.EqualTo(
            "The `PLAN_VALIDATION_RESULT_JSON` payload was invalid."));
    }

    private const string PlanId = "REVIEW-001";
    private const string TaskId = "REVIEW-001-002";
    private const string DependentTaskId = "REVIEW-001-003";

    // --- Routing tests ---

    [Test]
    public void Build_WithCommitEvidence_ReturnsReview()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId);

        Assert.That(review, Is.Not.Null);
        Assert.That(review!.TaskId, Is.EqualTo(TaskId));
    }

    [Test]
    public void Build_WithoutCommitEvidence_ReturnsNull()
    {
        var plan = MakePlan(evidence: null);
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId);

        Assert.That(review, Is.Null);
    }

    [Test]
    public void Build_WithEvidenceForDifferentTask_ReturnsNull()
    {
        var evidence = MakeEvidence() with { TaskId = "OTHER-TASK" };
        var plan = MakePlan(evidence);
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId);

        Assert.That(review, Is.Null);
    }

    // --- Presentation tests ---

    [Test]
    public void Build_IncludesCommitShaAndSummary()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.Multiple(() =>
        {
            Assert.That(review.Commit, Is.Not.Null);
            Assert.That(review.Commit!.Sha, Is.EqualTo("abc1234567890"));
            Assert.That(review.Commit.ShortSha, Is.EqualTo("abc1234"));
            Assert.That(review.Commit.Summary, Is.EqualTo("Implemented the feature."));
        });
    }

    [Test]
    public void Build_IncludesTestSummaryFromVerification()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.TestSummary, Is.EqualTo("Tests passed: 14"));
    }

    [Test]
    public void Build_WithFailedVerification_IncludesStatus()
    {
        var evidence = MakeEvidence() with
        {
            Verification = new DecomposeStepVerification("failed", "dotnet test", "3 tests failed")
        };
        var plan = MakePlan(evidence);
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.TestSummary, Is.EqualTo("Verification status: failed"));
    }

    [Test]
    public void Build_WithBuildOnlyVerification_DoesNotClaimTestsOrGeneralVerificationPassed()
    {
        var evidence = MakeEvidence() with
        {
            Verification = new DecomposeStepVerification(
                "passed",
                "dotnet build EqSim.sln -c Release",
                "Build passed and prose also claims tests passed")
        };

        var review = CompletedWorkReviewPresentationBuilder.Build(MakePlan(evidence), TaskId)!;

        Assert.That(review.TestSummary, Is.EqualTo("Build passed"));
    }

    [Test]
    public void Build_WithNoVerification_TestSummaryIsNull()
    {
        var evidence = MakeEvidence() with { Verification = null };
        var plan = MakePlan(evidence);
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.TestSummary, Is.Null);
    }

    [Test]
    public void Build_IncludesChangedFiles()
    {
        var plan = MakePlan(MakeEvidence(), affectedPaths: ["src/App.cs", "tests/AppTests.cs"]);
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.ChangedFiles, Has.Count.EqualTo(2));
        Assert.That(review.ChangedFiles, Does.Contain("src/App.cs"));
    }

    [Test]
    public void Build_WithNoAffectedPaths_ChangedFilesIsEmpty()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.ChangedFiles, Is.Empty);
    }

    [Test]
    public void Build_IncludesDownstreamTasks()
    {
        var plan = MakePlanWithDependency();
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.DownstreamTasks, Has.Count.EqualTo(1));
        Assert.That(review.DownstreamTasks[0], Is.EqualTo("Dependent task"));
    }

    [Test]
    public void Build_AcceptanceEffectStatesRequiredDecisionClearly()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.AcceptanceEffect,
            Is.EqualTo("Verify this step is complete to continue the plan."));
    }

    [Test]
    public void Build_AcceptanceEffectDoesNotExposeDownstreamBookkeeping()
    {
        var plan = MakePlanWithDependency();
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.AcceptanceEffect,
            Is.EqualTo("Verify this step is complete to continue the plan."));
    }

    [Test]
    public void Build_IncludesRetryRiskWarning()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.RetryRiskWarning, Is.Not.Null);
        Assert.That(review.RetryRiskWarning, Does.Contain("already committed"));
    }

    [Test]
    public void Build_IncludesStopReason()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.StopReason, Is.EqualTo("Worker stopped."));
    }

    [Test]
    public void Build_IncludesTaskTitle()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.TaskTitle, Is.EqualTo("Blocked task"));
    }

    [Test]
    public void Build_ShortCommitShaNotTruncated()
    {
        var evidence = MakeEvidence() with { Commit = "abc12" };
        var plan = MakePlan(evidence);
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.Commit!.ShortSha, Is.EqualTo("abc12"));
    }

    // --- Routing: Inbox actions ---

    [Test]
    public void BuildRecoveryMessage_WithEvidence_OffersDirectStepCompletion()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);

        Assert.That(message.Actions.Select(a => a.Label),
            Is.EqualTo(new[]
            {
                "View committed changes (baselin → abc1234)",
                "Continue Working on Step 1",
                "✎ Revise Remaining Plan…",
            }));
    }

    [Test]
    public void BuildRecoveryMessage_WithoutStructuredCategory_DoesNotOfferApprovalOverride()
    {
        var pending = new PendingDecomposePlan(
            "rev-1",
            new DecomposedTaskGroup(
                PlanId,
                "Recovery plan",
                "feature/recovery",
                "Exercise recovery.",
                [
                    new DecomposedSubTask(
                        TaskId, "Amendment task", [], "high",
                        AmendmentGateId: "REVIEW-001-GATE-001"),
                ]));

        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, MakeEvidence());

        Assert.Multiple(() =>
        {
            Assert.That(message.Actions.Select(action => action.Label),
                Has.No.Member("Approve Amendment & Continue"));
            Assert.That(message.Actions.Select(action => action.Label),
                Has.No.Member("Review Completed Work"));
            Assert.That(message.Body, Does.Contain("Unexpected State Encountered"));
        });
    }

    [Test]
    public void BuildRecoveryMessage_WithoutEvidence_UsesNavigationLinkNotLegacyReviewAction()
    {
        var pending = MakePending();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(message.Actions.Select(a => a.Label),
                Has.No.Member("Review Completed Work"));
            Assert.That(message.Actions[0].Label, Is.EqualTo("View workspace changes for Step 1"));
            Assert.That(message.Actions[0].RouteMode,
                Is.EqualTo(DecomposePlanInbox.RecoveryEvidenceRouteMode));
            Assert.That(message.Actions[0].Hint, Does.Contain("Exact task attribution is unavailable"));
        });
    }

    [Test]
    public void BuildRecoveryMessage_WithoutEvidence_OffersEvidenceRetryAndReplan()
    {
        var pending = MakePending();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow);

        Assert.That(message.Actions, Has.Count.EqualTo(3));
        Assert.That(message.Actions.Select(a => a.Label),
            Is.EqualTo(new[]
            {
                "View workspace changes for Step 1",
                "Continue Working on Step 1",
                "✎ Revise Remaining Plan…",
            }));
    }

    [Test]
    public void BuildRecoveryMessage_WithEvidence_HasCompletionCorrectionAndReplanActions()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);

        Assert.That(message.Actions, Has.Count.EqualTo(3));
        Assert.That(message.Actions.Select(action => action.Label),
            Has.No.Member("Assess & Continue"));
    }

    [Test]
    public void BuildRecoveryMessage_WithEvidence_UsesCompactReasonFirstPresentation()
    {
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            MakePending(), TaskId, "Worker stopped.", DateTimeOffset.UtcNow, MakeEvidence());

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Does.StartWith("**Unexpected State Encountered**"));
            Assert.That(message.Body, Does.Contain("What happened: Worker stopped."));
            Assert.That(message.Body, Does.Contain("Plan: **Recovery plan**"));
            Assert.That(message.Body, Does.Contain("Commits: `abc1234`"));
            Assert.That(message.Body, Does.Contain("Tests passed: 14"));
            Assert.That(message.Body, Does.Not.Contain("dotnet test"));
            Assert.That(message.Body, Does.Not.Contain("Implemented the feature"));
            Assert.That(message.Body, Does.Not.Contain("Recorded stop detail"));
        });
    }

    // --- Stale-action tests ---

    [Test]
    public void Reconcile_CompletedPlan_ArchivesReviewActions()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);
        var plan = MakePlan(evidence) with { LifecycleStatus = PlanLifecycleStatus.Completed };
        var result = DecomposeRecoveryInboxReconciler.Reconcile(message, plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsActionable, Is.False);
            Assert.That(result.ShouldArchive, Is.True);
            Assert.That(result.Message.Actions, Is.Empty);
        });
    }

    [Test]
    public void Reconcile_ExecutingPlan_ArchivesReviewActions()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);
        var plan = MakePlan(evidence) with { LifecycleStatus = PlanLifecycleStatus.Executing };
        var result = DecomposeRecoveryInboxReconciler.Reconcile(message, plan);

        Assert.That(result.ShouldArchive, Is.True);
        Assert.That(result.Message.Actions, Is.Empty);
    }

    [Test]
    public void Reconcile_CurrentInterrupted_WithEvidence_UsesDirectCompletionModel()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);
        var plan = MakePlan(evidence);
        var result = DecomposeRecoveryInboxReconciler.Reconcile(message, plan);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsActionable, Is.True);
            Assert.That(result.ShouldArchive, Is.False);
            Assert.That(result.Message.Actions, Has.Count.EqualTo(4));
            Assert.That(result.Message.Actions.Select(a => a.Label),
                Is.EqualTo(new[]
                {
                    "View committed changes (baselin → abc1234)",
                    "Step 1 Is Complete & Continue",
                    "Continue Working on Step 1",
                    "✎ Revise Remaining Plan…",
                }));
        });
    }

    // --- Accessibility tests ---

    [Test]
    public void Build_AllStringFieldsAreNonEmpty()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.Multiple(() =>
        {
            Assert.That(review.StopReason, Is.Not.Empty, "StopReason readable by screen reader");
            Assert.That(review.TaskTitle, Is.Not.Empty, "TaskTitle readable by screen reader");
            Assert.That(review.AcceptanceEffect, Is.Not.Empty, "AcceptanceEffect readable by screen reader");
            Assert.That(review.Commit!.Summary, Is.Not.Empty, "Commit summary readable by screen reader");
        });
    }

    [Test]
    public void Build_UsesPlainUnconfirmedHeadingAndBaselineAwareComparison()
    {
        var review = CompletedWorkReviewPresentationBuilder.Build(MakePlan(MakeEvidence()), TaskId)!;

        Assert.Multiple(() =>
        {
            Assert.That(review.Heading, Is.EqualTo("Blocked task has committed work, but completion is unconfirmed"));
            Assert.That(review.ComparisonLabel, Is.EqualTo("View committed changes (baselin → abc1234)"));
            Assert.That(review.ComparisonHint, Does.Contain("baselin → abc1234"));
        });
    }

    [Test]
    public void BuildCopyText_ExpandsTechnicalDetails()
    {
        var review = CompletedWorkReviewPresentationBuilder.Build(MakePlan(MakeEvidence()), TaskId)!;
        var copied = CompletedWorkReviewPresentationBuilder.BuildCopyText(review);

        Assert.Multiple(() =>
        {
            Assert.That(copied, Does.Contain("Technical details:"));
            Assert.That(copied, Does.Contain("Task ID: " + TaskId));
            Assert.That(copied, Does.Contain("Comparison baseline: baseline000"));
            Assert.That(copied, Does.Contain("Recorded task commit: abc1234567890"));
        });
    }

    [Test]
    public void BuildCopyText_CanOmitHeadingWhenCardAlreadyRenderedIt()
    {
        var review = CompletedWorkReviewPresentationBuilder.Build(MakePlan(MakeEvidence()), TaskId)!;

        var copied = CompletedWorkReviewPresentationBuilder.BuildCopyText(review, includeHeading: false);

        Assert.That(copied, Does.Not.Contain(review.Heading));
    }

    [Test]
    public void Build_WhenStopReasonMatchesOutcome_DoesNotRepeatItInTechnicalDetails()
    {
        var evidence = MakeEvidence() with { Summary = "Worker stopped." };

        var review = CompletedWorkReviewPresentationBuilder.Build(MakePlan(evidence), TaskId)!;

        Assert.Multiple(() =>
        {
            Assert.That(review.Outcome, Is.EqualTo("Worker stopped."));
            Assert.That(review.TechnicalDetails, Does.Not.Contain("Stop reason: Worker stopped."));
            Assert.That(review.TechnicalDetails, Does.Contain("Task ID: " + TaskId));
        });
    }

    [Test]
    public void Build_WhenStopReasonUsesShortCommitAndOutcomeUsesFullCommit_DoesNotRepeatIt()
    {
        const string fullCommit = "abc1234567890123456789012345678901234567";
        var evidence = MakeEvidence() with
        {
            Commit = fullCommit,
            Summary = $"Committed regression coverage in {fullCommit}."
        };
        var plan = MakePlan(evidence) with
        {
            InterruptionData = MakePlan(evidence).InterruptionData! with
            {
                Reason = "Committed regression coverage in abc1234."
            }
        };

        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.TechnicalDetails, Does.Not.Contain("Stop reason:"));
    }

    [Test]
    public void Build_WhenStopReasonDiffersFromOutcome_PreservesItInTechnicalDetails()
    {
        var review = CompletedWorkReviewPresentationBuilder.Build(MakePlan(MakeEvidence()), TaskId)!;

        Assert.That(review.TechnicalDetails, Does.Contain("Stop reason: Worker stopped."));
    }

    [TestCase("Tests/Parser/MyFileName.cs", "MyFileName.cs")]
    [TestCase(@"Tests\Parser\MyFileName.cs", "MyFileName.cs")]
    [TestCase("MyFileName.cs", "MyFileName.cs")]
    public void GetChangedFileDisplayName_ShowsOnlyBasename(string path, string expected)
    {
        Assert.That(
            CompletedWorkReviewPresentationBuilder.GetChangedFileDisplayName(path),
            Is.EqualTo(expected));
    }

    [Test]
    public void BuildRecoveryMessage_ActionHintsAreNonEmpty()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);

        Assert.Multiple(() =>
        {
            foreach (var action in message.Actions)
            {
                Assert.That(action.Label, Is.Not.Empty, $"Action label for tooltip/screen reader");
                Assert.That(action.Hint, Is.Not.Empty, $"Action hint for '{action.Label}' provides context");
            }
        });
    }

    // --- Helpers ---

    private static PlanTaskCommitEvidence MakeEvidence() => new(
        TaskId,
        "attempt-1",
        "baseline000",
        "abc1234567890",
        "Implemented the feature.",
        new DecomposeStepVerification(
            "passed",
            "dotnet test",
            "14 tests passed",
            new DecomposeStepTestResults(14, 14, 0, 0)));

    private static PendingDecomposePlan MakePending() => new(
        "rev-1",
        new DecomposedTaskGroup(
            PlanId,
            "Recovery plan",
            "feature/recovery",
            "Exercise recovery.",
            [new DecomposedSubTask(TaskId, "Blocked task", [], "high")]));

    private static Plan MakePlan(
        PlanTaskCommitEvidence? evidence,
        IReadOnlyList<string>? affectedPaths = null) =>
        new(
            PlanId,
            "rev-1",
            PlanSource.Inbox,
            PlanLifecycleStatus.Interrupted,
            "Recovery plan",
            "feature/recovery",
            "Exercise recovery.",
            [new PlanTask(TaskId, "Blocked task", "Blocked task", [], "high", PlanTaskStatus.Pending)],
            [],
            new PlanProgress(0, 1),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            new PlanInterruptionData(
                "Worker stopped.",
                PlanRecoveryState.PendingRecovery,
                1,
                InterruptedTaskId: TaskId,
                TaskCommitEvidence: evidence,
                AffectedPaths: affectedPaths));

    private static Plan MakePlanWithDependency()
    {
        var evidence = MakeEvidence();
        return new Plan(
            PlanId,
            "rev-1",
            PlanSource.Inbox,
            PlanLifecycleStatus.Interrupted,
            "Recovery plan",
            "feature/recovery",
            "Exercise recovery.",
            [
                new PlanTask(TaskId, "Blocked task", "Blocked task", [], "high", PlanTaskStatus.Pending),
                new PlanTask(DependentTaskId, "Dependent task", "Depends on blocked task",
                    [TaskId], "high", PlanTaskStatus.Pending),
            ],
            [],
            new PlanProgress(0, 2),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            new PlanInterruptionData(
                "Worker stopped.",
                PlanRecoveryState.PendingRecovery,
                1,
                InterruptedTaskId: TaskId,
                TaskCommitEvidence: evidence));
    }
}
