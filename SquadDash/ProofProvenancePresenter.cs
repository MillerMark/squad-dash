namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Classifies the source of proof evidence on a completed plan task or validation node.
/// </summary>
internal enum EvidenceSourceKind
{
    /// <summary>Evidence was assessed by AI (automated analysis without live observation).</summary>
    AiAssessed,
    /// <summary>Evidence was recorded by the host process (commit tracking, state transitions).</summary>
    HostRecorded,
    /// <summary>Evidence from automated tooling (test suites, CI output).</summary>
    Automated,
    /// <summary>Evidence from live UI observation (screenshot, trace of running UI).</summary>
    LiveUi,
    /// <summary>Evidence from observing application restart behavior.</summary>
    Restart,
    /// <summary>Evidence from direct human observation and attestation.</summary>
    HumanObservation,
}

/// <summary>
/// Structured provenance display content for a single proof item. Designed to be consumed
/// by tooltip, accessibility, and approval review surfaces without WPF dependencies.
/// </summary>
internal sealed record ProofProvenanceContent(
    /// <summary>Human-readable label for the evidence source (e.g., "AI-assessed validation").</summary>
    string SourceLabel,
    /// <summary>Classified evidence source kind.</summary>
    EvidenceSourceKind SourceKind,
    /// <summary>Short SHA for display (7 chars) or null if no commit is associated.</summary>
    string? CommitShortSha,
    /// <summary>Full commit SHA for link targets, or null.</summary>
    string? CommitFullSha,
    /// <summary>Declared proof requirements — assertions the plan required.</summary>
    IReadOnlyList<string> DeclaredRequirements,
    /// <summary>Returned evidence summaries from the executing agent.</summary>
    IReadOnlyList<string> ReturnedSummaries,
    /// <summary>Artifact references (traces, screenshots, logs) if any.</summary>
    IReadOnlyList<string> Artifacts,
    /// <summary>
    /// Combined accessible description suitable for AutomationProperties.Name.
    /// </summary>
    string AccessibleDescription);

/// <summary>
/// Pure, testable presentation model that builds structured provenance display content
/// from <see cref="PlanTask"/> or <see cref="PlanValidationNode"/> evidence data.
/// Never implies that declared assertions are host observations — keeps "declared requirement"
/// and "observed evidence" clearly distinct.
/// </summary>
internal static class ProofProvenancePresenter
{
    // ── Evidence source classification ────────────────────────────────────────

    /// <summary>
    /// Classifies a proof type string into an <see cref="EvidenceSourceKind"/>.
    /// </summary>
    internal static EvidenceSourceKind ClassifyProofType(string? proofType) => proofType switch
    {
        "ai-assessed" => EvidenceSourceKind.AiAssessed,
        "host-recorded" => EvidenceSourceKind.HostRecorded,
        "automated-test" => EvidenceSourceKind.Automated,
        "live-ui-observation" => EvidenceSourceKind.LiveUi,
        "restart-observation" => EvidenceSourceKind.Restart,
        "human-observation" => EvidenceSourceKind.HumanObservation,
        _ => EvidenceSourceKind.AiAssessed,
    };

    /// <summary>
    /// Returns a human-readable label for the given evidence source kind.
    /// </summary>
    internal static string FormatSourceLabel(EvidenceSourceKind kind) => kind switch
    {
        EvidenceSourceKind.AiAssessed => "AI-assessed validation",
        EvidenceSourceKind.HostRecorded => "Host-recorded commit",
        EvidenceSourceKind.Automated => "Automated test evidence",
        EvidenceSourceKind.LiveUi => "Live UI observation",
        EvidenceSourceKind.Restart => "Restart observation",
        EvidenceSourceKind.HumanObservation => "Human observation",
        _ => "Unknown evidence source",
    };

    // ── Commit formatting ─────────────────────────────────────────────────────

    /// <summary>
    /// Formats a full commit SHA into a short 7-character display string.
    /// Returns null for null or whitespace input.
    /// </summary>
    internal static string? FormatShortSha(string? fullSha) =>
        string.IsNullOrWhiteSpace(fullSha) ? null : fullSha.Length >= 7 ? fullSha[..7] : fullSha;

