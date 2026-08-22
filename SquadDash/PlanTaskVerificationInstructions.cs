namespace SquadDash;

internal static class PlanTaskVerificationInstructions
{
    internal const string EnhancedIndependentReview =
        "Perform an enhanced independent review. Identify the highest-risk claims and failure modes from the approved step, " +
        "actual diff, production paths, and tests. Test plausible alternative explanations and negative cases; inspect " +
        "integration boundaries and whether the tests would fail when the behavior is broken; and identify behavior that " +
        "could remain broken while the reported tests still pass.";
}
