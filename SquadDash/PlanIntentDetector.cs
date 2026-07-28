using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Represents how a user prompt relates to plan creation.
/// </summary>
internal enum PlanCreationIntent
{
    /// <summary>The prompt does not involve plan-creation intent.</summary>
    None,

    /// <summary>
    /// "Plan" is mentioned in a discussion-only context (e.g. "I plan to do X",
    /// "what's the plan?") — no TASKS_JSON mandate should be injected.
    /// </summary>
    Discussion,

    /// <summary>
    /// The user explicitly wants to create a plan (e.g. "create a plan for X",
    /// "draft a plan for the migration"). TASKS_JSON should be emitted.
    /// </summary>
    ExplicitCreate,

    /// <summary>
    /// The user wants a plan AND wants it immediately implemented (e.g.
    /// "create a plan and implement it"). TASKS_JSON should still be emitted —
    /// the approval boundary prevents implementation until the user approves.
    /// </summary>
    PlanAndImplement,
}

/// <summary>
/// Classifies a user prompt to determine whether it expresses explicit plan-creation intent.
/// Pure logic — no UI, I/O, or WPF dependencies; fully testable in isolation.
/// <para>
/// Explicit intent triggers — classified as <see cref="PlanCreationIntent.ExplicitCreate"/>
/// or <see cref="PlanCreationIntent.PlanAndImplement"/>:
/// <list type="bullet">
///   <item>Creation verb + "plan": create/draft/devise/prepare/make/write/design/propose a plan</item>
///   <item>"plan out X": e.g. "plan out the authentication migration"</item>
/// </list>
/// </para>
/// <para>
/// Non-triggering patterns — classified as <see cref="PlanCreationIntent.Discussion"/>:
/// <list type="bullet">
///   <item>"I plan to do X" — first-person future statement</item>
///   <item>"what's the plan?" — information question about an existing plan</item>
///   <item>"my plan is to…" — personal intent statement</item>
///   <item>"the plan is…", "plan A vs plan B" — attributive/comparative uses</item>
/// </list>
/// </para>
/// </summary>
internal static class PlanIntentDetector
{
    /// <summary>
    /// Matches explicit plan-creation requests addressed to the AI:
    /// "create a plan", "draft me a plan for X", "devise a plan", etc.
    /// Handles optional indirect object ("me"/"us") followed by optional article.
    /// Does NOT match "I plan to…" or possessive/attributive uses.
    /// </summary>
    private static readonly Regex ExplicitCreatePattern = new(
        @"\b(?:create|draft|devise|prepare|make|write|design|propose|outline|generate|formulate|produce)\s+" +
        @"(?:(?:me|us|for\s+me|for\s+us)\s+)?(?:(?:a|an|the)\s+)?plan\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// "plan out X" — e.g. "plan out the database migration", "plan out our sprint".
    /// </summary>
    private static readonly Regex PlanOutPattern = new(
        @"\bplan\s+out\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Signals that the user wants the plan PLUS immediate implementation.
    /// TASKS_JSON is still emitted — the approval boundary blocks implementation.
    /// </summary>
    private static readonly Regex ImplAfterPlanPattern = new(
        @"\bplan\b.{0,80}\b(?:and|then|&)\s+(?:implement|execute|apply|build|deploy|develop|code)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Discussion-only uses of "plan" that should NOT trigger TASKS_JSON injection.
    /// </summary>
    private static readonly Regex DiscussionPattern = new(
        @"\b(?:" +
        @"i\s+plan\s+to" +                              // "I plan to implement this"
        @"|(?:my|our|your|their)\s+plan\s+(?:is|was|will\s+be|has\s+been)" + // "my plan is to…"
        @"|what(?:'s|\s+is)\s+(?:the|your|a|our)\s+plan" + // "what's the plan?"
        @"|do\s+you\s+have\s+a\s+plan" +                // "do you have a plan?"
        @"|the\s+plan\s+(?:is|was|will\s+be|has\s+been)" + // "the plan is to…"
        @"|plan\s+[a-c]\b" +                            // "plan A", "plan B", "plan C"
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the given <paramref name="prompt"/> against the known plan-creation patterns.
    /// Returns <see cref="PlanCreationIntent.None"/> for null or whitespace-only input.
    /// </summary>
    public static PlanCreationIntent Classify(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return PlanCreationIntent.None;

        var hasExplicit = ExplicitCreatePattern.IsMatch(prompt) || PlanOutPattern.IsMatch(prompt);
        if (hasExplicit)
        {
            return ImplAfterPlanPattern.IsMatch(prompt)
                ? PlanCreationIntent.PlanAndImplement
                : PlanCreationIntent.ExplicitCreate;
        }

        if (DiscussionPattern.IsMatch(prompt)) return PlanCreationIntent.Discussion;
        return PlanCreationIntent.None;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the prompt is an explicit plan-creation or
    /// plan-and-implement request — the two intents that mandate TASKS_JSON output.
    /// </summary>
    public static bool IsExplicitPlanRequest(string? prompt) =>
        Classify(prompt) is PlanCreationIntent.ExplicitCreate or PlanCreationIntent.PlanAndImplement;
}
