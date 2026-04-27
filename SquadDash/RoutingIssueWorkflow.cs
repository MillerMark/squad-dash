using System;
using System.Text;
using System.Collections.Generic;

namespace SquadDash;

internal static class RoutingIssueWorkflow {
    public const string RepairQuickReply = "Repair Routing.md";
    public const string IgnoreQuickReply = "Ignore for now";

    public static string BuildSystemEntry(SquadRoutingDocumentAssessment assessment) {
        var builder = new StringBuilder();
        builder.AppendLine("[info] Squad checked `.squad/routing.md` and found a routing issue.");
        builder.AppendLine();

        foreach (var bullet in BuildIssueBullets(assessment))
            builder.AppendLine("- " + bullet);

        builder.AppendLine("- Squad may fall back to weaker default routing until this file is repaired.");
        builder.AppendLine();
        builder.Append('[').Append(RepairQuickReply).Append("] [").Append(IgnoreQuickReply).Append(']');
        return builder.ToString();
    }

    public static string BuildRepairInstruction() {
        return """
            Repair `.squad/routing.md` for this repo.

            Do not delegate this task. The Coordinator should handle it directly.

            Use only these files as authority:
            - `.squad/team.md`
            - the current `.squad/routing.md` if it exists
            - each agent `charter.md` under `.squad/agents/`

            Goals:
            - create or repair a valid `.squad/routing.md`
            - route work to the real named team members from `team.md`
            - preserve any useful human-authored notes or exceptions if possible
            - do not invent members, roles, specialties, or routing rules unsupported by those files

            When finished:
            - write the repaired `.squad/routing.md`
            - briefly summarize what you changed
            - mention if any existing notes could not be preserved
            """;
    }

    public static string BuildRepairQueuedMessage(string? backupPath) {
        return string.IsNullOrWhiteSpace(backupPath)
            ? "[info] Asking Squad to repair `.squad/routing.md` from `team.md` and the agent charters."
            : $"[info] Asking Squad to repair `.squad/routing.md` from `team.md` and the agent charters. A backup of the previous file was saved to `{backupPath}`.";
    }

    public static string BuildIgnoredMessage() {
        return "[info] Ignoring the current routing.md issue for now. SquadDash will ask again if `routing.md`, `team.md`, or an agent charter changes.";
    }

    public static string BuildRepairBlockedMessage() {
        return "[info] Squad can't repair `.squad/routing.md` yet because this workspace still has setup issues. Resolve the current install/runtime blockers first, then try the repair again.";
    }

    private static IReadOnlyList<string> BuildIssueBullets(SquadRoutingDocumentAssessment assessment) {
        return assessment.Status switch {
            SquadRoutingDocumentStatus.Missing => [
                "`.squad/routing.md` is missing."
            ],
            SquadRoutingDocumentStatus.UnfilledSeed => [
                "`.squad/routing.md` still contains placeholder template content."
            ],
            SquadRoutingDocumentStatus.InvalidCustom => [
                "`.squad/routing.md` does not contain a parseable routing table."
            ],
            _ when !string.IsNullOrWhiteSpace(assessment.DiagnosticMessage) => [
                assessment.DiagnosticMessage!
            ],
            _ => [
                "`.squad/routing.md` needs attention."
            ]
        };
    }
}
