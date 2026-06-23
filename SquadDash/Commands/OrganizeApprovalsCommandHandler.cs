namespace SquadDash.Commands;

using System;
using System.Collections.Generic;
using System.Text.Json;

internal sealed class OrganizeApprovalsCommandHandler : IHostCommandHandler {
    private readonly Action<IReadOnlyList<(string Sha, string Group)>> _applyAssignments;

    public OrganizeApprovalsCommandHandler(Action<IReadOnlyList<(string Sha, string Group)>> applyAssignments)
        => _applyAssignments = applyAssignments;

    public string CommandName => "organize_approvals";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters) {
        if (!parameters.TryGetValue("assignments", out var json) || string.IsNullOrWhiteSpace(json))
            return new HostCommandResult(false, "Missing assignments parameter.");

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new HostCommandResult(false, "assignments must be a JSON array.");

            var assignments = new List<(string Sha, string Group)>();
            foreach (var elem in doc.RootElement.EnumerateArray()) {
                if (!elem.TryGetProperty("sha", out var shaProp) || !elem.TryGetProperty("group", out var groupProp))
                    continue;
                var sha   = shaProp.GetString();
                var group = groupProp.GetString();
                if (!string.IsNullOrWhiteSpace(sha) && !string.IsNullOrWhiteSpace(group))
                    assignments.Add((sha!, group!));
            }

            _applyAssignments(assignments);
            return new HostCommandResult(true);
        }
        catch (JsonException ex) {
            return new HostCommandResult(false, $"Invalid assignments JSON: {ex.Message}");
        }
    }
}
