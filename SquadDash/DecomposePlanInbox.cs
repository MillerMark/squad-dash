using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>Builds and reads host-owned Inbox messages for pending decomposition plans.</summary>
internal static class DecomposePlanInbox
{
    internal const string AttachmentType = "decompose-plan";
    internal const string ActionRouteMode = "decompose";

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions DecisionOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static bool RequestsInboxDelivery(DecomposedTaskGroup group) =>
        string.Equals(group.Delivery, "inbox", StringComparison.OrdinalIgnoreCase);

    internal static string BuildMessageId(PendingDecomposePlan plan) =>
        $"decompose-plan-{plan.Group.GroupId}-{plan.Revision}";

    internal static bool ResponseAddressesPlan(PendingDecomposePlan plan, string? rawResponse)
    {
        if (DecomposeDecisionParser.TryParse(rawResponse, out var decision) && decision is not null &&
            string.Equals(decision.GroupId, plan.Group.GroupId, StringComparison.Ordinal) &&
            string.Equals(decision.Revision, plan.Revision, StringComparison.Ordinal))
            return true;
        return TasksJsonParser.TryParse(rawResponse ?? string.Empty, out var replacement) &&
               replacement is not null &&
               string.Equals(replacement.GroupId, plan.Group.GroupId, StringComparison.Ordinal);
    }

    internal static InboxMessage BuildMessage(
        PendingDecomposePlan plan,
        DateTimeOffset timestamp,
        bool explicitlyRequested)
    {
        var group = plan.Group;
        var reason = explicitlyRequested
            ? "This decomposition plan was sent to your Inbox as requested."
            : "This decomposition plan was staged in the transcript but was not acted on before the conversation moved on.";

        return new InboxMessage
        {
            Id = BuildMessageId(plan),
            Subject = $"Pending plan: {group.GroupTitle}",
            From = "SquadDash",
            Timestamp = timestamp,
            Read = false,
            Priority = "high",
            Body = $"{reason}\n\n" +
                   $"**{group.GroupTitle}**  \n" +
                   $"{group.Summary}\n\n" +
                   $"Proposed branch: `{group.Branch}`  \n" +
                   $"Tasks: {group.Tasks.Count}  \n" +
                   $"Plan revision: `{plan.Revision}`\n\n" +
                   "Open the attached dependency graph to inspect the plan, then choose an action above.",
            Attachments =
            [
                new InboxAttachment
                {
                    Type = AttachmentType,
                    Label = "View plan and dependencies",
                    PlanGroupId = group.GroupId,
                    PlanRevision = plan.Revision,
                    // The IDs point at the live pending plan. The snapshot keeps the attachment
                    // viewable for audit if the pending file is later accepted and removed.
                    Content = JsonSerializer.Serialize(plan, SnapshotOptions),
                }
            ],
            Actions =
            [
                BuildAction("Add to Backlog", plan, "add-to-backlog", null,
                    "Add all tasks and their dependencies to tasks.md."),
                BuildAction("Execute in New Branch", plan, "execute-new-branch", group.Branch,
                    $"Create {group.Branch} and start the dependency-aware loop."),
                BuildAction("Execute in Active Branch", plan, "execute-active-branch", null,
                    "Start the dependency-aware loop on the currently active branch."),
            ],
        };
    }

    internal static bool TryReadSnapshot(InboxAttachment attachment, out PendingDecomposePlan? plan)
    {
        plan = null;
        if (!string.Equals(attachment.Type, AttachmentType, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(attachment.Content))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<PendingDecomposePlan>(attachment.Content, SnapshotOptions);
            if (parsed is null ||
                !string.Equals(parsed.Group.GroupId, attachment.PlanGroupId, StringComparison.Ordinal) ||
                !string.Equals(parsed.Revision, attachment.PlanRevision, StringComparison.Ordinal))
                return false;
            plan = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.Inbox,
                $"Decompose plan attachment snapshot could not be parsed: {ex.Message}");
            return false;
        }
    }

    private static InboxAction BuildAction(
        string label,
        PendingDecomposePlan plan,
        string action,
        string? branch,
        string hint)
    {
        var decision = new DecomposeDecision(plan.Group.GroupId, plan.Revision, action, branch);
        return new InboxAction
        {
            Label = label,
            RouteMode = ActionRouteMode,
            Prompt = "DECOMPOSE_DECISION_JSON:\n" + JsonSerializer.Serialize(decision, DecisionOptions),
            Hint = hint,
        };
    }
}
