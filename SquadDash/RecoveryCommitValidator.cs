using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed record RecoveryCommitRangeEntry(string Commit, string Subject);

/// <summary>
/// Pure-logic helpers for validating orphan-commit recovery actions.
/// No WPF or I/O dependencies — all methods are statically testable.
/// </summary>
internal static class RecoveryCommitValidator
{
    /// <summary>
    /// Parses oldest-to-newest output produced by
    /// <c>git log --reverse --format=%H%x09%s baseline..HEAD</c>.
    /// Malformed rows are rejected so the UI never offers an unidentified commit.
    /// </summary>
    internal static IReadOnlyList<RecoveryCommitRangeEntry> ParseCommitRange(string logOutput)
    {
        var entries = new List<RecoveryCommitRangeEntry>();
        foreach (var line in logOutput.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0 || separator == line.Length - 1)
                throw new InvalidOperationException("Git returned a malformed commit-range row.");
            var commit = line[..separator].Trim();
            if (commit.Length < 7 || !commit.All(Uri.IsHexDigit))
                throw new InvalidOperationException($"Git returned an invalid commit ID '{commit}'.");
            entries.Add(new RecoveryCommitRangeEntry(commit, line[(separator + 1)..].Trim()));
        }
        return entries;
    }

    /// <summary>
    /// Finds the newest recorded completed-task commit in a newest-to-oldest HEAD history.
    /// Stored abbreviated SHAs are matched only when they identify exactly one history entry.
    /// </summary>
    internal static string? FindNewestRecordedCommit(
        IEnumerable<string> newestFirstHistory,
        IEnumerable<string?> recordedCommits)
    {
        var history = newestFirstHistory
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        var recorded = recordedCommits
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        foreach (var historyCommit in history)
        {
            if (recorded.Any(candidate =>
                    historyCommit.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                return historyCommit;
        }
        return null;
    }

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
