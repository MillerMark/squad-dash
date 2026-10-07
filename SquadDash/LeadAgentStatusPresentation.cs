namespace SquadDash;

internal static class LeadAgentStatusPresentation
{
    internal static void Update(
        AgentStatusCard card,
        string status,
        string bubble,
        string detail,
        Action<SpinnerActivityKind> pulseActivity)
    {
        card.StatusText = status;
        card.BubbleText = bubble;
        card.DetailText = detail;

        // Status refreshes are not activity: only explicit active states emit pulses.
        SpinnerActivityKind? kind = status switch
        {
            "Thinking" or "Recovering" => SpinnerActivityKind.Thinking,
            "Streaming" => SpinnerActivityKind.Writing,
            "Tooling" => SpinnerActivityKind.Reading,
            _ => null,
        };
        if (kind is { } activityKind)
            pulseActivity(activityKind);
    }
}
