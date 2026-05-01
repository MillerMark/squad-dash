using System.IO;
using System.Text;
using System.Text.Json;

namespace SquadDash;

internal sealed class HostCommandRegistry {
    private static readonly IReadOnlyList<HostCommandDescriptor> BuiltInCommands = [
        new HostCommandDescriptor(
            Name:               "start_loop",
            Description:        "Starts the SquadDash native loop",
            Parameters:         Array.Empty<HostCommandParameterDescriptor>(),
            ResultBehavior:     HostCommandResultBehavior.Silent),
        new HostCommandDescriptor(
            Name:               "stop_loop",
            Description:        "Stops the SquadDash native loop after the current iteration",
            Parameters:         Array.Empty<HostCommandParameterDescriptor>(),
            ResultBehavior:     HostCommandResultBehavior.Silent),
        new HostCommandDescriptor(
            Name:               "get_queue_status",
            Description:        "Returns the current prompt queue items as JSON",
            Parameters:         Array.Empty<HostCommandParameterDescriptor>(),
            ResultBehavior:     HostCommandResultBehavior.InjectResultAsContext),
        new HostCommandDescriptor(
            Name:               "open_panel",
            Description:        "Opens a named panel. Valid names: Approvals, Tasks, Trace",
            Parameters:         [new HostCommandParameterDescriptor("name", "string", Required: true)],
            ResultBehavior:     HostCommandResultBehavior.Silent),
        new HostCommandDescriptor(
            Name:               "inject_text",
            Description:        "Feeds arbitrary text back to the AI as the next user turn",
            Parameters:         [new HostCommandParameterDescriptor("text", "string", Required: true)],
            ResultBehavior:     HostCommandResultBehavior.InjectResultAsContext),
        new HostCommandDescriptor(
            Name:               "clear_approved",
            Description:        "Clears approved entries from the Approvals panel",
            Parameters:         Array.Empty<HostCommandParameterDescriptor>(),
            ResultBehavior:     HostCommandResultBehavior.Silent),
    ];

    internal IReadOnlyList<HostCommandDescriptor> GetCommands(string? workspaceFolder) {
        var extensions = LoadExtensionCommands(workspaceFolder);
        if (extensions.Count == 0)
            return BuiltInCommands;

        var builtInNames = new HashSet<string>(
            BuiltInCommands.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        var merged = new List<HostCommandDescriptor>(BuiltInCommands);
        foreach (var ext in extensions) {
            if (!builtInNames.Contains(ext.Name))
                merged.Add(ext);
        }
        return merged;
    }

    internal ValidationResult Validate(
        HostCommandInvocation invocation,
        HostCommandDescriptor descriptor) {
        var missing = descriptor.Parameters
            .Where(p => p.Required)
            .Where(p => invocation.Parameters is null ||
                        !invocation.Parameters.TryGetValue(p.Name, out var v) ||
                        string.IsNullOrWhiteSpace(v))
            .Select(p => p.Name)
            .ToArray();

        if (missing.Length == 0)
            return ValidationResult.Ok();

        return ValidationResult.Fail(
            $"Missing required parameter(s) for '{descriptor.Name}': {string.Join(", ", missing)}");
    }

    internal string BuildCatalogInstruction(string? workspaceFolder) {
        var commands = GetCommands(workspaceFolder);
        var sb = new StringBuilder();

        sb.AppendLine("You may invoke SquadDash host commands by appending a HOST_COMMAND_JSON block at the very end of your response, after all other content:");
        sb.AppendLine();
        sb.AppendLine("HOST_COMMAND_JSON:");
        sb.AppendLine("[");
        sb.AppendLine("  { \"command\": \"command_name\" },");
        sb.AppendLine("  { \"command\": \"open_panel\", \"parameters\": { \"name\": \"Approvals\" } }");
        sb.AppendLine("]");
        sb.AppendLine();
        sb.AppendLine("Commands are executed sequentially. Commands that return output inject that output as your next user turn.");
        sb.AppendLine();
        sb.AppendLine("Available commands:");
        foreach (var cmd in commands) {
            var paramList = cmd.Parameters.Count > 0
                ? "(" + string.Join(", ", cmd.Parameters.Select(p => p.Name)) + ")"
                : string.Empty;
            sb.AppendLine($"- {cmd.Name}{paramList}: {cmd.Description}");
        }
        sb.AppendLine();
        sb.Append("Only emit HOST_COMMAND_JSON when you intend to invoke a command. Do not include it in every response.");

        return sb.ToString();
    }

    private static IReadOnlyList<HostCommandDescriptor> LoadExtensionCommands(string? workspaceFolder) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            return [];

        var path = Path.Combine(workspaceFolder, ".squad", "commands.json");
        if (!File.Exists(path))
            return [];

        try {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<HostCommandDescriptor>();
            foreach (var element in document.RootElement.EnumerateArray()) {
                var cmd = TryParseExtensionCommand(element);
                if (cmd is not null)
                    result.Add(cmd);
            }
            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException) {
            SquadDashTrace.Write(TraceCategory.Performance, $"HostCommandRegistry: failed to load {path}: {ex.Message}");
            return [];
        }
    }