    // ── PlanTask provenance ───────────────────────────────────────────────────

    /// <summary>
    /// Builds provenance content for a completed <see cref="PlanTask"/> with proof evidence.
    /// Returns null when the task has no proof requirements or evidence.
    /// </summary>
    internal static ProofProvenanceContent? BuildForTask(PlanTask? task)
    {
        if (task is null)
            return null;
        if (task.ProofRequirements is not { Count: > 0 } requirements)
            return null;

        var evidence = task.ProofEvidence ?? [];
        var primaryType = evidence.Count > 0
            ? evidence[0].ProofType
            : requirements[0].ProofType;
        var sourceKind = ClassifyProofType(primaryType);

        var declaredRequirements = requirements
            .Select(r => r.Description)
            .ToList();

        var summaries = evidence
            .Where(e => !string.IsNullOrWhiteSpace(e.Summary))
            .Select(e => e.Summary)
            .ToList();

        var artifacts = evidence
            .Where(e => e.Artifacts is { Count: > 0 })
            .SelectMany(e => e.Artifacts!)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();

        var shortSha = FormatShortSha(task.Commit);
        var sourceLabel = FormatSourceLabel(sourceKind);

        var accessible = BuildAccessibleDescription(
            sourceLabel, shortSha, declaredRequirements, summaries);

        return new ProofProvenanceContent(
            SourceLabel: sourceLabel,
            SourceKind: sourceKind,
            CommitShortSha: shortSha,
            CommitFullSha: string.IsNullOrWhiteSpace(task.Commit) ? null : task.Commit,
            DeclaredRequirements: declaredRequirements,
            ReturnedSummaries: summaries,
            Artifacts: artifacts,
            AccessibleDescription: accessible);
    }

    // ── PlanValidationNode provenance ─────────────────────────────────────────

    /// <summary>
    /// Builds provenance content for a completed <see cref="PlanValidationNode"/>.
    /// Returns null when the validation has no assertions or no validated commit.
    /// </summary>
    internal static ProofProvenanceContent? BuildForValidation(PlanValidationNode? validation)
    {
        if (validation is null)
            return null;
        if (validation.Assertions is not { Count: > 0 } assertions)
            return null;

        var sourceKind = EvidenceSourceKind.AiAssessed;
        if (validation.Commands is { Count: > 0 })
            sourceKind = EvidenceSourceKind.Automated;

        var shortSha = FormatShortSha(validation.ValidatedCommit);
        var sourceLabel = FormatSourceLabel(sourceKind);

        var summaries = new List<string>();
        if (!string.IsNullOrWhiteSpace(validation.Summary))
            summaries.Add(validation.Summary);

        var evidenceItems = validation.Evidence ?? [];
        foreach (var item in evidenceItems)
        {
            if (!string.IsNullOrWhiteSpace(item))
                summaries.Add(item);
        }

        var declaredRequirements = assertions.ToList();

        var accessible = BuildAccessibleDescription(
            sourceLabel, shortSha, declaredRequirements, summaries);

        return new ProofProvenanceContent(
            SourceLabel: sourceLabel,
            SourceKind: sourceKind,
            CommitShortSha: shortSha,
            CommitFullSha: string.IsNullOrWhiteSpace(validation.ValidatedCommit) ? null : validation.ValidatedCommit,
            DeclaredRequirements: declaredRequirements,
            ReturnedSummaries: summaries,
            Artifacts: [],
            AccessibleDescription: accessible);
    }

    // ── Accessible description ────────────────────────────────────────────────

    private static string BuildAccessibleDescription(
        string sourceLabel,
        string? shortSha,
        IReadOnlyList<string> declaredRequirements,
        IReadOnlyList<string> summaries)
    {
        var parts = new List<string> { $"Evidence source: {sourceLabel}" };
        if (shortSha is not null)
            parts.Add($"Validated commit: {shortSha}");
        if (declaredRequirements.Count > 0)
            parts.Add($"Declared requirements: {string.Join("; ", declaredRequirements)}");
        if (summaries.Count > 0)
            parts.Add($"Returned evidence: {string.Join("; ", summaries)}");
        return string.Join(". ", parts);
    }
}
