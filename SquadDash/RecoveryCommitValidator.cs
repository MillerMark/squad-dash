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
    /// Returns <see langword="true"/> only when the commit changed at least one file and every
    /// changed file is outside the host-owned paths. Directory entries ending in a slash match
    /// all descendants. A mixed source/host-state commit must not be adopted as task work.
    /// </summary>
    internal static bool ContainsOnlyNonHostChanges(
        IEnumerable<string> changedPaths,
        IReadOnlyCollection<string> hostOwnedPaths)
    {
        var paths = changedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .ToArray();
        return paths.Length > 0 && paths.All(path =>
            !hostOwnedPaths.Any(hostPath => IsHostOwnedPath(path, hostPath)));
    }

    private static bool IsHostOwnedPath(string normalizedPath, string hostPath)
    {
        var normalizedHost = NormalizePath(hostPath);
        var isDirectory = normalizedHost.EndsWith("/", StringComparison.Ordinal);
        normalizedHost = normalizedHost.TrimEnd('/');
        return string.Equals(normalizedPath.TrimEnd('/'), normalizedHost, StringComparison.OrdinalIgnoreCase) ||
               (isDirectory && normalizedPath.StartsWith(normalizedHost + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path) => path.Trim().Replace('\\', '/');
}
