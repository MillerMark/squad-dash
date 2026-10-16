using System;
using System.Linq;

namespace SquadDash;

internal static class PlanApprovedReviewNotesFollowUp
{
    internal const string SourceTag = "plan-approved-review-notes";

    internal static string BuildCompletionKey(Plan plan) =>
        $"{plan.PlanId}\u001f{plan.Revision}";

    internal static string? BuildPrompt(Plan completedPlan)
    {
        if (completedPlan.LifecycleStatus != PlanLifecycleStatus.Completed)
            return null;

        var notedItems = PlanHumanReviewChecklist.Build(completedPlan, completedPlan.ApprovalGates)
            .Where(item => item.IsChecked && !string.IsNullOrWhiteSpace(item.Note))
            .ToArray();
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
}
