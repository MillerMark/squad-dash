using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerRefreshArchitectureTests
{
    [Test]
    public void RefreshPlan_RebuildsInPlaceWithoutConstructingAnotherWindow()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("internal void RefreshPlan", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private static ToolTip BuildTaskToolTip", methodStart, StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(nextMethod, Is.GreaterThan(methodStart));

        var refreshMethod = source[methodStart..nextMethod];
        Assert.Multiple(() =>
        {
            Assert.That(refreshMethod, Does.Contain("var displayedDurablePlan = ApplyCommittedColumnReorder(durablePlan);")
                .And.Contain("RebuildPreservingScroll(displayedPlan, displayedDurablePlan)"),
                "Refresh should rebuild the existing viewer's visual tree through the scroll-preserving helper.");
            Assert.That(source, Does.Contain("private void RebuildPreservingScroll(PendingDecomposePlan plan, Plan? durablePlan)")
                .And.Contain("BuildContent(plan, durablePlan)"),
                "The shared helper must rebuild this existing viewer rather than constructing another window.");
            Assert.That(refreshMethod, Does.Not.Contain("new PlanViewerWindow"),
                "Constructing a hidden Window during refresh can keep WPF alive during restart and binds handlers to the wrong owner.");
        });
    }

    [Test]
    public void StagedEditing_RemainsInsideTheExistingInspectorAndGraphContextMenus()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private bool PopulateTaskEditor")
                .And.Contain("private bool PopulateValidationEditor")
                .And.Contain("private bool PopulateGateEditor"));
            Assert.That(source, Does.Contain("new BlockUIContainer(panel)")
                .And.Contain("AddButton(_isInspectorEditing ? \"Cancel edit\" : \"Edit\""),
                "Editors must be hosted by the existing inspector and stay read-only until Edit.");
            Assert.That(source, Does.Contain("Header = \"Add validation after this step\"")
                .And.Contain("Header = \"Delete step\"")
                .And.Contain("Header = \"Delete validation\""));
            Assert.That(source, Does.Contain("AddButton(\"Undo\"")
                .And.Contain("AddButton(\"Redo\"")
                .And.Contain("Review with AI"));
            Assert.That(source, Does.Contain("TourActionLabel = ReviewActionLabel(annotation)")
                .And.Contain("TourPrevRequested")
                .And.Contain("TourNextRequested")
                .And.Contain("ApplyActiveReviewChange"));
            Assert.That(source, Does.Not.Contain("new PlanEditorWindow")
                .And.Not.Contain("new PlanReviewWindow"));
        });
    }

    [Test]
    public void ContextMenus_SelectTheirElementAndRespectHistoricalDependencyLocks()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("element.PreviewMouseRightButtonDown")
                .And.Contain("SelectTaggedElement(sender)"),
                "Every tagged visualizer element must select itself before its context menu opens.");
            Assert.That(source, Does.Contain("var itemEnabled = definitionEditable && !wouldCycle && lockTip is null;")
                .And.Contain("!IsTaskDefinitionEditable(taskId)"),
                "Completed steps must not expose actionable dependency edits.");
            Assert.That(source, Does.Contain("\"Required verification checkpoint\"")
                .And.Contain("Remove approval checkpoint ({relationship})"),
                "Approval menu labels should remain compact while details live in tooltips.");
        });
    }

    [Test]
    public void HumanApprovalContextMenus_UseTheLowContrastThemedSeparator()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private Separator CreateThemedMenuSeparator()")
                .And.Contain("TryFindResource(\"ThemedMenuSeparatorStyle\") as Style"));
            Assert.That(source, Does.Contain("milestoneMenu.Items.Add(CreateThemedMenuSeparator())")
                .And.Contain("joinMenu.Items.Add(CreateThemedMenuSeparator())")
                .And.Contain("beforeMenu.Items.Add(CreateThemedMenuSeparator())")
                .And.Contain("afterMenu.Items.Add(CreateThemedMenuSeparator())"),
                "Every human-approval placement should use the separator whose brush resolves to LineColor.");
        });
    }

    [Test]
    public void AiReview_UsesSilentHostTurnAndDestructiveHighContrastAction()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var conversation = File.ReadAllText(FindRepoFile("SquadDash", "TranscriptConversationManager.cs"));
        var overlay = File.ReadAllText(
            FindRepoFile("SquadDash", "Callouts", "UI", "TourCalloutNavigationOverlay.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(main, Does.Contain("sourceTag: \"plan-visualizer-review\"")
                .And.Contain("_pendingPromptSuppressTranscript")
                .And.Contain("SuppressTranscript = suppressTranscript"));
            Assert.That(conversation, Does.Contain("turn.SuppressTranscript"));
            Assert.That(overlay, Does.Contain("DangerButtonBg")
                .And.Contain("DangerButtonFg")
                .And.Contain("ActionClicked"));
        });
    }

    [Test]
    public void DurableEditing_FreezesExecutedWorkAndDefersSavesAcrossTheActiveTaskBoundary()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(main, Does.Contain("Func<DecomposedTaskGroup, PlanDefinitionEditResult>? saveDefinitionEdit")
                .And.Contain("PlanRevisionApplier.Apply(")
                .And.Contain("RequestPlanPauseAfterCurrentStep(current)")
                .And.Contain("PlanEditPauseRequiredException"),
                "Plans-panel viewers must receive a durable edit path that pauses active execution before saving.");
            Assert.That(viewer, Does.Contain("IsTaskDefinitionEditable")
                .And.Contain("IsValidationDefinitionEditable")
                .And.Contain("IsGateDefinitionEditable")
                .And.Contain("TryValidateUnlockedChanges")
                .And.Contain("_deferredDefinitionEdit"),
                "The inspector, context menus, persistence boundary, and deferred Save must share lifecycle locks.");
            Assert.That(viewer, Does.Contain("Undo/redo is blocked at the execution boundary")
                .And.Contain("_editHistory?.ReplaceCurrent(updatedDefinition.Group)"),
                "Live execution updates must advance the history lock boundary for an already-open viewer.");
        });
    }

    [Test]
    public void CollectAction_CleansPendingStateAndStartupReconcilesLegacyMatches()
    {
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var normalizedMain = main.Replace("\r\n", "\n", StringComparison.Ordinal);
        var collectStart = main.IndexOf(
            "if (action == \"collect\")",
            StringComparison.Ordinal);
        var collectEnd = main.IndexOf(
            "if (action != \"add-to-backlog\"",
            collectStart,
            StringComparison.Ordinal);

        Assert.That(collectStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(collectEnd, Is.GreaterThan(collectStart));
        var collectPath = main[collectStart..collectEnd];
        Assert.Multiple(() =>
        {
            Assert.That(collectPath, Does.Contain(
                    "new PendingDecomposePlanStore(_currentWorkspace.SquadFolderPath)")
                .And.Contain("new PlanCollectionService(")
                .And.Contain("pendingStore)"),
                "The Add to Plans host action must supply the transient store so collection removes its proposal.");
            Assert.That(normalizedMain, Does.Contain("RepairStalePlanExecutingState();\n        ReconcileCollectedPendingPlans();")
                .And.Contain("PendingPlanCollectionReconciler.Reconcile("),
                "Workspace hydration must remove legacy same-revision pending proposals before rebuilding cards.");
        });
    }

    [Test]
    public void EditableQueueTabDispatch_PreservesSourceIdentityForLivePlanActivity()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var standardStart = source.IndexOf(
            "private async Task DispatchQueuedTabAsync", StringComparison.Ordinal);
        var quickReplyStart = source.IndexOf(
            "private async Task DispatchRightmostQueuedTabAsQuickReplyAsync", StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private PromptQueueItem? GetAutoDispatchCandidate", quickReplyStart, StringComparison.Ordinal);

        Assert.That(standardStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(quickReplyStart, Is.GreaterThan(standardStart));
        Assert.That(nextMethod, Is.GreaterThan(quickReplyStart));

        var standardDispatch = source[standardStart..quickReplyStart];
        var quickReplyDispatch = source[quickReplyStart..nextMethod];
        foreach (var dispatch in new[] { standardDispatch, quickReplyDispatch })
        {
            Assert.Multiple(() =>
            {
                Assert.That(dispatch, Does.Contain("_pec.CurrentDispatchedItem = item;")
                    .And.Contain("_pec.ExecutePromptAsync("),
                    "Manual queue-tab sends must expose the item identity while the prompt runs.");
                Assert.That(dispatch, Does.Contain("_pec.CurrentDispatchedItem = null;"),
                    "Manual queue-tab sends must release their dispatch identity afterward.");
            });
        }
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

        Assert.Fail($"Could not find {Path.Combine(pathParts)} from {TestContext.CurrentContext.TestDirectory}.");
        return string.Empty;
    }
}
