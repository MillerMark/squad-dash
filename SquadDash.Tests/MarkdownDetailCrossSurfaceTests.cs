using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
internal sealed class MarkdownDetailCrossSurfaceTests
{
    [TestCase("plan")]
    [TestCase("task")]
    [TestCase("graph")]
    public void PlanConsumers_ValidEmptyUnlabelledAndFencedSections_RetainVisibleCopyAndFind(string surface)
    {
        using var workspace = new TestWorkspace();
        const string source = "Lead.\n\n:::detail\nOptional body.\n:::\n\n:::detail Empty\n:::\n\n" +
            ":::detail Samples\n```text\n:::detail Literal\nCode body.\n:::\n```\n:::\n\nTail.\n\n" +
            "```text\n:::detail External\nVisible code.\n:::\n```";
        var document = BuildPlanDocument(workspace, surface, source);
        using var session = new PlanDescriptionDocumentSession(new FlowDocumentScrollViewer { Document = document });
        var details = MarkdownVisibleContent.Details(document).ToArray();
        Assert.That(details.Select(d => d.Label), Is.EqualTo(new[] { "More detail", "Empty", "Samples" }));
        Assert.That(details.All(d => !d.Expander.IsExpanded), Is.True);
        Assert.That(PlanDescriptionDocumentSession.GetCopyText(document).ReplaceLineEndings("\n"),
            Is.EqualTo("Lead.\n\nMore detail\n\nEmpty\n\nSamples\n\nTail.\n\n:::detail External\nVisible code.\n:::"));
        Assert.That(session.FindNext("Visible code."), Is.True, "A fence outside a disclosure stays visible.");
        Assert.That(session.FindNext("Optional body."), Is.False);
        Assert.That(session.FindNext("Code body."), Is.False);

        foreach (var detail in details) detail.Expander.IsExpanded = true;
        Assert.That(PlanDescriptionDocumentSession.GetCopyText(document).ReplaceLineEndings("\n"),
            Does.Contain("Optional body.").And.Contain(":::detail Literal\nCode body.\n:::").And.Contain("Tail."));
        Assert.That(session.FindNext("Optional body."), Is.True);
        Assert.That(session.FindNext("Code body."), Is.True);
        Assert.That(MarkdownVisibleContent.Details(details[2].Viewer.Document), Is.Empty,
            "Fenced markers must remain code, not become a nested disclosure.");
    }

    [TestCase("plan")]
    [TestCase("task")]
    [TestCase("graph")]
    public void PlanConsumers_MalformedRegionsStayLiteralWhileAdjacentSectionsRemainUsable(string surface)
    {
        using var workspace = new TestWorkspace();
        const string nested = ":::detail Outer\nOuter body.\n:::detail Inner\nInner body.\n:::\n:::";
        const string unbalanced = ":::detail Unclosed\nUnclosed body.";
        const string source = ":::\n\n:::detail First\nFirst body.\n:::\n\n" + nested +
            "\n\n:::detail Last\nLast body.\n:::\n\n" + unbalanced;
        var document = BuildPlanDocument(workspace, surface, source);
        using var session = new PlanDescriptionDocumentSession(new FlowDocumentScrollViewer { Document = document });
        var details = MarkdownVisibleContent.Details(document).ToArray();
        Assert.That(details.Select(d => d.Label), Is.EqualTo(new[] { "First", "Last" }));
        Assert.That(details.All(d => !d.Expander.IsExpanded), Is.True);
        var collapsed = PlanDescriptionDocumentSession.GetCopyText(document).ReplaceLineEndings("\n");
        Assert.That(collapsed, Does.StartWith(":::\n").And.Contain(nested).And.Contain(unbalanced)
            .And.Not.Contain("First body.").And.Not.Contain("Last body."));
        foreach (var literal in new[] { ":::detail Inner", "Inner body.", ":::detail Unclosed", "Unclosed body." })
            Assert.That(session.FindNext(literal), Is.True, literal);
        details[1].Expander.IsExpanded = true;
        Assert.That(PlanDescriptionDocumentSession.GetCopyText(document),
            Does.Contain("Last body.").And.Not.Contain("First body."));
        Assert.That(session.FindNext("Last body."), Is.True);
        Assert.That(session.FindNext("First body."), Is.False);
    }

