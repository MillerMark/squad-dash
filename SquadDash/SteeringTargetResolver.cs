using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// A live agent/session that can be shown in the steering target picker.
/// Running nodes without a provider session remain in the hierarchy as context,
/// but cannot be selected.
/// </summary>
internal sealed record SteeringTargetCandidate(
    string Id,
    string DisplayName,
    string? SessionId,
    string? ParentId,
    int StableOrder,
    bool IsRunning,
    DateTimeOffset? DirectLastActivityAt,
    int ActiveToolCount,
    bool IsWaitingOnChild,
    string? StatusText = null) {

    internal bool CanSteer => IsRunning && !string.IsNullOrWhiteSpace(SessionId);
}

internal static class SteeringTargetResolver {
    /// <summary>
    /// Selects the agent most likely to be doing consequential work right now.
    /// Direct activity is used rather than request-tree activity, because child
    /// activity is intentionally propagated into ancestor display state.
    /// </summary>
    internal static SteeringTargetCandidate? SelectDefault(
        IEnumerable<SteeringTargetCandidate> candidates) {
        var materialized = candidates.ToArray();
        return materialized
            .Where(candidate => candidate.CanSteer)
            .OrderBy(candidate => candidate.IsWaitingOnChild)
            .ThenByDescending(candidate => candidate.DirectLastActivityAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(candidate => candidate.ActiveToolCount > 0)
            .ThenByDescending(candidate => ResolveDepth(candidate, materialized))
            .ThenBy(candidate => candidate.StableOrder)
            .FirstOrDefault();
    }

    /// <summary>
    /// Produces parent-before-child display order while retaining deterministic
    /// ordering for siblings and malformed/orphaned lifecycle data.
    /// </summary>
    internal static IReadOnlyList<SteeringTargetCandidate> OrderForDisplay(
        IEnumerable<SteeringTargetCandidate> candidates) {
        var materialized = candidates
            .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(group => group.OrderBy(candidate => candidate.StableOrder).First())
            .ToArray();
        var ids = materialized.Select(candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);
        const string RootKey = "\0";
        var children = materialized
            .GroupBy(candidate => ids.Contains(candidate.ParentId ?? string.Empty)
                ? candidate.ParentId!
                : RootKey,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(candidate => candidate.StableOrder).ToArray(),
                StringComparer.Ordinal);
        var result = new List<SteeringTargetCandidate>(materialized.Length);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void AppendChildren(string? parentId) {
            if (!children.TryGetValue(parentId ?? RootKey, out var directChildren))
                return;

            foreach (var child in directChildren) {
                if (!visited.Add(child.Id))
                    continue;
                result.Add(child);
                AppendChildren(child.Id);
            }
        }

        AppendChildren(null);
        foreach (var candidate in materialized.OrderBy(candidate => candidate.StableOrder)) {
            if (!visited.Add(candidate.Id))
                continue;
            result.Add(candidate);
            AppendChildren(candidate.Id);
        }

        return result;
    }

    internal static int ResolveDepth(
        SteeringTargetCandidate candidate,
        IEnumerable<SteeringTargetCandidate> candidates) {
        var byId = candidates
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { candidate.Id };
        var depth = 0;
        var parentId = candidate.ParentId;
        while (!string.IsNullOrWhiteSpace(parentId) &&
               byId.TryGetValue(parentId, out var parent) &&
               visited.Add(parent.Id)) {
            depth++;
            parentId = parent.ParentId;
        }

        return depth;
    }
}
