namespace SquadDash;

internal sealed record HostCommandParameterDescriptor(
    string Name,
    string Type,      // "string", "int", "bool"
    bool Required,
    string? Description = null);

public enum HostCommandResultBehavior {
    Silent,
    InjectResultAsContext,
    NotifyUser
}

internal sealed record HostCommandDescriptor(
    string Name,
    string Description,
    IReadOnlyList<HostCommandParameterDescriptor> Parameters,
    HostCommandResultBehavior ResultBehavior,
    bool RequiresConfirmation = false);
