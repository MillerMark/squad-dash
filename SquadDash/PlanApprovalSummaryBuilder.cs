using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal enum ApprovalSummaryKind { TaskBefore, TaskAfter, Stage, All, Boundary }

internal sealed record ApprovalSummaryItem(
    ApprovalSummaryKind Kind,
    IReadOnlyList<string> AfterTaskIds,
    IReadOnlyList<string> BeforeTaskIds,
    string? TaskId = null,
    int? LeftStage = null);

internal sealed record PlanApprovalSummary(
    bool BetweenEveryStage,
    IReadOnlyList<ApprovalSummaryItem> Items);

/// <summary>Converts stored approval gates into concise, presentation-anchor-aware prose data.</summary>
internal static class PlanApprovalSummaryBuilder
{
    internal static PlanApprovalSummary Build(Plan plan, IReadOnlyDictionary<string, int> levels)
    {
        var stageCount = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
        var ordered = plan.ApprovalGates
            .OrderBy(gate => gate.BeforeTaskIds.Select(id => levels.GetValueOrDefault(id, int.MaxValue)).DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(gate => gate.AfterTaskIds.Select(id => levels.GetValueOrDefault(id, int.MaxValue)).DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(gate => gate.GateId, StringComparer.Ordinal)
            .ToArray();

        var stageAnchors = ordered.Select(ParseStageAnchor).Where(stage => stage.HasValue)
            .Select(stage => stage!.Value).ToHashSet();
        var betweenEveryStage = stageCount > 1 && ordered.Length == stageCount - 1 &&
            Enumerable.Range(1, stageCount - 1).All(stageAnchors.Contains);
        if (betweenEveryStage)
            return new PlanApprovalSummary(true, []);

        var items = ordered.Select(gate => BuildItem(gate)).ToArray();
        return new PlanApprovalSummary(false, items);
    }

    private static ApprovalSummaryItem BuildItem(PlanApprovalGate gate)
    {
        var anchor = gate.PresentationAnchor ?? string.Empty;
        if (anchor.StartsWith("task-before:", StringComparison.Ordinal))
            return new(ApprovalSummaryKind.TaskBefore, gate.AfterTaskIds, gate.BeforeTaskIds,
                anchor["task-before:".Length..]);
        if (anchor.StartsWith("task-after:", StringComparison.Ordinal))
            return new(ApprovalSummaryKind.TaskAfter, gate.AfterTaskIds, gate.BeforeTaskIds,
                anchor["task-after:".Length..]);
        if (ParseStageAnchor(gate) is { } stage)
            return new(ApprovalSummaryKind.Stage, gate.AfterTaskIds, gate.BeforeTaskIds, LeftStage: stage);
        if (anchor.StartsWith("all:", StringComparison.Ordinal))
            return new(ApprovalSummaryKind.All, gate.AfterTaskIds, gate.BeforeTaskIds);
        return new(ApprovalSummaryKind.Boundary, gate.AfterTaskIds, gate.BeforeTaskIds);
    }

    private static int? ParseStageAnchor(PlanApprovalGate gate)
    {
        const string prefix = "stage:";
        return gate.PresentationAnchor?.StartsWith(prefix, StringComparison.Ordinal) == true &&
               int.TryParse(gate.PresentationAnchor[prefix.Length..], out var stage)
            ? stage
            : null;
    }
}
