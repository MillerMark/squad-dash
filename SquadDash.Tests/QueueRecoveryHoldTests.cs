using System;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class QueueRecoveryHoldTests
{
    [Test]
    public void Normalize_TrimsIdentityAndConvertsTimestampToUtc()
    {
        var local = new DateTimeOffset(2026, 8, 20, 16, 30, 0, TimeSpan.FromHours(-4));
        var normalized = QueueRecoveryHoldState.Normalize(new QueueRecoveryHoldState(
            " PLAN-1 ", " TASK-5 ", " ATTEMPT-2 ", " provider recovery required ", local));

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Is.Not.Null);
            Assert.That(normalized!.PlanId, Is.EqualTo("PLAN-1"));
            Assert.That(normalized.TaskId, Is.EqualTo("TASK-5"));
            Assert.That(normalized.AttemptId, Is.EqualTo("ATTEMPT-2"));
            Assert.That(normalized.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Matches_IsPlanScopedAndOptionallyTaskScoped()
    {
        var hold = new QueueRecoveryHoldState(
            "PLAN-1", "TASK-5", "ATTEMPT-2", "recovery", DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(hold.Matches("PLAN-1"), Is.True);
            Assert.That(hold.Matches("PLAN-1", "TASK-5"), Is.True);
            Assert.That(hold.Matches("PLAN-1", "TASK-6"), Is.False);
            Assert.That(hold.Matches("PLAN-2", "TASK-5"), Is.False);
        });
    }

    [Test]
    public void WorkspaceStore_RoundTripsRecoveryHoldSeparatelyFromManualPauseSettings()
    {
        using var workspace = new TestWorkspace();
        var workspacePath = workspace.GetPath("repo");
        Directory.CreateDirectory(workspacePath);
        var store = new WorkspaceConversationStore(workspace.GetPath("state"));
        var hold = new QueueRecoveryHoldState(
            "PLAN-1", "TASK-5", "ATTEMPT-2", "provider recovery required", DateTimeOffset.UtcNow);

        store.Save(workspacePath, WorkspaceConversationState.Empty with { QueueRecoveryHold = hold });
        var loaded = store.Load(workspacePath);

        Assert.That(loaded.QueueRecoveryHold, Is.EqualTo(hold with
        {
            CreatedAt = hold.CreatedAt.ToUniversalTime(),
        }));
    }

    [Test]
    public void MainWindow_UsesRecoveryHoldInsteadOfManualPauseForProviderFailures()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var failureMethod = Slice(
            source,
            "private void StopRequiredPlanWorkAfterProviderFailure(",
            "private void HandlePromptFailure(");
        var recoveryPreparation = Slice(
            source,
            "private void PrepareForHostRecoveryAction()",
            "private static string BuildPlanRecoveryAssessmentPrompt(");

        Assert.Multiple(() =>
        {
            Assert.That(failureMethod, Does.Contain("SetQueueRecoveryHoldForRequiredPlan"));
            Assert.That(failureMethod, Does.Not.Contain("SetQueuePaused(true)"));
            Assert.That(recoveryPreparation, Does.Not.Contain("SetQueuePaused(false)"),
                "A plan recovery action must not erase an explicit user pause.");
        });
    }

    [Test]
    public void AssignmentRepairPrompt_RejectsHistoricalFailureCarryoverAndRequiresObservedLaunch()
    {
        var prompt = PlanAssignmentRepairPrompt.Build(
            "PLAN-1",
            "TASK-5",
            "revision-1",
            [new DecomposedAgentAssignment("arjun-sen", "Validate preserved work", true)],
            "routing context",
            "missing current-attempt launch evidence");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("has not attempted the required delegation yet"));
            Assert.That(prompt, Does.Contain("earlier transcript turns"));
            Assert.That(prompt, Does.Contain("validation/adoption mode"));
            Assert.That(prompt, Does.Contain("Never claim an actual primary agent"));
        });
    }

    [Test]
    public void PlanContinuation_RemainsAtQueueTailWhileRepairStaysImmediatelyBeforeIt()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var enqueuePrompt = Slice(
            source,
            "private PromptQueueItem? EnqueuePrompt(",
            "private void EnqueueCurrentPrompt()");
        var ensureContinuation = Slice(
            source,
            "private void EnsurePlanContinuationQueueItem()",
            "private void EnqueueFilteredLoopJob(");
        var enqueueLoopJob = Slice(
            source,
            "private void EnqueueFilteredLoopJob(",
            "private void ShowTranscriptPlanPreflightRecovery(");

        Assert.Multiple(() =>
        {
            Assert.That(enqueuePrompt, Does.Contain("!isPlanRepair"),
                "A repair must not jump ahead of unrelated queued prompts.");
            Assert.That(enqueuePrompt, Does.Contain("PlaceNewQueueItemBeforePlanContinuation"),
                "A repair is placed ahead of the locked continuation only after normal tail enqueue.");
            Assert.That(ensureContinuation, Does.Not.Contain("QueuedLoopJobTag"),
                "Creating a continuation must not pull it ahead of queued work.");
            Assert.That(enqueueLoopJob, Does.Contain(
                "_promptQueue.Reorder(continuation.Id, _promptQueue.Count - 1)"),
                "New work must leave the locked continuation at the final queue position.");
        });
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail($"Could not find {Path.Combine(pathParts)}.");
        return string.Empty;
    }
}