    private static HostCommandDescriptor? TryParseExtensionCommand(JsonElement element) {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("name", out var nameProp) ||
            nameProp.ValueKind != JsonValueKind.String)
            return null;

        var name = nameProp.GetString();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var description = element.TryGetProperty("description", out var descProp) &&
                          descProp.ValueKind == JsonValueKind.String
            ? descProp.GetString() ?? string.Empty
            : string.Empty;

        var parameters = new List<HostCommandParameterDescriptor>();
        if (element.TryGetProperty("parameters", out var paramsProp) &&
            paramsProp.ValueKind == JsonValueKind.Array) {
            foreach (var p in paramsProp.EnumerateArray()) {
                var param = TryParseExtensionParameter(p);
                if (param is not null)
                    parameters.Add(param);
            }
        }

        var resultBehavior = HostCommandResultBehavior.Silent;
        if (element.TryGetProperty("resultBehavior", out var rbProp) &&
            rbProp.ValueKind == JsonValueKind.String) {
            resultBehavior = rbProp.GetString() switch {
                "inject_result_as_context" => HostCommandResultBehavior.InjectResultAsContext,
                "notify_user"              => HostCommandResultBehavior.NotifyUser,
                _                          => HostCommandResultBehavior.Silent
            };
        }

        var requiresConfirmation = element.TryGetProperty("requiresConfirmation", out var rcProp) &&
                                   rcProp.ValueKind == JsonValueKind.True;

        return new HostCommandDescriptor(name.Trim(), description, parameters, resultBehavior, requiresConfirmation);
    }

    private static HostCommandParameterDescriptor? TryParseExtensionParameter(JsonElement element) {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("name", out var nameProp) ||
            nameProp.ValueKind != JsonValueKind.String)
            return null;

        var name = nameProp.GetString();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var type = element.TryGetProperty("type", out var typeProp) &&
                   typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString() ?? "string"
            : "string";

        var required = element.TryGetProperty("required", out var reqProp) &&
                       reqProp.ValueKind == JsonValueKind.True;

        var desc = element.TryGetProperty("description", out var descProp) &&
                   descProp.ValueKind == JsonValueKind.String
            ? descProp.GetString()
            : null;

        return new HostCommandParameterDescriptor(name.Trim(), type, required, desc);
    }
}

internal sealed class ValidationResult {
    public bool IsValid { get; }
    public string? ErrorMessage { get; }

    private ValidationResult(bool isValid, string? errorMessage) {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    internal static ValidationResult Ok() => new(true, null);
    internal static ValidationResult Fail(string message) => new(false, message);
}
