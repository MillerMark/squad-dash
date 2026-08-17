namespace SquadDash;

/// <summary>
/// Recovers a provider result that SquadDash already captured durably before a later
/// host-side validation failure. This is evidence discovery only; Git ancestry and
/// atomic-commit validation remain host responsibilities.
/// </summary>
internal static class InterruptedPlanEvidenceResolver
{
    internal static IReadOnlyList<DecomposeStepResult> FindTaskResults(
        IReadOnlyList<TranscriptTurnRecord> turns,
        string planId,
        string revision,
        string taskId)
    {
        var results = new List<DecomposeStepResult>();
        var seenCommits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The first complete result for an execution attempt is the task handoff. Later matching
        // blocks are host-requested envelope repairs and must not silently replace its commit with
        // whatever happened to be HEAD when the repair ran.
        for (var turnIndex = 0; turnIndex < turns.Count; turnIndex++)
        {
            var segments = turns[turnIndex].GetResponseSegments();
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                var blocks = segments[segmentIndex].ProtocolJsonBlocks;
                if (blocks is null) continue;
                for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
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
                        !string.IsNullOrWhiteSpace(result.Commit) &&
                        seenCommits.Add(result.Commit))
                        results.Add(result);
                }
            }
        }

        return results;
    }
}
