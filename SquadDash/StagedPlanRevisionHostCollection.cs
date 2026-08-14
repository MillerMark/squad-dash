namespace SquadDash;

internal sealed record StagedPlanRevisionHostCandidate(
    string ToolCallId,
    string AgentName,
    string RawResponse,
    StagedPlanRevisionProposalPayload Payload);

internal static class StagedPlanRevisionHostCollection
{
    internal const string Protocol = "staged_plan_revision";

    internal static bool TryCreateCandidate(
        SquadSdkEvent evt,
        out StagedPlanRevisionHostCandidate? candidate,
        out string? error)
    {
        candidate = null;
        error = null;
        if (!string.Equals(evt.Type, "subagent_completed", StringComparison.Ordinal) ||
            evt.HostProtocolVerified != true ||
            !string.Equals(evt.HostProtocol, Protocol, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(evt.ToolCallId) ||
            string.IsNullOrWhiteSpace(evt.AgentName) ||
            string.IsNullOrWhiteSpace(evt.HostProtocolGroupId) ||
            string.IsNullOrWhiteSpace(evt.HostProtocolBaseRevision) ||
            string.IsNullOrWhiteSpace(evt.LatestResponse))
        {
            error = "The verified staged-plan completion event was missing correlation data.";
            return false;
        }

        if (!StagedPlanRevisionProposalParser.TryParse(
                evt.LatestResponse, out var payload, out var parseError) || payload is null)
        {
            error = parseError ?? "The verified staged-plan completion did not contain a valid delta.";
            return false;
        }

        var expectedGroupId = evt.HostProtocolGroupId.Trim();
        var expectedBaseRevision = evt.HostProtocolBaseRevision.Trim();
        if (!string.Equals(payload.GroupId, expectedGroupId, StringComparison.Ordinal) ||
            !string.Equals(payload.BaseRevision, expectedBaseRevision, StringComparison.Ordinal))
        {
            error = $"The staged-plan delta identity did not match its verified request " +
                    $"({expectedGroupId} at {expectedBaseRevision}).";
            return false;
        }

        candidate = new StagedPlanRevisionHostCandidate(
            evt.ToolCallId.Trim(),
            evt.AgentName.Trim(),
            evt.LatestResponse,
            payload);
        return true;
    }
}

internal sealed class StagedPlanRevisionCollectionLedger
{
    private readonly HashSet<string> _toolCallIds = new(StringComparer.Ordinal);

    internal bool TryReserve(string toolCallId) =>
        !string.IsNullOrWhiteSpace(toolCallId) && _toolCallIds.Add(toolCallId.Trim());
}

internal sealed class StagedPlanRevisionTurnCollectionState
{
    private readonly HashSet<string> _addressedPlans = new(StringComparer.Ordinal);

    internal bool Handled { get; private set; }

    internal void MarkHandled(string groupId, string baseRevision)
    {
        Handled = true;
        if (!string.IsNullOrWhiteSpace(groupId) && !string.IsNullOrWhiteSpace(baseRevision))
            _addressedPlans.Add(BuildKey(groupId, baseRevision));
    }

    internal bool Addresses(PendingDecomposePlan plan) =>
        _addressedPlans.Contains(BuildKey(plan.Group.GroupId, plan.Revision));

    internal void Reset()
    {
        Handled = false;
        _addressedPlans.Clear();
    }

    private static string BuildKey(string groupId, string revision) =>
        groupId.Trim() + "\n" + revision.Trim();
}
