namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure presentation logic for plan validation shields. Derives visual state,
/// tooltip content, and task-highlighting sets from durable plan state.
/// Stateless and testable without WPF dependencies.
/// </summary>
internal static class ValidationShieldPresenter
{
    // ── Shield visual state ───────────────────────────────────────────────────

    /// <summary>Visual state categories for the validation shield icon.</summary>
    internal enum ShieldVisualState
    {
        /// <summary>Outlined shield with outlined check — waiting for prerequisites.</summary>
        Pending,
        /// <summary>Outlined shield with outlined check — prerequisites met, ready to validate.</summary>
        Ready,
        /// <summary>Active/animated — validation currently executing.</summary>
        Validating,
        /// <summary>Filled shield with high-contrast check — validation passed.</summary>
        Passed,
        /// <summary>Filled shield with X mark — validation failed.</summary>
        Failed,
        /// <summary>Dimmed/dashed — validation was invalidated, needs rerun.</summary>
        Stale,
    }

    /// <summary>
    /// Derives the shield visual state from the durable <see cref="PlanValidationStatus"/> string.
    /// </summary>
    internal static ShieldVisualState DeriveVisualState(string? status) => status switch
    {
        PlanValidationStatus.Ready      => ShieldVisualState.Ready,
        PlanValidationStatus.Validating => ShieldVisualState.Validating,
        PlanValidationStatus.Passed     => ShieldVisualState.Passed,
        PlanValidationStatus.Failed     => ShieldVisualState.Failed,
        PlanValidationStatus.Stale      => ShieldVisualState.Stale,
        _ => ShieldVisualState.Pending,
    };

    internal static bool ShowsActivitySpinner(string? status) =>
        string.Equals(status, PlanValidationStatus.Validating, StringComparison.Ordinal);

    // ── Tooltip content ───────────────────────────────────────────────────────

    /// <summary>Structured tooltip content for a validation shield.</summary>
    internal sealed record ShieldTooltipContent(
        string Title,
        string Description,
        string StatusLabel,
        IReadOnlyList<string> Assertions,
        IReadOnlyList<string> PrerequisiteLabels,
        IReadOnlyList<string> BlockedLabels,
        IReadOnlyList<string>? Evidence,
        string? Summary);

    /// <summary>
    /// Builds tooltip content for a validation node using only model data.
    /// </summary>
    internal static ShieldTooltipContent BuildTooltipContent(
        PlanValidationNode validation,
        IReadOnlyList<PlanTask> allTasks)
    {
        var tasksByIdMap = allTasks.ToDictionary(t => t.TaskId, StringComparer.Ordinal);

        string TaskLabel(string id) => tasksByIdMap.TryGetValue(id, out var task)
            ? $"{task.Title ?? task.Description} ({id})"
            : id;

        var prereqLabels = validation.AfterTaskIds.Select(TaskLabel).ToArray();
        var blockedLabels = validation.BeforeTaskIds.Select(TaskLabel).ToArray();

        return new ShieldTooltipContent(
            Title: validation.Title,
            Description: validation.Description,
            StatusLabel: FormatStatus(validation.Status),
            Assertions: validation.Assertions,
            PrerequisiteLabels: prereqLabels,
            BlockedLabels: blockedLabels,
            Evidence: validation.Evidence,
            Summary: validation.Summary);
    }

    /// <summary>Human-readable status label.</summary>
    internal static string FormatStatus(string status) => status switch
    {
        PlanValidationStatus.Ready      => "Ready to validate",
        PlanValidationStatus.Validating => "Validating now",
        PlanValidationStatus.Passed     => "Passed",
        PlanValidationStatus.Failed     => "Failed",
        PlanValidationStatus.Stale      => "Needs revalidation",
        _                               => "Waiting for prerequisite tasks",
    };

