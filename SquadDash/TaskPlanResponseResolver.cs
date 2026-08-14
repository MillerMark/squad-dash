namespace SquadDash;

/// <summary>Normalizes inline and file-backed task-plan responses into one parser input.</summary>
internal static class TaskPlanResponseResolver
{
    internal static bool TryResolve(
        string? rawResponse,
        string workspaceRoot,
        out string parserInput,
        out string? source)
    {
        return TryResolve(
            rawResponse,
            responseSegments: null,
            protocolJsonBlocks: null,
            workspaceRoot,
            out parserInput,
            out source,
            out _,
            out _);
    }

    /// <summary>
    /// Resolves a task-plan payload from every durable representation of a completed turn.
    /// The combined response buffer is normally authoritative, but streamed response segments
    /// and their extracted protocol blocks provide a lossless fallback when finalization and
    /// transcript persistence observe different response representations.
    /// </summary>
    internal static bool TryResolve(
        string? rawResponse,
        IEnumerable<string?>? responseSegments,
        IEnumerable<TranscriptProtocolJsonBlock>? protocolJsonBlocks,
        string workspaceRoot,
        out string parserInput,
        out string? source,
        out string? diagnostic,
        out bool payloadDetected)
    {
        parserInput = string.Empty;
        source = null;
        diagnostic = null;
        payloadDetected = false;

        string? firstMalformedInline = null;
        string? firstMalformedInlineSource = null;
        var candidates = BuildCandidates(rawResponse, responseSegments, protocolJsonBlocks);
        foreach (var candidate in candidates)
        {
            if (candidate.Text.Contains("TASKS_JSON:", StringComparison.Ordinal))
            {
                payloadDetected = true;
                if (TasksJsonParser.TryParse(candidate.Text, out var inlineGroup) && inlineGroup is not null)
                {
                    parserInput = candidate.Text;
                    source = candidate.Source;
                    return true;
                }

                firstMalformedInline ??= candidate.Text;
                firstMalformedInlineSource ??= candidate.Source;
            }

            if (!candidate.Text.Contains(AgentArtifactStore.DisplayArtifactMarker, StringComparison.Ordinal))
                continue;

            payloadDetected = true;
            if (!StructuredJsonBlockParser.TryExtractObject<AgentArtifactReference>(
                    candidate.Text,
                    AgentArtifactStore.DisplayArtifactMarker,
                    out var extraction) ||
                extraction is null)
            {
                diagnostic ??= "The plan artifact reference was malformed.";
                continue;
            }

            if (!AgentArtifactStore.TryMaterialize(
                    workspaceRoot,
                    extraction.Payload,
                    AgentArtifactStore.DefaultMaxInboxBytes,
                    archive: true,
                    out var artifact,
                    out var artifactError) ||
                artifact is null)
            {
                diagnostic ??= artifactError;
                continue;
            }

            var artifactCandidate = artifact.Content.Contains("TASKS_JSON:", StringComparison.Ordinal)
                ? artifact.Content
                : "TASKS_JSON:\n" + artifact.Content;
            if (!TasksJsonParser.TryParse(artifactCandidate, out var artifactGroup) || artifactGroup is null)
            {
                diagnostic ??= $"The plan artifact at {artifact.SourceRelativePath} did not contain a valid TASKS_JSON plan.";
                continue;
            }

            parserInput = artifactCandidate;
            source = artifact.SourceRelativePath;
            return true;
        }

        // Preserve the existing automatic-repair path for an inline payload that was detected
        // but failed parsing. The caller owns the richer parser diagnostic and repair prompt.
        if (firstMalformedInline is not null)
        {
            parserInput = firstMalformedInline;
            source = firstMalformedInlineSource;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<ResponseCandidate> BuildCandidates(
        string? rawResponse,
        IEnumerable<string?>? responseSegments,
        IEnumerable<TranscriptProtocolJsonBlock>? protocolJsonBlocks)
    {
        var candidates = new List<ResponseCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Add(rawResponse, "inline");

        var segments = responseSegments?
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment!)
            .ToArray() ?? [];
        if (segments.Length > 0)
        {
            Add(string.Join("\n", segments), "response-segments");
            foreach (var segment in segments)
                Add(segment, "response-segment");
        }

        if (protocolJsonBlocks is not null)
        {
            foreach (var block in protocolJsonBlocks.Where(block =>
                         string.Equals(block.Marker.TrimEnd(':'), "TASKS_JSON", StringComparison.Ordinal)))
            {
                Add(TranscriptTextUtilities.BuildProtocolJsonClipboardText(block.Marker, block.Json), "protocol-block");
            }
        }

        return candidates;

        void Add(string? text, string candidateSource)
        {
            if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
                return;
            candidates.Add(new ResponseCandidate(text, candidateSource));
        }
    }

    private sealed record ResponseCandidate(string Text, string Source);
}
