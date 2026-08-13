using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

internal enum QuickReplyTone
{
    Default,
    Warning,
    Destructive,
}

internal sealed record PendingDecomposeApprovalTag(string GroupId, string Revision);
internal sealed record PendingDecomposePlanLinkTag(string GroupId, string Revision);
internal sealed record DecomposeRecoveryTag(string GroupId, string Revision, string TaskId);
internal sealed class DecomposeRecoveryCardTag : ICopyable
{
    internal DecomposeRecoveryCardTag(
        DecomposeRecoveryTag identity,
        FrameworkElement actionsPanel)
    {
        Identity = identity;
        ActionsPanel = actionsPanel;
    }

    internal DecomposeRecoveryTag Identity { get; }
    internal FrameworkElement ActionsPanel { get; }
    internal string CopyText { get; set; } = string.Empty;
    public string GetCopyText() => CopyText;
}
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
        object? toolTip = null,
        QuickReplyTone tone = QuickReplyTone.Default)
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
        var styleKey = tone switch
        {
            QuickReplyTone.Warning => "QuickReplyWarningButtonStyle",
            QuickReplyTone.Destructive => "QuickReplyDestructiveButtonStyle",
            _ => "QuickReplyButtonStyle",
        };
        if (Application.Current?.TryFindResource(styleKey) is Style style)
            button.Style = style;

        // QuickReplyButtonStyle follows the environment scale. Transcript buttons additionally
        // follow the independently zoomable transcript font, just like transcript prose.
        button.FontSize = transcriptFontSize;
        var resourcePrefix = tone switch
        {
            QuickReplyTone.Warning => "QuickReplyWarning",
            QuickReplyTone.Destructive => "QuickReplyDestructive",
            _ => "QuickReply",
        };
        button.SetResourceReference(Control.BackgroundProperty, resourcePrefix + "Surface");
        button.SetResourceReference(Control.ForegroundProperty, resourcePrefix + "Text");
        button.SetResourceReference(Control.BorderBrushProperty, resourcePrefix + "Border");
        return button;
    }

    internal static QuickReplyTone ParseTone(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "warning" => QuickReplyTone.Warning,
        "destructive" => QuickReplyTone.Destructive,
        _ => QuickReplyTone.Default,
    };

    internal static BlockUIContainer CreateContainer(UIElement child, object? tag = null) =>
        new(child)
        {
            Margin = new Thickness(0, 2, 0, 10),
            Tag = tag ?? ContainerMarker.Instance,
        };

    internal static bool IsQuickReplyContainer(BlockUIContainer container) =>
        container.Tag is QuickReplyCopyData or PendingDecomposeApprovalTag or DecomposeRecoveryTag or DecomposeRecoveryCardTag or
            PlanGateApprovalTag or PlanPreflightRecoveryTag or TranscriptApprovalCardTag or ContainerMarker;

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
            if (block is BlockUIContainer { Tag: DecomposeRecoveryTag } or
                BlockUIContainer { Tag: DecomposeRecoveryCardTag } or
                Section { Tag: DecomposeRecoveryTag })
                blocks.Remove(block);
            else if (block is Section section)
                RemoveDecomposeRecoveryContainers(section.Blocks);
        }
    }

    /// <summary>
    /// Removes only the actionable controls from an existing recovery surface for one plan.
    /// The explanatory narrative and plan link remain in the transcript while a newer,
    /// more specific recovery card becomes the single owner of the next action.
    /// </summary>
    internal static void RemoveDecomposeRecoveryActions(BlockCollection blocks, string groupId)
    {
        foreach (var block in blocks.ToArray())
        {
            if (block is BlockUIContainer { Tag: DecomposeRecoveryTag tag } &&
                string.Equals(tag.GroupId, groupId, StringComparison.Ordinal))
            {
                blocks.Remove(block);
                continue;
            }
            if (block is BlockUIContainer { Tag: DecomposeRecoveryCardTag cardTag } &&
                string.Equals(cardTag.Identity.GroupId, groupId, StringComparison.Ordinal))
            {
                cardTag.ActionsPanel.Visibility = Visibility.Collapsed;
                continue;
            }

            if (block is Section section)
                RemoveDecomposeRecoveryActions(section.Blocks, groupId);
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

    /// <summary>
    /// Returns the actionable buttons from the newest transcript quick-reply container,
    /// including buttons hosted inside rich approval and recovery cards.
    /// </summary>
    internal static IReadOnlyList<Button> FindLatestActionButtons(BlockCollection blocks)
    {
        foreach (var block in blocks.Cast<Block>().Reverse())
        {
            if (block is Section section)
            {
                var nested = FindLatestActionButtons(section.Blocks);
                if (nested.Count > 0) return nested;
            }

            if (block is not BlockUIContainer container ||
                !IsQuickReplyContainer(container) ||
                container.Child is not DependencyObject child)
                continue;
            var buttons = EnumerateButtons(child)
                .Where(button => button.Visibility == Visibility.Visible &&
                                 button.IsEnabled &&
                                 button.Content is string)
                .ToArray();
            if (buttons.Length > 0) return buttons;
        }
        return [];
    }
}
