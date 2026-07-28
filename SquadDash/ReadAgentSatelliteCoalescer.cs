using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Pure-logic helper that decides whether a new read_agent poll call should reuse
/// an existing open ToolTranscriptEntry (satellite coalescing) rather than creating
/// a new transcript row per poll.
/// </summary>
internal static class ReadAgentSatelliteCoalescer
{
    /// <summary>
    /// Walks <paramref name="toolEntries"/> in reverse to find the most recent open
    /// read_agent entry whose ArgsJson contains the given <paramref name="agentId"/>.
    /// Returns null if no matching entry exists or if the most recent match is already
    /// completed (terminal condition — a new row should start).
    /// </summary>
    /// <remarks>
    /// NOTE: ToolTranscriptEntry contains WPF UI controls (Expander, TextBlock, etc.)
    /// and therefore cannot be instantiated in pure unit tests. The FindActiveEntry
    /// method is tested manually / via integration. Only TryExtractAgentId is covered
    /// by AgentPollCoalescingTests because it has no WPF dependency.
    /// </remarks>
    internal static ToolTranscriptEntry? FindActiveEntry(
        IReadOnlyList<ToolTranscriptEntry> toolEntries,
        string agentId)
    {
        for (var i = toolEntries.Count - 1; i >= 0; i--)
        {
            var candidate = toolEntries[i];
            if (!string.Equals(candidate.Descriptor.ToolName, "read_agent", StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateAgentId = TryExtractAgentId(candidate.ArgsJson);
            if (!string.Equals(candidateAgentId, agentId, StringComparison.Ordinal))
                continue;

            // Found a read_agent entry for this agent_id.
            // If it's already completed, don't reuse it — let a new row start.
            if (candidate.IsCompleted)
                return null;

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Extracts the <c>agent_id</c> string value from a JSON args string.
    /// Returns null if the JSON is null/empty, malformed, or does not contain the field.
    /// </summary>
    internal static string? TryExtractAgentId(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            return root.TryGetProperty("agent_id", out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
