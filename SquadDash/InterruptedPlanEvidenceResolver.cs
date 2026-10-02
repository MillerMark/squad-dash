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
                var segment = segments[segmentIndex];
                var blocks = segments[segmentIndex].ProtocolJsonBlocks;
                for (var blockIndex = 0; blockIndex < (blocks?.Count ?? 0); blockIndex++)
                {
                    var block = blocks![blockIndex];
                    if (!string.Equals(
                            block.Marker.TrimEnd(':'),
                            DecomposeStepResultParser.Marker.TrimEnd(':'),
                            StringComparison.Ordinal))
                        continue;
                    if (DecomposeStepResultParser.TryParse(
                            DecomposeStepResultParser.Marker + "\n" + block.Json,
                            out var result,
                            out _))
                        TryAdd(result);
                }

                // Older transcript capture did not recognize a protocol marker used as a
                // Markdown fence label, so ProtocolJsonBlocks is empty. Reparse the exact stored
                // response text through the same strict protocol parser during restart recovery.
                if (DecomposeStepResultParser.TryParse(segment.Text, out var recovered, out _))
                    TryAdd(recovered);
            }
        }

        return results;

        void TryAdd(DecomposeStepResult? result)
        {
            if (result is not null &&
                string.Equals(result.GroupId, planId, StringComparison.Ordinal) &&
                string.Equals(result.Revision, revision, StringComparison.Ordinal) &&
                string.Equals(result.TaskId, taskId, StringComparison.Ordinal) &&
                string.Equals(result.Status, "complete", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(result.Commit) &&
                seenCommits.Add(result.Commit))
                results.Add(result);
        }
    }
}
