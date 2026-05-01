using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// A prompt injection that fires when the user's input matches a regular-expression pattern.
/// The <see cref="InjectionText"/> may contain <c>{variable}</c> placeholders that are replaced
/// with workspace-specific values before the text is appended to the outgoing prompt.
/// </summary>
/// <param name="Id">
///   Stable identifier used for tracing.  Must be unique across all registered injections.
/// </param>
/// <param name="Pattern">
///   Case-insensitive regular expression applied to the raw user prompt text.
///   When it matches, <see cref="InjectionText"/> is appended to the supplemental context block.
/// </param>
/// <param name="InjectionText">
///   The text to inject.  Supports <c>{workspaceFolder}</c> substitution (more variables may be
///   added in future).  The injection is framing / conditional guidance — "if the user is asking
///   about X, follow these conventions" — not an imperative command.
/// </param>
internal sealed record TriggeredPromptInjection(string Id, string Pattern, string InjectionText);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Evaluates a set of <see cref="TriggeredPromptInjection"/> entries against a user prompt and
/// returns the resolved injection texts for those whose patterns matched.
/// </summary>
internal static class TriggeredInjectionEvaluator {

    /// <summary>
    /// Matches <paramref name="userPrompt"/> against every injection in <paramref name="injections"/>.
    /// For each match, variable placeholders in <see cref="TriggeredPromptInjection.InjectionText"/>
    /// are replaced from <paramref name="variables"/>, and the result is included in the return list.
    /// </summary>
    /// <param name="userPrompt">The raw prompt text typed or spoken by the user.</param>
    /// <param name="injections">All registered injections (built-in and workspace-local).</param>
    /// <param name="variables">
    ///   Key→value map of substitution variables.
    ///   Currently supported: <c>workspaceFolder</c>.
    /// </param>
    /// <returns>
    ///   Matched injection entries paired with their fully-resolved injection text.
    ///   Empty when nothing fired.
    /// </returns>
    internal static IReadOnlyList<(TriggeredPromptInjection Injection, string ResolvedText)> Evaluate(
        string userPrompt,
        IEnumerable<TriggeredPromptInjection> injections,
        IReadOnlyDictionary<string, string> variables) {

        if (string.IsNullOrWhiteSpace(userPrompt))
            return [];

        var matched = new List<(TriggeredPromptInjection, string)>();

        foreach (var injection in injections) {
            bool isMatch;
            try {
                isMatch = Regex.IsMatch(userPrompt, injection.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50));
            }
            catch (RegexMatchTimeoutException) {
                isMatch = false;
            }
            catch (ArgumentException) {
                // Malformed pattern — skip rather than crash
                continue;
            }

            if (!isMatch) continue;

            var resolved = injection.InjectionText;
            foreach (var (key, value) in variables)
                resolved = resolved.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);

            matched.Add((injection, resolved));
        }

        return matched;
    }

    /// <summary>
    /// Builds the <see cref="IReadOnlyDictionary{TKey,TValue}"/> of substitution variables
    /// from the current workspace context.  Pass <c>null</c> for any unknown values.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildVariables(string? workspaceFolder) {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(workspaceFolder))
            dict["workspaceFolder"] = workspaceFolder;
        return dict;
    }
}
