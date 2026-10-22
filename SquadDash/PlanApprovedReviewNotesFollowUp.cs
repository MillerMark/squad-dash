using System;
using System.Linq;

namespace SquadDash;

internal static class PlanApprovedReviewNotesFollowUp
{
    internal const string SourceTag = "plan-approved-review-notes";

    internal static string BuildCompletionKey(Plan plan) =>
        $"{plan.PlanId}\u001f{plan.Revision}";

    internal static int CountNotedItems(Plan completedPlan) =>
        ResolveNotedItems(completedPlan).Length;

    internal static string BuildQueuedStatus(int noteCount, string? waitReason = null)
    {
        var countLabel = noteCount == 1 ? "1 approved note" : $"{noteCount} approved notes";
        var status = $"Post-completion review queued · Checking {countLabel} for follow-up tasks…";
        return string.IsNullOrWhiteSpace(waitReason)
            ? status
            : status + " " + waitReason.Trim();
    }

    internal static string? BuildPrompt(Plan completedPlan)
    {
        if (completedPlan.LifecycleStatus != PlanLifecycleStatus.Completed)
            return null;

        var notedItems = ResolveNotedItems(completedPlan);
        if (notedItems.Length == 0)
            return null;

        var notes = string.Join("\n", notedItems.Select(item =>
            "- Question: " + item.Text + "\n" +
            "  Answer: Yes\n" +
            "  User note: " + PlanHumanReviewItemResponse.NormalizeNote(item.Note)));

        return
            $"The plan \"{completedPlan.Title}\" ({completedPlan.PlanId}) is complete. " +
            "The user approved these human-review items but left notes. Review them now and decide whether any immediate follow-up is warranted. " +
            "If no action is needed, say so briefly.\n\n" +
            "Approved review items with user notes:\n" +
            notes;
    }

    private static PlanHumanReviewChecklistItem[] ResolveNotedItems(Plan completedPlan) =>
        PlanHumanReviewChecklist.Build(completedPlan, completedPlan.ApprovalGates)
            .Where(item => item.IsChecked && !string.IsNullOrWhiteSpace(item.Note))
            .ToArray();
}
