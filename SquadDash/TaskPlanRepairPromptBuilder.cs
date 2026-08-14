using System;
using System.Collections.Generic;

namespace SquadDash;

internal static class TaskPlanRepairPromptBuilder
{
    internal static string Build(
        string? rejectedCandidate,
        string? validationError,
        string liveSpecification)
    {
        var candidate = string.IsNullOrWhiteSpace(rejectedCandidate)
            ? "(The rejected candidate was unavailable.)"
            : rejectedCandidate.Trim();
        var diagnostics = new List<string>();

        if (!string.IsNullOrWhiteSpace(rejectedCandidate) &&
            !TasksJsonParser.TryParse(rejectedCandidate, out _, out var parserDiagnostic) &&
            parserDiagnostic is not null)
        {
            diagnostics.Add($"{parserDiagnostic.Code}: {parserDiagnostic.Message}");
        }

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            var normalizedError = validationError.Trim();
            if (!diagnostics.Exists(value =>
                    value.EndsWith(normalizedError, StringComparison.Ordinal)))
            {
                diagnostics.Add(normalizedError);
            }
        }

        if (diagnostics.Count == 0)
            diagnostics.Add("The validator did not provide a more specific diagnostic.");

        return "The previous TASKS_JSON candidate failed host validation.\n\n" +
               "Exact validation diagnostics:\n- " + string.Join("\n- ", diagnostics) + "\n\n" +
               "The complete rejected candidate is included below. Preserve its intended scope, " +
               "but correct every schema violation and every affected reference. If an active Planning " +
               "Architect authored it, re-engage that same specialist with this entire repair context.\n\n" +
               "--- BEGIN REJECTED TASKS_JSON CANDIDATE ---\n" + candidate + "\n" +
               "--- END REJECTED TASKS_JSON CANDIDATE ---\n\n" +
               "Respond with one complete corrected TASKS_JSON block, not a patch or substitution list. " +
               "Read the live specification directly and perform a complete conformance preflight: marker, " +
               "exact property names, group-derived task and validation IDs, required fields, enum values, " +
               "dependency references, validation boundaries, commands collections, and proof-contract fields. " +
               "Do not execute work and do not emit DECOMPOSE_DECISION_JSON.\n\n" +
               "Follow this live specification exactly:\n\n" + liveSpecification;
    }
}
