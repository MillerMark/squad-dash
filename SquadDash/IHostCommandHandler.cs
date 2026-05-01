namespace SquadDash;

internal interface IHostCommandHandler {
    string CommandName { get; }
    HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters);
}
