namespace SquadDash;

internal sealed class HostCommandExecutor {
    private readonly Dictionary<string, IHostCommandHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    internal void Register(IHostCommandHandler handler) =>
        _handlers[handler.CommandName] = handler;

    internal IReadOnlyList<(HostCommandInvocation Invocation, HostCommandDescriptor Descriptor, HostCommandResult Result)>
        Execute(
            IReadOnlyList<HostCommandInvocation> invocations,
            HostCommandRegistry registry,
            string? workspaceFolder) {
        var commands = registry.GetCommands(workspaceFolder);
        var descriptorMap = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var results = new List<(HostCommandInvocation, HostCommandDescriptor, HostCommandResult)>();

        foreach (var invocation in invocations) {
            descriptorMap.TryGetValue(invocation.Command, out var descriptor);
            _handlers.TryGetValue(invocation.Command, out var handler);

            if (descriptor is null && handler is null) {
                SquadDashTrace.Write(TraceCategory.Performance,
                    $"HostCommandExecutor: unknown command '{invocation.Command}' — skipping");
                continue;
            }

            if (descriptor is not null) {
                var validation = registry.Validate(invocation, descriptor);
                if (!validation.IsValid) {
                    results.Add((invocation, descriptor,
                        new HostCommandResult(false, ErrorMessage: validation.ErrorMessage)));
                    continue;
                }
            }

            descriptor ??= new HostCommandDescriptor(
                invocation.Command, string.Empty,
                Array.Empty<HostCommandParameterDescriptor>(),
                HostCommandResultBehavior.Silent);

            if (handler is null) {
                results.Add((invocation, descriptor,
                    new HostCommandResult(false, ErrorMessage: $"No handler registered for command '{invocation.Command}'")));
                continue;
            }

            HostCommandResult result;
            try {
                result = handler.Execute(invocation.Parameters ?? new Dictionary<string, string>());
            }
            catch (Exception ex) {
                result = new HostCommandResult(false, ErrorMessage: ex.Message);
            }

            results.Add((invocation, descriptor, result));
        }

        return results;
    }

    internal IReadOnlyList<(HostCommandInvocation, HostCommandDescriptor, HostCommandResult)>?
        TryParseAndExecute(
            string rawResponse,
            HostCommandRegistry registry,
            string? workspaceFolder,
            out string bodyWithoutCommandBlock) {
        bodyWithoutCommandBlock = rawResponse;

        if (!HostCommandParser.TryExtract(rawResponse, out var body, out var commands))
            return null;

        bodyWithoutCommandBlock = body;
        return Execute(commands, registry, workspaceFolder);
    }
}
