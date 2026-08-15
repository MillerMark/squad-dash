namespace SquadDash;

/// <summary>Normalizes inline and file-backed task-plan responses into one parser input.</summary>
internal static class TaskPlanResponseResolver
{
    internal const string FileMarker = "TASKS_JSON_FILE:";

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

            if (candidate.Text.Contains(FileMarker, StringComparison.Ordinal))
            {
                payloadDetected = true;
                if (TryResolveArtifact(
                        candidate.Text,
                        FileMarker,
                        workspaceRoot,
                        requirePlanShape: false,
                        out parserInput,
                        out source,
                        out var fileDiagnostic))
                {
                    return true;
                }

                diagnostic ??= fileDiagnostic;
            }

            // Backward-compatible recovery for model responses that incorrectly used the
            // user-visible artifact protocol for a machine-consumed task plan. New prompts
            // explicitly require TASKS_JSON_FILE so this path can remain tolerant without
            // teaching the obsolete transport back to the model.
            if (!candidate.Text.Contains(AgentArtifactStore.DisplayArtifactMarker, StringComparison.Ordinal))
                continue;

            if (TryResolveArtifact(
                    candidate.Text,
                    AgentArtifactStore.DisplayArtifactMarker,
                    workspaceRoot,
                    requirePlanShape: true,
                    out parserInput,
                    out source,
                    out var displayDiagnostic))
            {
                payloadDetected = true;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(displayDiagnostic))
            {
                payloadDetected = true;
                diagnostic ??= displayDiagnostic;
            }
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

    internal static bool LooksLikeTaskPlanArtifactContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        StructuredJsonBlockExtraction<System.Text.Json.JsonElement>? extraction;
        if (content.Contains("TASKS_JSON:", StringComparison.Ordinal))
        {
            if (!StructuredJsonBlockParser.TryExtractObject(content, "TASKS_JSON:", out extraction))
                return false;
        }
        else if (!StructuredJsonBlockParser.TryExtractSingleObject(content, out extraction))
        {
            return false;
        }

        var root = extraction!.Payload;
        return root.ValueKind == System.Text.Json.JsonValueKind.Object &&
               root.TryGetProperty("groupId", out var groupId) &&
               groupId.ValueKind == System.Text.Json.JsonValueKind.String &&
               root.TryGetProperty("tasks", out var tasks) &&
               tasks.ValueKind == System.Text.Json.JsonValueKind.Array;
    }

    private static bool TryResolveArtifact(
        string candidateText,
        string marker,
        string workspaceRoot,
        bool requirePlanShape,
        out string parserInput,
        out string? source,
        out string diagnostic)
    {
        parserInput = string.Empty;
        source = null;
        diagnostic = string.Empty;

        if (!StructuredJsonBlockParser.TryExtractObject<AgentArtifactReference>(
                candidateText,
                marker,
                out var extraction) ||
            extraction is null)
        {
            diagnostic = "The plan artifact reference was malformed.";
            return false;
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
            diagnostic = artifactError;
            return false;
        }

        if (requirePlanShape && !LooksLikeTaskPlanArtifactContent(artifact.Content))
        {
            // This is an ordinary user-visible artifact, not a plan transport.
            diagnostic = string.Empty;
            return false;
        }

        var artifactCandidate = artifact.Content.Contains("TASKS_JSON:", StringComparison.Ordinal)
            ? artifact.Content
            : "TASKS_JSON:\n" + artifact.Content;
        parserInput = artifactCandidate;
        source = artifact.SourceRelativePath;
        return true;
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
