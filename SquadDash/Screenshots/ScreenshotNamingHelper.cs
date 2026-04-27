using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SquadDash.Screenshots;

/// <summary>
/// Suggests human-readable, kebab-case screenshot names derived from the WPF
/// element names recorded in the edge anchors.
///
/// <para>
/// The coordinator calls <see cref="SuggestName"/> when the user has not
/// provided an explicit name, then offers the suggestion for confirmation or
/// editing before the manifest is written.
/// </para>
///
/// <para><b>Naming prompt template</b> (for Mira Quill's interactive use):</para>
/// <code>
/// Given these anchor element names: {Top.ElementNames}, {Right.ElementNames},
/// {Bottom.ElementNames}, {Left.ElementNames} and the theme "{Theme}", suggest
/// a concise kebab-case screenshot name that describes the captured UI region.
/// Format: [primary-control]-[state?]-[variant?]-[theme]
/// Examples: "agent-card-selected-dark", "toolbar-hover-light", "sidebar-collapsed-dark"
/// </code>
/// </summary>
public static partial class ScreenshotNamingHelper
{
    /// <summary>
    /// Suggests a kebab-case screenshot name derived from the most specific
    /// anchor element names visible in the capture.
    /// </summary>
    /// <param name="theme">
    ///   The active UI theme string, e.g. <c>"Dark"</c> or <c>"Light"</c>.
    ///   Appended as the final token.
    /// </param>
    /// <param name="anchors">
    ///   The four edge anchor records for the capture.  Unnamed anchors
    ///   (<see cref="EdgeAnchorRecord.ElementNames"/> is empty) are skipped.
    /// </param>
    /// <returns>
    ///   A kebab-case suggestion such as <c>"agent-card-top-dark"</c>, built from
    ///   the distinct named-anchor element names.  Falls back to
    ///   <c>"capture-{theme}-{yyyyMMddHHmmss}"</c> when all anchors are unnamed.
    /// </returns>
    public static string SuggestName(string theme, IReadOnlyList<EdgeAnchorRecord> anchors)
    {
        var normalizedTheme = (theme ?? "unknown").ToLowerInvariant();

        // Collect distinct non-null element names from all anchors, preserving anchor
        // order (Top → Right → Bottom → Left) so the suggestion is deterministic.
        // Each anchor may now have multiple tied names; flatten them all.
        var names = anchors
            .SelectMany(a => a.ElementNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            // All anchors unnamed — fall back to a timestamp-based name so the
            // file is still uniquely identifiable even without structural context.
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            return $"capture-{normalizedTheme}-{ts}";
        }

        // Convert each element name to lowercase kebab tokens, flatten, deduplicate,
        // and cap at 4 tokens to keep names readable.
        var tokens = names
            .SelectMany(SplitToKebabTokens)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        tokens.Add(normalizedTheme);

        return string.Join("-", tokens.Select(t => t.ToLowerInvariant()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a PascalCase, camelCase, or underscore/hyphen-separated identifier
    /// into lowercase kebab tokens, dropping single-character noise.
    /// </summary>
    /// <example>
    ///   "AgentStatusCard"  → ["agent", "status", "card"]
    ///   "agentCard"        → ["agent", "card"]
    ///   "agent_card"       → ["agent", "card"]
    /// </example>
    private static IEnumerable<string> SplitToKebabTokens(string name)
    {
        // Normalise separators, then inject a space before each uppercase-after-lowercase
        // transition to split PascalCase / camelCase words.
        var normalised = name.Replace('_', ' ').Replace('-', ' ');
        var spaced     = PascalSplitRegex().Replace(normalised, " $1").Trim();

        return spaced
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1);   // drop single-char tokens (e.g. "x", "y")
    }

    /// <summary>Matches an uppercase letter immediately preceded by a lowercase letter.</summary>
    [GeneratedRegex(@"(?<=[a-z])([A-Z])")]
    private static partial Regex PascalSplitRegex();
}
