namespace SquadDash.Commands;

internal sealed class StartLoopCommandHandler : IHostCommandHandler {
    private readonly Action _startLoop;

    public StartLoopCommandHandler(Action startLoop) => _startLoop = startLoop;

    public string CommandName => "start_loop";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        _startLoop();
        return new HostCommandResult(true);
    }
}
