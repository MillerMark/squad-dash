using System.Windows;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>The production presentation paths shared by the graph and selected-element inspector.</summary>
internal sealed class PlanDescriptionPresentation(
    string? workspaceFolder,
    string planId,
    IReadOnlyList<PlanTask> tasks,
    double fontSize,
    Action<string>? onLinkClicked = null,
    Func<string, string, Hyperlink>? createTaskLink = null,
    MarkdownDetailStateStore? store = null)
{
    private readonly MarkdownDetailStateStore _store = store ?? new();

    internal void AppendPlanDescription(FlowDocument target, string summary) =>
        Append(target, Build(summary, taskId: null));

    internal void AppendTaskDescription(FlowDocument target, string taskId, string description) =>
        Append(target, BuildTaskDescription(taskId, description));

    internal FlowDocument BuildTaskDescription(string taskId, string description) => Build(description, taskId);

    private FlowDocument Build(string markdown, string? taskId)
    {
        MarkdownDetailStateContext? context = null;
        if (!string.IsNullOrWhiteSpace(workspaceFolder))
            context = new(taskId is null
                ? MarkdownDetailIdentity.ForPlanDescription(workspaceFolder, planId)
                : MarkdownDetailIdentity.ForTaskDescription(workspaceFolder, planId, taskId), _store);

        var document = MarkdownFlowDocumentBuilder.BuildPlanDescription(markdown, fontSize, onLinkClicked, context);
        if (context is null && MarkdownVisibleContent.Details(document).Any())
            SquadDashTrace.Write("PlanDescription", "No workspace identity; detail state cannot be persisted.");
        foreach (var (child, _) in MarkdownVisibleContent.Documents(document, visibleOnly: false))
        {
            if (createTaskLink is not null)
                PlanDescriptionTaskReferenceFormatter.Apply(child, tasks, createTaskLink);
            foreach (var block in MarkdownVisibleContent.Blocks(child.Blocks))
            {
                block.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                block.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                if (!MarkdownFlowDocumentBuilder.TryGetHeadingLevel(block, out var level))
                    continue;
                block.SetResourceReference(TextElement.ForegroundProperty, "SectionHeader");
                block.SetResourceReference(TextElement.FontSizeProperty, level <= 2 ? "FontSizeLargePlus" : "FontSizeLarge");
                block.FontWeight = FontWeights.SemiBold;
                block.Margin = new Thickness(0, level <= 2 ? 13 : 10, 0, 2);
            }
        }
        return document;
    }

    private static void Append(FlowDocument target, FlowDocument rendered)
    {
        foreach (var block in rendered.Blocks.Cast<Block>().ToArray())
        {
            rendered.Blocks.Remove(block);
            target.Blocks.Add(block);
        }
        MarkdownVisibleContent.Bind(target);
    }
}
