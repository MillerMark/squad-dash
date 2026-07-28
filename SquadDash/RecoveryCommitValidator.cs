using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helpers for validating orphan-commit recovery actions.
/// No WPF or I/O dependencies — all methods are statically testable.
/// </summary>
internal static class RecoveryCommitValidator
{
    /// <summary>
    /// Parses <c>git log --oneline {baseline}..HEAD</c> output into candidate commits.
    /// Returns the single candidate SHA if exactly one commit is found,
    /// <see langword="null"/> if zero commits (nothing to adopt or revert),
    /// or throws <see cref="InvalidOperationException"/> if multiple commits are present (ambiguous).
    /// </summary>
    internal static string? ExtractSingleCandidateCommit(string logOutput)
    {
        var lines = logOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0) return null;
        if (lines.Count > 1)
            throw new InvalidOperationException(
                $"Ambiguous: {lines.Count} commits found between baseline and HEAD. " +
                "Adopt or revert one commit at a time.");

        return lines[0].Split(' ', 2)[0];
    }

    /// <summary>
    /// Given a list of all tasks and their statuses, returns any task IDs that
    /// depend on <paramref name="targetTaskId"/> AND are already marked Complete or Partial.
    /// These tasks would be at risk if the target task's commit were reverted.
    /// </summary>
    internal static IReadOnlyList<string> FindDownstreamCompletedDependents(
        IReadOnlyList<PlanTask> tasks,
        string targetTaskId)
    {
        return tasks
            .Where(t =>
                t.DependsOn.Contains(targetTaskId, StringComparer.Ordinal) &&
                t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial)
            .Select(t => t.TaskId)
            .ToList();
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="changedPaths"/> contains at least one
    /// file that is NOT present in <paramref name="hostOwnedPaths"/>.
    /// A commit that only touched host-owned files (tasks.md, plan JSON) should not be adopted
    /// as task work without scrutiny.
    /// </summary>
    internal static bool HasNonHostChanges(
        IEnumerable<string> changedPaths,
        IReadOnlyCollection<string> hostOwnedPaths)
    {
        return changedPaths.Any(path =>
            !hostOwnedPaths.Any(h =>
                string.Equals(
                    path.Replace('\\', '/'),
                    h.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase)));
    }
}
