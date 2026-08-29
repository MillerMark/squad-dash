using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

internal enum QuickReplyTone
{
    Default,
    Warning,
    Destructive,
}

internal sealed record PendingDecomposeApprovalTag(string GroupId, string Revision);
internal sealed record PendingDecomposePlanLinkTag(string GroupId, string Revision);
internal sealed record PendingDecomposeInboxBannerTag(string GroupId, string Revision);
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
    internal Func<string>? CopyTextProvider { get; set; }
    public string GetCopyText() => CopyTextProvider?.Invoke() ?? CopyText;
}
internal sealed record PlanGateApprovalTag(string PlanId, string GateId);
internal sealed record PlanPreflightRecoveryTag(string GroupId, string Revision);
internal sealed record PlanPreflightRecoveryReceiptTag(string GroupId, string Revision, DateTimeOffset ResolvedAt);
internal sealed record TranscriptProtocolJsonIndicatorTag(string Marker);

/// <summary>
/// Shared sizing policy for UI cards embedded in a transcript FlowDocument. Stretching the card
/// lets WPF remeasure it whenever the transcript width changes; no per-card resize subscription is
/// required and historical cards remain responsive.
/// </summary>
internal static class TranscriptCardLayout
{
    private static readonly DependencyProperty HostContainerProperty = DependencyProperty.RegisterAttached(
        "HostContainer",
        typeof(BlockUIContainer),
        typeof(TranscriptCardLayout));
    private static readonly DependencyProperty CopyableProperty = DependencyProperty.RegisterAttached(
        "Copyable",
        typeof(ICopyable),
        typeof(TranscriptCardLayout));

    internal static T MakeResponsive<T>(T card) where T : FrameworkElement
    {
        card.Width = double.NaN;
        card.MaxWidth = double.PositiveInfinity;
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        return card;
    }

    internal static BlockUIContainer CreateContainer(
        UIElement child,
        object? tag = null,
        Thickness? margin = null)
    {
        if (child is FrameworkElement element)
            MakeResponsive(element);
        var container = new BlockUIContainer(child)
        {
            Margin = margin ?? new Thickness(0, 2, 0, 10),
            Tag = tag,
        };
        child.SetValue(HostContainerProperty, container);
        if (tag is not ICopyable && child is Border)
            child.SetValue(CopyableProperty, new VisualTranscriptCardCopyData(child));
        return container;
    }

