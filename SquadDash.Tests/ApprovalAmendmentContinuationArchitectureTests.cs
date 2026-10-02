using System;
using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalAmendmentContinuationArchitectureTests
{
    [Test]
    public void AmendmentHandler_QueuesContinuationWhileItsPromptOwnsCoordinator()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf(
            "private async Task<bool> ApplyGateAmendmentResponseAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private void SaveDecomposeRecoveryInboxReminder(",
            start,
            StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var handler = source[start..end];
        var busyCheck = handler.IndexOf(
            "if (_isPromptRunning || IsLoopRunning || _queueDrainActive)",
            StringComparison.Ordinal);
        var queueContinuation = handler.IndexOf(
            "ScheduleApprovalResumeWhenIdle(result.UpdatedPlan.PlanId);",
            StringComparison.Ordinal);
        var immediateStart = handler.IndexOf(
            "await StartBackloggedDecomposeGroupAsync(",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(busyCheck, Is.GreaterThanOrEqualTo(0));
            Assert.That(queueContinuation, Is.GreaterThan(busyCheck));
            Assert.That(immediateStart, Is.GreaterThan(queueContinuation),
                "The handler must queue 5A before trying an immediate start.");
        });
    }

    [Test]
    public void PlanViewer_UsesRecoveryPanelResumeForRecoverableAmendment()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.That(source, Does.Contain(
            "durablePlan.LifecycleStatus == PlanLifecycleStatus.Executing &&")
            .And.Contain("!planRunnerActive && onResumePlan is not null"));
        Assert.That(source, Does.Contain("\"Resume Plan\","));
        Assert.That(source, Does.Contain("\"Resume this executing plan because it currently has no active runner.\""));
        Assert.That(source, Does.Contain("!planRunnerActive && onResumePlan is not null"));
        Assert.That(source, Does.Contain("planRunnerActive && onPausePlan is not null"));
        Assert.That(source, Does.Not.Contain("amendmentPending ? \"Resume Amendment\" : \"Resume\""));
        Assert.That(source, Does.Contain("currentResumePlanAction.Label")
            .And.Contain("currentResumePlanAction.Hint")
            .And.Contain("currentResumePlanAction.Tone")
            .And.Not.Contain("Resume Plan Anyway…"),
            "Safe recovery labels and styling must come from the shared action set.");
    }

    [Test]
    public void RecoveryAcceptance_LeavesHumanProofAmendmentGateForExplicitReview()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf(
            "private async Task<bool> AcceptInterruptedStepAfterReviewAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<bool> AcceptAwaitingReviewStepAsync(",
            start,
            StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var handler = source[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain(
                "PlanAmendmentGateAcceptancePolicy.CanCombineWithTaskAcceptance(storedPlan, pendingTask)"));
            Assert.That(handler, Does.Contain("HasReadyPendingApprovalGate(acceptedPlan!)"));
            Assert.That(handler, Does.Contain("ProcessPlanApprovalsAfterStepAsync("));
        });
    }

    [Test]
    public void RecoveryAcceptance_CompletesTerminalPlanBeforeAnyResumeAttempt()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf(
            "private async Task<bool> AcceptInterruptedStepAfterReviewAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<bool> AcceptAwaitingReviewStepAsync(",
            start,
            StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var handler = source[start..end];
        var terminalCheck = handler.IndexOf(
            "PlanExecutionBoundaryPolicy.IsReadyToComplete(updated)",
            StringComparison.Ordinal);
        var completion = handler.IndexOf(
            "PlanStoreUpdater.ApplyCompleted(updated with { InterruptionData = null })",
            StringComparison.Ordinal);
        var resume = handler.LastIndexOf(
            "await StartOrResumeDurablePlanAsync(acceptedPlan)",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(terminalCheck, Is.GreaterThanOrEqualTo(0));
            Assert.That(completion, Is.GreaterThan(terminalCheck));
            Assert.That(resume, Is.GreaterThan(completion));
            Assert.That(handler, Does.Contain(
                "if (acceptedPlan!.LifecycleStatus == PlanLifecycleStatus.Completed)"));
        });
    }

    [Test]
    public void PlanViewer_DoesNotOfferPauseWhileAlreadyWaitingForApproval()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var pauseCondition = source.IndexOf(
            "else if (PlanRecoveryControlPolicy.HasActionableInterruption(durablePlan) &&",
            StringComparison.Ordinal);
        var editHistory = source.IndexOf("if (_editHistory is not null", pauseCondition, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(pauseCondition, Is.GreaterThanOrEqualTo(0));
            Assert.That(editHistory, Is.GreaterThan(pauseCondition));
            Assert.That(source[pauseCondition..editHistory],
                Does.Not.Contain("PlanLifecycleStatus.AwaitingApproval"));
        });
    }

    [Test]
    public void PlanViewer_DoesNotOfferTaskSpecificRecoveryWithoutTaskIdentity()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(
                "onContinueInterruptedTask is not null && !safeResume && interruptedActionTask is not null"));
            Assert.That(source, Does.Contain(
                "onContinueInterruptedTask is not null && interruptedTask is not null"));
            Assert.That(source, Does.Not.Contain("?.Title ?? \"Step\""),
                "A missing durable task identity must not produce a truncated 'Continue Working on Step' action.");
        });
    }

    [Test]
    public void RestartedTerminalRepair_IsReclaimedBeforeNewWorkPreflight()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var start = source.IndexOf(
            "private async Task<bool> StartDecomposeLoopAsync(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private void TryStartDecomposeGroupFromResponse(",
            start,
            StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var method = source[start..end];
        var replayDecision = method.IndexOf(
            "PlanRepairReplayPolicy.ShouldReplayBeforeMutationPreflight(",
            StringComparison.Ordinal);
        var preflight = method.IndexOf(
            "await EnsurePlanWorktreeReadyAsync(_currentWorkspace.FolderPath, groupId);",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(replayDecision, Is.GreaterThanOrEqualTo(0));
            Assert.That(preflight, Is.GreaterThan(replayDecision));
            Assert.That(method, Does.Contain("if (!isConfirmedContinuation && !replayPendingResult)"));
            Assert.That(source, Does.Contain("skipWorktreePreflight: replayingPendingResult"));
        });
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(pathParts)}'.");
    }
}
