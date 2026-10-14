namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanRecoveryTranscriptHydrationTests
{
    [Test]
    public void WorkspaceLoad_RestoresInterruptedPlanActionsAfterTranscriptHydrationAndRepair()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var loadBlock = WorkspaceLoadBlock(source);
        const string loadCall = "\"conversation-manager\"";
        const string repairCall = "\"stale-plan-repair\"";
        const string restoreCall = "\"interrupted-plan-recovery-surfaces\"";

        var loadIndex = loadBlock.IndexOf(loadCall, StringComparison.Ordinal);
        var repairIndex = loadBlock.IndexOf(repairCall, loadIndex, StringComparison.Ordinal);
        var restoreIndex = loadBlock.IndexOf(restoreCall, repairIndex, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0), "Conversation hydration call was not found.");
            Assert.That(repairIndex, Is.GreaterThan(loadIndex), "Stale plan repair must run after hydration.");
            Assert.That(restoreIndex, Is.GreaterThan(repairIndex),
                "Interrupted-plan recovery controls must be reconstructed after hydration and stale-state repair.");
        });
    }

    [Test]
    public void WorkspaceLoad_AwaitsHydrationBeforeRestoringPendingPlanActions()
    {
        var mainWindowSource = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var managerSource = File.ReadAllText(FindRepoFile("SquadDash", "TranscriptConversationManager.cs"));
        var loadBlock = WorkspaceLoadBlock(mainWindowSource);
        const string loadCall = "\"conversation-manager\"";
        const string restoreCall = "\"pending-decompose-approval-surfaces\"";

        var loadIndex = loadBlock.IndexOf(loadCall, StringComparison.Ordinal);
        var restoreIndex = loadBlock.IndexOf(restoreCall, loadIndex, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(restoreIndex, Is.GreaterThan(loadIndex),
                "Pending-plan actions must be reconciled after transcript hydration.");
            Assert.That(mainWindowSource, Does.Contain("await TraceWorkspaceLoadPhaseAsync("),
                "Workspace load timing should still await conversation hydration.");
            Assert.That(managerSource, Does.Contain("await RenderAllSequentiallyAsync(agentRenders, turns, dataLoadMs);"),
                "LoadWorkspaceConversationAsync must not return while its document render is still queued.");
            Assert.That(managerSource, Does.Not.Contain("_ = RenderAllSequentiallyAsync(agentRenders, turns, dataLoadMs);"));
        });
    }

    private static string WorkspaceLoadBlock(string source)
    {
        var loadStart = source.IndexOf("LOAD_CONVERSATION_START", StringComparison.Ordinal);
        var loadEnd = source.IndexOf("LOAD_CONVERSATION_END", loadStart, StringComparison.Ordinal);
        Assert.That(loadStart, Is.GreaterThanOrEqualTo(0), "Workspace load start trace was not found.");
        Assert.That(loadEnd, Is.GreaterThan(loadStart), "Workspace load end trace was not found.");
        return source[loadStart..loadEnd];
    }

    [Test]
    public void WorkspaceLoad_LogsHydrationAndRestorePhaseTimings()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var loadBlock = WorkspaceLoadBlock(source);

        Assert.Multiple(() =>
        {
            Assert.That(loadBlock, Does.Contain("TraceWorkspaceLoadPhaseAsync"));
            Assert.That(loadBlock, Does.Contain("TraceWorkspaceLoadPhase"));
            Assert.That(loadBlock, Does.Contain("\"conversation-manager\""));
            Assert.That(loadBlock, Does.Contain("\"interrupted-plan-recovery-surfaces\""));
            Assert.That(loadBlock, Does.Contain("\"human-proof-coverage-review\""));
            Assert.That(source, Does.Contain("PERSISTED_TURN_RENDER_SLOW"));
            Assert.That(source, Does.Contain("RESPONSE_RENDER_SLOW"));
        });
    }

    [Test]
    public void PersistedToolRendering_DefersDetailTextUntilToolExpanderOpens()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("expander.Expanded += (_, _) => EnsureToolDetailText(entry);"));
            Assert.That(source, Does.Contain("if (entry.Expander.IsExpanded)")
                .And.Contain("EnsureToolDetailText(entry);"));
            Assert.That(source, Does.Contain("private static string BuildToolDetailText(ToolTranscriptEntry entry)"));
            Assert.That(source, Does.Not.Contain(
                "entry.DetailTextBox.Text = entry.DetailContent ?? ToolTranscriptFormatter.BuildDetailContent"));
        });
    }

    [Test]
    public void PendingPlanMovedToInbox_RestoresLinkedBannerAndUnreadCalloutAfterHydration()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var restoreStart = source.IndexOf(
            "private void RestorePendingDecomposeApprovalSurfaces(bool promoteUnaddressedToInbox = false)",
            StringComparison.Ordinal);
        var bannerStart = source.IndexOf(
            "private void AppendPlanMovedToInboxBanner(",
            restoreStart,
            StringComparison.Ordinal);
        var bannerEnd = source.IndexOf(
            "private Paragraph CreatePendingDecomposePlanLinkParagraph(",
            bannerStart,
            StringComparison.Ordinal);
        var restoreAndBanner = source[restoreStart..bannerEnd];
        var calloutStart = source.IndexOf(
            "private async Task<InboxAttentionTargetKind?> ShowInboxAttentionCalloutAsync(",
            StringComparison.Ordinal);
        var calloutEnd = source.IndexOf(
            "private void DeferApprovalCallout(",
            calloutStart,
            StringComparison.Ordinal);
        var callout = source[calloutStart..calloutEnd];

        Assert.Multiple(() =>
        {
            Assert.That(restoreAndBanner, Does.Contain("AppendPlanMovedToInboxBanner(plan, reminder)"));
            Assert.That(restoreAndBanner, Does.Contain("IsUnreadAutomaticReminder(plan, reminder)"));
            Assert.That(restoreAndBanner, Does.Contain("ShouldCreateAutomaticReminderOnWorkspaceHydration("));
            Assert.That(restoreAndBanner, Does.Contain("SaveDecomposePlanInboxReminder(plan, explicitlyRequested: false)"));
            Assert.That(restoreAndBanner, Does.Contain("new Hyperlink(new Run(\"Inbox\"))"));
            Assert.That(restoreAndBanner, Does.Contain("new Hyperlink(new Run(\"View plan\"))"));
            Assert.That(restoreAndBanner, Does.Contain("OpenOrFocusInboxMessage(message.Id)"));
            Assert.That(restoreAndBanner, Does.Contain("OpenDecomposePlanAttachment(attachment)"));
            Assert.That(restoreAndBanner, Does.Contain("ReplacePendingDecomposeApprovalSurfaceWithInboxBanner("));
            Assert.That(restoreAndBanner, Does.Not.Contain("blocks.Add(paragraph)"),
                "A missing historical anchor must not make an old Inbox receipt look like new transcript activity.");
            Assert.That(callout, Does.Contain("OpenTourMenuItemAsync("));
            Assert.That(callout, Does.Contain("FindVisibleMessageRow(messageId)"));
            Assert.That(callout, Does.Contain("_docsPanelState?.InboxPanelVisible == true"),
                "Persisted Inbox visibility must win the startup race with deferred panel restoration.");
            Assert.That(callout, Does.Contain("ApprovalInboxAttentionText"));
            Assert.That(callout, Does.Contain("PlanInboxAttentionText"));
            Assert.That(callout, Does.Contain("_deferredApprovalCallout is not null"),
                "Approval attention must retain priority over the plan reminder cue.");
        });
    }

    [Test]
    public void PersistedTurnRendering_DoesNotAttachCurrentRecoveryActionsToHistoryBatches()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.That(source, Does.Not.Contain("AppendPersistedDecomposeRecoveryIfNeeded"),
            "Current recovery actions must be owned by post-hydration restoration, not by a batch's last persisted turn.");
    }

    [Test]
    public void ExhaustedTaskPlanRepair_RestoresRecoverySurfaceAndReportsSpecificFailure()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private void QueueDecomposeRepair(",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void AppendPendingDecomposeApproval(",
            methodStart,
            StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("TaskPlanRepairPromptBuilder.Build("));
            Assert.That(method, Does.Contain("rejectedCandidate"));
            Assert.That(method, Does.Contain("RestoreInterruptedPlanRecoverySurfaces();"));
            Assert.That(method, Does.Contain("TaskPlanRepairRetryPolicy.Evaluate("));
            Assert.That(method, Does.Contain("Reason: {validationError}"));
        });
    }

    [Test]
    public void PlansPanel_OpensAuthoritativeDurableRevision_NotPendingProposalWithSameId()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private void OpenPlanFromStore(Plan plan)",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void ArchivePlan(Plan plan)",
            methodStart,
            StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("PendingDecomposePlanAdapter.FromPlan(plan)"));
            Assert.That(method, Does.Not.Contain("PendingDecomposePlanStore"),
                "The Plans panel must not replace durable progress with a pending proposal sharing the plan ID.");
        });
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

        Assert.Fail($"Could not find {Path.Combine(pathParts)} from {TestContext.CurrentContext.TestDirectory}.");
        return string.Empty;
    }
}