    [TestCase("inbox")]
    [TestCase("plan")]
    [TestCase("task")]
    public void CopyEvent_PartialRootSelectionCrossingDetailBoundary_TracksBothStates(string surface)
    {
        using var workspace = new TestWorkspace();
        const string source = "Excluded prefix. Start here.\n\n:::detail Evidence\nOptional body.\n:::\n\nEnd here. Excluded suffix.";
        var document = surface == "inbox"
            ? MarkdownFlowDocumentBuilder.BuildInbox(source)
            : BuildPlanDocument(workspace, surface, source);
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var session = surface == "inbox" ? null : new PlanDescriptionDocumentSession(viewer);
        using var inboxSelections = surface == "inbox" ? MarkdownVisibleContent.TrackSelections(viewer) : null;
        try
        {
            viewer.ApplyTemplate();
            var detail = MarkdownVisibleContent.Details(document).Single();
            var paragraphs = document.Blocks.OfType<Paragraph>().ToArray();
            var first = paragraphs.First().Inlines.OfType<Run>().Single();
            var last = paragraphs.Last().Inlines.OfType<Run>().Single();
            viewer.Selection.Select(first.ContentStart.GetPositionAtOffset("Excluded prefix. ".Length)!,
                last.ContentStart.GetPositionAtOffset("End here.".Length)!);

            foreach (var expanded in new[] { false, true, false })
            {
                detail.Expander.IsExpanded = expanded;
                string? copied = null;
                var e = new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "unfiltered"), false);
                if (surface == "inbox")
                    InboxMessageWindow.CopyVisibleSelection(viewer, e, text => copied = text);
                else
                    PlanDescriptionDocumentSession.CopySelection(viewer, e, text => copied = text);
                var expected = string.Join(Environment.NewLine, expanded
                    ? new[] { "Start here.", "", "Evidence", "Optional body.", "", "End here." }
                    : new[] { "Start here.", "", "Evidence", "", "End here." });
                Assert.Multiple(() =>
                {
                    Assert.That(copied, Is.EqualTo(expected), $"expanded={expanded}");
                    Assert.That(MarkdownVisibleContent.GetSelectedText(viewer), Is.EqualTo(expected));
                    Assert.That(e.CommandCancelled && e.Handled, Is.True);
                });
            }
        }
        finally { MarkdownVisibleContent.Detach(document); }
    }

    [TestCase("plan")]
    [TestCase("task")]
    public void InspectorFindNext_CyclesNestedOwnersAndResetsAfterCollapse(string surface)
    {
        using var workspace = new TestWorkspace();
        const string source = "needle root\n\n:::detail First\nneedle first\n:::\n\n:::detail Second\nneedle second\n:::";
        var document = BuildPlanDocument(workspace, surface, source);
        var viewer = new FlowDocumentScrollViewer { Document = document };
        using var session = new PlanDescriptionDocumentSession(viewer);
        var details = MarkdownVisibleContent.Details(document).ToArray();
        foreach (var detail in details) detail.Expander.IsExpanded = true;
        var matches = PlanDescriptionDocumentSession.FindAll(document, "needle");
        Assert.That(matches.Select(m => m.Part.Document),
            Is.EqualTo(new[] { document, details[0].Viewer.Document, details[1].Viewer.Document }));
        Assert.That(session.FindNext("needle"), Is.True);
        Assert.That(viewer.Selection.Text, Is.EqualTo("needle"));
        Assert.That(session.FindNext("needle"), Is.True);
        Assert.That(details[0].Viewer.Selection.Text, Is.EqualTo("needle"));
        Assert.That(viewer.Selection.IsEmpty, Is.True);
        Assert.That(session.FindNext("needle"), Is.True);
        Assert.That(details[1].Viewer.Selection.Text, Is.EqualTo("needle"));
        Assert.That(details[0].Viewer.Selection.IsEmpty, Is.True);

        details[1].Expander.IsExpanded = false;
        matches[2].Select(viewer);
        string? copied = null;
        PlanDescriptionDocumentSession.CopySelection(viewer,
            new DataObjectCopyingEventArgs(new DataObject(DataFormats.UnicodeText, "stale"), false),
            text => copied = text);
        Assert.That(copied, Is.Null);
        Assert.That(session.FindNext("needle"), Is.True);
        Assert.That(viewer.Selection.Text, Is.EqualTo("needle"), "Collapse resets navigation to the first visible owner.");
        Assert.That(PlanDescriptionDocumentSession.FindAll(document, "needle"), Has.Count.EqualTo(2));
        details[1].Expander.IsExpanded = true;
        Assert.That(session.FindNext("needle"), Is.True);
        Assert.That(viewer.Selection.Text, Is.EqualTo("needle"), "Expansion also invalidates the navigation index.");
        details[1].Expander.IsExpanded = false;
        Assert.That(session.FindNext("needle second"), Is.False);
    }

    [Test]
    public void SharedIsolatedStore_IdenticalTextSeparatesConsumersAndDuplicatesAcrossFreshRebuilds()
    {
        using var workspace = new TestWorkspace();
        const string source = ":::detail Same\nSame body.\n:::\n\n:::detail Same\nSame body.\n:::";
        var documents = new List<FlowDocument>();
        FlowDocument Build(string surface, string id = "one")
        {
            var document = surface == "inbox"
                ? MarkdownFlowDocumentBuilder.BuildInbox(source, detailState: new(
                    MarkdownDetailIdentity.ForInboxMessage(workspace.RootPath, id),
                    new MarkdownDetailStateStore(workspace.GetPath("global", "markdown-detail-state.json"))))
                : BuildPlanDocument(workspace, surface, source, id);
            documents.Add(document);
            return document;
        }
        try
        {
            var inbox = Build("inbox");
            var plan = Build("plan");
            var task = Build("task");
            var graph = Build("graph");
            var independent = new[] { inbox, plan, task }.SelectMany(MarkdownVisibleContent.Details).ToArray();
            var originalKeys = independent.Select(d => d.Key).ToArray();
            Assert.That(originalKeys.Distinct().Count(), Is.EqualTo(6));
            Assert.That(MarkdownVisibleContent.Details(graph).Select(d => d.Key),
                Is.EqualTo(MarkdownVisibleContent.Details(task).Select(d => d.Key)));
            Assert.That(independent.All(d => !d.Expander.IsExpanded), Is.True);
            foreach (var document in new[] { inbox, plan, task })
                MarkdownVisibleContent.Details(document).Last().Expander.IsExpanded = true;
            Assert.That(MarkdownVisibleContent.Details(graph).Select(d => d.Expander.IsExpanded),
                Is.EqualTo(new[] { false, true }));
            foreach (var surface in new[] { "inbox", "plan", "task", "graph" })
                Assert.That(MarkdownVisibleContent.Details(Build(surface, "unrelated")).All(d => !d.Expander.IsExpanded),
                    Is.True, $"Identical text in unrelated {surface} must not inherit state.");
            foreach (var document in documents) MarkdownVisibleContent.Detach(document);
            documents.Clear();

            var rebuilt = new[] { Build("inbox"), Build("plan"), Build("task") };
            Assert.That(rebuilt.SelectMany(MarkdownVisibleContent.Details).Select(d => d.Key), Is.EqualTo(originalKeys));
            foreach (var document in rebuilt)
            {
                var details = MarkdownVisibleContent.Details(document).ToArray();
                Assert.That(details.Select(d => d.Expander.IsExpanded), Is.EqualTo(new[] { false, true }));
                details[0].Expander.IsExpanded = true;
                details[1].Expander.IsExpanded = false;
            }
            foreach (var document in documents) MarkdownVisibleContent.Detach(document);
            documents.Clear();
            foreach (var surface in new[] { "inbox", "plan", "task", "graph" })
                Assert.That(MarkdownVisibleContent.Details(Build(surface)).Select(d => d.Expander.IsExpanded),
                    Is.EqualTo(new[] { true, false }), $"{surface}: restore both saved states through a fresh store.");
        }
        finally
        {
            foreach (var document in documents) MarkdownVisibleContent.Detach(document);
        }
    }

    private static FlowDocument BuildPlanDocument(TestWorkspace workspace, string surface, string source, string id = "one")
    {
        var presentation = new PlanDescriptionPresentation(workspace.RootPath, id, [], 13,
            store: new MarkdownDetailStateStore(workspace.GetPath("global", "markdown-detail-state.json")));
        if (surface == "graph") return presentation.BuildTaskDescription("task", source);
        var document = new FlowDocument();
        if (surface == "plan") presentation.AppendPlanDescription(document, source);
        else if (surface == "task") presentation.AppendTaskDescription(document, "task", source);
        else throw new ArgumentOutOfRangeException(nameof(surface));
        return document;
    }
}
