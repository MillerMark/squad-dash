using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.IO;
using System.Text.Json;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class MarkdownFlowDocumentBuilderTests {

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FlowDocument Build(string markdown) =>
        MarkdownFlowDocumentBuilder.Build(markdown, baseFontSize: 13.0);

    private static List GetOrderedList(FlowDocument doc, int index = 0) {
        var lists = doc.Blocks.OfType<List>()
            .Where(l => l.MarkerStyle == TextMarkerStyle.Decimal)
            .ToList();
        Assert.That(lists, Has.Count.GreaterThan(index),
            $"Expected at least {index + 1} ordered list(s) in document");
        return lists[index];
    }

    private static int CountOrderedLists(FlowDocument doc) =>
        doc.Blocks.OfType<List>().Count(l => l.MarkerStyle == TextMarkerStyle.Decimal);

    private static string GetListItemText(List list, int itemIndex) {
        var item = list.ListItems.ElementAt(itemIndex);
        var para = item.Blocks.OfType<Paragraph>().First();
        return string.Concat(para.Inlines.OfType<Run>().Select(r => r.Text));
    }

    // ── Tight ordered list (no blank lines) ─────────────────────────────────

    [Test]
    public void TightOrderedList_ThreeItems_AllInOneList() {
        var doc = Build("1. Alpha\n2. Beta\n3. Gamma");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        var list = GetOrderedList(doc);
        Assert.That(list.ListItems.Count, Is.EqualTo(3));
    }

    [Test]
    public void TightOrderedList_NumbersAreSequential() {
        var doc  = Build("1. First\n2. Second\n3. Third");
        var list = GetOrderedList(doc);

        Assert.That(GetListItemText(list, 0), Does.Contain("First"));
        Assert.That(GetListItemText(list, 1), Does.Contain("Second"));
        Assert.That(GetListItemText(list, 2), Does.Contain("Third"));
    }

    // ── Loose ordered list (blank lines between items) ───────────────────────

    [Test]
    public void LooseOrderedList_BlankLineBetweenTwoItems_SingleList() {
        var doc = Build("1. Alpha\n\n2. Beta");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1),
            "Blank line between ordered items should not split into separate lists");
        var list = GetOrderedList(doc);
        Assert.That(list.ListItems.Count, Is.EqualTo(2));
    }

    [Test]
    public void LooseOrderedList_BlankLineBetweenItems_TextPreserved() {
        var doc  = Build("1. First item\n\n2. Second item");
        var list = GetOrderedList(doc);

        Assert.That(GetListItemText(list, 0), Does.Contain("First item"));
        Assert.That(GetListItemText(list, 1), Does.Contain("Second item"));
    }

    [Test]
    public void LooseOrderedList_ThreeItemsWithBlanks_AllInOneList() {
        var doc = Build("1. One\n\n2. Two\n\n3. Three");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        var list = GetOrderedList(doc);
        Assert.That(list.ListItems.Count, Is.EqualTo(3));
    }

    [Test]
    public void LooseOrderedList_MultiParagraphItem_ContentPreserved() {
        // The clipboard example: items have multi-line body paragraphs
        var markdown =
            "1. **Silent error suppression** — catch blocks that swallow failures.\n" +
            "\n" +
            "2. **Catch-and-return-sentinel** — functions that catch internally.";

        var doc = Build(markdown);

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        Assert.That(GetOrderedList(doc).ListItems.Count, Is.EqualTo(2));
    }

    [Test]
    public void LooseOrderedList_TrailingBlankAfterLastItem_DoesNotCreateExtraList() {
        var doc = Build("1. Only\n\n2. Two\n\n");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
    }

    // ── Regression: blank line followed by non-list content breaks list ──────

    [Test]
    public void OrderedList_BlankThenParagraph_ListEndsCorrectly() {
        var doc = Build("1. Item one\n\nThis is a paragraph.");

        // Only one ordered list, with one item — blank + paragraph terminates the list
        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        Assert.That(GetOrderedList(doc).ListItems.Count, Is.EqualTo(1));

        // The paragraph should also appear in the document
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        Assert.That(paragraphs.Any(p =>
            string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text))
                  .Contains("This is a paragraph")),
            Is.True, "Paragraph after list should still render");
    }

    [Test]
    public void BuildInbox_StylesCamelCaseAndPascalCaseFileAsInlineCode() {
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            "Open PlanViewerWindow.cs and call selectElement while ordinary words remain prose.",
            baseFontSize: 13.0);
        var runs = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(run => run.Text == "PlanViewerWindow.cs").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Single(run => run.Text == "selectElement").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Any(run => run.Text.Contains("ordinary") &&
                                        !run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    [Test]
    public void BuildInbox_RespectsExplicitBackticksInHeading() {
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            "# Inspect `raw_value` in PlanViewerWindow.cs",
            baseFontSize: 13.0);
        var runs = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(run => run.Text == "raw_value").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Single(run => run.Text == "PlanViewerWindow.cs").FontFamily.Source,
                Does.Contain("Consolas"));
        });
    }

    [Test]
    public void BuildInbox_FencedCopyReadyTextUsesTranscriptCodeSurface()
    {
        const string prompt = "In this component, make IncrementCount add 2.";
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            $"Submit exactly:\n\n```text\n{prompt}\n```",
            baseFontSize: 13.0);

        var container = doc.Blocks.OfType<BlockUIContainer>().Single();
        var grid = (Grid)container.Child;
        var textBox = grid.Children.OfType<TextBox>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(textBox.Text, Is.EqualTo(prompt));
            Assert.That(textBox.Tag, Is.EqualTo("codeblock"));
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.ContextMenu?.Items.OfType<MenuItem>().Select(item => item.Header),
                Does.Contain("Copy"));
        });
    }

    [Test]
    public void BuildPlanDescription_RendersReadableMarkdownAndMarksHeadingLevels()
    {
        const string markdown =
            "Create the shell in PlanViewerWindow.cs.\n\n" +
            "## Observable outcome\n\n" +
            "The **published app** remains *usable* and calls `RenderScene`.\n\n" +
            "```csharp\nRenderScene();\n```";

        var doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(markdown, baseFontSize: 13.0);
        var heading = doc.Blocks.OfType<Paragraph>()
            .Single(block => MarkdownFlowDocumentBuilder.TryGetHeadingLevel(block, out _));
        var proseRuns = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();
        var codeBlock = doc.Blocks.OfType<BlockUIContainer>().Single();
        var codeTextBox = ((Grid)codeBlock.Child).Children.OfType<TextBox>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(MarkdownFlowDocumentBuilder.TryGetHeadingLevel(heading, out var level), Is.True);
            Assert.That(level, Is.EqualTo(2));
            Assert.That(string.Concat(heading.Inlines.OfType<Run>().Select(run => run.Text)),
                Is.EqualTo("Observable outcome"));
            Assert.That(proseRuns.Single(run => run.Text == "published app").FontWeight,
                Is.EqualTo(FontWeights.Bold));
            Assert.That(proseRuns.Single(run => run.Text == "usable").FontStyle,
                Is.EqualTo(FontStyles.Italic));
            Assert.That(proseRuns.Single(run => run.Text == "PlanViewerWindow.cs").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(proseRuns.Single(run => run.Text == "RenderScene").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(codeTextBox.Text, Is.EqualTo("RenderScene();"));
            Assert.That(codeTextBox.Tag, Is.EqualTo("codeblock"));
        });
    }

    [Test]
    public void BuildPlanDescription_LegacyPlainTextRemainsOneReadableParagraph()
    {
        const string description = "Implement the existing behavior without Markdown.";

        var doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(description, baseFontSize: 13.0);
        var paragraph = doc.Blocks.OfType<Paragraph>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)),
                Is.EqualTo(description));
            Assert.That(MarkdownFlowDocumentBuilder.TryGetHeadingLevel(paragraph, out _), Is.False);
        });
    }

    [Test]
    public void BuildPlanDescription_IncompleteMarkdownRemainsSafeAndVisible()
    {
        const string description = "Keep **unfinished emphasis visible.\n\n```text\npartial code";

        FlowDocument? doc = null;
        Assert.DoesNotThrow(() =>
            doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(description, baseFontSize: 13.0));

        var visibleText = string.Join("\n", doc!.Blocks.Select(block => block switch
        {
            Paragraph paragraph => string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)),
            BlockUIContainer { Child: Grid grid } => grid.Children.OfType<TextBox>().Single().Text,
            _ => string.Empty,
        }));
        Assert.Multiple(() =>
        {
            Assert.That(visibleText, Does.Contain("Keep **unfinished emphasis visible."));
            Assert.That(visibleText, Does.Contain("partial code"));
        });
    }

    [Test]
    public void BuildInbox_RendersContextualFileOrFolderLinkAndRoutesItsTarget()
    {
        string? clickedTarget = null;
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            "2. Open [the sample solution](app://open-path:samples/manual-test/Sample.sln) and continue.",
            baseFontSize: 13.0,
            onLinkClicked: target => clickedTarget = target);
        var link = doc.Blocks.OfType<List>()
            .Single()
            .ListItems.Single()
            .Blocks.OfType<Paragraph>()
            .Single()
            .Inlines.OfType<Hyperlink>()
            .Single();

        Assert.That(string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)),
            Is.EqualTo("the sample solution"));
        Assert.That(link.Tag, Is.EqualTo("app://open-path:samples/manual-test/Sample.sln"));

        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.That(clickedTarget, Is.EqualTo("app://open-path:samples/manual-test/Sample.sln"));
    }

    [Test]
    public void AddInboxInlineText_StylesIdentifiersOnTranscriptCards() {
        var paragraph = new Paragraph();

        MarkdownFlowDocumentBuilder.AddInboxInlineText(
            paragraph.Inlines,
            "Inspect ResponseTextBuilder in `MainWindow.xaml.cs`.");

        var runs = paragraph.Inlines.OfType<Run>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(run => run.Text == "ResponseTextBuilder").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Single(run => run.Text == "MainWindow.xaml.cs").FontFamily.Source,
                Does.Contain("Consolas"));
        });
    }

    [Test]
    public void AddInboxInlineText_StylesProtocolPayloadNameWithoutItalicizingUnderscores()
    {
        var paragraph = new Paragraph();

        MarkdownFlowDocumentBuilder.AddInboxInlineText(
            paragraph.Inlines,
            "The response did not contain a valid DECOMPOSE_STEP_RESULT_JSON payload.");

        var protocolRun = paragraph.Inlines.OfType<Run>()
            .Single(run => run.Text == "DECOMPOSE_STEP_RESULT_JSON");
        Assert.Multiple(() =>
        {
            Assert.That(protocolRun.FontFamily.Source, Does.Contain("Consolas"));
            Assert.That(protocolRun.FontStyle, Is.Not.EqualTo(FontStyles.Italic));
        });
    }

    [Test]
    public void Build_DefaultRenderer_DoesNotAutoStyleIdentifiers() {
        var doc = Build("PlanViewerWindow.cs remains ordinary outside Inbox.");
        var runs = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();

        Assert.That(runs, Has.None.Matches<Run>(run =>
            run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void PlanDescriptionPresentation_AppendPlanAndTaskDescriptions_UseFactoryKeysAndStartCollapsed()
    {
        using var workspace = new TestWorkspace();
        var task = new PlanTask("TASK-01", "Task one", "Task body", [], "high", PlanTaskStatus.Pending, DisplayStepLabel: "1");
        var presentation = new PlanDescriptionPresentation(
            workspace.RootPath,
            "PLAN-STEP4",
            [task],
            13.0,
            store: new MarkdownDetailStateStore(workspace.GetPath("state.json")));
        var target = new FlowDocument();
        const string planSummary = "Plan intro.\n\n:::detail Plan detail\nPlan detail body.\n:::";
        const string taskDescription = "Task intro.\n\n:::detail Task detail\nTask detail body.\n:::";

        try
        {
            presentation.AppendPlanDescription(target, planSummary);
            presentation.AppendTaskDescription(target, "TASK-01", taskDescription);

            var details = MarkdownVisibleContent.Details(target).ToArray();
            var planKey = MarkdownDetailIdentity.ForPlanDescription(workspace.RootPath, "PLAN-STEP4")
                .GetSectionKey("Plan detail", "Plan detail body.");
            var taskKey = MarkdownDetailIdentity.ForTaskDescription(workspace.RootPath, "PLAN-STEP4", "TASK-01")
                .GetSectionKey("Task detail", "Task detail body.");

            Assert.Multiple(() =>
            {
                Assert.That(details, Has.Length.EqualTo(2));
                Assert.That(details[0].Key, Is.EqualTo(planKey));
                Assert.That(details[1].Key, Is.EqualTo(taskKey));
                Assert.That(details.All(detail => !detail.Expander.IsExpanded), Is.True);
            });

            details[0].Expander.IsExpanded = true;
            Assert.That(MarkdownVisibleContent.GetText(target), Does.Contain("Plan detail body."));
        }
        finally
        {
            MarkdownVisibleContent.Detach(target);
        }
    }

    [Test]
    public void PlanDescriptionPresentation_StoreIdentityRestoresAcrossStagedToDurableAndSyncsInspectorWithGraph()
    {
        using var workspace = new TestWorkspace();
        var group = new DecomposedTaskGroup(
            "PLAN-STEP4",
            "Step 4",
            "main",
            "Summary\n\n:::detail Plan persisted\nPlan summary body.\n:::",
            [new DecomposedSubTask("TASK-01", "Task lead.\n\n:::detail Shared body\n1. checklist item\n:::", [], "high", "Task one")]);
        var pending = new PendingDecomposePlan("rev-step4", group, DateTimeOffset.UtcNow);
        var durable = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UnixEpoch);
        var storePath = workspace.GetPath("state.json");
        var store = new MarkdownDetailStateStore(storePath);

        FlowDocument? inspectorA = null;
        FlowDocument? graphA = null;
        FlowDocument? inspectorB = null;
        FlowDocument? graphB = null;
        FlowDocument? planB = null;
        FlowDocument? planA = null;
        try
        {
            var stagedPresenter = new PlanDescriptionPresentation(
                workspace.RootPath,
                pending.Group.GroupId,
                durable.Tasks,
                12.0,
                store: store);
            inspectorA = new FlowDocument();
            stagedPresenter.AppendTaskDescription(inspectorA, "TASK-01", group.Tasks[0].Description);
            graphA = stagedPresenter.BuildTaskDescription("TASK-01", group.Tasks[0].Description);
            var inspectorDetailA = MarkdownVisibleContent.Details(inspectorA).Single();
            var graphDetailA = MarkdownVisibleContent.Details(graphA).Single();

            Assert.That(inspectorDetailA.Expander.IsExpanded || graphDetailA.Expander.IsExpanded, Is.False);
            inspectorDetailA.Expander.IsExpanded = true;
            Assert.That(graphDetailA.Expander.IsExpanded, Is.True);
            graphDetailA.Expander.IsExpanded = false;
            Assert.That(inspectorDetailA.Expander.IsExpanded, Is.False);
            graphDetailA.Expander.IsExpanded = true;
            Assert.That(inspectorDetailA.Expander.IsExpanded, Is.True);

            planA = new FlowDocument();
            stagedPresenter.AppendPlanDescription(planA, group.Summary);
            var planDetailA = MarkdownVisibleContent.Details(planA).Single();
            Assert.That(planDetailA.Expander.IsExpanded, Is.False);
            planDetailA.Expander.IsExpanded = true;
            MarkdownVisibleContent.Detach(inspectorA);
            MarkdownVisibleContent.Detach(graphA);
            MarkdownVisibleContent.Detach(planA);

            var durablePresenter = new PlanDescriptionPresentation(
                workspace.RootPath,
                durable.PlanId,
                durable.Tasks,
                19.0,
                store: new MarkdownDetailStateStore(storePath));
            inspectorB = new FlowDocument();
            durablePresenter.AppendTaskDescription(inspectorB, "TASK-01", group.Tasks[0].Description);
            graphB = durablePresenter.BuildTaskDescription("TASK-01", group.Tasks[0].Description);
            planB = new FlowDocument();
            durablePresenter.AppendPlanDescription(planB, group.Summary);
            var inspectorDetailB = MarkdownVisibleContent.Details(inspectorB).Single();
            var graphDetailB = MarkdownVisibleContent.Details(graphB).Single();
            var planDetailB = MarkdownVisibleContent.Details(planB).Single();

            Assert.Multiple(() =>
            {
                Assert.That(durable.PlanId, Is.EqualTo(pending.Group.GroupId));
                Assert.That(inspectorDetailB.Expander.IsExpanded, Is.True);
                Assert.That(graphDetailB.Expander.IsExpanded, Is.True);
                Assert.That(graphB.FontSize, Is.EqualTo(19.0));
                Assert.That(planDetailB.Expander.IsExpanded, Is.True);
                Assert.That(planDetailB.Key, Is.EqualTo(planDetailA.Key));
                Assert.That(inspectorDetailB.Key, Is.EqualTo(inspectorDetailA.Key));
                Assert.That(inspectorDetailB.Key, Is.Not.EqualTo(planDetailB.Key));
            });
            inspectorDetailB.Expander.IsExpanded = false;
            planDetailB.Expander.IsExpanded = false;
            var reopened = new PlanDescriptionPresentation(workspace.RootPath, durable.PlanId,
                durable.Tasks, 12.0, store: new MarkdownDetailStateStore(storePath));
            var reopenedDocument = new FlowDocument();
            try
            {
                reopened.AppendPlanDescription(reopenedDocument, group.Summary);
                reopened.AppendTaskDescription(reopenedDocument, "TASK-01", group.Tasks[0].Description);
                Assert.That(MarkdownVisibleContent.Details(reopenedDocument).All(d => !d.Expander.IsExpanded), Is.True);
            }
            finally { MarkdownVisibleContent.Detach(reopenedDocument); }
        }
        finally
        {
            if (planB is not null) MarkdownVisibleContent.Detach(planB);
            if (planA is not null) MarkdownVisibleContent.Detach(planA);
            if (graphB is not null) MarkdownVisibleContent.Detach(graphB);
            if (inspectorB is not null) MarkdownVisibleContent.Detach(inspectorB);
            if (graphA is not null) MarkdownVisibleContent.Detach(graphA);
            if (inspectorA is not null) MarkdownVisibleContent.Detach(inspectorA);
        }
    }

    [Test]
    public void PlanDescriptionPresentation_ToggleStateDoesNotMutatePendingOrDurableSnapshotsOrRevision()
    {
        using var workspace = new TestWorkspace();
        var group = new DecomposedTaskGroup(
            "PLAN-STEP4-SNAPSHOT",
            "Snapshot plan",
            "main",
            "Summary",
            [new DecomposedSubTask("TASK-01", "Task body\n\n:::detail Identity\nUnchanged payload.\n:::", [], "high", "Task one")]);
        var pending = new PendingDecomposePlan("rev-unchanged", group, DateTimeOffset.UtcNow);
        var durable = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UnixEpoch);
        var pendingSnapshot = JsonSerializer.Serialize(pending.Group);
        var durableSnapshot = JsonSerializer.Serialize(durable);
        var pendingRevision = pending.Revision;
        var durableRevision = durable.Revision;

        var presentation = new PlanDescriptionPresentation(
            workspace.RootPath,
            durable.PlanId,
            durable.Tasks,
            13.0,
            store: new MarkdownDetailStateStore(workspace.GetPath("state.json")));
        var document = new FlowDocument();
        try
        {
            presentation.AppendTaskDescription(document, "TASK-01", group.Tasks[0].Description);
            var detail = MarkdownVisibleContent.Details(document).Single();
            detail.Expander.IsExpanded = true;
            detail.Expander.IsExpanded = false;
            detail.Expander.IsExpanded = true;

            Assert.Multiple(() =>
            {
                Assert.That(pending.Revision, Is.EqualTo(pendingRevision));
                Assert.That(durable.Revision, Is.EqualTo(durableRevision));
                Assert.That(durable.Source, Is.EqualTo(PlanSource.TasksJson));
                Assert.That(JsonSerializer.Serialize(pending.Group), Is.EqualTo(pendingSnapshot));
                Assert.That(JsonSerializer.Serialize(durable), Is.EqualTo(durableSnapshot));
            });
        }
        finally
        {
            MarkdownVisibleContent.Detach(document);
        }
    }

    [Test]
    public void PlanDescriptionDocumentSession_CopyFindSelectionAndLinksRespectVisibilityAndSharedProjection()
    {
        using var workspace = new TestWorkspace();
        var tasks = (IReadOnlyList<PlanTask>)
        [
            new PlanTask("TASK-01", "First", "Description", [], "high", PlanTaskStatus.Pending, DisplayStepLabel: "1"),
        ];
        var openedTargets = new List<string>();
        string? openedTaskId = null;
        var presentation = new PlanDescriptionPresentation(
            workspace.RootPath,
            "PLAN-STEP4-LINKS",
            tasks,
            14.0,
            onLinkClicked: target => openedTargets.Add(target),
            createTaskLink: (label, taskId) =>
            {
                var link = new Hyperlink(new Run(label)) { Tag = taskId };
                link.Click += (_, _) => openedTaskId = taskId;
                return link;
            },
            store: new MarkdownDetailStateStore(workspace.GetPath("state.json")));
        var root = new FlowDocument();
        const string description =
            "## Inspector heading\n" +
            "See task-01 and [Guide](https://example.com).\n\n" +
            ":::detail Expanded evidence\n" +
            "### Nested heading\nSee task-01.\n\n1. checklist item\n\n" +
            "```js\nconsole.log('ok');\n```\n\n" +
            "[Inside](https://example.org)\n" +
            ":::";
        presentation.AppendTaskDescription(root, "TASK-01", description);
        var viewer = new FlowDocumentScrollViewer { Document = root };
        var session = new PlanDescriptionDocumentSession(viewer);
        try
        {
            var heading = root.Blocks.OfType<Paragraph>()
                .Single(block => MarkdownFlowDocumentBuilder.TryGetHeadingLevel(block, out _));
            var detail = MarkdownVisibleContent.Details(root).Single();
            var nestedHeading = detail.Viewer.Document.Blocks.OfType<Paragraph>()
                .Single(block => MarkdownFlowDocumentBuilder.TryGetHeadingLevel(block, out _));
            var collapsedCopy = PlanDescriptionDocumentSession.GetCopyText(root);

            Assert.Multiple(() =>
            {
                Assert.That(MarkdownFlowDocumentBuilder.TryGetHeadingLevel(heading, out var level) && level == 2, Is.True);
                Assert.That(heading.FontWeight, Is.EqualTo(FontWeights.SemiBold));
                Assert.That(nestedHeading.FontWeight, Is.EqualTo(FontWeights.SemiBold));
                Assert.That(Hyperlinks(detail.Viewer.Document).Any(link => Equals(link.Tag, "TASK-01")), Is.True);
                Assert.That(collapsedCopy, Does.Contain("Expanded evidence").And.Not.Contain("checklist item").And.Not.Contain("console.log"));
                Assert.That(PlanDescriptionDocumentSession.FindAll(root, "console.log"), Is.Empty);
            });

            detail.Expander.IsExpanded = true;
            var expandedCopy = PlanDescriptionDocumentSession.GetCopyText(root);
            var listMatch = PlanDescriptionDocumentSession.FindAll(root, "1. checklist item").Single();
            var codeMatch = PlanDescriptionDocumentSession.FindAll(root, "console.log('ok');").Single();
            listMatch.Select(viewer);
            string? copied = null;
            var listCopyEvent = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "raw"), false);
            PlanDescriptionDocumentSession.CopySelection(viewer, listCopyEvent, text => copied = text);
            Assert.That(copied, Is.EqualTo("1. checklist item"));

            codeMatch.Select(viewer);
            var codeCopyEvent = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "raw"), false);
            PlanDescriptionDocumentSession.CopySelection(viewer, codeCopyEvent, text => copied = text);

            Assert.Multiple(() =>
            {
                Assert.That(expandedCopy, Does.Contain("1. checklist item").And.Contain("console.log('ok');"));
                Assert.That(copied, Is.EqualTo("console.log('ok');"));
            });

            foreach (var link in Hyperlinks(root))
                link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(openedTaskId, Is.EqualTo("TASK-01"));
                Assert.That(openedTargets, Does.Contain("https://example.com"));
                Assert.That(openedTargets, Does.Contain("https://example.org"));
            });
            detail.Expander.IsExpanded = false;
            codeMatch.Select(viewer);
            copied = null;
            PlanDescriptionDocumentSession.CopySelection(viewer,
                new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "raw"), false),
                text => copied = text);
            Assert.Multiple(() =>
            {
                Assert.That(copied, Is.Null);
                Assert.That(PlanDescriptionDocumentSession.FindAll(root, "console.log"), Is.Empty);
                Assert.That(PlanDescriptionDocumentSession.GetCopyText(root), Does.Not.Contain("console.log")
                    .And.Not.Contain(":::"));
            });
        }
        finally
        {
            session.Dispose();
        }
    }

    [Test]
    public void PlanViewerArchitecture_PlanAndTaskDescriptionsRouteThroughPresentationAndSharedConsumers()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var buildContent = ExtractMethod(source, "private void BuildContent(PendingDecomposePlan plan, Plan? durablePlan)");
        var populateSelected = ExtractMethod(source, "private void RefreshDetailPanel()");
        var populateTask = ExtractMethod(source, "private void PopulateTaskDetail(PlanTask task, Plan? plan)");
        var appendTask = ExtractMethod(source, "private void AppendTaskDescription(string description, string taskId)");
        var bindPreview = ExtractMethod(source, "private void BindTaskDescriptionPreview(string taskId, string description, TextBlock preview)");
        var prependToolbar = ExtractMethod(source, "private void PrependInspectorToolbar()");

        Assert.Multiple(() =>
        {
            Assert.That(buildContent, Does.Contain("_descriptionPresentation = new PlanDescriptionPresentation("));
            Assert.That(buildContent, Does.Contain("durablePlan?.PlanId ?? plan.Group.GroupId"));
            Assert.That(populateSelected, Does.Contain("_descriptionPresentation!.AppendPlanDescription(_detailDocument, group.Summary);"));
            Assert.That(populateSelected, Does.Contain("PopulateTaskDetail(durableTask, _durablePlan!);"));
            Assert.That(populateSelected, Does.Contain("PopulateTaskDetail(minimalTask, null!);"));
            Assert.That(populateTask, Does.Contain("AppendTaskDescription(task.Description, task.TaskId);"));
            Assert.That(appendTask, Does.Contain("_descriptionPresentation!.AppendTaskDescription(_detailDocument, taskId, description);"));
            Assert.That(bindPreview, Does.Contain("var document = _descriptionPresentation!.BuildTaskDescription(taskId, description);")
                .And.Contain("preview.Text = MarkdownVisibleContent.GetText(document);"));
            Assert.That(prependToolbar, Does.Contain("_descriptionSession = new PlanDescriptionDocumentSession(_detailViewer);")
                .And.Contain("PlanDescriptionDocumentSession.GetCopyText(_detailDocument!)"));
        });
    }

    [Test]
    public void PlanViewerArchitecture_VerificationDetailSessionIsOwnedByRefreshCycle()
    {
        var source = File.ReadAllText(FindRepoFile("SquadDash", "PlanViewerWindow.cs"));
        var refreshDetail = ExtractMethod(source, "private void RefreshDetailPanel()");
        var populateVerification = ExtractMethod(source, "private void PopulateVerificationDetail(string taskTitle, string taskId, string verificationLevel, bool verificationOnly, PlanTask? durableTask = null)");
        var verificationBranchStart = refreshDetail.IndexOf("else if (_selectedElement.Kind == \"verification\")", StringComparison.Ordinal);
        Assert.That(verificationBranchStart, Is.GreaterThanOrEqualTo(0), "Could not find verification branch in RefreshDetailPanel.");
        var verificationBranchEnd = refreshDetail.IndexOf("else if (_selectedElement.Kind == \"gate\")", verificationBranchStart, StringComparison.Ordinal);
        Assert.That(verificationBranchEnd, Is.GreaterThan(verificationBranchStart), "Could not isolate verification branch in RefreshDetailPanel.");
        var verificationBranch = refreshDetail[verificationBranchStart..verificationBranchEnd];

        Assert.Multiple(() =>
        {
            Assert.That(verificationBranch, Does.Contain("PopulateVerificationDetail(taskTitle, taskId, verificationLevel, verificationOnly, durableTask);"));
            Assert.That(verificationBranch, Does.Not.Contain("PrependInspectorToolbar();"),
                "Verification branch must not create/attach a document session directly.");
            Assert.That(populateVerification, Does.Not.Contain("PrependInspectorToolbar();"),
                "PopulateVerificationDetail must not attach duplicate toolbar/session owners.");
            Assert.That(refreshDetail, Does.Contain("_descriptionSession?.Dispose();")
                .And.Contain("_descriptionSession = null;")
                .And.Contain("PrependInspectorToolbar();"));
        });
    }

    private static IEnumerable<Hyperlink> Hyperlinks(FlowDocument document)
    {
        foreach (var (child, _) in MarkdownVisibleContent.Documents(document, visibleOnly: false))
            foreach (var paragraph in MarkdownVisibleContent.Blocks(child.Blocks).OfType<Paragraph>())
                foreach (var link in Hyperlinks(paragraph.Inlines))
                    yield return link;
    }

    private static IEnumerable<Hyperlink> Hyperlinks(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Hyperlink link) yield return link;
            else if (inline is Span span)
                foreach (var nested in Hyperlinks(span.Inlines))
                    yield return nested;
        }
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Could not find signature: {signature}");
        var braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThan(start), $"Could not find method body for signature: {signature}");

        var depth = 0;
        for (var index = braceStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}') depth--;
            if (depth == 0)
                return source[start..(index + 1)];
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
