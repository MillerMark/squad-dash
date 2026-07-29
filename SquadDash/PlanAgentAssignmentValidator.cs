using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash;

internal static class PlanAgentAssignmentValidator
{
    internal static string? ValidateGeneric(
        string taskId,
        string revision,
        PlanExecutionAttemptState? attempt,
        string? reportedAttemptId,
        IReadOnlyList<DecomposeAgentExecution>? reported)
    {
        if (attempt is null || !attempt.AllowsGenericPrimary)
            return $"Task {taskId} explicitly authorizes generic routing but has no host-owned execution attempt.";
        if (!string.Equals(attempt.TaskId, taskId, StringComparison.Ordinal) ||
            !string.Equals(attempt.Revision, revision, StringComparison.Ordinal) ||
            !string.Equals(attempt.AttemptId, reportedAttemptId, StringComparison.Ordinal))
            return $"Task {taskId} reported stale or incorrect generic execution-attempt evidence.";
        if (!string.Equals(attempt.Status, "active", StringComparison.Ordinal))
            return $"Task {taskId} execution attempt {attempt.AttemptId} is not active.";
        if (string.IsNullOrWhiteSpace(attempt.GenericPrimaryToolCallId))
            return $"Task {taskId} did not launch its one host-observed generic primary worker.";
        if (attempt.GenericCompletedAt is null || attempt.GenericSucceeded != true)
            return $"Task {taskId}'s generic primary worker did not complete successfully in the current attempt.";
        if (attempt.UnexpectedPrimaryToolCallIds is { Count: > 0 })
            return $"Task {taskId} launched more than one generic primary worker.";
        if (attempt.GenericChildToolCallIds is { Count: > 0 })
            return $"Task {taskId}'s generic primary worker launched prohibited child workers.";
        if (reported is { Count: > 0 })
            return $"Task {taskId} reported roster agent executions for an explicitly generic task.";
        return null;
    }

    internal static string? Validate(
        string taskId,
        string revision,
        IReadOnlyList<DecomposedAgentAssignment>? expected,
        PlanExecutionAttemptState? attempt)
    {
        if (expected is not { Count: > 0 })
            return null;
        if (attempt is null)
            return $"Task {taskId} has verified assignments but no host-owned execution attempt.";
        if (!string.Equals(attempt.TaskId, taskId, StringComparison.Ordinal) ||
            !string.Equals(attempt.Revision, revision, StringComparison.Ordinal))
            return $"Task {taskId} has stale assignment evidence from another task or revision.";
        if (!string.Equals(attempt.Status, "active", StringComparison.Ordinal))
            return $"Task {taskId} execution attempt {attempt.AttemptId} is not active.";
        if (attempt.UnexpectedPrimaryToolCallIds is { Count: > 0 })
            return $"Task {taskId} launched undeclared coordinator-owned primary worker(s): " +
                   string.Join(", ", attempt.UnexpectedPrimaryToolCallIds) + ".";

        foreach (var assignment in expected)
        {
            var evidence = attempt.Assignments.FirstOrDefault(item =>
                string.Equals(item.AgentHandle, assignment.AgentHandle, StringComparison.OrdinalIgnoreCase));
            if (evidence is null || string.IsNullOrWhiteSpace(evidence.PrimaryToolCallId))
                return $"Task {taskId} did not launch required verified roster assignment '{assignment.AgentHandle}' in the current attempt.";
            if (evidence.CompletedAt is null || evidence.Succeeded != true)
                return $"Assignment '{assignment.AgentHandle}' did not complete successfully in the current attempt.";

            var missingContext = evidence.RequiredContextPaths
                .Where(required => !(evidence.ObservedContextPaths ?? [])
                    .Any(observed => PlanExecutionAttemptState.PathsEqual(required, observed)))
                .Select(Path.GetFileName)
                .ToArray();
            if (missingContext.Length > 0)
                return $"Assignment '{assignment.AgentHandle}' did not produce host-observed reads for: {string.Join(", ", missingContext)}.";

            if (!assignment.AllowGenericChildren && evidence.ChildToolCallIds is { Count: > 0 })
                return $"Assignment '{assignment.AgentHandle}' launched generic children even though the plan forbids them.";
        }

        return null;
    }

    internal static string? ValidateWrapUp(
        string taskId,
        IReadOnlyList<DecomposedAgentAssignment>? expected,
        PlanExecutionAttemptState? attempt,
        string? reportedAttemptId,
        IReadOnlyList<DecomposeAgentExecution>? reported)
    {
        if (expected is not { Count: > 0 })
            return null;
        if (attempt is null || !string.Equals(attempt.AttemptId, reportedAttemptId, StringComparison.Ordinal))
            return $"Task {taskId} omitted or reported the wrong host executionAttemptId.";
        if (reported is not { Count: > 0 })
            return $"Task {taskId} omitted its structured agentExecutions coordinator wrap-up.";

        if (reported.Count != expected.Count)
            return $"Task {taskId} coordinator wrap-up contained undeclared primary assignments.";

        foreach (var expectedAssignment in expected)
        {
            var evidence = attempt.Assignments.FirstOrDefault(item =>
                string.Equals(item.AgentHandle, expectedAssignment.AgentHandle, StringComparison.OrdinalIgnoreCase));
            if (evidence is null)
                return $"Task {taskId} has no host-owned evidence for required primary assignment '{expectedAssignment.AgentHandle}'.";

            var matchingReports = reported.Where(item =>
                string.Equals(item.RequestedAgent, expectedAssignment.AgentHandle, StringComparison.OrdinalIgnoreCase)).ToArray();
            var report = matchingReports.Length == 1 ? matchingReports[0] : null;
            if (report is null ||
                !string.Equals(report.ActualPrimaryAgent, expectedAssignment.AgentHandle, StringComparison.OrdinalIgnoreCase))
                return $"Task {taskId} coordinator wrap-up did not identify required primary assignment '{expectedAssignment.AgentHandle}'.";

            // Tool-call IDs and child lineage are host-internal evidence. Older result payloads may
            // still contain those fields, but model-reported values are intentionally non-authoritative.
            // Validate() has already checked the host-observed launch, lifecycle, context reads, and
            // child policy for this exact attempt.
        }

        return null;
    }
}
