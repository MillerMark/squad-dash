namespace SquadDash;

internal enum ToolEventRoutingDecision
{
    Ignore,
    UseExistingEntry,
    CreateEntry
}

/// <summary>
/// Decides whether a streamed tool event can use a registered transcript row or
/// requires an active turn in order to create a new row.
/// </summary>
internal static class ToolEventRoutingPolicy
{
    internal static ToolEventRoutingDecision Resolve(
        bool hasToolCallId,
        bool hasExistingEntry,
        bool hasActiveTurn)
    {
        if (!hasToolCallId)
            return ToolEventRoutingDecision.Ignore;
        if (hasExistingEntry)
            return ToolEventRoutingDecision.UseExistingEntry;
        return hasActiveTurn
            ? ToolEventRoutingDecision.CreateEntry
            : ToolEventRoutingDecision.Ignore;
    }
}
