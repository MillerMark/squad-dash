namespace SquadDash;

/// <summary>
/// Keeps action-specific human-review turns on one response contract. Injecting the
/// generic review-activity protocol beside one of these contracts gives the provider
/// two valid but incompatible answers and can turn a no-op into apparent success.
/// </summary>
internal static class HumanReviewResponseContractPolicy
{
    internal const string ClarificationDraftSourceTag = "plan-human-review-clarification-draft";
    internal const string WordingDraftSourceTag = "plan-human-review-wording-draft";
    internal const string RepairSourceTag = "plan-human-review-clarification-repair";

    internal static bool RequiresClarificationProtocol(string? sourceTag) =>
        sourceTag is ClarificationDraftSourceTag or WordingDraftSourceTag or RepairSourceTag;
}
