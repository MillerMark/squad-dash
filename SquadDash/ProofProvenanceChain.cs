using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>
/// Ordered provenance chain for a plan task across retry/replan attempts.
/// Each entry records the proof provenance from a prior execution attempt so that
/// recovery paths preserve attribution history.
/// </summary>
internal sealed record ProofProvenanceChain(
    [property: JsonPropertyName("entries")]
    IReadOnlyList<ProofProvenanceEntry> Entries)
{
    /// <summary>Creates an empty provenance chain.</summary>
    internal static ProofProvenanceChain Empty => new([]);

    /// <summary>
    /// Returns a new chain with the given entry appended.
    /// </summary>
    internal ProofProvenanceChain Append(ProofProvenanceEntry entry) =>
        new([.. Entries, entry]);

    /// <summary>
    /// Builds a human-readable provenance summary combining all entries.
    /// </summary>
    internal string BuildSummary()
    {
        if (Entries.Count == 0)
            return string.Empty;

        var parts = new List<string>(Entries.Count);
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            var label = $"Attempt {i + 1}: {e.SourceLabel}";
            if (e.CommitShortSha is not null)
                label += $" ({e.CommitShortSha})";
            if (!string.IsNullOrWhiteSpace(e.Summary))
                label += $" — {e.Summary}";
            parts.Add(label);
        }
        return string.Join("; ", parts);
    }
}

/// <summary>
/// Single provenance entry within a <see cref="ProofProvenanceChain"/>.
/// Captures the essential provenance data from one execution attempt.
/// </summary>
internal sealed record ProofProvenanceEntry(
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("sourceLabel")] string SourceLabel,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("commitShortSha")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CommitShortSha = null,
    [property: JsonPropertyName("commitFullSha")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CommitFullSha = null,
    [property: JsonPropertyName("summary")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Summary = null,
    [property: JsonPropertyName("recoveryKind")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RecoveryKind = null,
    [property: JsonPropertyName("recordedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RecordedAt = null);
