using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

internal sealed record PendingDecomposeApprovalTag(string GroupId, string Revision);
internal sealed record PendingDecomposePlanLinkTag(string GroupId, string Revision);
internal sealed record DecomposeRecoveryTag(string GroupId, string Revision, string TaskId);
internal sealed record PlanGateApprovalTag(string PlanId, string GateId);
internal sealed record PlanPreflightRecoveryTag(string GroupId, string Revision);

/// <summary>Creates consistently styled, transcript-scaled quick-reply controls.</summary>
internal static class TranscriptQuickReplyFactory
{
    private sealed class ContainerMarker
    {
        internal static readonly ContainerMarker Instance = new();
    }

    internal static Button CreateButton(
        object content,
        double transcriptFontSize,
        object? tag = null,
        object? toolTip = null)
    {
        var button = new Button
        {
            Content = content,
            Tag = tag,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            MinHeight = 28,
            ToolTip = toolTip,
        };
        if (Application.Current?.TryFindResource("QuickReplyButtonStyle") is Style style)
            button.Style = style;

        // QuickReplyButtonStyle follows the environment scale. Transcript buttons additionally
        // follow the independently zoomable transcript font, just like transcript prose.
        button.FontSize = transcriptFontSize;
        button.SetResourceReference(Control.BackgroundProperty, "QuickReplySurface");
        button.SetResourceReference(Control.ForegroundProperty, "QuickReplyText");
        button.SetResourceReference(Control.BorderBrushProperty, "QuickReplyBorder");
        return button;
    }

    internal static BlockUIContainer CreateContainer(UIElement child, object? tag = null) =>
        new(child)
        {
            Margin = new Thickness(0, 2, 0, 10),
            Tag = tag ?? ContainerMarker.Instance,
        };

    internal static bool IsQuickReplyContainer(BlockUIContainer container) =>
        container.Tag is QuickReplyCopyData or PendingDecomposeApprovalTag or DecomposeRecoveryTag or
            PlanGateApprovalTag or PlanPreflightRecoveryTag or ContainerMarker;

    internal static void RemovePendingDecomposeApprovalContainers(
        BlockCollection blocks,
        Func<PendingDecomposeApprovalTag, Block?>? createMissingPlanLink = null)
    {
        foreach (var block in blocks.ToArray())
        {
            if (block is BlockUIContainer { Tag: PendingDecomposeApprovalTag approvalTag })
            {
                var hasMatchingLink = block.PreviousBlock?.Tag is PendingDecomposePlanLinkTag linkTag &&
                                      string.Equals(linkTag.GroupId, approvalTag.GroupId, StringComparison.Ordinal) &&
                                      string.Equals(linkTag.Revision, approvalTag.Revision, StringComparison.Ordinal);
                if (!hasMatchingLink && createMissingPlanLink?.Invoke(approvalTag) is { } linkBlock)
                    blocks.InsertBefore(block, linkBlock);
                blocks.Remove(block);
                continue;
            }

            if (block is Section section)
                RemovePendingDecomposeApprovalContainers(section.Blocks, createMissingPlanLink);
        }
    }

    internal static void RemoveDecomposeRecoveryContainers(BlockCollection blocks)
    {
        foreach (var block in blocks.ToArray())
        {
            if (block is BlockUIContainer { Tag: DecomposeRecoveryTag })
                blocks.Remove(block);
            else if (block is Section section)
                RemoveDecomposeRecoveryContainers(section.Blocks);
        }
    }

    internal static IEnumerable<Button> EnumerateButtons(DependencyObject root)
    {
        if (root is Button button)
            yield return button;

        var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            foreach (var descendant in EnumerateButtons(child))
                yield return descendant;
        }
    }
}
