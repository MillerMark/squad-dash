namespace SquadDash;

internal sealed record PlanValidationAssertionDetails(
    int Number,
    string Assertion,
    bool? Passed,
    string Evidence)
{
    internal string StatusLabel => Passed switch
    {
        true => "Passed",
        false => "Failed",
        _ => "No evidence",
    };
}

internal sealed record PlanValidationDetailsPresentation(
    string PlanId,
    string PlanTitle,
    string ValidationId,
    string ValidationTitle,
    string Status,
    string Summary,
    string? ValidatedCommit,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PlanValidationAssertionDetails> Assertions)
{
    internal int FailedCount => Assertions.Count(item => item.Passed == false);
}

/// <summary>
/// Rehydrates the host-owned assertion evidence that older durable plan files store in the
/// canonical "[PASS|FAIL] assertion: evidence" representation. Matching is exact against the
/// approved assertion text; no filenames, ownership, or remediation details are inferred.
/// </summary>
internal static class PlanValidationDetailsPresentationBuilder
{
    internal static PlanValidationDetailsPresentation Build(Plan plan, PlanValidationNode validation)
    {
        var storedEvidence = validation.Evidence ?? [];
        var assertions = validation.Assertions
            .Select((assertion, index) => BuildAssertion(index + 1, assertion, storedEvidence))
            .ToArray();

        return new PlanValidationDetailsPresentation(
            plan.PlanId,
            plan.Title,
            validation.ValidationId,
            validation.Title,
            validation.Status,
            validation.Summary ?? "No validation summary was recorded.",
            validation.ValidatedCommit,
            validation.CompletedAt,
            assertions);
    }

    private static PlanValidationAssertionDetails BuildAssertion(
        int number,
        string assertion,
        IReadOnlyList<string> evidence)
    {
        var passedPrefix = $"[PASS] {assertion}: ";
        var failedPrefix = $"[FAIL] {assertion}: ";
        var passed = evidence.FirstOrDefault(item => item.StartsWith(passedPrefix, StringComparison.Ordinal));
        if (passed is not null)
            return new PlanValidationAssertionDetails(
                number,
                assertion,
                true,
                passed[passedPrefix.Length..]);

        var failed = evidence.FirstOrDefault(item => item.StartsWith(failedPrefix, StringComparison.Ordinal));
        if (failed is not null)
            return new PlanValidationAssertionDetails(
                number,
                assertion,
                false,
                failed[failedPrefix.Length..]);

        return new PlanValidationAssertionDetails(
            number,
            assertion,
            null,
            "No assertion-specific evidence was recorded.");
    }
}
