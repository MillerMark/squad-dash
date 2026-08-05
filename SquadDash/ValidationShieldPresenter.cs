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
    internal const double BaseShieldIconWidth = 24;
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
            case AnchorKind.Rail when anchor.StageIndex >= 0 && anchor.StageIndex < stageBoundaryXs.Count:
                left = stageBoundaryXs[anchor.StageIndex] - 72 * s;
                top = graphTop - (90 + stackIndex * BaseShieldStackSpacing) * s;
                break;

            case AnchorKind.Before when anchor.TaskId is not null && taskPositions.TryGetValue(anchor.TaskId, out var beforePos):
                // The 24px shield is centered inside a 144px title container. Offset the
                // container so the visible shield—not its invisible container—aligns with
                // the task's entry edge.
                left = beforePos.X -
                       (BaseShieldVisualWidth - BaseShieldIconWidth) / 2 * s;
                top = beforePos.Y + nodeHeight + (8 + stackIndex * BaseShieldStackSpacing) * s;
                break;

            case AnchorKind.After when anchor.TaskId is not null && taskPositions.TryGetValue(anchor.TaskId, out var afterPos):
                // Mirror the entry calculation so the visible shield's right edge aligns
                // exactly with the task's exit edge.
                left = afterPos.X + nodeWidth -
                       (BaseShieldVisualWidth + BaseShieldIconWidth) / 2 * s;
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
            .GroupBy(a => a.StageIndex >= 0 ? $"stage:{a.StageIndex}" : "rail")
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
        return topStackCount == 0
            ? 0
            : (BaseRailTopPadding + topStackCount * BaseShieldStackSpacing) * scaleFactor;
    }

    /// <summary>
    /// Infers the milestone where a complex validation becomes runnable. A validation waits
    /// for every prerequisite, so its visual boundary follows the latest prerequisite stage,
    /// not the earliest stage mentioned in its contract.
    /// </summary>
    internal static int InferComplexValidationStageIndex(
        IReadOnlyList<int> afterLevels,
        IReadOnlyList<int> beforeLevels,
        int stageCount)
    {
        if (afterLevels.Count == 0 || beforeLevels.Count == 0 || stageCount < 2)
            return -1;

        var latestPrerequisiteStage = afterLevels.Max();
        return latestPrerequisiteStage >= 0 &&
               latestPrerequisiteStage < stageCount - 1 &&
               beforeLevels.All(level => level > latestPrerequisiteStage)
            ? latestPrerequisiteStage
            : -1;
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

    // ── ALL cluster footprint & connector collision ────────────────────────────

    /// <summary>Axis-aligned bounding rectangle.</summary>
    internal readonly record struct LayoutRect(double Left, double Top, double Width, double Height)
    {
        internal double Right => Left + Width;
        internal double Bottom => Top + Height;
    }

    /// <summary>
    /// Computes the bounding rectangle for an ALL badge and its attached shield stack,
    /// including title labels. The footprint is used for connector collision avoidance.
    /// </summary>
    internal static LayoutRect ComputeAllClusterFootprint(
        double gateCenterX, double gateCenterY, int shieldCount, double scaleFactor)
    {
        var s = scaleFactor;
        // Badge width ≈ 58px centered on gate
        var badgeHalfWidth = 29.0 * s;
        // Shields are 144px wide, centered on gate
        var shieldHalfWidth = BaseShieldVisualWidth / 2.0 * s;
        // A bare ALL badge must not reserve the width of a validation title that does not
        // exist. Doing so can falsely place a nearby connector endpoint inside the obstacle,
        // which makes every otherwise-valid forward detour appear impossible.
        var halfWidth = shieldCount > 0
            ? Math.Max(badgeHalfWidth, shieldHalfWidth)
            : badgeHalfWidth;

        var left = gateCenterX - halfWidth;
        var width = halfWidth * 2;

        // Top: badge extends BaseAllBadgeHalfHeight above gate center
        var top = gateCenterY - BaseAllBadgeHalfHeight * s;

        // Bottom: badge (2 * BaseAllBadgeHalfHeight) + shield stack below gate center
        double height;
        if (shieldCount <= 0)
        {
            height = BaseAllBadgeHalfHeight * 2 * s;
        }
        else
        {
            // Shields start at gateCenterY + BaseAllValidationTopOffset and each occupies BaseShieldStackSpacing
            var shieldBottom = (BaseAllValidationTopOffset + shieldCount * BaseShieldStackSpacing) * s;
            height = (gateCenterY + shieldBottom) - top;
        }

        return new LayoutRect(left, top, width, height);
    }

    /// <summary>Input used to vertically arrange ALL clusters sharing one boundary.</summary>
    internal readonly record struct AllClusterStackItem(double CenterY, int ShieldCount);

    /// <summary>
    /// Resolves the center Y coordinates of ALL clusters sharing the same boundary so the
    /// complete badge + validation-title footprint of an upper cluster cannot overlap the
    /// badge or validation stack below it. Returned centers preserve the caller's item order.
    /// </summary>
    internal static IReadOnlyList<double> StackAllClusterCenters(
        IReadOnlyList<AllClusterStackItem> items,
        double scaleFactor)
    {
        if (items.Count == 0) return [];

        var resolved = items.Select(item => item.CenterY).ToArray();
        var order = Enumerable.Range(0, items.Count)
            .OrderBy(index => items[index].CenterY)
            // When centers coincide, keep the taller validation-bearing cluster above the
            // shorter one so the visual reading order is deterministic.
            .ThenByDescending(index => items[index].ShieldCount)
            .ThenBy(index => index)
            .ToArray();
        LayoutRect? previous = null;
        var gap = BaseClusterConnectorClearance * scaleFactor;

        foreach (var index in order)
        {
            var centerY = resolved[index];
            var footprint = ComputeAllClusterFootprint(
                gateCenterX: 0,
                gateCenterY: centerY,
                shieldCount: items[index].ShieldCount,
                scaleFactor);
            if (previous is { } upper && footprint.Top < upper.Bottom + gap)
            {
                centerY += upper.Bottom + gap - footprint.Top;
                resolved[index] = centerY;
                footprint = ComputeAllClusterFootprint(
                    gateCenterX: 0,
                    gateCenterY: centerY,
                    shieldCount: items[index].ShieldCount,
                    scaleFactor);
            }
            previous = footprint;
        }

        return resolved;
    }

    /// <summary>
    /// Checks whether a straight connector line between two points intersects any ALL cluster footprint.
    /// Returns true if the path is clear (no intersections).
    /// </summary>
    internal static bool IsConnectorPathClear(
        (double X, double Y) connectorStart,
        (double X, double Y) connectorEnd,
        IReadOnlyList<LayoutRect> allClusterFootprints)
    {
        if (allClusterFootprints.Count == 0) return true;

        foreach (var rect in allClusterFootprints)
        {
            if (LineIntersectsRect(connectorStart.X, connectorStart.Y,
                                   connectorEnd.X, connectorEnd.Y, rect))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Computes waypoints that route a connector around ALL cluster footprints.
    /// Returns null if no detour is needed (path is clear).
    /// Returns an empty collection when an obstruction exists but neither a forward-only upper
    /// nor lower route is clear; callers may then use an explicitly styled fallback connector.
    /// The returned list includes the start, any intermediate waypoints, and the end.
    /// </summary>
    internal static IReadOnlyList<(double X, double Y)>? ComputeConnectorDetour(
        (double X, double Y) connectorStart,
        (double X, double Y) connectorEnd,
        IReadOnlyList<LayoutRect> allClusterFootprints,
        double scaleFactor)
    {
        if (IsConnectorPathClear(connectorStart, connectorEnd, allClusterFootprints))
            return null;

        var clearance = BaseClusterConnectorClearance * scaleFactor;
        var intersecting = allClusterFootprints
            .Where(r => LineIntersectsRect(connectorStart.X, connectorStart.Y,
                                           connectorEnd.X, connectorEnd.Y, r))
            .OrderBy(r => r.Left)
            .ToArray();
        if (intersecting.Length == 0)
            return [];

        var minX = Math.Min(connectorStart.X, connectorEnd.X);
        var maxX = Math.Max(connectorStart.X, connectorEnd.X);
        var horizontallyRelevant = allClusterFootprints
            .Where(rect => rect.Right >= minX && rect.Left <= maxX)
            .ToArray();
        if (horizontallyRelevant.Length == 0)
            horizontallyRelevant = intersecting;

        // Reserve a final horizontal run into the target. Arrowheads are right-facing, so a
        // vertical final segment would create a misleading curl immediately before the task.
        if (maxX - minX <= clearance * 2)
            return [];
        var entryX = Math.Clamp(intersecting.Min(rect => rect.Left) - clearance, minX, maxX);
        var exitX = Math.Clamp(
            intersecting.Max(rect => rect.Right) + clearance,
            entryX,
            maxX - clearance);
        if (entryX > exitX)
            return [];

        var upperY = horizontallyRelevant.Min(rect => rect.Top) - clearance;
        var lowerY = horizontallyRelevant.Max(rect => rect.Bottom) + clearance;
        var candidates = new[]
        {
            BuildOrthogonalDetour(connectorStart, connectorEnd, entryX, exitX, upperY),
            BuildOrthogonalDetour(connectorStart, connectorEnd, entryX, exitX, lowerY),
        };

        return candidates
            .Where(route => IsConnectorRouteForwardOnly(route) &&
                            IsConnectorRouteClear(route, allClusterFootprints))
            .OrderBy(ConnectorRouteLength)
            .FirstOrDefault() ?? [];
    }

    /// <summary>True when every segment in a waypoint route clears every obstacle.</summary>
    internal static bool IsConnectorRouteClear(
        IReadOnlyList<(double X, double Y)> route,
        IReadOnlyList<LayoutRect> footprints)
    {
        if (route.Count < 2) return false;
        for (var index = 0; index < route.Count - 1; index++)
        {
            if (!IsConnectorPathClear(route[index], route[index + 1], footprints))
                return false;
        }
        return true;
    }

    /// <summary>True when a left-to-right connector never reverses horizontal direction.</summary>
    internal static bool IsConnectorRouteForwardOnly(
        IReadOnlyList<(double X, double Y)> route)
    {
        if (route.Count < 2) return false;
        var direction = Math.Sign(route[^1].X - route[0].X);
        if (direction == 0) return route.All(point => Math.Abs(point.X - route[0].X) < 0.01);

        for (var index = 1; index < route.Count; index++)
        {
            var delta = route[index].X - route[index - 1].X;
            if (direction > 0 && delta < -0.01) return false;
            if (direction < 0 && delta > 0.01) return false;
        }
        return true;
    }

    /// <summary>Geometry for one rounded interior turn in a routed connector.</summary>
    internal readonly record struct RoundedRouteCorner(
        int PointIndex,
        (double X, double Y) Entry,
        (double X, double Y) Control,
        (double X, double Y) Exit,
        double Radius);

    /// <summary>
    /// Computes rounded-corner geometry for a polyline route. At each real turn, the cutback
    /// distance is half the shorter adjacent segment. The original corner is the quadratic
    /// Bézier control point; Entry and Exit are the curve endpoints on the adjoining segments.
    /// </summary>
    internal static IReadOnlyList<RoundedRouteCorner> ComputeRoundedRouteCorners(
        IReadOnlyList<(double X, double Y)> route)
    {
        if (route.Count < 3) return [];

        var corners = new List<RoundedRouteCorner>();
        for (var index = 1; index < route.Count - 1; index++)
        {
            var previous = route[index - 1];
            var corner = route[index];
            var next = route[index + 1];
            var incomingX = corner.X - previous.X;
            var incomingY = corner.Y - previous.Y;
            var outgoingX = next.X - corner.X;
            var outgoingY = next.Y - corner.Y;
            var incomingLength = Math.Sqrt(incomingX * incomingX + incomingY * incomingY);
            var outgoingLength = Math.Sqrt(outgoingX * outgoingX + outgoingY * outgoingY);
            if (incomingLength < 0.01 || outgoingLength < 0.01) continue;

            var incomingUnitX = incomingX / incomingLength;
            var incomingUnitY = incomingY / incomingLength;
            var outgoingUnitX = outgoingX / outgoingLength;
            var outgoingUnitY = outgoingY / outgoingLength;
            var cross = incomingUnitX * outgoingUnitY - incomingUnitY * outgoingUnitX;
            var dot = incomingUnitX * outgoingUnitX + incomingUnitY * outgoingUnitY;
            if (Math.Abs(cross) < 0.0001 && dot > 0) continue;

            var radius = Math.Min(incomingLength, outgoingLength) / 2.0;
            corners.Add(new RoundedRouteCorner(
                index,
                (corner.X - incomingUnitX * radius, corner.Y - incomingUnitY * radius),
                corner,
                (corner.X + outgoingUnitX * radius, corner.Y + outgoingUnitY * radius),
                radius));
        }
        return corners;
    }

    private static IReadOnlyList<(double X, double Y)> BuildOrthogonalDetour(
        (double X, double Y) start,
        (double X, double Y) end,
        double entryX,
        double exitX,
        double laneY)
    {
        var points = new List<(double X, double Y)> { start };
        AddDistinct(points, (entryX, start.Y));
        AddDistinct(points, (entryX, laneY));
        AddDistinct(points, (exitX, laneY));
        AddDistinct(points, (exitX, end.Y));
        AddDistinct(points, end);
        return points;
    }

    private static void AddDistinct(
        ICollection<(double X, double Y)> points,
        (double X, double Y) point)
    {
        var last = points.Last();
        if (Math.Abs(last.X - point.X) < 0.01 && Math.Abs(last.Y - point.Y) < 0.01)
            return;
        points.Add(point);
    }

    private static double ConnectorRouteLength(IReadOnlyList<(double X, double Y)> route)
    {
        var length = 0.0;
        for (var index = 0; index < route.Count - 1; index++)
        {
            var dx = route[index + 1].X - route[index].X;
            var dy = route[index + 1].Y - route[index].Y;
            length += Math.Sqrt(dx * dx + dy * dy);
        }
        return length;
    }

    /// <summary>
    /// Tests whether a line segment from (x1,y1)→(x2,y2) intersects an axis-aligned rectangle.
    /// Uses Liang-Barsky clipping algorithm.
    /// </summary>
    private static bool LineIntersectsRect(double x1, double y1, double x2, double y2, LayoutRect rect)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;

        double tMin = 0, tMax = 1;

        // Check each edge
        double[] p = { -dx, dx, -dy, dy };
        double[] q = { x1 - rect.Left, rect.Right - x1, y1 - rect.Top, rect.Bottom - y1 };

        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-10)
            {
                // Line parallel to edge — if outside, no intersection
                if (q[i] < 0) return false;
            }
            else
            {
                var t = q[i] / p[i];
                if (p[i] < 0)
                    tMin = Math.Max(tMin, t);
                else
                    tMax = Math.Min(tMax, t);
                if (tMin > tMax) return false;
            }
        }

        return true;
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
