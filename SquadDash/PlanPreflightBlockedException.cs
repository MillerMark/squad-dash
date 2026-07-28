using System;
using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Thrown when plan execution is blocked because the working tree contains genuine uncommitted
/// changes that would prevent a branch switch. Carries structured context so callers can show
/// a targeted recovery UI instead of a raw error panel.
/// </summary>
internal sealed class PlanPreflightBlockedException : Exception
{
    /// <summary>Human-readable name of the blocking condition, e.g. "Uncommitted changes".</summary>
    public string Condition { get; }

    /// <summary>Repo-relative paths of the files that are dirty.</summary>
    public IReadOnlyList<string> ChangedPaths { get; }

    /// <summary>The branch that was the switch target when the block was detected.</summary>
    public string? TargetBranch { get; }

    public PlanPreflightBlockedException(
        string condition,
        IReadOnlyList<string> changedPaths,
        string? targetBranch)
        : base(BuildMessage(condition, changedPaths, targetBranch))
    {
        Condition    = condition;
        ChangedPaths = changedPaths;
        TargetBranch = targetBranch;
    }

    private static string BuildMessage(
        string condition,
        IReadOnlyList<string> changedPaths,
        string? targetBranch)
    {
        var branch = string.IsNullOrWhiteSpace(targetBranch) ? string.Empty : $" (branch '{targetBranch}')";
        return $"{condition}{branch}: {changedPaths.Count} file(s) must be committed or stashed first.";
    }
}
