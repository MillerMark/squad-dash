namespace SquadDash.Commands;

internal sealed class StartLoopCommandHandler : IHostCommandHandler {
    private readonly Action _startLoop;

    public StartLoopCommandHandler(Action startLoop) => _startLoop = startLoop;

    public string CommandName => "start_loop";

    /// <summary>
    /// When set, called with the <c>groupId</c> parameter value whenever a
    /// <c>start_loop</c> command arrives with a <c>groupId</c> key.
    /// If not set, or when no <c>groupId</c> is present, <see cref="_startLoop"/> fires instead.
    /// </summary>
    public Action<string>? OnGroupIdExtracted { get; set; }

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        // Case-insensitive lookup for groupId.
        string? groupId = null;
        foreach (var kvp in parameters)
        {
            if (string.Equals(kvp.Key, "groupId", StringComparison.OrdinalIgnoreCase))
            {
                groupId = kvp.Value;
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(groupId) && OnGroupIdExtracted is not null)
            OnGroupIdExtracted(groupId);
        else
            _startLoop();

        return new HostCommandResult(true);
    }
}
