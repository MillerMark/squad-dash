using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic engine that deterministically infers which approval gate is the primary
/// visual controller when no stored presentation anchor exists. Priority order:
/// 1. Exact stage milestone match → primary
/// 2. Exact ALL join match → primary
/// 3. Task exit or entry → primary
/// All equivalent controls (same logical boundary) get half-opacity.
/// One logical gate yields one summary item.
/// </summary>
internal static class ApprovalAnchorInferenceEngine
{
    /// <summary>
    /// Infers the full presentation model for all gates in a plan.
    /// Returns null when the plan has no approval gates.
    /// </summary>
    internal static ApprovalAnchorPresentation? Infer(
        Plan plan,
        IReadOnlyDictionary<string, int> levels,
        double fontSizeFactor = 1.0)
    {
        if (plan.ApprovalGates.Count == 0)
            return null;

        var anchors = plan.ApprovalGates
            .Select(gate => (Gate: gate,
                Anchor: PlanApprovalPresentationAnchorResolver.Resolve(gate, plan.Tasks, levels) ?? string.Empty))
            .ToArray();

        var primaryGateId = SelectPrimary(anchors);
        if (primaryGateId is null)
            return null;

        var primaryAnchor = anchors.First(a =>
            string.Equals(a.Gate.GateId, primaryGateId, StringComparison.Ordinal)).Anchor;

        var equivalentIds = anchors
            .Where(a => !string.Equals(a.Gate.GateId, primaryGateId, StringComparison.Ordinal) &&
                        string.Equals(a.Anchor, primaryAnchor, StringComparison.Ordinal))
            .Select(a => a.Gate.GateId)
            .ToArray();

        var sentence = BuildRequirementsSentence(primaryAnchor, plan, levels);
        var summaryItems = BuildSummaryItems(anchors);

        return new ApprovalAnchorPresentation(
            primaryGateId,
            primaryAnchor,
            equivalentIds,
            sentence,
            summaryItems);
    }

    /// <summary>
    /// Computes font metrics for approval anchor display given a base size and DPI factor.
    /// </summary>
    internal static ApprovalAnchorFontMetrics ComputeFontMetrics(
        double baseFontSize,
        double fontSizeFactor)
    {
        var effective = baseFontSize * fontSizeFactor;
        return new ApprovalAnchorFontMetrics(baseFontSize, fontSizeFactor, effective);
    }

    /// <summary>
    /// Selects the primary gate using deterministic priority:
    /// 1. First gate with a stage milestone anchor
    /// 2. First gate with an ALL join anchor
    /// 3. First gate with a task-exit or task-entry anchor
    /// 4. First gate by declaration order (fallback)
    /// </summary>
    private static string? SelectPrimary(
        IReadOnlyList<(PlanApprovalGate Gate, string Anchor)> anchors)
    {
        if (anchors.Count == 0)
            return null;

        // Priority 1: stage milestone
        var stage = anchors.FirstOrDefault(a => a.Anchor.StartsWith("stage:", StringComparison.Ordinal));
        if (stage.Gate is not null)
            return stage.Gate.GateId;

        // Priority 2: ALL join
        var allJoin = anchors.FirstOrDefault(a => a.Anchor.StartsWith("all:", StringComparison.Ordinal));
        if (allJoin.Gate is not null)
            return allJoin.Gate.GateId;

        // Priority 3: task exit or entry
        var taskAnchor = anchors.FirstOrDefault(a =>
            a.Anchor.StartsWith("task-after:", StringComparison.Ordinal) ||
            a.Anchor.StartsWith("task-before:", StringComparison.Ordinal));
        if (taskAnchor.Gate is not null)
            return taskAnchor.Gate.GateId;

        // Fallback: first by declaration order
        return anchors[0].Gate.GateId;
    }

    /// <summary>
    /// Builds the human-readable "Human approval requirements" sentence from the primary anchor.
    /// </summary>
    internal static string BuildRequirementsSentence(
        string anchor,
        Plan plan,
        IReadOnlyDictionary<string, int> levels)
    {
        if (anchor.StartsWith("stage:", StringComparison.Ordinal) &&
            int.TryParse(anchor["stage:".Length..], out var stageNum))
        {
            var stageCount = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
            return $"Human approval required between stage {stageNum} and stage {stageNum + 1} of {stageCount}.";
        }

        if (anchor.StartsWith("all:", StringComparison.Ordinal))
        {
            var taskIds = anchor["all:".Length..].Split('|');
            var names = taskIds.Select(id => ResolveTaskTitle(plan, id)).ToArray();
            return $"Human approval required at ALL join before {string.Join(", ", names)}.";
        }

        if (anchor.StartsWith("task-after:", StringComparison.Ordinal))
        {
            var taskId = anchor["task-after:".Length..];
            return $"Human approval required after {ResolveTaskTitle(plan, taskId)} completes.";
        }

        if (anchor.StartsWith("task-before:", StringComparison.Ordinal))
        {
            var taskId = anchor["task-before:".Length..];
            return $"Human approval required before {ResolveTaskTitle(plan, taskId)} starts.";
        }

        return "Human approval required at gate boundary.";
    }

    private static string ResolveTaskTitle(Plan plan, string taskId)
    {
        var task = plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        return task?.Title ?? taskId;
    }

    private static IReadOnlyList<ApprovalAnchorSummaryItem> BuildSummaryItems(
        IReadOnlyList<(PlanApprovalGate Gate, string Anchor)> anchors)
    {
        return anchors.Select(a => new ApprovalAnchorSummaryItem(
            a.Gate.GateId,
            a.Anchor,
            BuildItemDescription(a.Anchor, a.Gate))).ToArray();
    }

    private static string BuildItemDescription(string anchor, PlanApprovalGate gate)
    {
        if (anchor.StartsWith("stage:", StringComparison.Ordinal))
            return $"Stage milestone: {gate.Message}";
        if (anchor.StartsWith("all:", StringComparison.Ordinal))
            return $"ALL join: {gate.Message}";
        if (anchor.StartsWith("task-after:", StringComparison.Ordinal))
            return $"After task: {gate.Message}";
        if (anchor.StartsWith("task-before:", StringComparison.Ordinal))
            return $"Before task: {gate.Message}";
        return $"Boundary: {gate.Message}";
    }
}
