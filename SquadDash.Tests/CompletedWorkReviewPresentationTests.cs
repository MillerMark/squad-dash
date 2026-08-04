namespace SquadDash.Tests;

[TestFixture]
internal sealed class CompletedWorkReviewPresentationTests
{
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

        Assert.That(review.TestSummary, Does.Contain("Tests passed"));
        Assert.That(review.TestSummary, Does.Contain("14 tests passed"));
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

        Assert.That(review.TestSummary, Does.Contain("Tests failed"));
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
    public void Build_AcceptanceEffectMentionsTaskTitle()
    {
        var plan = MakePlan(MakeEvidence());
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.AcceptanceEffect, Does.Contain("Blocked task"));
        Assert.That(review.AcceptanceEffect, Does.Contain("complete"));
    }

    [Test]
    public void Build_AcceptanceEffectMentionsDownstreamTasksWhenPresent()
    {
        var plan = MakePlanWithDependency();
        var review = CompletedWorkReviewPresentationBuilder.Build(plan, TaskId)!;

        Assert.That(review.AcceptanceEffect, Does.Contain("unblocks"));
        Assert.That(review.AcceptanceEffect, Does.Contain("Dependent task"));
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
    public void BuildRecoveryMessage_WithEvidence_IncludesCombinedReviewAndAcceptAction()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);

        Assert.That(message.Actions.Select(a => a.Label),
            Does.Contain("Review & Accept Completed Work"));
    }

    [Test]
    public void BuildRecoveryMessage_WithoutEvidence_OmitsReviewAction()
    {
        var pending = MakePending();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow);

        Assert.That(message.Actions.Select(a => a.Label),
            Has.No.Member("Review & Accept Completed Work"));
    }

    [Test]
    public void BuildRecoveryMessage_WithoutEvidence_StillHasAssessAndReplan()
    {
        var pending = MakePending();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow);

        Assert.That(message.Actions, Has.Count.EqualTo(2));
        Assert.That(message.Actions.Select(a => a.Label),
            Is.EqualTo(new[] { "Assess & Continue", "Replan Remaining Work" }));
    }

    [Test]
    public void BuildRecoveryMessage_WithEvidence_HasThreeActions()
    {
        var pending = MakePending();
        var evidence = MakeEvidence();
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            pending, TaskId, "Worker stopped.", DateTimeOffset.UtcNow, evidence);

        Assert.That(message.Actions, Has.Count.EqualTo(3));
    }

    [Test]
    public void BuildRecoveryMessage_WithEvidence_UsesCompactReasonFirstPresentation()
    {
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            MakePending(), TaskId, "Worker stopped.", DateTimeOffset.UtcNow, MakeEvidence());

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Does.StartWith(
                "**Plan execution stopped unexpectedly after producing committed work. Recovery is available.**"));
            Assert.That(message.Body, Does.Contain("**Why it stopped:** **Worker stopped.**"));
            Assert.That(message.Body, Does.Contain("Commits: `abc1234`"));
            Assert.That(message.Body, Does.Contain("Tests: 14 tests passed."));
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
    public void Reconcile_CurrentInterrupted_WithEvidence_PreservesAllActions()
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
            Assert.That(result.Message.Actions, Has.Count.EqualTo(3));
            Assert.That(result.Message.Actions.Select(a => a.Label),
                Does.Contain("Review & Accept Completed Work"));
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
        new DecomposeStepVerification("passed", "dotnet test", "14 tests passed"));

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
