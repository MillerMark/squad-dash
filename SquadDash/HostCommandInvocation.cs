namespace SquadDash;

internal sealed record HostCommandInvocation(
    string Command,
    IReadOnlyDictionary<string, string>? Parameters = null);
