namespace SquadDash.Commands;

internal sealed class InjectTextCommandHandler : IHostCommandHandler {
    private readonly Action<string> _injectText;

    public InjectTextCommandHandler(Action<string> injectText) => _injectText = injectText;

    public string CommandName => "inject_text";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        if (!parameters.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
            return new HostCommandResult(false, ErrorMessage: "Missing required parameter: text");
        _injectText(text.Trim());
        return new HostCommandResult(true, Output: text.Trim());
    }
}
