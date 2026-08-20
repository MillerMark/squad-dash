using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Adds a narrow coordinator instruction when an ordinary user prompt explicitly asks to
/// continue a durable plan that it identifies by ID, title, or attached plan content.
/// This class never performs the continuation; it only requires the coordinator to return
/// the host-owned recovery protocol so the normal validated transition remains authoritative.
/// </summary>
internal static partial class PlanContinuationPromptContext
{
    internal static string Build(string? userPrompt, IReadOnlyList<Plan> plans)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || !ContinuationVerb().IsMatch(userPrompt))
            return string.Empty;

        var matches = plans
            .Where(IsContinuationCandidate)
            .Where(plan =>
                userPrompt.Contains(plan.PlanId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(plan.DisplayName) &&
                 userPrompt.Contains(plan.DisplayName, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(plan => plan.PlanId, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
            return string.Empty;

        if (matches.Length > 1)
        {
            return "## Host-owned plan continuation required\n\n" +
                   "The user explicitly asked to continue a durable plan, but this prompt identifies more than one " +
                   "nonterminal plan. Ask the user which plan to continue. Do not switch branches, merge, run Git, " +
                   "or delegate a plan task until SquadDash has accepted one plan recovery response.";
        }

        var plan = matches[0];
        var payload = JsonSerializer.Serialize(new
        {
            groupId = plan.PlanId,
            revision = plan.Revision,
            action = "continue-working",
        });
        var builder = new StringBuilder();
        builder.AppendLine("## Host-owned plan continuation required");
        builder.AppendLine();
        builder.AppendLine($"The user's prompt explicitly asks to continue **{plan.DisplayName}** (`{plan.PlanId}`).");
        builder.AppendLine($"Current durable state: `{plan.LifecycleStatus}`. Plan branch: `{plan.Branch}`.");
        builder.AppendLine();
        builder.AppendLine("Do not independently switch or merge branches, run plan implementation commands, or delegate a plan task.");
        builder.AppendLine("Respond with the exact recovery payload below. SquadDash will validate it, perform branch/worktree preflight, " +
                           "obtain any required branch choice, activate durable execution, and only then authorize task delegation.");
        builder.AppendLine();
        builder.AppendLine(DecomposeRecoveryDecisionParser.Marker);
        builder.AppendLine(payload);
        return builder.ToString().TrimEnd();
    }

    private static bool IsContinuationCandidate(Plan plan) =>
        plan.LifecycleStatus is PlanLifecycleStatus.Approved or
            PlanLifecycleStatus.Interrupted or
            PlanLifecycleStatus.Blocked or
            PlanLifecycleStatus.WaitingForCleanWorktree;

    [GeneratedRegex(@"\b(resume|continue|retry|restart)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContinuationVerb();
}
