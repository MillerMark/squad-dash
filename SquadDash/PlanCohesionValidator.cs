using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Validates plan cohesion: each task must describe an observable outcome and name how
/// its output reaches a production consumer. Artifact-only wording is rejected unless
/// integration responsibility is present.
/// </summary>
internal static class PlanCohesionValidator
{
    /// <summary>
    /// Observable-outcome signal phrases that indicate the description names a verifiable result.
    /// </summary>
    private static readonly string[] OutcomeSignals =
    [
        "observable outcome",
        "end-to-end proof",
        "user can",
        "users can",
        "the build succeeds",
        "the build passes",
        "the test suite passes",
        "tests pass",
        "test passes",
        "visible in",
        "appears in",
        "displays",
        "renders",
        "produces",
        "returns",
        "emits",
        "launches",
        "opens",
        "navigates",
    ];

    /// <summary>
    /// Consumer signal phrases that indicate the description names a production call site.
    /// </summary>
    private static readonly string[] ConsumerSignals =
    [
        "calls",
        "invokes",
        "consumes",
        "subscribes",
        "routes through",
        "routed through",
        "wired",
        "integrated into",
        "linked from",
        "referenced by",
        "registered in",
        "injected into",
        "dispatches to",
        "delegates to",
        "reaches",
        "production consumer",
        "call site",
    ];

    /// <summary>
    /// Artifact-only phrases that are insufficient without integration responsibility.
    /// </summary>
    private static readonly Regex ArtifactOnlyPattern = new(
        @"^(add|create|write|introduce|implement|define|extract|move|refactor|set up|scaffold)\s+(a\s+|the\s+)?" +
        @"(helper|utility|class|method|function|module|service|interface|abstraction|component|" +
        @"tests?|unit tests?|test file|test class|documentation|docs|readme|config|configuration)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool HasObservableOutcome(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;
        var lower = description.ToLowerInvariant();
        return OutcomeSignals.Any(signal => lower.Contains(signal, StringComparison.Ordinal));
    }

    internal static bool HasProductionConsumer(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;
        var lower = description.ToLowerInvariant();
        return ConsumerSignals.Any(signal => lower.Contains(signal, StringComparison.Ordinal));
    }

    internal static bool IsArtifactOnly(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;
        // Check each sentence — if the entire description is artifact-only with no integration signal
        var sentences = description.Split(['.', ';'], StringSplitOptions.RemoveEmptyEntries);
        var hasArtifactSentence = sentences.Any(s => ArtifactOnlyPattern.IsMatch(s.Trim()));
        return hasArtifactSentence && !HasObservableOutcome(description) && !HasProductionConsumer(description);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the final task looks like a tailored end-to-end proof
    /// rather than a generic documentation or test reminder.
    /// </summary>
    internal static bool HasTailoredFinalProof(DecomposedSubTask finalTask)
    {
        var desc = finalTask.Description ?? string.Empty;
        var title = finalTask.Title ?? string.Empty;
        var combined = (title + " " + desc).ToLowerInvariant();

        // Reject generic final steps
        string[] genericPatterns =
        [
            "update documentation",
            "clean up",
            "run the test suite",
            "finalize",
            "wrap up",
        ];
        if (genericPatterns.Any(p => combined.StartsWith(p, StringComparison.Ordinal) ||
                                     combined.Contains(p, StringComparison.Ordinal)) &&
            !HasObservableOutcome(desc))
            return false;

        return HasObservableOutcome(desc) || combined.Contains("end-to-end proof");
    }

    /// <summary>
    /// Validates the full plan for cohesion. Returns a list of validation issues.
    /// An empty list means the plan is cohesion-compliant.
    /// </summary>
    internal static IReadOnlyList<string> Validate(DecomposedTaskGroup group)
    {
        var issues = new List<string>();
        if (group.Tasks is not { Count: > 0 })
            return issues;

        foreach (var task in group.Tasks)
        {
            var desc = task.Description ?? string.Empty;

            if (!HasObservableOutcome(desc) && !HasProductionConsumer(desc))
            {
                // Only warn on genuinely artifact-only descriptions
                if (IsArtifactOnly(desc))
                {
                    issues.Add(
                        $"Task '{task.Id}' uses artifact-only wording without naming an observable outcome or production consumer.");
                }
            }
        }

        // Validate final task is a tailored proof
        var finalTask = group.Tasks[^1];
        if (!HasTailoredFinalProof(finalTask))
        {
            issues.Add(
                $"Final task '{finalTask.Id}' does not describe a tailored end-to-end proof with an observable outcome.");
        }

        return issues;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the plan passes cohesion validation.
    /// Logs any issues to trace.
    /// </summary>
    internal static bool IsValid(DecomposedTaskGroup group)
    {
        var issues = Validate(group);
        foreach (var issue in issues)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanCohesionValidator: {issue}");
        }
        return issues.Count == 0;
    }
}
