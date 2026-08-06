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
        parserInput = string.Empty;
        source = null;
        if (string.IsNullOrWhiteSpace(rawResponse)) return false;

        if (rawResponse.Contains("TASKS_JSON:", StringComparison.Ordinal))
        {
            parserInput = rawResponse;
            source = "inline";
            return true;
        }

        if (!StructuredJsonBlockParser.TryExtractObject<AgentArtifactReference>(
                rawResponse,
                AgentArtifactStore.DisplayArtifactMarker,
                out var extraction) ||
            extraction is null)
            return false;

        if (!AgentArtifactStore.TryMaterialize(
                workspaceRoot,
                extraction.Payload,
                AgentArtifactStore.DefaultMaxInboxBytes,
                archive: true,
                out var artifact,
                out _) ||
            artifact is null)
            return false;

        var candidate = artifact.Content.Contains("TASKS_JSON:", StringComparison.Ordinal)
            ? artifact.Content
            : "TASKS_JSON:\n" + artifact.Content;
        if (!TasksJsonParser.TryParse(candidate, out var group) || group is null)
            return false;

        parserInput = candidate;
        source = artifact.SourceRelativePath;
        return true;
    }
}
