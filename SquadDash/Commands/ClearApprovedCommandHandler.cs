namespace SquadDash.Commands;

internal sealed class ClearApprovedCommandHandler : IHostCommandHandler {
    private readonly Action _clearApproved;

    public ClearApprovedCommandHandler(Action clearApproved) => _clearApproved = clearApproved;

    public string CommandName => "clear_approved";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        _clearApproved();
        return new HostCommandResult(true);
    }
}