    // ── Task highlighting ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the set of task IDs that should be highlighted when a shield is hovered.
    /// Includes both prerequisite (afterTaskIds) and blocked downstream (beforeTaskIds + transitive).
    /// </summary>
    internal static ValidationHighlightSet ComputeHighlightedTasks(
        PlanValidationNode validation,
        IReadOnlyList<PlanTask> allTasks)
    {
        var prerequisiteIds = new HashSet<string>(validation.AfterTaskIds, StringComparer.Ordinal);
        var directlyBlocked = new HashSet<string>(validation.BeforeTaskIds, StringComparer.Ordinal);

        // Compute transitive downstream: tasks that depend (directly or transitively) on blocked tasks.
        var allBlocked = new HashSet<string>(directlyBlocked, StringComparer.Ordinal);
        var frontier = new Queue<string>(directlyBlocked);
        var taskDependents = BuildDependentsMap(allTasks);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!taskDependents.TryGetValue(current, out var dependents)) continue;
            foreach (var dep in dependents)
            {
                if (allBlocked.Add(dep))
                    frontier.Enqueue(dep);
            }
        }

        return new ValidationHighlightSet(prerequisiteIds, allBlocked);
    }

    /// <summary>Prerequisite and blocked task ID sets for hover highlighting.</summary>
    internal sealed record ValidationHighlightSet(
        IReadOnlySet<string> PrerequisiteTaskIds,
        IReadOnlySet<string> BlockedTaskIds);

    // ── Compact summary for Plans panel ───────────────────────────────────────

    /// <summary>Summary counts for plan validation nodes, suitable for compact display.</summary>
    internal sealed record ValidationSummary(
        int Total,
        int Passed,
        int Failed,
        int Stale,
        int Validating,
        int Ready,
        int Pending);

    /// <summary>
    /// Summarizes validation states for a plan. Returns null if the plan has no validations.
    /// </summary>
    internal static ValidationSummary? Summarize(Plan plan)
    {
        var validations = plan.Validations;
        if (validations is null or { Count: 0 })
            return null;

        int passed = 0, failed = 0, stale = 0, validating = 0, ready = 0, pending = 0;
        foreach (var v in validations)
        {
            switch (v.Status)
            {
                case PlanValidationStatus.Passed: passed++; break;
                case PlanValidationStatus.Failed: failed++; break;
                case PlanValidationStatus.Stale: stale++; break;
                case PlanValidationStatus.Validating: validating++; break;
                case PlanValidationStatus.Ready: ready++; break;
                default: pending++; break;
            }
        }
        return new ValidationSummary(validations.Count, passed, failed, stale, validating, ready, pending);
    }

    /// <summary>
    /// Builds a concise label for the plan validation summary (e.g. "2/3 passed", "1 failed").
    /// Returns null if there are no validations.
    /// </summary>
    internal static string? BuildSummaryLabel(ValidationSummary? summary)
    {
        if (summary is null) return null;
        if (summary.Failed > 0) return $"{summary.Failed} validation{(summary.Failed > 1 ? "s" : "")} failed";
        if (summary.Validating > 0) return "Validating…";
        if (summary.Passed == summary.Total) return $"All {summary.Total} validations passed";
        if (summary.Passed > 0) return $"{summary.Passed}/{summary.Total} validations passed";
        if (summary.Ready > 0) return $"{summary.Ready} validation{(summary.Ready > 1 ? "s" : "")} ready";
        return $"{summary.Total} validation{(summary.Total > 1 ? "s" : "")} pending";
    }

    // ── Layout positioning (pure computation) ───────────────────────────────

    /// <summary>The kind of anchor position for a validation shield.</summary>
    internal enum AnchorKind { Stage, All, Before, After, Rail }

    /// <summary>Describes where a validation shield is anchored in the plan graph.</summary>
    internal sealed record ShieldAnchor(
        AnchorKind Kind,
        string? TaskId = null,
        int StageIndex = -1,
        string? AllKey = null);

    /// <summary>Computed position for a single validation shield.</summary>
    internal sealed record ShieldLayoutPosition(
        double Left,
        double Top,
        int StackIndex,
        ShieldAnchor Anchor);

    /// <summary>Layout measurements for the validation rail.</summary>
    internal sealed record ValidationRailMetrics(
        double RailHeight,
        double GraphTop,
        double MaxRight,
        double MaxBottom);

    /// <summary>Per-shield width and height constants (before scale).</summary>
    internal const double BaseShieldVisualWidth = 144;
    internal const double BaseShieldStackSpacing = 66;
    internal const double BaseShieldVisualHeight = 64;
    internal const double BaseRailTopPadding = 42;
    internal const double BaseAllValidationTopOffset = 24;
    internal const double BaseAllBadgeHalfHeight = 17;
    internal const double BaseClusterConnectorClearance = 14;

    /// <summary>
    /// Computes the layout position for a single validation shield given its anchor,
    /// stage boundary positions, task node positions, gate centers, and stacking state.
    /// </summary>
    internal static ShieldLayoutPosition ComputeShieldPosition(
        ShieldAnchor anchor,
        int stackIndex,
        double scaleFactor,
        IReadOnlyList<double> stageBoundaryXs,
        IReadOnlyDictionary<string, (double X, double Y)> taskPositions,
        double nodeWidth,
        double nodeHeight,
        double graphTop,
        IReadOnlyList<(double CenterX, double CenterY, string AllKey)>? gateCenters,
        ref double fallbackNextLeft)
    {
        var s = scaleFactor;
        double left, top;

        switch (anchor.Kind)
        {
            case AnchorKind.Stage when anchor.StageIndex >= 0 && anchor.StageIndex < stageBoundaryXs.Count:
                left = stageBoundaryXs[anchor.StageIndex] - 72 * s;
                top = graphTop - (112 + stackIndex * BaseShieldStackSpacing) * s;
                break;

            case AnchorKind.Before when anchor.TaskId is not null && taskPositions.TryGetValue(anchor.TaskId, out var beforePos):
                left = beforePos.X - 72 * s;
                top = beforePos.Y + nodeHeight + (8 + stackIndex * BaseShieldStackSpacing) * s;
                break;

            case AnchorKind.After when anchor.TaskId is not null && taskPositions.TryGetValue(anchor.TaskId, out var afterPos):
                left = afterPos.X + nodeWidth - 72 * s;
                top = afterPos.Y + nodeHeight + (8 + stackIndex * BaseShieldStackSpacing) * s;
                break;

            case AnchorKind.All when anchor.AllKey is not null && gateCenters is not null:
                var matchingGate = gateCenters.FirstOrDefault(g =>
                    string.Equals(g.AllKey, anchor.AllKey, StringComparison.Ordinal));
                if (matchingGate != default)
                {
                    left = matchingGate.CenterX - 72 * s;
                    top = matchingGate.CenterY + (24 + stackIndex * BaseShieldStackSpacing) * s;
                }
                else
                {
                    left = fallbackNextLeft;
                    top = 28 * s;
                    fallbackNextLeft += 156 * s;
                }
                break;

            default:
                left = fallbackNextLeft;
                top = 28 * s;
                fallbackNextLeft += 156 * s;
                break;
        }

        return new ShieldLayoutPosition(left, top, stackIndex, anchor);
    }

    /// <summary>
    /// Computes the vertical space reserved for top-rail validation stacks (stage/rail anchors).
    /// Returns the rail height in scaled pixels.
    /// </summary>
    internal static double ComputeValidationRailHeight(
        IReadOnlyList<ShieldAnchor> anchors, double scaleFactor)
    {
        var topStackCount = anchors
            .Where(a => a.Kind is AnchorKind.Stage or AnchorKind.Rail)
            .GroupBy(a => a.Kind == AnchorKind.Stage ? $"stage:{a.StageIndex}" : "rail")
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
        return topStackCount == 0
            ? 0
            : (BaseRailTopPadding + topStackCount * BaseShieldStackSpacing) * scaleFactor;
    }

    /// <summary>
    /// Computes the extra row spacing needed when validations attach below a task node.
    /// </summary>
    internal static double ComputeAttachedTaskSpacing(
        int attachedValidationCount, double nodeHeight, double baseRowSpacing, double scaleFactor)
    {
        if (attachedValidationCount == 0)
            return Math.Max(baseRowSpacing, nodeHeight + 40 * scaleFactor);
        return Math.Max(baseRowSpacing,
            nodeHeight + (18 + attachedValidationCount * BaseShieldStackSpacing) * scaleFactor);
    }

    /// <summary>
    /// Moves an ALL badge and its attached validation stack as one cluster when an unrelated
    /// connector would cross the cluster's badge/shield/title footprint. The preferred escape
    /// lane is above the connector, matching the plan's top-to-bottom reading order.
    /// </summary>
    internal static double AvoidConnectorOverlapForAllCluster(
        double initialCenterY,
        int attachedValidationCount,
        IReadOnlyList<double> foreignConnectorYs,
        double scaleFactor)
    {
        if (attachedValidationCount <= 0 || foreignConnectorYs.Count == 0)
            return initialCenterY;

        var clusterTopOffset = BaseAllBadgeHalfHeight * scaleFactor;
        var clusterBottomOffset =
            (BaseAllValidationTopOffset + attachedValidationCount * BaseShieldStackSpacing) * scaleFactor;
        var clearance = BaseClusterConnectorClearance * scaleFactor;
        var crossings = foreignConnectorYs
            .Where(y => y >= initialCenterY - clusterTopOffset - clearance &&
                        y <= initialCenterY + clusterBottomOffset + clearance)
            .ToArray();
        if (crossings.Length == 0)
            return initialCenterY;

        return crossings.Min() - clearance - clusterBottomOffset;
    }

    // ── Title truncation ─────────────────────────────────────────────────────

    /// <summary>Maximum character length for shield titles before truncation.</summary>
    internal const int MaxTitleLength = 28;

    /// <summary>
    /// Truncates a validation title to fit within the shield label area.
    /// Returns the original title if short enough, otherwise truncates with ellipsis.
    /// </summary>
    internal static string TruncateTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;
        if (title.Length <= MaxTitleLength) return title;
        return title[..(MaxTitleLength - 1)] + "…";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, List<string>> BuildDependentsMap(IReadOnlyList<PlanTask> tasks)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            foreach (var dep in task.DependsOn)
            {
                if (!map.TryGetValue(dep, out var list))
                {
                    list = [];
                    map[dep] = list;
                }
                list.Add(task.TaskId);
            }
        }
        return map;
    }
}
