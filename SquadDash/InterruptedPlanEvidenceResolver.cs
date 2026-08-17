namespace SquadDash;

/// <summary>
/// Recovers a provider result that SquadDash already captured durably before a later
/// host-side validation failure. This is evidence discovery only; Git ancestry and
/// atomic-commit validation remain host responsibilities.
/// </summary>
internal static class InterruptedPlanEvidenceResolver
{
    internal static DecomposeStepResult? FindLatestTaskResult(
        IReadOnlyList<TranscriptTurnRecord> turns,
        string planId,
        string revision,
        string taskId)
    {
        for (var turnIndex = turns.Count - 1; turnIndex >= 0; turnIndex--)
        {
            var segments = turns[turnIndex].GetResponseSegments();
            for (var segmentIndex = segments.Count - 1; segmentIndex >= 0; segmentIndex--)
            {
                var blocks = segments[segmentIndex].ProtocolJsonBlocks;
                if (blocks is null) continue;
                for (var blockIndex = blocks.Count - 1; blockIndex >= 0; blockIndex--)
                {
                    var block = blocks[blockIndex];
                    if (!string.Equals(
                            block.Marker.TrimEnd(':'),
                            DecomposeStepResultParser.Marker.TrimEnd(':'),
                            StringComparison.Ordinal))
                        continue;
                    if (!DecomposeStepResultParser.TryParse(
                            DecomposeStepResultParser.Marker + "\n" + block.Json,
                            out var result,
                            out _) ||
                        result is null)
                        continue;
                    if (string.Equals(result.GroupId, planId, StringComparison.Ordinal) &&
                        string.Equals(result.Revision, revision, StringComparison.Ordinal) &&
                        string.Equals(result.TaskId, taskId, StringComparison.Ordinal) &&
                        string.Equals(result.Status, "complete", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(result.Commit))
                        return result;
                }
            }
        }

        return null;
    }
}
