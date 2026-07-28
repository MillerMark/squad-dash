using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal static class PlanAgentAssignmentValidator
{
    internal static string? Validate(
        string taskId,
        string revision,
        IReadOnlyList<DecomposedAgentAssignment>? expected,
        IEnumerable<BackgroundAgentLaunchInfo> observed)
    {
        if (expected is not { Count: > 0 })
            return null;

        var verified = observed
            .Where(launch => launch.IsVerifiedRosterAssignment &&
                             string.Equals(launch.AssignedTaskId, taskId, StringComparison.Ordinal) &&
                             string.Equals(launch.AssignedPlanRevision, revision, StringComparison.Ordinal))
            .Select(launch => launch.AssignedAgentHandle)
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expected
            .Select(assignment => assignment.AgentHandle)
            .Where(handle => !verified.Contains(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return missing.Length == 0
            ? null
            : $"Task {taskId} did not launch its required verified roster assignment(s): {string.Join(", ", missing)}.";
    }

    internal static string? ValidateWrapUp(
        string taskId,
        IReadOnlyList<DecomposedAgentAssignment>? expected,
        IReadOnlyList<DecomposeAgentExecution>? reported)
    {
        if (expected is not { Count: > 0 })
            return null;
        if (reported is not { Count: > 0 })
            return $"Task {taskId} omitted its structured agentExecutions coordinator wrap-up.";

        var reportedHandles = reported
            .Where(item => string.Equals(
                item.RequestedAgent, item.ActualPrimaryAgent, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RequestedAgent)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expected
            .Select(item => item.AgentHandle)
            .Where(handle => !reportedHandles.Contains(handle))
            .ToArray();
        return missing.Length == 0
            ? null
            : $"Task {taskId} coordinator wrap-up did not confirm required primary assignment(s): {string.Join(", ", missing)}.";
    }
}
