using System.IO;

namespace SquadDash;

internal sealed record PlanViewerTitleClipboardContent(string Name, string? FilePath)
{
    internal static PlanViewerTitleClipboardContent Create(
        Plan? durablePlan, string groupTitle, string? squadFolderPath) =>
        new(
            durablePlan?.DisplayName ?? groupTitle,
            durablePlan is not null &&
            !string.IsNullOrWhiteSpace(durablePlan.PlanId) &&
            !string.IsNullOrWhiteSpace(squadFolderPath)
                ? Path.GetFullPath(Path.Combine(squadFolderPath, "plans", durablePlan.PlanId + ".json"))
                : null);
}
