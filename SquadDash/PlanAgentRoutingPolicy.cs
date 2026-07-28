namespace SquadDash;

internal static class PlanAgentRoutingPolicy
{
    internal const string PlanExecutionOnly = "plan-execution-only";
    internal const string Always = "always";
    internal const string Off = "off";

    internal static string Normalize(string? value) => value switch
    {
        Always => Always,
        Off => Off,
        _ => PlanExecutionOnly
    };
}
