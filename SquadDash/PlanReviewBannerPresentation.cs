namespace SquadDash;

internal sealed record PlanReviewBannerPresentation(
    string Title,
    string Summary,
    string ProgressText,
    int CheckedCount,
    int TotalCount,
    bool AllItemsChecked)
{
    internal static PlanReviewBannerPresentation Create(PlanReviewAttentionPresentation attention)
    {
        ArgumentNullException.ThrowIfNull(attention);

        var checklist = attention.Checklist ?? [];
        var total = checklist.Count;
        var checkedCount = checklist.Count(item => item.IsChecked);
        var allItemsChecked = total > 0 && checkedCount == total;
        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            attention.Task.DisplayStepLabel,
            attention.Task.Title ?? attention.Task.TaskId);
        var summary = string.IsNullOrWhiteSpace(attention.Summary)
            ? $"{stepLabel} is waiting for human review."
            : attention.Summary.Trim();
        var progressText = total switch
        {
            0 => "No checks listed.",
            1 => checkedCount == 1 ? "1 check confirmed." : "0 of 1 check confirmed.",
            _ when allItemsChecked => $"All {total} checks confirmed.",
            _ => $"{checkedCount} of {total} checks confirmed.",
        };

        return new PlanReviewBannerPresentation(
            string.IsNullOrWhiteSpace(attention.Title) ? "Review Required" : attention.Title.Trim(),
            summary,
            progressText,
            checkedCount,
            total,
            allItemsChecked);
    }
}
