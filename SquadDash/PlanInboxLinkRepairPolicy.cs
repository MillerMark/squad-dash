namespace SquadDash;

internal sealed record PlanInboxLinkRepairDecision(
    string? ResultError,
    string? PendingRepairError);

internal static class PlanInboxLinkRepairPolicy
{
    internal static bool HasDeliveryIntent(string? rawResponse) =>
        !string.IsNullOrWhiteSpace(rawResponse) &&
        (rawResponse.Contains(AgentArtifactStore.InboxMessageFileMarker, StringComparison.Ordinal) ||
         rawResponse.Contains("INBOX_MESSAGE_JSON:", StringComparison.Ordinal));

    internal static bool ShouldStageDelivery(
        bool isExecutingPlan,
        bool envelopeAccepted,
        string? rawResponse) =>
        isExecutingPlan &&
        !envelopeAccepted &&
        HasDeliveryIntent(rawResponse);

    internal static ActiveLoopExecutionState ClearPendingDelivery(
        ActiveLoopExecutionState execution) =>
        execution with
        {
            PendingInboxLinkRepairError = null,
            PendingInboxDeliveryRawResponse = null,
        };

    internal static PlanInboxLinkRepairDecision Evaluate(
        string? pendingRepairError,
        bool hasInboxDelivery,
        string? currentValidationError)
    {
        if (pendingRepairError is null)
        {
            return currentValidationError is null
                ? new PlanInboxLinkRepairDecision(null, null)
                : new PlanInboxLinkRepairDecision(currentValidationError, currentValidationError);
        }

        if (!hasInboxDelivery)
        {
            return new PlanInboxLinkRepairDecision(
                "The Inbox-delivery repair response did not re-emit the required corrected " +
                "INBOX_MESSAGE_JSON or INBOX_MESSAGE_JSON_FILE delivery.",
                pendingRepairError);
        }

        return currentValidationError is null
            ? new PlanInboxLinkRepairDecision(null, null)
            : new PlanInboxLinkRepairDecision(currentValidationError, currentValidationError);
    }
}
