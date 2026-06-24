using System.Collections.Generic;
using System.Text;

namespace SquadDash;

internal static class SquadTeamRootRepairWorkflow {
    public const string CleanQuickReply = "Clean up Squad pollution";
    public const string IgnoreQuickReply = "Ignore";

    public static string BuildNoticeEntry(SquadTeamRootAssessment assessment) {
        var sb = new StringBuilder();
        sb.AppendLine("[info] SquadDash detected root-level Squad pollution — Squad CLI was writing files to the workspace root instead of `.squad/`.");

        if (assessment.ConfigNeedsRepair) {
            sb.AppendLine();
            sb.AppendLine("The `teamRoot` setting in `.squad/config.json` has been corrected automatically. Future Squad CLI runs will write to `.squad/` instead.");
        }

        if (assessment.HasPollution) {
            sb.AppendLine();
            sb.AppendLine("The following Squad-generated items are duplicated at the workspace root and can be safely removed:");
            foreach (var item in assessment.PollutionItems)
                sb.AppendLine($"- `{item}`");
        }

        sb.AppendLine();
        sb.Append($"[{CleanQuickReply}] [{IgnoreQuickReply}]");
        return sb.ToString();
    }

    public static string BuildConfigOnlyFixedMessage() =>
        "[info] Fixed `teamRoot` in `.squad/config.json` — Squad CLI will now write files to `.squad/` instead of the workspace root.";

    public static string BuildCleanupDoneMessage(SquadTeamRootCleanupResult result) {
        if (!result.AnySuccess && !result.AnyFailure)
            return "[info] No Squad root-level pollution found to clean up.";

        var sb = new StringBuilder();
        if (result.AnySuccess) {
            sb.Append($"[info] Cleaned {result.CleanedItems.Count} root-level Squad item(s): ");
            sb.AppendJoin(", ", result.CleanedItems);
            sb.Append('.');
        }

        if (result.AnyFailure) {
            if (result.AnySuccess)
                sb.AppendLine();
            sb.Append("Could not remove: ");
            sb.AppendJoin(", ", result.FailedItems);
            sb.Append('.');
        }

        return sb.ToString();
    }

    public static string BuildIgnoredMessage() =>
        "[info] Ignoring root-level Squad pollution for now.";
}
