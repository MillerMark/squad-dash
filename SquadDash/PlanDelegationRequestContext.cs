using System.Text.Json;

namespace SquadDash;

internal static class PlanDelegationRequestContext
{
    internal static string Build(
        string planId,
        string revision,
        string workItemId,
        string phase,
        string? attemptId,
        string branch)
    {
        var request = new Dictionary<string, string>
        {
            ["planId"] = planId,
            ["revision"] = revision,
            ["workItemId"] = workItemId,
            ["phase"] = phase,
            ["branch"] = branch,
        };
        if (!string.IsNullOrWhiteSpace(attemptId))
            request["attemptId"] = attemptId;
        var payload = JsonSerializer.Serialize(request);
        return "Set `plan_execution_request` on the `delegate_roster_agent` call to this exact host-scoped object:\n" +
               payload + "\n" +
               "This metadata authorizes only this plan phase; do not omit, alter, or reuse it for another launch.";
    }
}
