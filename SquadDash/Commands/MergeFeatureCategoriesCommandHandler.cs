namespace SquadDash.Commands;

using System;
using System.Collections.Generic;
using System.Text.Json;

internal sealed class MergeFeatureCategoriesCommandHandler : IHostCommandHandler
{
    private readonly Action<IReadOnlyList<(string Source, string Target)>> _applyMerges;

    internal MergeFeatureCategoriesCommandHandler(
        Action<IReadOnlyList<(string Source, string Target)>> applyMerges) =>
        _applyMerges = applyMerges;

    public string CommandName => "merge_feature_categories";

    public HostCommandResult Execute(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("merges", out var json) || string.IsNullOrWhiteSpace(json))
            return new HostCommandResult(false, ErrorMessage: "Missing merges parameter.");

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return new HostCommandResult(false, ErrorMessage: "merges must be a JSON array.");

            var merges = new List<(string Source, string Target)>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("source", out var sourceProperty) ||
                    !element.TryGetProperty("target", out var targetProperty))
                    continue;
                var source = sourceProperty.GetString()?.Trim();
                var target = targetProperty.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) ||
                    string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                    continue;
                merges.Add((source, target));
            }

            if (merges.Count == 0)
                return new HostCommandResult(false, ErrorMessage: "No valid category merges were supplied.");

            _applyMerges(merges);
            return new HostCommandResult(true);
        }
        catch (JsonException ex)
        {
            return new HostCommandResult(false, ErrorMessage: $"Invalid merges JSON: {ex.Message}");
        }
    }
}
