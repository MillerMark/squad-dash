namespace SquadDash.Commands;

internal sealed class StopLoopCommandHandler : IHostCommandHandler {
    private readonly Action _stopLoop;

    public StopLoopCommandHandler(Action stopLoop) => _stopLoop = stopLoop;

    public string CommandName => "stop_loop";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        _stopLoop();
        return new HostCommandResult(true);
    }
}
