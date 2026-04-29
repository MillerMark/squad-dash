namespace SquadDash;

internal sealed record LoopMdConfig(
    double IntervalMinutes,
    double TimeoutMinutes,
    string Description,
    string Instructions);
