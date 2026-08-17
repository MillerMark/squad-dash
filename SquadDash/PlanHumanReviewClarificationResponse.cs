using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanHumanReviewClarificationAction
{
    internal const string Explain = "explain";
    internal const string NoChange = "no-change";
    internal const string Reword = "reword";
    internal const string Remove = "remove";
    internal const string Automate = "automate";
    internal const string Split = "split";
    internal const string Merge = "merge";
    internal const string NeedsHumanDecision = "needs-human-decision";

    internal static bool IsSupported(string value) => value is
        Explain or NoChange or Reword or Remove or Automate or Split or Merge or NeedsHumanDecision;

    internal static bool ChangesReviewContract(string value) => value is
        Reword or Remove or Automate or Split or Merge;
}

internal static class PlanHumanReviewClarificationRequestKind
{
    internal const string Clarify = "clarify";
    internal const string RefreshWording = "refresh-wording";

    internal static bool IsSupported(string value) => value is Clarify or RefreshWording;
}

internal sealed record PlanHumanReviewClarificationResponse(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("gateId")] string GateId,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("userFacingAnswer")] string UserFacingAnswer,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("replacementText")] string? ReplacementText = null,
    [property: JsonPropertyName("replacementItems")] IReadOnlyList<string>? ReplacementItems = null,
    [property: JsonPropertyName("targetItemIds")] IReadOnlyList<string>? TargetItemIds = null,
    [property: JsonPropertyName("preserveCheckedSelections")] bool PreserveCheckedSelections = true,
    [property: JsonPropertyName("evidence")] IReadOnlyList<string>? Evidence = null,
    [property: JsonPropertyName("existingValidationId")] string? ExistingValidationId = null,
    [property: JsonPropertyName("requestKind")] string RequestKind = PlanHumanReviewClarificationRequestKind.Clarify);

internal static class PlanHumanReviewClarificationResponseParser
{
    internal const string Marker = "PLAN_HUMAN_REVIEW_CLARIFICATION_JSON:";

    internal static bool TryParse(
        string? text,
        out PlanHumanReviewClarificationResponse? response,
        out string? error)
    {
        response = null;
        error = null;
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<PlanHumanReviewClarificationResponse>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        var payload = extraction.Payload;
        var action = payload.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        var requestKind = string.IsNullOrWhiteSpace(payload.RequestKind)
            ? PlanHumanReviewClarificationRequestKind.Clarify
            : payload.RequestKind.Trim().ToLowerInvariant();
        var replacementItems = Normalize(payload.ReplacementItems);
        var targetItemIds = Normalize(payload.TargetItemIds);
        var evidence = Normalize(payload.Evidence);
        if (string.IsNullOrWhiteSpace(payload.PlanId) ||
            string.IsNullOrWhiteSpace(payload.Revision) ||
            string.IsNullOrWhiteSpace(payload.GateId) ||
            string.IsNullOrWhiteSpace(payload.ItemId) ||
            !PlanHumanReviewClarificationRequestKind.IsSupported(requestKind) ||
            !PlanHumanReviewClarificationAction.IsSupported(action) ||
            string.IsNullOrWhiteSpace(payload.UserFacingAnswer) ||
            string.IsNullOrWhiteSpace(payload.Reason))
        {
            error = "The clarification response is missing its target, action, answer, or reason.";
            return false;
        }

        if (action == PlanHumanReviewClarificationAction.Reword &&
            string.IsNullOrWhiteSpace(payload.ReplacementText))
            error = "A reword response requires replacementText.";
        else if (action == PlanHumanReviewClarificationAction.Split && replacementItems.Count < 2)
            error = "A split response requires at least two replacementItems.";
        else if (action == PlanHumanReviewClarificationAction.Merge &&
                 (targetItemIds.Count < 2 || string.IsNullOrWhiteSpace(payload.ReplacementText)))
            error = "A merge response requires at least two targetItemIds and replacementText.";
        else if (action == PlanHumanReviewClarificationAction.Automate &&
                 string.IsNullOrWhiteSpace(payload.ExistingValidationId))
            error = "An automate response requires existingValidationId; new validations require a plan revision.";
        else if (requestKind == PlanHumanReviewClarificationRequestKind.RefreshWording &&
                 (action != PlanHumanReviewClarificationAction.Reword || payload.PreserveCheckedSelections))
            error = "A refresh-wording response requires action=reword and preserveCheckedSelections=false.";
        if (error is not null) return false;

        response = payload with
        {
            PlanId = payload.PlanId.Trim(),
            Revision = payload.Revision.Trim(),
            GateId = payload.GateId.Trim(),
            ItemId = payload.ItemId.Trim(),
            Action = action,
            UserFacingAnswer = payload.UserFacingAnswer.Trim(),
            Reason = payload.Reason.Trim(),
            ReplacementText = payload.ReplacementText?.Trim(),
            ReplacementItems = replacementItems,
            TargetItemIds = targetItemIds,
            Evidence = evidence,
            ExistingValidationId = payload.ExistingValidationId?.Trim(),
            RequestKind = requestKind,
        };
        return true;
    }

