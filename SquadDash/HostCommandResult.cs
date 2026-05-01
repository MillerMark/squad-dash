namespace SquadDash;

internal sealed record HostCommandResult(
    bool Success,
    string? Output = null,
    string? ErrorMessage = null) {
    internal bool HasOutput => !string.IsNullOrWhiteSpace(Output);
}
