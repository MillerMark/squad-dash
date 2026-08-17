namespace SquadDash;

internal enum HumanReviewClarificationChangeIcon
{
    Edit,
    Remove,
    Check,
}

internal sealed record HumanReviewClarificationDialogPresentation(
    string CurrentRequirement,
    string ProposedChange,
    string Reason,
    string PrimaryActionLabel,
    string SecondaryActionLabel,
    HumanReviewClarificationChangeIcon ChangeIcon)
{
    internal static HumanReviewClarificationDialogPresentation Create(
        PlanHumanReviewClarificationResponse response,
        PlanHumanReviewChecklistItem item)
    {
        var (proposed, primaryLabel, icon) = response.Action switch
        {
            PlanHumanReviewClarificationAction.Reword => (
                response.ReplacementText!,
                "Apply Rewording",
                HumanReviewClarificationChangeIcon.Edit),
            PlanHumanReviewClarificationAction.Remove => (
                "Remove this requirement from human review.",
                "Remove Requirement",
                HumanReviewClarificationChangeIcon.Remove),
            PlanHumanReviewClarificationAction.Automate => (
                $"Replace this human check with automated validation `{response.ExistingValidationId}`.",
                "Use Automated Validation",
                HumanReviewClarificationChangeIcon.Check),
            PlanHumanReviewClarificationAction.Split => (
                string.Join("\n", response.ReplacementItems!.Select(value => "- " + value)),
                "Split Requirement",
                HumanReviewClarificationChangeIcon.Edit),
            PlanHumanReviewClarificationAction.Merge => (
                response.ReplacementText!,
                "Merge Requirements",
                HumanReviewClarificationChangeIcon.Edit),
            _ => throw new ArgumentOutOfRangeException(
                nameof(response), response.Action, "The clarification action does not change human review."),
        };

        return new HumanReviewClarificationDialogPresentation(
            item.Text,
            proposed,
            response.Reason,
            primaryLabel,
            "Keep Current Requirement",
            icon);
    }
}
