using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record ManualTestKitApprovalBindingPayload(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("planRevision")] string PlanRevision,
    [property: JsonPropertyName("gateIds")] IReadOnlyList<string> GateIds);

/// <summary>
/// Adds and reads the host-owned connection between an AI-authored manual-test-kit message and
/// the durable approval gate whose proof questions it explains. AI never supplies this attachment;
/// SquadDash derives it from the active plan task and its stable proof requirement IDs.
/// </summary>
internal static class ManualTestKitApprovalBinding
{
    internal const string AttachmentType = "manual-test-kit-approval-binding";
    private const string ApprovalCheckTokenPrefix = "{{approval-check:";
    private static readonly Regex ApprovalCheckTokenRegex = new(
        @"\{\{approval-check:([^}\r\n]+)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static bool IsBindingAttachment(InboxAttachment attachment) =>
        string.Equals(attachment.Type, AttachmentType, StringComparison.OrdinalIgnoreCase);

    internal static InboxMessage RemoveUntrustedBinding(InboxMessage message)
    {
        var attachments = message.Attachments.Where(attachment => !IsBindingAttachment(attachment)).ToArray();
        return attachments.Length == message.Attachments.Count
            ? message
            : message with { Attachments = attachments };
    }

    internal static ManualTestKitApprovalBindingPayload? Read(InboxMessage message)
    {
        var attachment = message.Attachments.FirstOrDefault(IsBindingAttachment);
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.Content))
            return null;
        try
        {
            var payload = JsonSerializer.Deserialize<ManualTestKitApprovalBindingPayload>(
                attachment.Content,
                JsonOptions);
            return payload is { PlanId.Length: > 0, GateIds.Count: > 0 } ? payload : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static InboxMessage BindForCompletedTask(InboxMessage message, Plan plan, string taskId)
    {
        var gateIds = plan.ApprovalGates
            .Where(gate => gate.AfterTaskIds.Contains(taskId, StringComparer.Ordinal))
            .Where(gate => HasEveryProofId(message.Body, gate))
            .Select(gate => gate.GateId)
            .ToArray();
        return gateIds.Length == 0 ? message : Bind(message, plan, gateIds);
    }

    internal static InboxMessage TryBindExistingMessage(
        InboxMessage message,
        IReadOnlyList<Plan> plans)
    {
        if (Read(message) is not null ||
            !ContainsManualTestKitIdentity(message.Subject, message.Body))
            return message;

        var candidates = plans
            .SelectMany(plan => plan.ApprovalGates
                .Where(gate => gate.Status == PlanGateStatus.AwaitingApproval)
                .Where(gate => HasEveryProofId(message.Body, gate))
                .Select(gate => (Plan: plan, Gate: gate)))
            .ToArray();
        if (candidates.Length == 0)
            return message;

        var candidatePlans = candidates
            .Select(candidate => candidate.Plan.PlanId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidatePlans.Length != 1)
            return message;

        var plan = candidates[0].Plan;
        return Bind(
            message,
            plan,
            candidates.Select(candidate => candidate.Gate.GateId).Distinct(StringComparer.Ordinal).ToArray());
    }

    internal static string? ValidateSourceBody(Plan plan, string taskId, string body)
    {
        var gateIds = plan.ApprovalGates
            .Where(gate => gate.AfterTaskIds.Contains(taskId, StringComparer.Ordinal))
            .Select(gate => gate.GateId)
            .ToArray();
        return ValidateSourceBodyForGates(plan, gateIds, body);
    }

    internal static string? ValidateSourceBodyForGates(
        Plan plan,
        IReadOnlyList<string> gateIds,
        string body)
    {
        var expectedIds = plan.ApprovalGates
            .Where(gate => gateIds.Contains(gate.GateId, StringComparer.Ordinal))
            .SelectMany(gate => gate.ProofRequirements ?? [])
            .Select(requirement => requirement.RequirementId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (expectedIds.Length == 0)
            return null;

        var actualIds = ApprovalCheckTokenRegex.Matches(body ?? string.Empty)
            .Select(match => match.Groups[1].Value.Trim())
            .ToArray();
        var errors = new List<string>();
        foreach (var expectedId in expectedIds.Where(expectedId =>
                     !actualIds.Contains(expectedId, StringComparer.Ordinal)))
            errors.Add($"missing `{{{{approval-check:{expectedId}}}}}`");
        foreach (var unknownId in actualIds.Where(actualId =>
                     !expectedIds.Contains(actualId, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal))
            errors.Add($"unknown approval marker `{unknownId}`");
        foreach (var duplicate in actualIds.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"approval marker `{duplicate.Key}` appears {duplicate.Count()} times");
        errors.AddRange(ApprovalOperatorGuidePolicy.Validate(body));

        return errors.Count == 0
            ? null
            : "The manual test kit cannot be bound to the approval checklist: " + string.Join("; ", errors) +
              ". Re-emit the corrected kit; preserve the completed implementation work.";
    }

    internal static bool LooksLikeSourceForPlan(InboxMessage message, Plan plan)
    {
        if (!ContainsManualTestKitIdentity(message.Subject, message.Body))
            return false;

        var proofIds = plan.ApprovalGates
            .SelectMany(gate => gate.ProofRequirements ?? [])
            .Select(requirement => requirement.RequirementId)
            .ToHashSet(StringComparer.Ordinal);
        return ApprovalCheckTokenRegex.Matches(message.Body ?? string.Empty)
            .Select(match => match.Groups[1].Value.Trim())
            .Any(proofIds.Contains);
    }

    private static InboxMessage Bind(InboxMessage message, Plan plan, IReadOnlyList<string> gateIds)
    {
        var attachments = message.Attachments
            .Where(attachment => !IsBindingAttachment(attachment))
            .Append(new InboxAttachment
            {
                Type = AttachmentType,
                Label = "Manual test approval binding",
                PlanGroupId = plan.PlanId,
                PlanRevision = plan.Revision,
                Content = JsonSerializer.Serialize(
                    new ManualTestKitApprovalBindingPayload(plan.PlanId, plan.Revision, gateIds),
                    JsonOptions),
            })
            .ToArray();
        return message with { Attachments = attachments };
    }

    private static bool HasEveryProofId(string body, PlanApprovalGate gate) =>
        gate.ProofRequirements is { Count: > 0 } requirements &&
        requirements.All(requirement => body.Contains(requirement.RequirementId, StringComparison.Ordinal));

    private static bool ContainsManualTestKitIdentity(string subject, string body) =>
        subject.Contains("manual test", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("manual test kit", StringComparison.OrdinalIgnoreCase) ||
        body.Contains(ApprovalCheckTokenPrefix, StringComparison.Ordinal);
}

/// <summary>
/// Materializes host-owned checklist slots in an Inbox approval procedure. New messages use an explicit
/// {{approval-check:requirement-id}} marker. The proof-ID fallback keeps already-delivered kits
/// usable without guessing across plans or requirements.
/// </summary>
internal static class ManualTestKitChecklistComposer
{
    private const string TokenPrefix = "{{approval-check:";
    internal const string SetupErrorMarker = "**Approval setup error:**";

    internal static string Compose(
        string body,
        IReadOnlyList<PlanHumanReviewChecklistItem> items)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        var usesExplicitSlots = lines.Any(line =>
            line.Trim().StartsWith(TokenPrefix, StringComparison.Ordinal));
        foreach (var item in items)
        {
            var checklistIndex = FindChecklistQuestionIndex(lines, item.Text);
            if (checklistIndex >= 0)
            {
                ReplaceSectionHeading(lines, checklistIndex, item);
                continue;
            }

            var token = TokenPrefix + item.RequirementId + "}}";
            var tokenIndex = lines.FindIndex(line =>
                string.Equals(line.Trim(), token, StringComparison.Ordinal));
            if (tokenIndex >= 0)
            {
                lines[tokenIndex] = $"- ☐ {item.Text}";
                ReplaceSectionHeading(lines, tokenIndex, item);
                continue;
            }

            // The proof-ID fallback is only for legacy kits that predate explicit slots. Once a
            // kit uses the slot protocol, a missing slot is a contract error—not permission to
            // guess a checkbox location from an incidental mention of the proof ID.
            if (!usesExplicitSlots)
            {
                InsertAfterExistingObservation(lines, item, items);
                checklistIndex = FindChecklistQuestionIndex(lines, item.Text);
                if (checklistIndex >= 0)
                    ReplaceSectionHeading(lines, checklistIndex, item);
            }
        }

        // Approval slots are host protocol, never operator-facing prose. An unmatched slot means
        // the validation contract is inconsistent, so replace it with an actionable error and
        // record the exact protocol failure in the trace instead of silently hiding it.
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
                !trimmed.EndsWith("}}", StringComparison.Ordinal))
                continue;

            var requirementId = trimmed[TokenPrefix.Length..^2];
            SquadDashTrace.Write(
                TraceCategory.Inbox,
                $"APPROVAL_SETUP_ERROR: unmatched manual-test-kit marker requirementId={requirementId}");
            lines[index] = BuildSetupError(
                $"The validation kit contains an approval check named `{requirementId}` that does not match the active checkpoint.");
        }

        foreach (var item in items.Where(item => FindChecklistQuestionIndex(lines, item.Text) < 0))
        {
            SquadDashTrace.Write(
                TraceCategory.Inbox,
                $"APPROVAL_SETUP_ERROR: missing manual-test-kit placement gateId={item.GateId} requirementId={item.RequirementId}");
            lines.Add(string.Empty);
            lines.Add(BuildSetupError(
                $"The required check “{item.Text}” could not be placed in these instructions."));
        }
        return string.Join("\n", lines);
    }

    private static string BuildSetupError(string detail) =>
        $"> ⚠ {SetupErrorMarker} {detail} Do not continue; ask SquadDash to repair the validation kit.";

    private static int FindChecklistQuestionIndex(IReadOnlyList<string> lines, string question)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (string.Equals(line, $"- ☐ {question}", StringComparison.Ordinal) ||
                string.Equals(line, $"- ☑ {question}", StringComparison.Ordinal) ||
                string.Equals(line, $"☐ {question}", StringComparison.Ordinal) ||
                string.Equals(line, $"☑ {question}", StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    private static void ReplaceSectionHeading(
        IList<string> lines,
        int checklistIndex,
        PlanHumanReviewChecklistItem item)
    {
        for (var index = checklistIndex - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                // A section may deliberately contain one shared workflow followed by several atomic
                // observations. Preserve its AI-authored common heading instead of allowing the last
                // checkbox processed to rename the entire section.
                var nextHeading = Enumerable.Range(index + 1, lines.Count - index - 1)
                    .FirstOrDefault(candidate => lines[candidate].Trim().StartsWith("## ", StringComparison.Ordinal));
                var sectionEnd = nextHeading == 0 ? lines.Count : nextHeading;
                var approvalSlotCount = Enumerable.Range(index + 1, sectionEnd - index - 1)
                    .Count(candidate => IsApprovalSlot(lines[candidate]));
                if (approvalSlotCount > 1)
                    return;

                lines[index] = $"## {PlanHumanReviewChecklist.ResolveDisplayTitle(item.Title, item.RequirementId)}";
                return;
            }

            // Do not cross into an earlier observation if malformed input omitted this section's
            // heading. The source remains readable and the checkbox-placement validator still
            // reports actual marker defects.
            if (line.StartsWith("- ☐ ", StringComparison.Ordinal) ||
                line.StartsWith("- ☑ ", StringComparison.Ordinal))
                return;
        }
    }

    private static bool IsApprovalSlot(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
               trimmed.StartsWith("- ☐ ", StringComparison.Ordinal) ||
               trimmed.StartsWith("- ☑ ", StringComparison.Ordinal) ||
               trimmed.StartsWith("☐ ", StringComparison.Ordinal) ||
               trimmed.StartsWith("☑ ", StringComparison.Ordinal);
    }

    private static void InsertAfterExistingObservation(
        List<string> lines,
        PlanHumanReviewChecklistItem item,
        IReadOnlyList<PlanHumanReviewChecklistItem> allItems)
    {
        var anchor = lines.FindIndex(line => line.Contains(item.RequirementId, StringComparison.Ordinal));
        if (anchor < 0)
            return;

        var otherIds = allItems
            .Where(candidate => !string.Equals(
                candidate.RequirementId,
                item.RequirementId,
                StringComparison.Ordinal))
            .Select(candidate => candidate.RequirementId)
            .ToArray();
        var end = lines.Count;
        for (var index = anchor + 1; index < lines.Count; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal) ||
                otherIds.Any(id => lines[index].Contains(id, StringComparison.Ordinal)))
            {
                end = index;
                break;
            }
        }

        var observationEnd = -1;
        for (var index = anchor + 1; index < end; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (trimmed.Contains("Fail:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("Observe:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("Pass:", StringComparison.OrdinalIgnoreCase))
                observationEnd = index;
        }
        var insertionIndex = observationEnd >= 0 ? observationEnd + 1 : anchor + 1;
        lines.InsertRange(insertionIndex, ["", $"- ☐ {item.Text}", ""]);
    }
}