    internal static string BuildProtocolContext(
        Plan plan,
        PlanApprovalGate gate,
        PlanHumanReviewChecklistItem item,
        string stepLabel)
    {
        var example = JsonSerializer.Serialize(new
        {
            planId = plan.PlanId,
            revision = plan.Revision,
            gateId = gate.GateId,
            itemId = item.ItemId,
            action = PlanHumanReviewClarificationAction.Explain,
            requestKind = PlanHumanReviewClarificationRequestKind.Clarify,
            userFacingAnswer = "Explain how the user can observe this requirement, or why it should change.",
            reason = "Evidence-based rationale for the recommendation.",
            replacementText = (string?)null,
            replacementItems = Array.Empty<string>(),
            targetItemIds = new[] { item.ItemId },
            preserveCheckedSelections = true,
            evidence = Array.Empty<string>(),
            existingValidationId = (string?)null,
        });
        return $$"""
            ## Clarify one human-review requirement
            This is a non-mutating clarification turn. Do not edit files, execute plan work, approve the
            checkpoint, or continue the plan. Answer the user's question, then recommend exactly one action.

            Authoritative target:
            - planId: {{plan.PlanId}}
            - revision: {{plan.Revision}}
            - plan: {{plan.Title}}
            - gateId: {{gate.GateId}}
            - reviewed step: {{stepLabel}}
            - checklist itemId: {{item.ItemId}}
            - requirement: {{JsonSerializer.Serialize(item.Text)}}
            - candidate commit: {{item.CandidateCommit}}
            - currently checked: {{item.IsChecked.ToString().ToLowerInvariant()}}

            Supported actions:
            - explain: answer how to verify it; do not change the review contract.
            - no-change: explain why the existing requirement is appropriate.
            - reword: supply clearer human-observable replacementText.
            - remove: remove a redundant or non-observable human requirement.
            - automate: transfer responsibility to an existing compatible durable validation by supplying
              existingValidationId. Never invent a new validation here; use needs-human-decision when the plan
              would need a new validation node and revision.
            - split: supply two or more focused replacementItems.
            - merge: identify two or more targetItemIds and supply replacementText.
            - needs-human-decision: explain concrete choices when evidence cannot justify a recommendation.

            Set preserveCheckedSelections=false whenever changed wording invalidates a prior human observation.
            Material changes are recommendations only; SquadDash asks the user before applying them and records
            an audit disposition. End with exactly one payload matching this complete example (null and empty
            arrays are accepted where shown):
            {{Marker}}
            {{example}}
            """;
    }

    internal static string BuildRefreshWordingProtocolContext(
        Plan plan,
        PlanApprovalGate gate,
        PlanHumanReviewChecklistItem item,
        string stepLabel)
    {
        var example = JsonSerializer.Serialize(new
        {
            planId = plan.PlanId,
            revision = plan.Revision,
            gateId = gate.GateId,
            itemId = item.ItemId,
            action = PlanHumanReviewClarificationAction.Reword,
            requestKind = PlanHumanReviewClarificationRequestKind.RefreshWording,
            userFacingAnswer = "The requirement now uses the current UI terminology and remains human-observable.",
            reason = "The reviewed surface changed after the original requirement was written.",
            replacementText = "Does the current interface show the renamed command and its expected state?",
            replacementItems = Array.Empty<string>(),
            targetItemIds = new[] { item.ItemId },
            preserveCheckedSelections = false,
            evidence = Array.Empty<string>(),
            existingValidationId = (string?)null,
        });
        return $$"""
            ## Refresh one human-review requirement
            This is a read-only wording assessment. Inspect the current repository, relevant changes after the
            candidate commit, and the attached plan to determine whether names, labels, behavior, or observable
            outcomes have changed. Do not edit files, execute plan work, approve the checkpoint, or continue the
            plan.

            Authoritative target:
            - planId: {{plan.PlanId}}
            - revision: {{plan.Revision}}
            - plan: {{plan.Title}}
            - gateId: {{gate.GateId}}
            - reviewed step: {{stepLabel}}
            - checklist itemId: {{item.ItemId}}
            - current requirement: {{JsonSerializer.Serialize(item.Text)}}
            - candidate commit originally reviewed: {{item.CandidateCommit}}
            - currently checked: {{item.IsChecked.ToString().ToLowerInvariant()}}

            Return a self-contained, concise, human-observable replacement requirement using current terminology.
            Do not change its underlying intent, silently broaden its scope, or convert it into an implementation
            invariant. The response must use requestKind=refresh-wording, action=reword, and
            preserveCheckedSelections=false so the user must review the refreshed wording again.

            End with exactly one payload matching this complete example (null and empty arrays are accepted where
            shown):
            {{Marker}}
            {{example}}
            """;
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

}
