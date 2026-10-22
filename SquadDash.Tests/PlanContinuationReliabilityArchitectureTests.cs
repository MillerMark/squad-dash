using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanContinuationReliabilityArchitectureTests
{
    [Test]
    public void DeferredPlanContinuation_IsPersistedReclaimedAndClearedOnlyAfterStart()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var apply = Method(
            source,
            "private async Task ApplyDecomposeRecoveryFromResponseSafelyAsync",
            "private void TryDispatchPendingDecomposeRecoveryResponse");
        var watchdog = Method(
            source,
            "private void EnsurePendingPlanContinuationDispatched()",
            "private void RestorePendingPlanContinuation()");
        var start = Method(
            source,
            "private async Task<bool> StartDecomposeLoopAsync(",
            "private void TryStartDecomposeGroupFromResponse");

        Assert.Multiple(() =>
        {
            Assert.That(apply, Does.Contain("UpdatePendingPlanContinuationState("));
            Assert.That(watchdog, Does.Contain("Reclaiming a deferred plan-continuation dispatch")
                .And.Contain("BuildProtocolResponse()"));
            Assert.That(start, Does.Contain("await _loopController.StartAsync(")
                .And.Contain("UpdatePendingPlanContinuationState(null)")
                .And.Contain("Cleared durable plan continuation after confirmed plan start"));
        });
    }

    [Test]
    public void RestartRequest_PersistsPlanCheckpointBeforeShutdown()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var handler = Method(
            source,
            "private void HandleRestartRequestChanged()",
            "private void TryAdoptPendingRestartRequestForClose()");

        Assert.That(handler, Does.Contain("RestartCheckpointRequestedAt = DateTimeOffset.UtcNow")
            .And.Contain("UpdateActiveLoopExecutionState(")
            .And.Contain("LoopIterationProgressKind.RestartCheckpoint"));
    }

    [Test]
    public void TranscriptInboxAndViewer_ResolveTheSameRecoveryActionSet()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var inbox = File.ReadAllText(FindRepoFile("SquadDash", "DecomposePlanInbox.cs"));
        var presentation = File.ReadAllText(FindRepoFile("SquadDash", "PlanRecoveryPresentation.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(main, Does.Contain("PlanRecoveryActionSetBuilder.Build(")
                .And.Contain("recoveryActionSet?.Actions"));
            Assert.That(viewer, Does.Contain("_resolveInterruptedActionSet?.Invoke(durablePlan)")
                .And.Contain("currentInterruptedSurfaceAction.Label")
                .And.Contain("currentEndAction.Tone")
                .And.Contain("PlanRecoveryLifecycleControlPolicy.ShouldShowOrdinaryControls"));
            Assert.That(inbox, Does.Contain("PlanRecoveryActionSetBuilder.Build(")
                .And.Contain("PlanRecoveryActionSetBuilder.BuildConservativeFallback(")
                .And.Contain("canonicalActions.Actions"));
            Assert.That(presentation, Does.Contain("\"End Plan\"")
                .And.Contain("QuickReplyTone.Destructive"));
        });
    }

    [Test]
    public void ContinueWorking_UsesAwaitedPrimaryCallbackWithoutRenderingASecondButton()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var primaryCallback = Method(
            main,
            "Func<Plan, Task<bool>>? onAdoptVerifiedCommitRange =",
            "var interruptedPrimaryActionLabel =");
        var continuationCallback = Method(
            main,
            "Action<Plan>? onContinueInterruptedTask =",
            "Action<Plan>? onReplanRemainingWork =");

        Assert.Multiple(() =>
        {
            Assert.That(primaryCallback,
                Does.Contain("PlanRecoverySurfaceActionKind.ContinueWorking")
                    .And.Contain("return await RetryDecomposeTaskAsync("),
                "the primary continuation must retain its awaited result and failure handling");
            Assert.That(continuationCallback,
                Does.Contain("PlanRecoverySurfaceActionKind.ContinueWorking")
                    .And.Contain("RetryDecomposeTaskAsync("),
                "secondary Continue Working remains available when completion is the primary choice");
            Assert.That(viewer,
                Does.Contain("var primaryOwnsContinuation = onAdoptVerifiedCommitRange is not null")
                    .And.Contain("if (!primaryOwnsContinuation && onContinueInterruptedTask is not null"),
                "the viewer must suppress only a duplicate rendering, not the awaited callback");
        });
    }

    [Test]
    public void AssessedRecovery_RendersCanonicalEndPlanLast()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var assessedRecovery = Method(
            viewer,
            "durablePlan.InterruptionData?.RecoveryAssessment is not null &&",
            "durablePlan.InterruptionData?.RecoveryAssessment is null &&");

        Assert.Multiple(() =>
        {
            Assert.That(assessedRecovery,
                Does.Contain("currentEndAction")
                    .And.Contain("tone: currentEndAction.Tone")
                    .And.Contain("PlanRecoveryControlPolicy.HasActionableInterruption(durablePlan)"));
            Assert.That(assessedRecovery.LastIndexOf("currentEndAction", StringComparison.Ordinal),
                Is.GreaterThan(assessedRecovery.LastIndexOf("currentReviseAction", StringComparison.Ordinal)),
                "End Plan must be rendered after the non-destructive assessed-recovery actions");
        });
    }

    [Test]
    public void UnexpectedStateCard_UsesOneDiagnosticsLinkAndCanonicalRecoveryButtons()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var diagnosticsWindow = File.ReadAllText(
            FindRepoFile("SquadDash", "WorkspaceInstallDiagnosticsWindow.cs"));
        var recoveryCard = Method(
            main,
            "private void AppendDecomposeRecoveryActions(",
            "private async Task ShowRecoveryReassessmentWhenStaleAsync(");

        Assert.Multiple(() =>
        {
            Assert.That(recoveryCard,
                Does.Contain("new Hyperlink(new Run(\"Diagnostics\"))")
                    .And.Contain("ShowDiagnosticsReportWindow(\"Plan Recovery Diagnostics\"")
                    .And.Contain("foreach (var action in recoveryActionSet?.Actions ?? [])")
                    .And.Not.Contain("\"Copy Diagnostic Report\"")
                    .And.Not.Contain("\"Open Logs\""));
            Assert.That(diagnosticsWindow,
                Does.Contain("new CopyButton")
                    .And.Contain("ThemedButtonStyle")
                    .And.Contain("FontSizeSmall")
                    .And.Contain("CardSurface"));
        });
    }

    [Test]
    public void PauseRequest_UsesSafeControlsAndCannotBecomeARecoveryCard()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var requestPause = Method(
            main,
            "private bool RequestPlanPauseAfterCurrentStep(",
            "private bool CancelPlanPauseAfterCurrentStep(");
        var pauseCard = Method(
            main,
            "private void ShowPlanPauseRequestedEntry(",
            "private sealed record PlanPauseRequestedTag");
        var recoveryCard = Method(
            main,
            "private void AppendDecomposeRecoveryActions(",
            "private async Task ShowRecoveryReassessmentWhenStaleAsync(");
        var endPlan = Method(
            main,
            "private void EndInterruptedPlan(",
            "private void SyncCodeHealthPanel(");
        var activeWorkGuard = Method(
            main,
            "private bool HasActivePlanWork(",
            "private bool IsPlanRecoveryActionBusy(");
        var recoveryBusyGuard = Method(
            main,
            "private bool IsPlanRecoveryActionBusy(",
            "private bool CancelPlanPauseAfterCurrentStep(");
        var cancelPause = Method(
            main,
            "private bool CancelPlanPauseAfterCurrentStep(",
            "private void ShowPlanPauseRequestedEntry(");
        var pauseTransition = Method(
            main,
            "private bool TrySetPlanPauseState(",
            "private void RefreshOpenPlanViewersAfterPauseTransition(");
        var viewerRefresh = Method(
            main,
            "private void RefreshOpenPlanViewersAfterPauseTransition(",
            "private bool CancelPlanPauseAfterCurrentStep(");

        Assert.Multiple(() =>
        {
            Assert.That(requestPause, Does.Contain("TrySetPlanPauseState(plan, pause: true")
                .And.Contain("return false;")
                .And.Contain("ShowPlanPauseRequestedEntry(current)")
                .And.Not.Contain("ScheduleDecomposeSystemEntry("));
            Assert.That(pauseCard, Does.Contain("KeepPlanRunningLabel")
                .And.Contain("ViewActiveWorkLabel")
                .And.Not.Contain("End Plan")
                .And.Not.Contain("RetryDecomposeTaskAsync"));
            Assert.That(recoveryCard, Does.Contain("PlanRecoveryControlPolicy.HasActionableInterruption(durablePlan)")
                .And.Contain("planRunnerActive"));
            Assert.That(endPlan, Does.Contain("HasActivePlanWork(current.PlanId)")
                .And.Contain("HasActionableInterruption(current)")
                .And.Contain("MessageBox.Show("));
            Assert.That(activeWorkGuard, Does.Contain("_isPromptRunning")
                .And.Contain("_loopController.IsRunning")
                .And.Contain("PlanExecutionAttempt is { Status: \"active\" }")
                .And.Contain("ActiveVerificationTaskId")
                .And.Contain("ActiveValidationId"));
            Assert.That(recoveryBusyGuard, Does.Contain("_isPromptRunning")
                .And.Contain("IsLoopRunning")
                .And.Contain("_loopController.IsRunning")
                .And.Contain("HasActivePlanWork(planId)"));
            Assert.That(main, Does.Contain("IsPlanRecoveryActionBusy(plan.Group.GroupId)")
                .And.Contain("IsPlanRecoveryActionBusy(displayedPlan.PlanId)"));
            Assert.That(main, Does.Contain("TrySetPlanPauseState(current, pause: false, out var resumed"));
            Assert.That(main, Does.Contain("TrySetPlanPauseState(durablePlan, pause: false, out var resumable")
                .And.Not.Contain("PlanStoreUpdater.ApplyPause(")
                .And.Not.Contain("PlanStoreUpdater.ApplyResume("));
            Assert.That(pauseTransition, Does.Contain("PlanPauseTransitionCoordinator.TryApply")
                .And.Contain("TryPublishPlanProgress(candidate, out var persistError)"));
            Assert.That(viewerRefresh, Does.Contain("entry.Window.RefreshPlan("));
            Assert.That(cancelPause, Does.Contain("TrySetPlanPauseState(current, pause: false")
                .And.Contain("RemovePlanPauseRequestedEntries(plan.PlanId)"));
            Assert.That(pauseCard, Does.Contain("RemovePlanPauseRequestedEntries(plan.PlanId)"));
            Assert.That(main, Does.Contain("if (pausedAfterStep && stoppedPlanId is not null)")
                .And.Contain("RemovePlanPauseRequestedEntries(stoppedPlanId)"));
            Assert.That(viewer, Does.Contain("KeepPlanRunningLabel")
                .And.Contain("ViewActiveWorkLabel")
                .And.Contain("BuildPauseRequestedMessage(durablePlan)")
                .And.Contain("IsActivePauseRequest("));
        });
    }

    [Test]
    public void PlanViewerPauseResume_RestoresAwaitingApprovalSurface()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var resumeCallback = Method(
            main,
            "Action<Plan>? onResumePlan =",
            "Action<Plan>? onPausePlan =");
        var pauseCallback = Method(
            main,
            "Action<Plan>? onPausePlan =",
            "Func<Plan, Task<bool>>? onAdoptVerifiedCommitRange =");

        Assert.Multiple(() =>
        {
            Assert.That(resumeCallback, Does.Contain("TrySetPlanPauseState(current, pause: false, out var resumed")
                .And.Contain("RemovePlanPauseRequestedEntries(resumed.PlanId)")
                .And.Contain("resumed.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval")
                .And.Contain("gate.Status == PlanGateStatus.AwaitingApproval")
                .And.Contain("RestoreAwaitingApprovalGateUI(resumed.PlanId)"));
            Assert.That(pauseCallback, Does.Contain("TrySetPlanPauseState(current, pause: true")
                .And.Contain("RemoveApprovalCardsForPausedPlan(current.PlanId)"));
        });
    }

    [Test]
    public void RecoveryDeveloperPreview_BypassesOnlyTheDurableInterruptionRequirement()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var recoveryCard = Method(
            main,
            "private void AppendDecomposeRecoveryActions(",
            "private async Task ShowRecoveryReassessmentWhenStaleAsync(");

        Assert.Multiple(() =>
        {
            Assert.That(recoveryCard, Does.Contain("bool developerPreview = false"));
            Assert.That(recoveryCard, Does.Contain("if (!developerPreview &&"));
            Assert.That(recoveryCard, Does.Contain("includeEndPlan: !planRunnerActive"));
            Assert.That(main, Does.Contain("developerPreview: true"));
        });
    }

    [Test]
    public void ValidatedPartialRecovery_ContinuesItsAssessedUncommittedWorkWithoutAnotherStop()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var validation = Method(
            main,
            "private async Task ValidateAndApplyPlanRecoveryAssessmentAsync(",
            "private async Task AcceptAssessedPlanRecoveryAsync(");

        Assert.That(validation, Does.Contain("case PlanRecoveryClassification.Partial:")
            .And.Contain("allowUncommittedPreservedWork: true")
            .And.Contain("requiredPreservedSnapshot: currentSnapshot")
            .And.Contain("requiredRepositorySnapshot: currentSnapshot")
            .And.Contain("case PlanRecoveryClassification.NotStarted:")
            .And.Contain("requiredPreservedSnapshot: currentSnapshot"));

        var retry = Method(
            main,
            "private async Task<bool> RetryDecomposeTaskAsync(",
            "private async Task ApplyDecomposeDecisionSafelyAsync(");
        Assert.That(retry, Does.Contain("PlanRecoveryAssessmentSnapshotPolicy.Matches(")
            .And.Contain("requiredPreservedSnapshot")
            .And.Contain("confirmedSnapshot = await CapturePlanRecoveryRepositorySnapshotAsync(workspace)")
            .And.Contain("confirmedSnapshot.StatusSnapshot")
            .And.Contain("preservedPaths.Count > 0 ? confirmedSnapshot : null")
            .And.Contain("left the plan stopped")
            .And.Contain("TryPublishPlanRecoveryProgress(")
            .And.Contain("revalidatedBranch"));
    }

    [Test]
    public void PreservedWorkDispatch_UsesOneDurableSnapshotAcrossRestartAndDispatch()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var prepare = Method(
            main,
            "private async Task<string?> PrepareExecutingPlanIterationAsync()",
            "private string? AppendPlanExecutionJournal(");
        var finalize = Method(
            main,
            "private async Task FinalizeExecutingPlanIterationAsync()",
            "private async Task FinalizeTaskVerificationTurnAsync(");
        var start = Method(
            main,
            "private async Task<bool> StartDecomposeLoopAsync(",
            "private void TryStartDecomposeGroupFromResponse");

        Assert.Multiple(() =>
        {
            Assert.That(start, Does.Contain("persistedRecoveryContinuation")
                .And.Contain("requestedRecoveryContinuation")
                .And.Contain("durableRevisionAtStart")
                .And.Contain("PlanRecoveryContinuationState.Normalize(")
                .And.Not.Contain("expectedRevision ?? priorExecution?.DecomposeRevision")
                .And.Contain("effectiveRecoveryContinuation = requestedRecoveryContinuation ?? persistedRecoveryContinuation")
                .And.Contain("PlanExecutionResumeEnvelope.Create("));
            Assert.That(prepare, Does.Contain("execution.RecoveryContinuation")
                .And.Contain("CapturePlanRecoveryRepositorySnapshotAsync(")
                .And.Contain("PlanRecoveryContinuationPolicy.Evaluate(")
                .And.Not.Contain("RecoveryContinuation = null"));
            Assert.That(prepare, Does.Not.Contain("MatchesConfirmedPaths("),
                "dispatch must not reinterpret a canonical snapshot as a separate path-only check");
            Assert.That(finalize, Does.Contain("ShouldClearAfterPromptCompletion(")
                .And.Contain("RecoveryContinuation = null"));
        });
    }

    [Test]
    public void ValidatedPlanCommit_WaitsForCleanWorktreeInsteadOfFailingTheStep()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var finalize = Method(
            main,
            "private async Task FinalizeExecutingPlanIterationAsync()",
            "private async Task FinalizeTaskVerificationTurnAsync(");
        var validation = Method(
            main,
            "private sealed record DecomposeStepCommitValidation(",
            "private void StopAndOfferDecomposeRecovery(");
        var cleanPause = Method(
            main,
            "private void PausePlanForCleanWorktree(",
            "private void OnNativeLoopIterationCompleted(");

        Assert.Multiple(() =>
        {
            Assert.That(validation, Does.Contain("PlanPreflightBlockedException? WorktreeBlock")
                .And.Contain("EnsurePlanWorktreeReadyAsync(workspace, result.GroupId)")
                .And.Contain("catch (PlanPreflightBlockedException blocked)")
                .And.Contain("return new(null, verificationOnly ? head : resolved, commits, anomalies, changedPaths, worktreeBlock)"));
            Assert.That(finalize, Does.Contain("postCommitWorktreeBlock = commitValidation.WorktreeBlock")
                .And.Contain("PlanStoreUpdater.ApplyTaskVerificationPending(")
                .And.Contain("PausePlanForCleanWorktree(postCommitWorktreeBlock, groupId)")
                .And.Contain("completedAfterAcceptance")
                .And.Contain("postCommitWorktreeBlock is not null && !completedAfterAcceptance")
                .And.Contain("TryPublishPlanStepAccepted("));
            Assert.That(cleanPause, Does.Contain("SelectSinglePendingVerificationTask(latest)")
                .And.Contain("ResumeInterruptedTaskVerificationAsync(latest, verificationTask.TaskId)"));
        });
    }

    [Test]
    public void VerificationHumanReview_RestoresApprovalCardInsteadOfRecoveryCard()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var finalize = Method(
            main,
            "private async Task FinalizeTaskVerificationTurnAsync(",
            "private async Task AcceptVerifiedTaskAsync(");
        var humanReviewStart = finalize.IndexOf(
            "if (combinableHumanReview is not null)",
            StringComparison.Ordinal);
        var humanReviewEnd = finalize.IndexOf(
            "var discrepancies = verification.MissingOrOverstatedWork.Count == 0",
            humanReviewStart,
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(humanReviewStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(humanReviewEnd, Is.GreaterThan(humanReviewStart));
        });

        var humanReview = finalize[humanReviewStart..humanReviewEnd];
        Assert.Multiple(() =>
        {
            Assert.That(humanReview, Does.Contain("PlanStoreUpdater.ApplyGateReady("));
            Assert.That(humanReview, Does.Contain("await RestoreAwaitingApprovalGateUIAsync(groupId);"));
            Assert.That(humanReview, Does.Not.Contain("AppendDecomposeRecoveryActions("));
        });
    }

    [Test]
    public void RecoveryTranscript_UsesPlanAndStepLabelsInsteadOfInternalIds()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var assessment = Method(
            main,
            "private async Task ValidateAndApplyPlanRecoveryAssessmentAsync(",
            "private async Task AcceptAssessedPlanRecoveryAsync(");
        var loopError = Method(
            main,
            "private void OnNativeLoopError(string msg)",
            "private void PausePlanForCleanWorktree(");

        Assert.Multiple(() =>
        {
            Assert.That(assessment, Does.Contain("assessedStepLabel")
                .And.Contain("has partial work")
                .And.Not.Contain("AI found partial work for {context.TaskId}"));
            Assert.That(loopError, Does.Contain("planDisplayName")
                .And.Contain("stepDisplayName")
                .And.Contain("recoverySourcePause ? \"plan_paused\" : \"plan_error\"")
                .And.Not.Contain("stopped at {blockedTaskId}"));
        });
    }

    [Test]
    public void RecoveryAssessmentSnapshot_ExcludesHostOwnedStateFromEveryGitComparison()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var capture = Method(
            main,
            "private async Task<PlanRecoveryRepositorySnapshot> CapturePlanRecoveryRepositorySnapshotAsync(",
            "private sealed record PendingRecoveryAnalysisContext(");

        Assert.Multiple(() =>
        {
            Assert.That(capture, Does.Contain("GetHostOwnedAssessmentPathsAsync(workspace)")
                .And.Contain("BuildGitPathspec(hostOwnedPaths)"));
            Assert.That(capture, Does.Contain("diff --binary --no-ext-diff HEAD {pathspec}")
                .And.Contain("status --porcelain --untracked-files=all {pathspec}")
                .And.Contain("ls-files --others --exclude-standard -z {pathspec}"));
        });
    }

    [Test]
    public void BackgroundStatusEntry_PreservesThePendingCoordinatorDelegation()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var showSystem = Method(
            main,
            "private TranscriptResponseEntry? ShowSystemTranscriptEntry(",
            "private void MaybePublishRoutingIssueSystemEntry(");
        var subagentStarted = Method(
            main,
            "private void HandleSubagentStarted(",
            "private void HandleSubagentMessageDelta(");

        Assert.Multiple(() =>
        {
            Assert.That(showSystem, Does.Contain("suspendedCoordinatorTurn")
                .And.Contain("_currentTurn = suspendedCoordinatorTurn")
                .And.Contain("active worker remains pending"));
            Assert.That(subagentStarted, Does.Contain("PersistAgentThreadSnapshot(thread)")
                .And.Not.Contain("SaveCurrentTurnToConversation("));
        });
    }

    [Test]
    public void ApprovedReviewNotes_AreQueuedFromCommonCompletionPublish()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var publish = Method(
            main,
            "private bool TryPublishPlanProgress(Plan plan, out string? error)",
            "private bool TryPublishPlanRecoveryProgress(");
        var recoveryPublish = Method(
            main,
            "private bool TryPublishPlanRecoveryProgress(",
            "private Plan? TryLoadPlanForCompletionTransition(");
        var finalApprovalCompletion = Method(
            main,
            "if (PlanExecutionBoundaryPolicy.IsReadyToComplete(resolution.UpdatedPlan))",
            "if (resolution.ShouldResume && !completedAtApproval");

        Assert.Multiple(() =>
        {
            Assert.That(publish, Does.Contain("QueueApprovedReviewNotesFollowUpOnCompletionTransition(previousPlan, plan)"));
            Assert.That(recoveryPublish, Does.Contain("QueueApprovedReviewNotesFollowUpOnCompletionTransition(expected, persistedPlan)"));
            Assert.That(finalApprovalCompletion, Does.Not.Contain("QueueApprovedReviewNotesFollowUp(completedPlan)"));
        });
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing {startMarker}");
        Assert.That(end, Is.GreaterThan(start), $"Missing {endMarker}");
        return source[start..end];
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail($"Could not find {Path.Combine(pathParts)}.");
        return string.Empty;
    }
}
