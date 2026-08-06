using System.Text.Json;

namespace SquadDash;

internal sealed record PlanRevisionPromptContext(string PlanId, string BaseRevision);

internal static class PlanRevisionPromptContextParser
{
    internal const string AttachmentType = "plan-revision";
    private const string PlanIdPrefix = "Plan ID: ";
    private const string BaseRevisionPrefix = "Base revision: ";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string BuildAttachment(Plan plan)
    {
        var group = PendingDecomposePlanAdapter.FromPlan(plan).Group with
        {
            Delivery = null,
            HostRevision = null,
        };
        var lockedIds = plan.Tasks
            .Where(task => task.Status != PlanTaskStatus.Pending ||
                           string.Equals(task.TaskId, plan.Progress.ExecutingTaskId, StringComparison.Ordinal))
            .Select(task => task.TaskId)
            .ToArray();
        var lockedText = lockedIds.Length == 0 ? "(none)" : string.Join(", ", lockedIds);
        var content =
            $"{PlanIdPrefix}{plan.PlanId}\n" +
            $"{BaseRevisionPrefix}{plan.Revision}\n" +
            $"Display revision: {Math.Max(1, plan.RevisionNumber)}\n" +
            $"Locked completed or active task IDs: {lockedText}\n\n" +
            "Revise this existing plan. Return the complete revised plan using the same groupId in a TASKS_JSON block. " +
            "Keep completed or active tasks unchanged; only pending, unstarted work may be revised.\n\n" +
            "TASKS_JSON:\n" + JsonSerializer.Serialize(group, JsonOptions);
        return AttachmentBlockFormatter.BuildTypedAttachmentBlock(AttachmentType, plan.Title, content);
    }

    internal static bool TryParse(string? prompt, out PlanRevisionPromptContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(prompt)) return false;

        foreach (var block in AttachmentBlockFormatter.ExtractAttachmentBlocks(prompt))
        {
            var (type, _) = AttachmentBlockFormatter.ExtractAttachmentMetadata(block);
            if (!string.Equals(type, AttachmentType, StringComparison.Ordinal)) continue;

            var content = AttachmentBlockFormatter.ExtractAttachmentContent(block);
            var planId = ReadValue(content, PlanIdPrefix);
            var baseRevision = ReadValue(content, BaseRevisionPrefix);
            if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(baseRevision))
                return false;

            context = new PlanRevisionPromptContext(planId, baseRevision);
            return true;
        }

        return false;
    }

    private static string? ReadValue(string content, string prefix) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();
}
