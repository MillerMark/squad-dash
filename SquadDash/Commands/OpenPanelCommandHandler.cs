namespace SquadDash.Commands;

internal sealed class OpenPanelCommandHandler : IHostCommandHandler {
    private readonly Action<string> _openPanel;

    public OpenPanelCommandHandler(Action<string> openPanel) => _openPanel = openPanel;

    public string CommandName => "open_panel";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        if (!parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new HostCommandResult(false, ErrorMessage: "Missing required parameter: name");
        _openPanel(name.Trim());
        return new HostCommandResult(true);
    }
}
