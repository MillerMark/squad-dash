using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

internal static class MarkdownDetailAuthoringExampleAssertions
{
    internal static void PlanningExampleUsesInspectorConsumers(string instruction, bool selectedPlan)
    {
        const string lead = "Persist each section's last state without changing message schemas.";
        const string label = "Why not window-local state?";
        const string body = "A window-local value is discarded when the window closes, so it cannot restore the reader's choice after restart.";
        const string markdown = lead + "\n\n:::detail " + label + "\n" + body + "\n:::";
        var lines = instruction.ReplaceLineEndings("\n").Split('\n');
        var jsonProperty = lines.Single(line => line.TrimStart().StartsWith("\"description\":", StringComparison.Ordinal)
            && line.Contains(":::detail", StringComparison.Ordinal));
        using var json = JsonDocument.Parse("{" + jsonProperty + "}");
        var authored = json.RootElement.GetProperty("description").GetString()!;
        Assert.That(authored, Is.EqualTo(markdown));
        Assert.That(string.Join("\n", lines.Select(line => line.StartsWith("  ", StringComparison.Ordinal) ? line[2..] : line)),
            Does.Contain("```text\n" + markdown + "\n```"), "The displayed and JSON-escaped shipped examples must agree.");

        using var workspace = new TestWorkspace();
        var presentation = new PlanDescriptionPresentation(workspace.RootPath, "example", [], 13,
            store: new MarkdownDetailStateStore(workspace.GetPath("state.json")));
        var document = new FlowDocument();
        if (selectedPlan) presentation.AppendPlanDescription(document, authored);
        else presentation.AppendTaskDescription(document, "task", authored);
        using var session = new PlanDescriptionDocumentSession(new FlowDocumentScrollViewer { Document = document });
        var detail = MarkdownVisibleContent.Details(document).Single();
        Assert.Multiple(() =>
        {
            Assert.That(detail.Expander.IsExpanded, Is.False);
            Assert.That(PlanDescriptionDocumentSession.GetCopyText(document),
                Is.EqualTo(string.Join(Environment.NewLine, lead, "", label)));
            Assert.That(session.FindNext(body), Is.False);
        });
        detail.Expander.IsExpanded = true;
        Assert.That(PlanDescriptionDocumentSession.GetCopyText(document),
            Is.EqualTo(string.Join(Environment.NewLine, lead, "", label, body)));
        Assert.That(session.FindNext(body), Is.True);
        Assert.That(detail.Viewer.Selection.Text, Is.EqualTo(body));
    }
}
