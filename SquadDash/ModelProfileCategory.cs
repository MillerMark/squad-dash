namespace SquadDash;

/// <summary>
/// Well-known agent categories that can each be assigned a distinct <see cref="ModelProfile"/>.
/// </summary>
internal static class ModelProfileCategory {
    internal const string Coordinator = "coordinator";
    internal const string SpawnedNamedAgents = "spawned-named-agents";
    internal const string TemporaryAgents = "temporary-agents";
    internal const string RAI = "rai";
    internal const string Scribe = "scribe";
    internal const string Ralph = "ralph";
    internal const string FactChecker = "fact-checker";
}
