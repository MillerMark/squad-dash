using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerInspectorTaskLinkArchitectureTests
{
    [Test]
    public void ThemeTokens_DefineTaskLinkTextAndMatchChipSelectedBorderInLightAndDarkThemes()
    {
        var light = File.ReadAllText(FindRepoFile("SquadDash", "Themes", "Light.xaml"));
        var dark = File.ReadAllText(FindRepoFile("SquadDash", "Themes", "Dark.xaml"));

        AssertThemeBrushColors(light, expectedTaskLinkText: "#1A52B1", expectedChipSelectedBorder: "#5B88D8");
        AssertThemeBrushColors(dark, expectedTaskLinkText: "#8FB1F0", expectedChipSelectedBorder: "#4A70B8");
    }

    [Test]
    public void InspectorTaskLink_CreateTaskLinkUsesTaskLinkTextHoverGlowAndNavigation()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private Hyperlink CreateTaskLink(string taskTitle, string taskId)");
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("link.SetResourceReference(TextElement.ForegroundProperty, \"TaskLinkText\")"));
            Assert.That(method, Does.Contain("TextDecorations = new TextDecorationCollection()"));
            Assert.That(method, Does.Not.Contain("TextDecorations.Underline"));
            Assert.That(method, Does.Contain("Cursor = Cursors.Hand"));
            Assert.That(method, Does.Contain("_borderByTask.TryGetValue(taskId, out var nodeBorder)"));
            Assert.That(method, Does.Contain("nodeBorder.Effect = TaskNodeGlowEffect();"));
            Assert.That(method, Does.Contain("nodeBorder.Effect = null;"));
            Assert.That(method, Does.Contain("link.Click += (_, _) => NavigateToTask(taskId);"));
        });
    }

    [Test]
    public void InspectorTaskHistory_TracksNavigationAndSupportsBackFromToolbarAndMouseButton()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var navigateToTask = ExtractMethod(source, "private void NavigateToTask(string taskId)");
        var navigateBack = ExtractMethod(source, "private void NavigateBack()");
        var prependInspectorToolbar = ExtractMethod(source, "private void PrependInspectorToolbar()");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private readonly List<SelectedElementIdentity> _inspectorHistory = [];")
                .And.Contain("private int _inspectorHistoryIndex = -1;")
                .And.Contain("private bool _suppressHistoryPush;"));
            Assert.That(navigateToTask, Does.Contain("SelectElement(\"task\", taskId);"));
            Assert.That(navigateBack, Does.Contain("if (_inspectorHistoryIndex <= 0) return;")
                .And.Contain("_inspectorHistoryIndex--;")
                .And.Contain("var target = _inspectorHistory[_inspectorHistoryIndex];")
                .And.Contain("_suppressHistoryPush = true;")
                .And.Contain("SelectElement(target.Kind, target.Id);")
                .And.Contain("_suppressHistoryPush = false;"));
            Assert.That(prependInspectorToolbar, Does.Contain("if (_inspectorHistoryIndex > 0)")
                .And.Contain("AddButton(\"← Back\", NavigateBack, \"Go back to the previously inspected element\")"));
            Assert.That(source, Does.Contain("if (e.ChangedButton != MouseButton.XButton1)")
                .And.Contain("NavigateBack();"));
        });
    }

    [Test]
    public void TaskDependencyDetail_UsesTaskLinksForKnownDependenciesAndRunFallbackForUnknownOnes()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private void PopulateTaskDetail(PlanTask task, Plan? plan)");
        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("if (task.DependsOn is { Count: > 0 })"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));"));
            Assert.That(method, Does.Contain("if (depTask is not null)"));
            Assert.That(method, Does.Contain("var depStatusRun = new Run(depStatusText);"));
            Assert.That(method, Does.Contain("depStatusRun.SetResourceReference(TextElement.ForegroundProperty, depIsActive ? \"ImportantText\" : \"SubtleText\");"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(depStatusRun);"));
            Assert.That(method, Does.Contain("depPara.Inlines.Add(new Run(depTitle));"));
            Assert.That(method.IndexOf("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));", StringComparison.Ordinal),
                Is.GreaterThan(method.IndexOf("if (depTask is not null)", StringComparison.Ordinal)));
            Assert.That(method.IndexOf("depPara.Inlines.Add(new Run(depTitle));", StringComparison.Ordinal),
                Is.GreaterThan(method.IndexOf("depPara.Inlines.Add(CreateTaskLink(depTitle, depId));", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void BorderByTask_IsClassFieldAndNotLocallyShadowed()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var classScope = source[..source.IndexOf("internal string? CurrentRevision", StringComparison.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(classScope, Does.Contain("private Dictionary<string, Border> _borderByTask = new(StringComparer.Ordinal);"));
            Assert.That(source, Does.Not.Contain("var _borderByTask ="));
            Assert.That(Regex.IsMatch(source, @"(?m)^\s*Dictionary<string,\s*Border>\s+_borderByTask\s*="), Is.False);
        });
    }

    [Test]
    public void CompletedTaskInspector_UsesStructuredBulletsAndRemovesDuplicateHandoffNoise()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private void PopulateTaskDetail(PlanTask task, Plan? plan)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("task.CompletionSummaryItems is { Count: > 0 }")
                .And.Contain("? [task.CompletionSummary]")
                .And.Contain("MarkerStyle = TextMarkerStyle.Disc"));
            Assert.That(method, Does.Contain("ProofProvenancePresenter.FormatShortSha(commits[0].Commit)"));
            Assert.That(method, Does.Not.Contain("AddSectionHeader(\"Handoff\")")
                .And.Not.Contain("new Run(\"Commit: \")")
                .And.Not.Contain("new Run(\"Verification: \")"));
            Assert.That(method, Does.Contain("AddSectionHeader(\"Changed Files\")"));
            Assert.That(method, Does.Contain("AddSectionHeader(\"Verification\")")
                .And.Not.Contain("AddSectionHeader(\"Verification History\")"));
            Assert.That(method, Does.Contain("? \"Task Description\"")
                .And.Contain(": \"Original Task Description\""));
            Assert.That(method.IndexOf("Task Description", StringComparison.Ordinal),
                Is.LessThan(method.IndexOf("AppendApprovalGuidance", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void CompletionSummary_LinksEveryBulletUsingTheSameLabelsAsGraphNodes()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var populateTask = ExtractMethod(source, "private void PopulateTaskDetail(PlanTask task, Plan? plan)");
        var buildContent = ExtractMethod(source, "private void BuildContent(PendingDecomposePlan plan, Plan? durablePlan)");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(
                "private Dictionary<string, string> _taskOrdinalById = new(StringComparer.Ordinal);"));
            Assert.That(buildContent, Does.Contain(
                "_taskOrdinalById = new Dictionary<string, string>(StringComparer.Ordinal);"));
            Assert.That(buildContent, Does.Contain("_taskOrdinalById = group.Tasks")
                .And.Contain("string.Equals(candidate.TaskId, task.Id, StringComparison.Ordinal))?.DisplayStepLabel")
                .And.Contain("?? (index + 1).ToString()")
                .And.Contain("_taskOrdinalById[task.Id], activityState)"));
            Assert.That(populateTask, Does.Contain("foreach (var item in completionItems)")
                .And.Contain("PlanCompletionSummaryFormatter.CreateParagraph(")
                .And.Contain("item, _taskOrdinalById, CreateTaskLink)")
                .And.Not.Contain("new Paragraph(new Run(item))"));
        });
    }

    [Test]
    public void ChangedFiles_AreNonWrappingScrollableLinksWithStatusColors()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var panel = ExtractMethod(source,
            "private ScrollViewer BuildChangedFilesPanel(");
        var rows = ExtractMethod(source,
            "private StackPanel BuildChangedFileRows(");

        Assert.Multiple(() =>
        {
            Assert.That(panel, Does.Contain("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto"));
            Assert.That(rows, Does.Contain("TextWrapping = TextWrapping.NoWrap")
                .And.Contain("FileChangeStatus.Added => \"+\"")
                .And.Contain("FileChangeStatus.Deleted => \"−\"")
                .And.Contain("_ => \"Δ\"")
                .And.Contain("DiffAddedSummary")
                .And.Contain("DiffRemovedSummary")
                .And.Contain("DiffModifiedSummary")
                .And.Contain("_onOpenTaskFileDiff(task, path)"));
        });
    }

    [Test]
    public void VerificationFindings_UseAmberTaskCheckAndCriticalFailureShapeAndKeepEvidenceExpanded()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private void PopulateTaskDetail(PlanTask task, Plan? plan)");
        var icon = ExtractMethod(source,
            "private static FrameworkElement CreateVerificationClaimIcon(string? disposition)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("CreateVerificationClaimIcon(finding.Disposition)")
                .And.Contain("new Run(VerificationCheckLeadingSpace)")
                .And.Contain("new Paragraph(new Run(finding.Evidence))"));
            Assert.That(icon, Does.Contain("IsSupportedVerificationDisposition(disposition)")
                .And.Contain("\"TaskSucceededGeometry\"")
                .And.Contain("FrameworkElement.WidthProperty, \"FontSizeLargePlus\"")
                .And.Contain("FrameworkElement.HeightProperty, \"FontSizeLargePlus\"")
                .And.Contain("isSupported ? \"PriorityHigh\" : \"PriorityCritical\""));
            Assert.That(source, Does.Contain("VerificationCheckLeadingSpace = \"\\u2005\""));
        });
    }

    [Test]
    public void VerificationInspector_ShowsNewestResultAsCurrentAndSeparatesEarlierAttempts()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source,
            "private void PopulateVerificationDetail(string taskTitle, string taskId, string verificationLevel, bool verificationOnly, PlanTask? durableTask = null)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("history.OrderByDescending(report => report.CompletedAt)")
                .And.Contain("PlanTaskVerificationVerdict.Accepted => \"Verification passed\"")
                .And.Contain("AddSectionHeader(\"Previous Verification Attempts\")"));
            Assert.That(method, Does.Contain("CreateVerificationClaimIcon(finding.Disposition)")
                .And.Contain("new Run(VerificationCheckLeadingSpace)")
                .And.Not.Contain("finding.Disposition.Equals(\"confirmed\""));
        });
    }

    [Test]
    public void GateInspector_BothSelectionPathsRenderRequirementsOutsideEditMode()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var refresh = ExtractMethod(source, "private void RefreshDetailPanel()");
        var append = ExtractMethod(source, "private void AppendGateReviewRequirements(PlanApprovalGate gate)");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("hitTarget.Tag = $\"humanapproval:{selectionAnchor}\";"));
            Assert.That(refresh, Does.Contain("PopulateHumanApprovalDetail(_selectedElement.Id);"));
            foreach (var signature in new[]
            {
                "private void PopulateGateDetail(PlanApprovalGate gate, Plan? plan)",
                "private void PopulateHumanApprovalDetail(string gateId)",
            })
            {
                var method = ExtractMethod(source, signature);
                Assert.That(method, Does.Contain("AppendGateReviewRequirements(gate);")
                    .And.Contain("AppendApprovalContractEditor(gate);"));
                Assert.That(method.IndexOf("AppendGateReviewRequirements(gate);", StringComparison.Ordinal),
                    Is.LessThan(method.IndexOf("// Status (color-coded)", StringComparison.Ordinal)));
            }
            Assert.That(append, Does.Contain("PlanGateReviewRenderer.CreateReviewBlocks(gate, approvalQuestions, hasClarifiedReview)")
                .And.Contain("if (blocks.Count > 0)")
                .And.Contain("AddSectionHeader(\"Verify the Following:\")")
                .And.Contain("_detailDocument.Blocks.Add(block)")
                .And.Not.Contain("_isInspectorEditing"));
        });
    }

    [Test]
    public void GateInspector_PendingReadPathsPreserveTheSameProofContractAsTheEditor()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var refresh = ExtractMethod(source, "private void RefreshDetailPanel()");
        var humanApproval = ExtractMethod(source, "private void PopulateHumanApprovalDetail(string gateId)");
        var editor = ExtractMethod(source, "private bool PopulateGateEditor(string gateIdentity)");
        Assert.Multiple(() =>
        {
            Assert.That(refresh, Does.Contain("PendingDecomposePlanAdapter.MapApprovalGates(_plan.Group, _plan.Revision)")
                .And.Contain("PopulateGateDetail(decomposedGates[gateIndex], null)")
                .And.Not.Contain("var minimalGate"));
            Assert.That(humanApproval, Does.Contain("PendingDecomposePlanAdapter.MapApprovalGates(_plan.Group, _plan.Revision)"));
            Assert.That(editor, Does.Contain("gate.ProofRequirements ?? []")
                .And.Contain("value.Title")
                .And.Contain("value.Description")
                .And.Contain("value.Question"));
        });
    }

    [Test]
    public void GateInspector_PreservesClarifiedAndLegacyReviewWithoutGeneratedProofRestatements()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private void AppendGateReviewRequirements(PlanApprovalGate gate)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("hasClarifiedReview || gate.ProofRequirements is not { Count: > 0 }")
                .And.Contain("? ResolvePresentedHumanReviewQuestions(gate)")
                .And.Contain("string.IsNullOrWhiteSpace(gate.Question) ? [] : [gate.Question]"));
            Assert.That(method, Does.Contain("gate.HumanReviewDispositions is { Count: > 0 }"));
            Assert.That(Regex.Matches(method, "AddSectionHeader\\(\"Verify the Following:\"\\)").Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void GateInspector_ReadViewOmitsInternalProofMetadataAndRawIdentifiers()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var method = ExtractMethod(source, "private void PopulateGateDetail(PlanApprovalGate gate, Plan? plan)");
        var renderer = File.ReadAllText(FindRepoFile("SquadDash", "PlanGateReviewRenderer.cs"));

        Assert.Multiple(() =>
        {
            foreach (var field in new[] { "ProofType", "FixtureTaskId", "FixtureOutputId", "AccessMode", "VerificationRecipe" })
                Assert.That(renderer, Does.Not.Contain($"requirement.{field}"));
            Assert.That(Regex.Matches(renderer, @"requirement\.RequirementId").Count, Is.EqualTo(1),
                "The requirement ID may only be used as the human-friendly title fallback.");
            Assert.That(renderer, Does.Not.Contain("new Run(requirement.RequirementId)")
                .And.Not.Contain("FontSize =")
                .And.Not.Contain("Brushes."));
            Assert.That(method, Does.Contain("if (_isInspectorEditing)\n        {\n            var idPara")
                .Or.Contain("if (_isInspectorEditing)\r\n        {\r\n            var idPara"),
                "Raw gate IDs are only useful in edit mode.");
        });
    }

    private static void AssertThemeBrushColors(
        string themeXaml,
        string expectedTaskLinkText,
        string expectedChipSelectedBorder)
    {
        var taskLinkTextColor = ExtractBrushColor(themeXaml, "TaskLinkText");
        var chipSelectedBorderColor = ExtractBrushColor(themeXaml, "ChipSelectedBorder");

        Assert.Multiple(() =>
        {
            Assert.That(taskLinkTextColor, Is.EqualTo(expectedTaskLinkText));
            Assert.That(chipSelectedBorderColor, Is.EqualTo(expectedChipSelectedBorder));
        });
    }

    private static string ExtractBrushColor(string themeXaml, string brushKey)
    {
        var pattern = "<SolidColorBrush\\s+x:Key=\"" + Regex.Escape(brushKey) + "\"\\s+Color=\"(?<color>#[0-9A-Fa-f]{6})\"\\s*/>";
        var match = Regex.Match(themeXaml, pattern);
        Assert.That(match.Success, Is.True, $"Expected brush key '{brushKey}' in theme.");
        return match.Groups["color"].Value.ToUpperInvariant();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Could not find method signature: {signature}");

        var braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThan(start), $"Could not find method body for signature: {signature}");

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
                depth--;

            if (depth == 0)
                return source[start..(i + 1)];
        }

        Assert.Fail($"Could not find method end for signature: {signature}");
        return string.Empty;
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