    /// <summary>Finds the transcript container that owns a clicked card descendant.</summary>
    internal static BlockUIContainer? FindContainer(DependencyObject? origin)
    {
        for (var current = origin; current is not null;)
        {
            if (current is BlockUIContainer direct)
                return direct;
            if (current.GetValue(HostContainerProperty) is BlockUIContainer registered)
                return registered;

            DependencyObject? parent = null;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                parent = VisualTreeHelper.GetParent(current);
            current = parent ?? LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    internal static bool IsCard(BlockUIContainer? container) =>
        container is not null && (container.Tag is ICopyable || container.Child is Border);

    internal static ICopyable? GetCopyable(BlockUIContainer container) =>
        container.Tag as ICopyable ?? container.Child.GetValue(CopyableProperty) as ICopyable;
}

internal sealed class VisualTranscriptCardCopyData(UIElement card) : ICopyable
{
    public string GetCopyText() => TranscriptCopyService.BuildVisualCardText(card) ?? string.Empty;
}

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
        TranscriptCardLayout.CreateContainer(child, tag ?? ContainerMarker.Instance);

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

    /// <summary>
    /// Replaces every historical link/action surface for one pending plan with a single
    /// authoritative surface at the end of the transcript. This is used after restart,
    /// when the durable pending-plan store—not the position of its original turn—owns
    /// whether actions are still available.
    /// </summary>
    internal static void ReplacePendingDecomposeApprovalSurface(
        BlockCollection blocks,
        string groupId,
        Block planLink,
        BlockUIContainer actions)
    {
        RemovePendingDecomposeApprovalSurface(blocks, groupId);
        blocks.Add(planLink);
        blocks.Add(actions);
    }

    /// <summary>
    /// Replaces a pending plan's historical receipt and action card with one compact Inbox
    /// receipt at the position of the first historical surface. Returns false only when no
    /// surface survived transcript hydration, allowing the caller to use a recovery fallback.
    /// </summary>
    internal static bool ReplacePendingDecomposeApprovalSurfaceWithInboxBanner(
        BlockCollection blocks,
        string groupId,
        Block banner)
    {
        var inserted = false;
        ReplacePendingDecomposeApprovalSurfaceWithInboxBannerCore(
            blocks,
            groupId,
            banner,
            ref inserted);
        return inserted;
    }

    private static void ReplacePendingDecomposeApprovalSurfaceWithInboxBannerCore(
        BlockCollection blocks,
        string groupId,
        Block banner,
        ref bool inserted)
    {
        foreach (var block in blocks.ToArray())
        {
            var isMatchingSurface =
                block.Tag is PendingDecomposePlanLinkTag linkTag &&
                string.Equals(linkTag.GroupId, groupId, StringComparison.Ordinal) ||
                block.Tag is PendingDecomposeApprovalTag approvalTag &&
                string.Equals(approvalTag.GroupId, groupId, StringComparison.Ordinal) ||
                block.Tag is PendingDecomposeInboxBannerTag bannerTag &&
                string.Equals(bannerTag.GroupId, groupId, StringComparison.Ordinal);

            if (isMatchingSurface)
            {
                if (!inserted)
                {
                    blocks.InsertBefore(block, banner);
                    inserted = true;
                }
                blocks.Remove(block);
                continue;
            }

            if (block is Section section)
                ReplacePendingDecomposeApprovalSurfaceWithInboxBannerCore(
                    section.Blocks,
                    groupId,
                    banner,
                    ref inserted);
        }
    }

    internal static bool ContainsPendingDecomposeApprovalSurface(
        BlockCollection blocks,
        string groupId,
        string revision)
    {
        foreach (var block in blocks)
        {
            if (block.Tag is PendingDecomposePlanLinkTag linkTag &&
                string.Equals(linkTag.GroupId, groupId, StringComparison.Ordinal) &&
                string.Equals(linkTag.Revision, revision, StringComparison.Ordinal))
            {
                return true;
            }
            if (block.Tag is PendingDecomposeApprovalTag approvalTag &&
                string.Equals(approvalTag.GroupId, groupId, StringComparison.Ordinal) &&
                string.Equals(approvalTag.Revision, revision, StringComparison.Ordinal))
            {
                return true;
            }
            if (block is Section section &&
                ContainsPendingDecomposeApprovalSurface(section.Blocks, groupId, revision))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemovePendingDecomposeApprovalSurface(BlockCollection blocks, string groupId)
    {
        foreach (var block in blocks.ToArray())
        {
            var isMatchingLink = block.Tag is PendingDecomposePlanLinkTag linkTag &&
                                 string.Equals(linkTag.GroupId, groupId, StringComparison.Ordinal);
            var isMatchingActions = block.Tag is PendingDecomposeApprovalTag approvalTag &&
                                    string.Equals(approvalTag.GroupId, groupId, StringComparison.Ordinal);
            if (isMatchingLink || isMatchingActions)
            {
                blocks.Remove(block);
                continue;
            }

            if (block is Section section)
                RemovePendingDecomposeApprovalSurface(section.Blocks, groupId);
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

    internal static void RemoveProtocolJsonIndicators(BlockCollection blocks)
    {
        foreach (var block in blocks.ToArray())
        {
            if (block.Tag is TranscriptProtocolJsonIndicatorTag)
            {
                blocks.Remove(block);
                continue;
            }
            if (block is Section section)
                RemoveProtocolJsonIndicators(section.Blocks);
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
