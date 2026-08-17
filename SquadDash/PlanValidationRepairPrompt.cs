namespace SquadDash;

/// <summary>
/// Builds a bounded repair prompt for a validation turn that completed but did not return
/// the required <see cref="PlanValidationResultParser.Marker"/> envelope. This is a single
/// repair attempt — if it fails, the validation is marked failed.
/// </summary>
internal static class PlanValidationRepairPrompt
{
    internal static string Build(
        Plan plan,
        PlanValidationNode validation,
        string reason,
        string? observedHead)
    {
        var evidence = string.Join(",\n", validation.Assertions.Select(assertion =>
            "    { \"assertion\": \"" + PlanValidationPromptBuilder.EscapeJson(assertion) +
            "\", \"passed\": true, \"evidence\": \"what was observed for this exact assertion\" }"));
        return $$"""
            SquadDash detected that your previous response completed normally but its required PLAN_VALIDATION_RESULT_JSON envelope was missing or did not match the approved validation contract.
            Reason: {{reason}}

            Please provide ONLY the validation result envelope now, based on the assessment you just performed. Do not re-run tools, re-examine files, or repeat previous work.
            Do NOT create any commits — validation is non-mutating.
            Return exactly {{validation.Assertions.Count}} assertionEvidence item(s), in the order shown below. Copy every assertion string verbatim. Do not split, merge, summarize, or rewrite an assertion.

            Required format:
            PLAN_VALIDATION_RESULT_JSON:
            {
              "validationId": "{{validation.ValidationId}}",
              "planId": "{{plan.PlanId}}",
              "passed": true,
              "summary": "concise description of validation outcome",
              "assertionEvidence": [
            {{evidence}}
              ],
              "validatedCommit": "{{observedHead ?? "<HEAD commit SHA observed during validation>"}}"
            }
            """;
    }
}
