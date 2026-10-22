using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
internal sealed class MarkdownDetailRenderingTests
{
    private const string Labelled = "Use the existing store.\n\n:::detail Why persist this?\nWindow-local state disappears when the app restarts.\n:::\n\nContinue with the implementation.";
    private const string Explanation = "Window-local state disappears when the app restarts.";

    [TestCase("3. Prepare\n4. Execute", "3. Prepare", "4. Execute")]
    [TestCase("- Prepare\n- Execute", "\u2022 Prepare", "\u2022 Execute")]
    public void VisibleListMarkers_ProductionCopyFindAndSelectionRetainMarkers(
        string source, string first, string second)
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var message = new InboxMessage { Id = "list-markers", Body = source };
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var selections = MarkdownVisibleContent.TrackSelections(viewer);
        Assert.That(InboxMessageWindow.BuildMessageClipboardText(message, document),
            Is.EqualTo(first + Environment.NewLine + second));
        var match = InboxMessageWindow.FindTextInRange(document, first);
        Assert.That(match, Is.Not.Null);
        match!.Select(viewer);
        Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo(first));
        Assert.That(MarkdownVisibleContent.GetSelectionText(document,
            new TextRange(document.ContentStart, document.ContentEnd)),
            Is.EqualTo(first + Environment.NewLine + second));
        MarkdownVisibleContent.Detach(document);
    }

    [TestCase("\U0001F534")]
    [TestCase("\U0001F7E0")]
    [TestCase("\U0001F7E1")]
    [TestCase("\U0001F7E2")]
    [TestCase("\U0001F535")]
    [TestCase("\U0001F7E3")]
    [TestCase("\u26AB")]
    [TestCase("\u26AA")]
    [TestCase("\U0001F7E4")]
    public void CircleStatus_ProductionCopyFindAndSelectionRetainSemanticText(string symbol)
    {
        var phrase = symbol + " Ready";
        var document = MarkdownFlowDocumentBuilder.BuildInbox(phrase);
        var message = new InboxMessage { Id = "status", Body = phrase };
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var selections = MarkdownVisibleContent.TrackSelections(viewer);
        Assert.That(InboxMessageWindow.BuildMessageClipboardText(message, document), Is.EqualTo(phrase));
        foreach (var query in new[] { phrase, symbol, "Ready" })
        {
            var match = InboxMessageWindow.FindTextInRange(document, query);
            Assert.That(match, Is.Not.Null);
            match!.Select(viewer);
            Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo(query));
        }
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void DetailStatusAndLists_ProductionCopyFindAndSelectionRespectExpansion()
    {
        const string phrase = "\U0001F7E2 Ready";
        const string source = ":::detail Status\n" + phrase + "\n\n3. Prepare\n4. Execute\n\n- Finish\n:::";
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var message = new InboxMessage { Id = "detail-status", Body = source };
        var detail = MarkdownVisibleContent.Details(document).Single();
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var selections = MarkdownVisibleContent.TrackSelections(viewer);
        foreach (var expanded in new[] { false, true, false })
        {
            detail.Expander.IsExpanded = expanded;
            var copy = InboxMessageWindow.BuildMessageClipboardText(message, document);
            var selectionCopy = MarkdownVisibleContent.GetSelectionText(document,
                new TextRange(document.ContentStart, document.ContentEnd));
            foreach (var query in new[] { phrase, "3. Prepare", "4. Execute", "\u2022 Finish" })
            {
                var match = InboxMessageWindow.FindTextInRange(document, query);
                if (expanded)
                {
                    Assert.That(copy, Does.Contain(query));
                    Assert.That(selectionCopy, Does.Contain(query));
                    Assert.That(match, Is.Not.Null);
                    Assert.That(match!.Part.Document, Is.SameAs(detail.Viewer.Document));
                    match.Select(viewer);
                    Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo(query));
                }
                else
                {
                    Assert.That(copy, Is.EqualTo("Status"));
                    Assert.That(selectionCopy, Is.EqualTo("Status"));
                    Assert.That(match, Is.Null);
                    Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.Empty);
                }
            }
        }
        Assert.That(message.Body, Is.EqualTo(source));
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void TableStatus_ProductionCopyAndFindRetainSemanticText()
    {
        const string phrase = "\U0001F7E2 Ready";
        const string source = "| Status |\n| --- |\n| " + phrase + " |";
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var message = new InboxMessage { Id = "table-status", Body = source };
        Assert.That(InboxMessageWindow.BuildMessageClipboardText(message, document), Does.Contain(phrase));
        var match = InboxMessageWindow.FindTextInRange(document, phrase);
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Part.Control, Is.TypeOf<TextBlock>());
        Assert.DoesNotThrow(() => match.Select(new FlowDocumentScrollViewer { Document = document }));
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void LabelledDetail_CopySearchAndSelectionFollowExpansion()
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(Labelled);
        var message = new InboxMessage { Id = "labelled", Body = Labelled };
        var detail = MarkdownVisibleContent.Details(document).Single();
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var selections = MarkdownVisibleContent.TrackSelections(viewer);
        var selection = new TextRange(document.ContentStart, document.ContentEnd);
        Assert.Multiple(() =>
        {
            Assert.That(detail.Expander.IsExpanded, Is.False);
            Assert.That(detail.Expander.Header, Is.TypeOf<TextBlock>());
            Assert.That(detail.Viewer.Document, Is.Not.SameAs(document));
            Assert.That(InboxMessageWindow.BuildMessageClipboardText(message, document), Does.Contain("Why persist this?")
                .And.Contain("Use the existing store.").And.Contain("Continue with the implementation.").And.Not.Contain(Explanation));
            Assert.That(MarkdownVisibleContent.GetSelectionText(document, selection), Does.Not.Contain(Explanation));
            Assert.That(InboxMessageWindow.FindTextInRange(document, Explanation), Is.Null);
        });

        detail.Expander.IsExpanded = true;
        var copied = InboxMessageWindow.BuildMessageClipboardText(message, document);
        Assert.Multiple(() =>
        {
            Assert.That(copied, Does.Contain(Explanation));
            Assert.That(copied.IndexOf(Explanation, StringComparison.Ordinal), Is.GreaterThan(copied.IndexOf("Why persist this?", StringComparison.Ordinal)));
            Assert.That(copied.IndexOf(Explanation, StringComparison.Ordinal), Is.LessThan(copied.IndexOf("Continue", StringComparison.Ordinal)));
            Assert.That(MarkdownVisibleContent.GetSelectionText(document, selection), Does.Contain(Explanation));
        });
        var match = InboxMessageWindow.FindTextInRange(document, Explanation)!;
        match.Select(viewer);
        Assert.That(detail.Viewer.Selection.Text, Is.EqualTo(Explanation));
        Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo(Explanation));

        var invalidations = 0;
        MarkdownVisibleContent.State(document).VisibilityChanged += () => invalidations++;
        detail.Expander.IsExpanded = false;
        match.Select(viewer); // An old match cannot resurrect hidden selection.
        Assert.Multiple(() =>
        {
            Assert.That(invalidations, Is.EqualTo(1));
            Assert.That(detail.Viewer.Selection.IsEmpty, Is.True);
            Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.Empty);
            Assert.That(InboxMessageWindow.FindTextInRange(document, Explanation), Is.Null);
        });
        MarkdownVisibleContent.Detach(document);
    }

    [TestCase(":::detail\nSupporting explanation.\n:::", "More detail", "Supporting explanation.")]
    [TestCase(":::detail   \nSupporting explanation.\n:::", "More detail", "Supporting explanation.")]
    [TestCase(":::detail Empty\n:::", "Empty", "")]
    [TestCase(":::detail expanded=true\nStill collapsed.\n:::", "expanded=true", "Still collapsed.")]
    public void ValidSections_DefaultLabelEmptyBodyAndNoAuthoredExpansion(string source, string label, string body)
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var detail = MarkdownVisibleContent.Details(document).Single();
        Assert.Multiple(() =>
        {
            Assert.That(detail.Label, Is.EqualTo(label));
            Assert.That(detail.Expander.IsExpanded, Is.False);
            Assert.That(MarkdownVisibleContent.GetText(document), Is.EqualTo(label));
            Assert.That(MarkdownVisibleContent.State(document).Diagnostics, Is.Empty);
        });
        detail.Expander.IsExpanded = true;
        Assert.That(MarkdownVisibleContent.GetText(document), Does.Contain(body));
        MarkdownVisibleContent.Detach(document);
    }

    [TestCase("Before.\n:::detail Outer\nOuter explanation.\n:::detail Inner\nInner explanation.\n:::\n:::\nAfter.", "Nested")]
    [TestCase("Before.\n:::detail Missing\nUnbalanced explanation.\nAfter.", "Unbalanced")]
    [TestCase("Before.\n:::\nAfter.", "Stray")]
    [TestCase(":::detail Outer\n:::detail Inner\nInner explanation.\n:::\nAfter.", "Unbalanced")]
    public void MalformedRegions_AreEntirelyLiteralWithDiagnostics(string source, string diagnostic)
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var message = new InboxMessage { Id = "malformed", Body = source };
        var text = InboxMessageWindow.BuildMessageClipboardText(message, document);
        Assert.Multiple(() =>
        {
            Assert.That(MarkdownVisibleContent.Details(document), Is.Empty);
            Assert.That(MarkdownVisibleContent.State(document).Diagnostics.Single(), Does.Contain(diagnostic));
            foreach (var line in source.Split('\n'))
                Assert.That(text, Does.Contain(line));
            Assert.That(InboxMessageWindow.FindTextInRange(document, "After."), Is.Not.Null);
        });
    }

    [Test]
    public void MultipleAndMalformedRegions_KeepUnaffectedSectionsUsable()
    {
        var source = ":::detail First\nFirst body.\n:::\n:::detail Outer\n:::detail Inner\nNested body.\n:::\n:::\n:::detail Last\nLast body.\n:::\nTail.";
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var details = MarkdownVisibleContent.Details(document).ToArray();
        Assert.That(details.Select(detail => detail.Label), Is.EqualTo(new[] { "First", "Last" }));
        details[1].Expander.IsExpanded = true;
        var text = MarkdownVisibleContent.GetText(document);
        Assert.That(text, Does.Not.Contain("First body.").And.Contain("Last body.").And.Contain(":::detail Inner").And.Contain("Tail."));
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void CodeFencedMarkers_AreOneOrdinaryCopyableCodeBlockWithoutWarnings()
    {
        const string code = ":::detail Example\nLiteral sample.\n:::";
        var document = MarkdownFlowDocumentBuilder.BuildInbox("```markdown\n" + code + "\n```");
        var part = MarkdownVisibleContent.Project(document).Single();
        Assert.Multiple(() =>
        {
            Assert.That(MarkdownVisibleContent.Details(document), Is.Empty);
            Assert.That(document.Blocks.Count, Is.EqualTo(1));
            Assert.That(part.Control, Is.TypeOf<TextBox>());
            Assert.That(part.Text.Replace("\r\n", "\n"), Is.EqualTo(code));
            Assert.That(InboxMessageWindow.FindTextInRange(document, "Literal sample."), Is.Not.Null);
            Assert.That(MarkdownVisibleContent.State(document).Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void DetailBody_CodeMarkersDoNotCloseOrNest_AndCodeCopyIsVisibilityGuarded()
    {
        const string source = ":::detail Samples\n```md\n:::detail Example\nLiteral sample.\n:::\n```\nAfter code.\n:::\nTail.";
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        var detail = MarkdownVisibleContent.Details(document).Single();
        var code = (TextBox)MarkdownVisibleContent.Project(detail.Viewer.Document).First().Control!;
        code.SelectAll();
        Assert.Multiple(() =>
        {
            Assert.That(CopyableCodeBlockBuilder.GetCopyText(code, false), Is.Empty);
            Assert.That(CopyableCodeBlockBuilder.GetCopyText(code, true), Is.Empty);
            Assert.That(InboxMessageWindow.FindTextInRange(document, "Literal sample."), Is.Null);
        });
        detail.Expander.IsExpanded = true;
        Assert.That(CopyableCodeBlockBuilder.GetCopyText(code, false), Does.Contain("Literal sample."));
        var viewer = new FlowDocumentScrollViewer { Document = document };
        InboxMessageWindow.FindTextInRange(document, "Literal sample.")!.Select(viewer);
        Assert.That(code.SelectedText, Is.EqualTo("Literal sample."));
        detail.Expander.IsExpanded = false;
        Assert.That(code.SelectedText, Is.Empty);
        Assert.That(CopyableCodeBlockBuilder.GetCopyText(code, false), Is.Empty);
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void SourceMap_PreservesDetailRegionAndAdjacentMarkdownBoundaries()
    {
        var document = MarkdownFlowDocumentBuilder.BuildWithMap(
            "Lead.\n:::detail Label\n# Body\n:::\nTail.", out var ranges);
        Assert.That(ranges, Is.EqualTo(new[] { (0, 0), (1, 3), (4, 4) }));
        Assert.That(document.Blocks.ElementAt(1), Is.TypeOf<BlockUIContainer>());
        MarkdownVisibleContent.Detach(document);
    }

    [TestCase("- Item\n  :::detail Label\nBody.\n:::\nTail.")]
    [TestCase("1. Item\n  :::detail Label\nBody.\n:::\nTail.")]
    public void IndentedStandaloneMarker_IsNotAbsorbedByPrecedingList(string source)
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(source);
        Assert.That(MarkdownVisibleContent.Details(document).Single().Label, Is.EqualTo("Label"));
        Assert.That(MarkdownVisibleContent.GetText(document), Does.Not.Contain("Body.").And.Contain("Tail."));
    }

    [Test]
    public void DetailBody_RetainsSharedMarkdownTypographyLinksListsTablesAndHeadingMetadata()
    {
        string? clicked = null;
        var document = MarkdownFlowDocumentBuilder.BuildPlanDescription(
            ":::detail Formatting\n# Heading\nA **bold phrase** and `raw_value` in PlanViewerWindow.cs.\n\n- Bullet\n\n1. Numbered\n\n| A | B |\n| --- | --- |\n| Cell | Value |\n\n[Link](https://example.com)\n:::",
            18, target => clicked = target);
        var detail = MarkdownVisibleContent.Details(document).Single();
        var body = detail.Viewer.Document;
        var heading = body.Blocks.OfType<Paragraph>().First();
        var link = body.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines.OfType<Hyperlink>()).Single();
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        detail.Expander.IsExpanded = true;
        Assert.Multiple(() =>
        {
            Assert.That(MarkdownFlowDocumentBuilder.TryGetHeadingLevel(heading, out var level) && level == 1, Is.True);
            Assert.That(body.FontSize, Is.EqualTo(18));
            Assert.That(body.Blocks.OfType<List>().Count(), Is.EqualTo(2));
            Assert.That(body.Blocks.OfType<BlockUIContainer>().Count(), Is.EqualTo(1));
            Assert.That(clicked, Is.EqualTo("https://example.com"));
            Assert.That(InboxMessageWindow.FindTextInRange(document, "A bold phrase and"), Is.Not.Null);
            Assert.That(MarkdownVisibleContent.GetText(document), Does.Contain("Cell").And.Contain("Numbered"));
        });
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void ClipboardEvent_UsesProjectionAndCancelsDefaultEvenOnContention()
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox(Labelled);
        var viewer = new FlowDocumentScrollViewer { Document = document };
        viewer.ApplyTemplate();
        viewer.Selection.Select(document.ContentStart, document.ContentEnd);
        string? copied = null;
        var e = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "unfiltered"), false);
        InboxMessageWindow.CopyVisibleSelection(viewer, e, text => copied = text);
        Assert.That(copied, Does.Contain("Why persist this?").And.Not.Contain(Explanation));
        Assert.That(e.CommandCancelled && e.Handled, Is.True);

        var failure = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "unfiltered"), false);
        Assert.DoesNotThrow(() => InboxMessageWindow.CopyVisibleSelection(viewer, failure,
            _ => throw new ExternalException("Clipboard busy")));
        Assert.That(failure.CommandCancelled && failure.Handled, Is.True);

        var detail = MarkdownVisibleContent.Details(document).Single();
        detail.Expander.IsExpanded = true;
        using var tracking = MarkdownVisibleContent.TrackSelections(viewer);
        MarkdownVisibleContent.Find(document, Explanation)!.Select(viewer);
        var expanded = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "unfiltered"), false);
        InboxMessageWindow.CopyVisibleSelection(viewer, expanded, text => copied = text);
        Assert.That(copied, Is.EqualTo(Explanation));
        Assert.That(expanded.CommandCancelled && expanded.Handled, Is.True);
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void State_RestoresBeforeViewExposure_SeparatesDuplicatesAndDetachesOldHandlers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "detail-tests-" + Guid.NewGuid().ToString("N"));
        var store = new MarkdownDetailStateStore(Path.Combine(folder, "state.json"));
        var identity = MarkdownDetailIdentity.ForInboxMessage(folder, "message");
        var state = new MarkdownDetailStateContext(identity, store);
        const string source = ":::detail Duplicate\nBody.\n:::\n:::detail Duplicate\nBody.\n:::";
        try
        {
            var document = MarkdownFlowDocumentBuilder.BuildInbox(source, detailState: state);
            var details = MarkdownVisibleContent.Details(document).ToArray();
            Assert.That(File.Exists(store.FilePath), Is.False, "Initialization does not save.");
            details[1].Expander.IsExpanded = true;
            Assert.That(details[0].Key, Is.Not.EqualTo(details[1].Key));
            MarkdownVisibleContent.Detach(document);
            details[1].Expander.IsExpanded = false;
            var rebuilt = MarkdownFlowDocumentBuilder.BuildInbox(source, 22, detailState: state);
            Assert.That(MarkdownVisibleContent.Details(rebuilt).Select(detail => detail.Expander.IsExpanded), Is.EqualTo(new[] { false, true }));
            MarkdownVisibleContent.Detach(rebuilt);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public void InboxWindow_RebuildCopySearchAndHiddenSelectionUseTheSamePersistentState() =>
        WpfTestContext.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "inbox-detail-" + Guid.NewGuid().ToString("N"));
            var store = new MarkdownDetailStateStore(Path.Combine(folder, "state.json"));
            var message = new InboxMessage { Id = "integration", Subject = "Message", Body = Labelled, Actions = [] };
            var window = new InboxMessageWindow(message, (_, _) => Task.FromResult(true),
                workspaceFolder: folder, detailStateStore: store);
            try
            {
                var viewer = (FlowDocumentScrollViewer)typeof(InboxMessageWindow)
                    .GetField("_bodyViewer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                var detail = MarkdownVisibleContent.Details(viewer.Document).Single();
                Assert.That(window.GetRenderedMessageText(), Does.Not.Contain(Explanation));
                Assert.That(window.SelectAndScrollToText(Explanation), Is.False);
                detail.Expander.IsExpanded = true;
                Assert.That(window.SelectAndScrollToText(Explanation), Is.True);
                Assert.That(detail.Viewer.Selection.Text, Is.EqualTo(Explanation));
                Assert.That(window.GetRenderedMessageText(), Does.Contain(Explanation));
                typeof(InboxMessageWindow).GetField("_bodyFontSize", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(window, 21d);
                window.NotifyAppearanceChanged();
                var current = MarkdownVisibleContent.Details(viewer.Document).Single();
                Assert.That(current, Is.Not.SameAs(detail));
                Assert.That(current.Expander.IsExpanded, Is.True);
                Assert.That(current.Viewer.Document.FontSize, Is.EqualTo(21));
                var style = new Style(typeof(Expander));
                current.Expander.Resources["ThemedExpanderStyle"] = style;
                Assert.That(current.Expander.Style, Is.SameAs(style));
                detail.Expander.IsExpanded = false; // Detached old view must not save.
                Assert.That(store.Load(current.Key!).IsExpanded, Is.True);
                var reopened = new InboxMessageWindow(message, (_, _) => Task.FromResult(true),
                    workspaceFolder: folder, detailStateStore: new MarkdownDetailStateStore(store.FilePath));
                try { Assert.That(reopened.GetRenderedMessageText(), Does.Contain(Explanation)); }
                finally { reopened.Close(); }
                Assert.That(window.SelectAndScrollToText(Explanation), Is.True);
                current.Expander.IsExpanded = false;
                Assert.That(current.Viewer.Selection.IsEmpty, Is.True);
                Assert.That(window.SelectAndScrollToText(Explanation), Is.False);
                Assert.That(window.GetRenderedMessageText(), Does.Not.Contain(Explanation));
                var collapsedReopen = new InboxMessageWindow(message, (_, _) => Task.FromResult(true),
                    workspaceFolder: folder, detailStateStore: new MarkdownDetailStateStore(store.FilePath));
                try
                {
                    Assert.That(collapsedReopen.GetRenderedMessageText(), Does.Not.Contain(Explanation));
                    Assert.That(collapsedReopen.SelectAndScrollToText(Explanation), Is.False);
                }
                finally { collapsedReopen.Close(); }
                Assert.That(message.Body, Is.EqualTo(Labelled), "Rendered visibility never changes durable source.");
            }
            finally
            {
                window.Close();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });

    [Test]
    public void SelectionOwnership_ChangingOwnersClearsPreviousNestedAndCodeSelections()
    {
        var document = MarkdownFlowDocumentBuilder.BuildInbox("Lead.\n:::detail Label\nNested text.\n```\nCode text.\n```\n:::\nTail.");
        var detail = MarkdownVisibleContent.Details(document).Single();
        detail.Expander.IsExpanded = true;
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var tracking = MarkdownVisibleContent.TrackSelections(viewer);
        MarkdownVisibleContent.Find(document, "Nested text.")!.Select(viewer);
        MarkdownVisibleContent.Find(document, "Code text.")!.Select(viewer);
        Assert.That(detail.Viewer.Selection.IsEmpty, Is.True);
        Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo("Code text."));
        MarkdownVisibleContent.Find(document, "Lead.")!.Select(viewer);
        Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo("Lead."));
        MarkdownVisibleContent.Detach(document);
    }

    [Test]
    public void InboxNestedPresentersAndClosedNoteExport_PreserveCallbacksAndProjectionParity() =>
        WpfTestContext.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "inbox-presenters-" + Guid.NewGuid().ToString("N"));
            var store = new MarkdownDetailStateStore(Path.Combine(folder, "state.json"));
            var plan = new Plan("PLAN-DETAIL", "rev-1", PlanSource.DecomposeDecision,
                PlanLifecycleStatus.AwaitingApproval, "Nested plan", "main", "Summary",
                [new PlanTask("TASK-1", "Prepare kit", "Prepare it", [], "high", PlanTaskStatus.Complete)],
                [new PlanApprovalGate("GATE-1", "Review", ["TASK-1"], [], PlanGateStatus.AwaitingApproval,
                    ProofRequirements: [new PlanTaskProofRequirement("proof-one", "live-ui-observation",
                        "Observe the result.", "Does it appear?")])],
                new PlanProgress(1, 1), new PlanTimestamps(DateTimeOffset.UtcNow));
            var source = "Lead.\n:::detail Review\n**Nested plan**\n\n`abc1234`\n\n" +
                InboxRelativeTimePresenter.Encode(DateTimeOffset.UtcNow.AddDays(-3)) +
                "\n\n{{approval-check:proof-one}}\n\n[Guide](https://example.com)\n\n" +
                "| Resource | Value |\n| --- | --- |\n| [Table link](https://example.org) | Cell |\n:::\nTail.";
            var message = ManualTestKitApprovalBinding.BindForCompletedTask(new InboxMessage
            {
                Id = "nested-presenters", Subject = "Blocked plan: Nested plan", Body = source, Actions = [],
                Attachments = [new InboxAttachment { Type = DecomposePlanInbox.AttachmentType, PlanGroupId = plan.PlanId }],
            }, plan, "TASK-1");
            string? openedCommit = null;
            InboxAttachment? openedPlan = null;
            bool? approvalSelection = null;
            var window = new InboxMessageWindow(message, (_, _) => Task.FromResult(true),
                workspaceFolder: folder, detailStateStore: store, loadPlan: _ => plan,
                openCommit: sha => openedCommit = sha, openDecomposePlan: attachment => openedPlan = attachment,
                onHumanReviewSelectionChanged: (_, _, value) => approvalSelection = value);
            try
            {
                var viewer = (FlowDocumentScrollViewer)typeof(InboxMessageWindow)
                    .GetField("_bodyViewer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                var detail = MarkdownVisibleContent.Details(viewer.Document).Single();
                Assert.That(window.GetRenderedMessageText(), Is.EqualTo(
                    InboxMessageWindow.BuildStoredMessageVisibleText(message, folder, _ => plan, store)));
                Assert.That(window.SelectAndScrollToText("Does it appear?"), Is.False);
                detail.Expander.IsExpanded = true;
                var body = detail.Viewer.Document;
                var rendered = window.GetRenderedMessageText();
                Assert.That(rendered, Is.EqualTo(
                    InboxMessageWindow.BuildStoredMessageVisibleText(message, folder, _ => plan, store)));
                Assert.That(rendered, Does.Contain("[Unanswered] Does it appear?").And.Contain("Cell")
                    .And.Not.Contain("utc-time:").And.Not.Contain("approval-check:"));
                Assert.That(window.SelectAndScrollToText("Does it appear?"), Is.True);
                Assert.That(MarkdownVisibleContent.Find(viewer.Document, "Cell"), Is.Not.Null);

                var links = MarkdownVisibleContent.Blocks(body.Blocks).OfType<Paragraph>()
                    .SelectMany(paragraph => Hyperlinks(paragraph.Inlines)).ToArray();
                links.Single(link => new TextRange(link.ContentStart, link.ContentEnd).Text == "abc1234")
                    .RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
                links.Single(link => new TextRange(link.ContentStart, link.ContentEnd).Text == "Nested plan")
                    .RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
                Assert.That(openedCommit, Is.EqualTo("abc1234"));
                Assert.That(openedPlan?.PlanGroupId, Is.EqualTo(plan.PlanId));
                Assert.That(links.Single(link => link.Tag is string).ContextMenu, Is.Not.Null);
                var tableLink = body.Blocks.OfType<BlockUIContainer>()
                    .SelectMany(block => MarkdownVisibleContent.TextBlocks(block.Child))
                    .SelectMany(text => Hyperlinks(text.Inlines)).Single();
                Assert.That(tableLink.ContextMenu, Is.Not.Null);

                var checkbox = MarkdownVisibleContent.Blocks(body.Blocks).OfType<Paragraph>()
                    .SelectMany(paragraph => paragraph.Inlines.OfType<InlineUIContainer>())
                    .SelectMany(inline => FindCheckBoxes(inline.Child))
                    .First();
                var savedDisclosureState = File.ReadAllText(store.FilePath);
                checkbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.That(approvalSelection, Is.True);
                Assert.That(File.ReadAllText(store.FilePath), Is.EqualTo(savedDisclosureState));
                Assert.That(window.GetRenderedMessageText(), Does.Contain("[Yes] Does it appear?"));
                detail.Expander.IsExpanded = false;
                Assert.That(window.GetRenderedMessageText(), Does.Not.Contain("Does it appear?").And.Not.Contain("Cell"));
                Assert.That(message.Body, Is.EqualTo(source));
            }
            finally
            {
                window.Close();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });

    [Test]
    public void ComposedHostDocument_RebindingKeepsVisibilityAndCodeCopyRootAware()
    {
        var source = MarkdownFlowDocumentBuilder.BuildPlanDescription(":::detail Sample\n```\nSample code.\n```\n:::");
        var host = new FlowDocument(new Paragraph(new Run("Host lead.")));
        var block = source.Blocks.FirstBlock;
        source.Blocks.Remove(block);
        host.Blocks.Add(block);
        MarkdownVisibleContent.Bind(host);
        var detail = MarkdownVisibleContent.Details(host).Single();
        var code = (TextBox)MarkdownVisibleContent.Project(detail.Viewer.Document).Single().Control!;
        var invalidations = 0;
        MarkdownVisibleContent.State(host).VisibilityChanged += () => invalidations++;
        Assert.That(CopyableCodeBlockBuilder.GetCopyText(code, false), Is.Empty);
        detail.Expander.IsExpanded = true;
        Assert.That(invalidations, Is.EqualTo(1));
        Assert.That(MarkdownVisibleContent.GetText(host), Does.Contain("Host lead.").And.Contain("Sample code."));
        Assert.That(CopyableCodeBlockBuilder.GetCopyText(code, false), Is.EqualTo("Sample code."));
        MarkdownVisibleContent.Detach(host);
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

    private static IEnumerable<CheckBox> FindCheckBoxes(DependencyObject root)
    {
        if (root is CheckBox checkBox)
            yield return checkBox;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        foreach (var nested in FindCheckBoxes(child))
            yield return nested;
    }
}
