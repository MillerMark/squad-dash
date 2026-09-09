using System.Text.Json;
using System.Text.Json.Serialization;

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
        body.Contains("manual test kit", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Materializes host-owned checklist slots in an Inbox runbook. New messages use an explicit
/// {{approval-check:requirement-id}} marker. The proof-ID fallback keeps already-delivered kits
/// usable without guessing across plans or requirements.
/// </summary>
internal static class ManualTestKitChecklistComposer
{
    private const string TokenPrefix = "{{approval-check:";

    internal static string Compose(
        string body,
        IReadOnlyList<PlanHumanReviewChecklistItem> items)
    {
        if (string.IsNullOrEmpty(body) || items.Count == 0)
            return body;

        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        foreach (var item in items)
        {
            if (ContainsChecklistQuestion(lines, item.Text))
                continue;

            var token = TokenPrefix + item.RequirementId + "}}";
            var tokenIndex = lines.FindIndex(line =>
                string.Equals(line.Trim(), token, StringComparison.Ordinal));
            if (tokenIndex >= 0)
            {
                lines[tokenIndex] = $"- ☐ {item.Text}";
                continue;
            }

            InsertAfterExistingObservation(lines, item, items);
        }
        return string.Join("\n", lines);
    }

    private static bool ContainsChecklistQuestion(IReadOnlyList<string> lines, string question) =>
        lines.Any(line =>
            string.Equals(line.Trim(), $"- ☐ {question}", StringComparison.Ordinal) ||
            string.Equals(line.Trim(), $"- ☑ {question}", StringComparison.Ordinal) ||
            string.Equals(line.Trim(), $"☐ {question}", StringComparison.Ordinal) ||
            string.Equals(line.Trim(), $"☑ {question}", StringComparison.Ordinal));

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
