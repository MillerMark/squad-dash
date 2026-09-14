using System.IO;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerRefreshArchitectureTests
{
    [Test]
    public void RightSideVerificationBadges_UseThemeAwareAmberForEveryLevelAndCompletionState()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var start = source.IndexOf("if (!verificationOnly)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var end = source.IndexOf("} // end if (!verificationOnly)", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        var badges = source[start..end];

        Assert.Multiple(() =>
        {
            foreach (var target in new[]
            {
                "checkPath.SetResourceReference(Shape.FillProperty",
                "hollowCheckPath.SetResourceReference(Shape.FillProperty",
                "indicator.SetResourceReference(Shape.StrokeProperty",
                "indicator.SetResourceReference(Shape.FillProperty",
                "hollowStarPath.SetResourceReference(Shape.FillProperty",
                "starPath.SetResourceReference(Shape.FillProperty",
            })
                Assert.That(badges, Does.Contain($"{target}, \"PriorityHigh\");"));

            Assert.That(badges, Does.Contain("return MakeCheckmarkIcon(applyCompletionOpacity: false);"));
            Assert.That(badges, Does.Not.Contain("\"TaskSucceededFill\""));
            Assert.That(badges, Does.Contain("Canvas.SetLeft(verificationIndicator, position.X + NodeWidth"));
        });
    }

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
                .And.Contain("null => \"Edit Plan Details\"")
                .And.Contain("\"task\" => \"Edit Task\"")
                .And.Contain("\"validation\" => \"Edit Validation\"")
                .And.Contain("\"gate\" or \"humanapproval\" => \"Edit Approval\""),
                "Editors must be hosted by the existing inspector and stay read-only until Edit.");
            Assert.That(source, Does.Contain("_isInspectorEditing = beginEditingSelection;")
                .And.Contain("if (_isInspectorEditing && TryPopulateStagedEditor())")
                .And.Contain("\"gate\" or \"humanapproval\" => PopulateGateEditor(_selectedElement.Id)")
                .And.Contain("return gate is null || gate.Status == PlanGateStatus.Pending;")
                .And.Contain("_inspectorPrimaryEditor = message;"),
                "Add-and-edit must use the same staged approval editor as Edit Approval, with Message as its primary field.");
            Assert.That(source, Does.Contain("Header = \"Add validation after this step\"")
                .And.Contain("Header = \"Delete step\"")
                .And.Contain("Header = \"Delete validation\""));
            Assert.That(source, Does.Contain("AddPrimaryButton(\"Undo\"")
                .And.Contain("AddPrimaryButton(\"Redo\"")
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
    public void LiveRecoveryRefresh_RecomputesActionForTheCurrentInterruptedTask()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(viewer, Does.Contain("_resolveInterruptedPrimaryAction?.Invoke(durablePlan)")
                .And.Contain("currentInterruptedPrimaryAction?.Label")
                .And.Contain("currentInterruptedPrimaryAction?.Hint"),
                "A rebuilt open viewer must not retain the recovery label from an earlier task.");
            Assert.That(main, Does.Contain("resolveInterruptedPrimaryAction: ResolveInterruptedPrimaryAction")
                .And.Contain("var currentTaskId = current.InterruptionData?.InterruptedTaskId;"),
                "Recovery labels and actions must resolve the task from each current durable-plan snapshot.");
        });
    }

    [Test]
    public void PendingTranscriptLink_ResolvesLatestRevisionBeforeViewerReuse()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private void OpenDecomposePlanViewer", StringComparison.Ordinal);
        var reuseLookup = source.IndexOf(
            "_openPlanViewerWindows.FirstOrDefault", methodStart, StringComparison.Ordinal);
        var latestAssignment = source.IndexOf(
            "plan = candidate;", methodStart, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(latestAssignment, Is.GreaterThan(methodStart).And.LessThan(reuseLookup),
                "A stale transcript snapshot must be replaced before matching an open viewer by revision.");
            Assert.That(source[methodStart..reuseLookup], Does.Contain("livePlan = candidate;")
                .And.Contain("durablePlan is null"),
                "The latest pending proposal must remain editable when opened through an older transcript link.");
        });
    }

    [Test]
    public void PlanEditor_UsesImportantTextAndRoutesCtrlWheelToGraphZoom()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(viewer, Does.Contain(
                    "editor.SetResourceReference(TextBox.ForegroundProperty, \"ImportantText\");")
                .And.Contain(
                    "editor.SetResourceReference(TextBox.CaretBrushProperty, \"ImportantText\");"),
                "All staged-plan text editors should use the high-contrast important-text theme brush.");
            Assert.That(viewer, Does.Contain("_graphCanvas.LayoutTransform = new ScaleTransform(zoom, zoom);")
                .And.Contain("(Keyboard.Modifiers & ModifierKeys.Control) != 0"));
            Assert.That(viewer, Does.Not.Contain("_onFontScaleDelta"),
                "Graph zoom must never change the environment font scale.");
            Assert.That(main, Does.Contain(
                    "squadFolderPath: _currentWorkspace?.SquadFolderPath"),
                "Plan viewers must receive the originating workspace for per-plan navigation storage.");
        });
    }

    [Test]
    public void TaskContractExamples_UseFirstClassEditorCardsAndRenderAsNormativeCodeBlocks()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(viewer, Does.Contain("Contract examples (optional)")
                .And.Contain("CreateContractExampleEditor(panel, task.ContractExamples)")
                .And.Contain("AddContractExampleCard(cardHost, rows)")
                .And.Contain("\"Add example\"")
                .And.Contain("\"Delete\"")
                .And.Contain("BuildContractExamples(contractExamples)"),
                "The staged task editor must provide first-class Add/Delete example cards.");
            Assert.That(viewer, Does.Not.Contain("Contract examples (optional JSON array)")
                .And.Not.Contain("TryParseContractExamples"),
                "Users should never need to author the example schema as raw JSON.");
            Assert.That(viewer, Does.Contain("Normative Contract Examples")
                .And.Contain("AppendContractExampleValue(\"Input\"")
                .And.Contain("AppendContractExampleValue(\"Expected behavior\"")
                .And.Contain("new FontFamily(\"Consolas\")"),
                "The inspector must distinguish normative input and expected behavior from prose.");
        });
    }

    [Test]
    public void InsertAndAssignmentInteractions_UpdateTheVisiblePlanImmediately()
    {
        var viewer = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var editor = File.ReadAllText(FindRepoFile("SquadDash", "StagedPlanEditor.cs"));
        var main = File.ReadAllText(FindRepoFile("SquadDash", "MainWindow.xaml.cs"));
        var thumbnail = File.ReadAllText(FindRepoFile("SquadDash", "PlanThumbnailRenderer.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(viewer, Does.Contain("RefreshDetailPanel();\n            RebuildPreservingScroll(saved, _durablePlan);")
                .Or.Contain("RefreshDetailPanel();\r\n            RebuildPreservingScroll(saved, _durablePlan);"),
                "The selected inserted step must reach the inspector before the deferred graph rebuild.");
            Assert.That(viewer, Does.Contain("ApplySelectionVisual();\n        RefreshDetailPanel();\n        FocusPrimaryInspectorEditorIfRequested(currentBuildSeq);")
                .Or.Contain("ApplySelectionVisual();\r\n        RefreshDetailPanel();\r\n        FocusPrimaryInspectorEditorIfRequested(currentBuildSeq);"),
                "The rebuilt graph and inspector must restore the inserted element's selection and edit form.");
            Assert.That(viewer, Does.Contain("_focusPrimaryEditorAfterRebuild = true;")
                .And.Contain("editor.Focus();")
                .And.Contain("Keyboard.Focus(editor);")
                .And.Contain("editor.SelectAll();"),
                "New definitions must place keyboard focus in the first editable field after rebuilding.");
            Assert.That(viewer, Does.Contain("CreateAssignToSubmenu")
                .And.Contain("CreateThemedMenuItem(\"Assign primary agent\"")
                .And.Contain("Primary agent (handle | role | allow generic children)")
                .And.Contain("Enter exactly one primary agent")
                .And.Not.Contain("ToggleTaskAgent")
                .And.Not.Contain("preserving other assignees"),
                "Editable task menus must expose one primary-agent selection from the active workspace roster.");
            Assert.That(viewer, Does.Contain("task.AgentAssignments!.Take(1)"));
            Assert.That(thumbnail, Does.Contain("task.AgentAssignments.Take(1)"),
                "Legacy multi-assignment data must not render multiple agents as executable primaries.");
            Assert.That(main, Does.Contain("getAssignableAgents: GetAssignablePlanAgentsForViewer")
                .And.Contain("PlanStepAgentResolver.ParseTeamMd("),
                "The viewer must receive the active workspace roster rather than infer agents from existing tasks.");
            Assert.That(editor, Does.Contain("AssignTaskToAgent")
                .And.Contain("AgentRoutingMode = \"assigned\"")
                .And.Contain("GenericAgentReason = null"),
                "Reassignment must persist an executable named-agent contract.");
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
    public void DeveloperNormalization_AppendsWithoutSuppressingExistingContextMenus()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var appendCount = source.Split(
            "AppendDeveloperNormalizeMenuItem(",
            StringSplitOptions.None).Length - 1;

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("canvas.ContextMenuOpening"),
                "The canvas must not handle routed context-menu events from task or gate controls.");
            Assert.That(appendCount, Is.GreaterThanOrEqualTo(4),
                "The developer command should be appended to the canvas and existing task menus through one helper.");
            Assert.That(source, Does.Contain("if (menu.Items.Count > 0)")
                .And.Contain("menu.Items.Add(CreateThemedMenuSeparator())")
                .And.Contain("menu.Items.Add(normalizeItem)"),
                "Appending the developer command must preserve the menu's regular items.");
        });
    }

    [Test]
    public void EmptyApprovalOctagon_OffersApprovalActionBeforeDeveloperNormalization()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var methodStart = source.IndexOf("private FrameworkElement CreateApprovalStop(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static ContextMenu CreateThemedMenu()", methodStart, StringComparison.Ordinal);

        Assert.That(methodStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd, Is.GreaterThan(methodStart));
        var method = source[methodStart..methodEnd];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (toggle is not null && stop is not null)")
                .And.Contain(": \"Require human approval here\""),
                "An editable empty octagon must expose the inverse of the removal action.");
            Assert.That(method, Does.Contain("AppendDeveloperNormalizeMenuItem(approvalMenu)"),
                "Normalization should be appended to the approval menu instead of replacing it.");
        });
    }

    [TestCase("Before", "before", "Review before starting")]
    [TestCase("After", "after", "Review after completing")]
    public void EmptyTaskApprovalOctagon_StagesSelectsAndEditsWithoutPrompting(
        string boundary, string anchorPrefix, string defaultLabel)
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var stopStart = source.IndexOf(
            $"var {anchorPrefix}Stop = CreateApprovalStop(", StringComparison.Ordinal);
        Assert.That(stopStart, Is.GreaterThanOrEqualTo(0));
        var addStart = source.IndexOf(
            "var taskTitle = capturedTaskForStop.Title", stopStart, StringComparison.Ordinal);
        Assert.That(addStart, Is.GreaterThan(stopStart));
        var callbackStart = source.IndexOf(
            "if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);",
            addStart, StringComparison.Ordinal);
        Assert.That(callbackStart, Is.GreaterThan(addStart));
        var addPath = source[addStart..callbackStart];
        var stagedStart = addPath.IndexOf("if (_saveDefinitionEdit is not null)", StringComparison.Ordinal);
        var runtimeStart = addPath.IndexOf(
            $"updated = PlanGateManager.AddGate{boundary}(", StringComparison.Ordinal);
        Assert.That(stagedStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(runtimeStart, Is.GreaterThan(stagedStart));
        var stagedPath = addPath[stagedStart..runtimeStart];
        var runtimePath = addPath[runtimeStart..];

        Assert.Multiple(() =>
        {
            Assert.That(addPath, Does.Not.Contain("SimpleInputDialog")
                .And.Contain("var defaultQ = $\"Has {taskTitle} been reviewed and approved?\";"),
                "Empty task octagons must add with the existing default question, never prompt.");
            Assert.That(stagedPath, Does.Contain($"var stagedGatePlan = PlanGateManager.AddGate{boundary}(")
                .And.Contain($"$\"{defaultLabel}: {{taskTitle}}\", question: defaultQ")
                .And.Contain("existing.GateId == gate.GateId")
                .And.Contain("!Equals(existing, gate)"),
                "The staged path must identify the added or expanded gate just like the task context menu.");
            Assert.That(stagedPath, Does.Contain("stagedGatePlan = PlanGateManager.SetPresentationAnchor(")
                .And.Contain($"stagedGatePlan, changedGate.GateId, {anchorPrefix}Anchor"),
                "The clicked octagon must remain primary, including when a compatible gate expands.");
            Assert.That(stagedPath, Does.Contain("ApplyStagedMutation(")
                .And.Contain("PendingDecomposePlanAdapter.FromPlan(stagedGatePlan).Group")
                .And.Contain("selectKind: \"humanapproval\"")
                .And.Contain("selectId: changedGate.GateId")
                .And.Contain("beginEditingSelection: true"),
                "The new gate must be selected and opened in the existing inspector editor.");
            Assert.That(stagedPath, Does.Match(@"return;\s*\}\s*$")
                .And.Not.Contain("onGatesChanged"),
                "The staged branch must return before the runtime callback to avoid applying twice.");
            Assert.That(runtimePath, Does.Contain($"$\"{defaultLabel}: {{taskTitle}}\", question: defaultQ")
                .And.Contain($"PlanGateManager.SetPresentationAnchor(updated, expanded{boundary}Gate.GateId, {anchorPrefix}Anchor)")
                .And.Not.Contain("ApplyStagedMutation")
                .And.Not.Contain("return;"),
                "Non-staged additions must still reach the original callback with their clicked anchor.");
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
