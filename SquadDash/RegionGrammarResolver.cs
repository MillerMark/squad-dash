using System;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash;

/// <summary>
/// Pure, UI-free policy engine that decides which installed AvalonEdit built-in grammar
/// (if any) a code region should use for syntax highlighting.
/// </summary>
/// <remarks>
/// <para>
/// The engine is driven by the persistent fence token stored on a region (see
/// <see cref="RegionLanguagePicker"/>). Three modes exist, distinguished exactly as
/// <see cref="RegionLanguagePicker.GetMode"/> distinguishes them:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Auto-detect</b> (empty token): the only mode that is allowed to run the content
///       heuristic. Callers typically invoke this on a debounce while the user types.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Plain text</b> (token <c>"text"</c>, case-insensitive): the user pinned the region
///       to plain text. <see cref="Resolve"/> returns <see langword="null"/> and never runs the
///       heuristic, never consults the alias map.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Explicit language</b> (any other non-empty token): the user pinned a specific
///       grammar. The token is resolved through the caller-supplied
///       alias map for <i>every</i> such token and the
///       heuristic is never run. Unknown/unsupported pinned tokens therefore resolve to
///       <see langword="null"/> (no highlighting) rather than silently falling back to detection.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Classifier precedence and limits (see <see cref="DetectBuiltIn"/>):</b> detection is
/// deterministic and bounded. Only the first <see cref="MaxScanLength"/> characters are examined,
/// using non-backtracking regexes. Tier-1 document signatures (<c>&lt;?php</c>, <c>&lt;?xml</c>,
/// <c>&lt;!DOCTYPE html&gt;</c>/<c>&lt;html&gt;</c>) are unmistakable and win immediately in that
/// order. Otherwise a tier&#8209;2 marker scan runs across C#, Java, C++, PowerShell, JavaScript
/// and CSS; a grammar is returned only when <i>exactly one</i> of those languages matches.
/// Zero matches, more than one match (ambiguous/overlapping fragments), prose, or empty input all
/// return <see langword="null"/>. Every candidate is additionally verified to be installed via
/// <see cref="HighlightingManager"/>, so this type can never name a grammar that is not actually
/// loaded. Only well-separated, commonly-used languages are classified on purpose: the goal is a
/// small, high-precision classifier, not broad coverage.
/// </para>
/// </remarks>
public static class RegionGrammarResolver
{
    /// <summary>Upper bound on the number of leading characters the classifier inspects.</summary>
    public const int MaxScanLength = 8192;

    private const RegexOptions SharedOptions =
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking;

    // Document signatures take precedence over markers in embedded scripts.
    private static readonly Regex PhpSignature = Make(@"<\?(php|=)", ignoreCase: true);
    private static readonly Regex XmlSignature = Make(@"^\s*<\?xml\b", ignoreCase: true);
    private static readonly Regex HtmlSignature = Make(
        @"<!DOCTYPE\s+html|<html[\s>]|</(html|head|body|div|span|table|ul|ol|li|p|a|h[1-6])>",
        ignoreCase: true);

    // Require a single matching language when no document signature is present.
    private static readonly LanguageMarkers[] Tier2 =
    {
        new("C#", new[]
        {
            Make(@"\busing\s+System\b"),
            Make(@"\bnamespace\s+[A-Za-z_][\w.]*\s*;"),
            Make(@"\bConsole\.(Write|WriteLine)\b"),
            Make(@"\bpublic\s+static\s+void\s+Main\s*\(\s*string\[\]"),
        }),
        new("Java", new[]
        {
            Make(@"\bimport\s+java\."),
            Make(@"\bSystem\.out\.print"),
            Make(@"\bpublic\s+static\s+void\s+main\s*\(\s*String"),
            Make(@"\bpackage\s+[a-z][\w.]*\s*;"),
        }),
        new("C++", new[]
        {
            Make(@"#\s*include\s*<"),
            Make(@"\bstd::"),
            Make(@"\bcout\s*<<"),
        }),
        new("PowerShell", new[]
        {
            Make(@"\b(Write|Get|Set|New|Remove|Invoke|Start|Stop|Import|Export)-[A-Z][A-Za-z]+"),
            Make(@"\[CmdletBinding"),
            Make(@"\$PSScriptRoot\b"),
            Make(@"\bparam\s*\(\s*\$"),
        }),
        new("JavaScript", new[]
        {
            Make(@"\bconsole\.log\s*\("),
            Make(@"\bmodule\.exports\b"),
            Make(@"\brequire\s*\(\s*['""]"),
            Make(@"\bdocument\.(getElementById|querySelector)"),
            Make(@"\bexport\s+default\b"),
        }),
        new("CSS", new[]
        {
            Make(@"@media\b", ignoreCase: true),
            Make(@"@import\b", ignoreCase: true),
            Make(@"[.#]?[-\w]+\s*\{[^{}]*:[^{}]+;[^{}]*\}"),
        }),
    };

    /// <summary>
    /// Returns <see langword="true"/> only for the auto-detect (empty) token; this is the sole mode
    /// in which <see cref="Resolve"/> is permitted to run the content heuristic.
    /// </summary>
    public static bool ShouldAutoDetect(string fenceToken) =>
        RegionLanguagePicker.GetMode(fenceToken ?? string.Empty) == RegionLanguageMode.AutoDetect;

    /// <summary>
    /// Resolves the grammar name for a region from its persistent <paramref name="fenceToken"/>.
    /// </summary>
    /// <param name="fenceToken">
    /// The region's persistent fence token. Empty =&gt; auto-detect; <c>"text"</c>
    /// (case-insensitive) =&gt; pinned plain text; any other non-empty value =&gt; explicit grammar.
    /// </param>
    /// <param name="content">The region body, used only in auto-detect mode.</param>
    /// <param name="resolveFenceToken">
    /// Caller-supplied alias map from an explicit fence token to an installed grammar name (or
    /// <see langword="null"/> when the token is unsupported). Consulted for every explicit token and
    /// never bypassed by the heuristic.
    /// </param>
    /// <param name="detectContent">
    /// Optional heuristic override (used for testing / alternate classifiers). When omitted,
    /// <see cref="DetectBuiltIn"/> is used. Invoked only in auto-detect mode.
    /// </param>
    /// <returns>An installed grammar name, or <see langword="null"/> for plain text / no match.</returns>
    public static string? Resolve(
        string fenceToken,
        string content,
        Func<string, string?> resolveFenceToken,
        Func<string, string?>? detectContent = null)
    {
        ArgumentNullException.ThrowIfNull(resolveFenceToken);

        var token = fenceToken ?? string.Empty;
        return RegionLanguagePicker.GetMode(token) switch
        {
            RegionLanguageMode.AutoDetect => (detectContent ?? DetectBuiltIn)(content ?? string.Empty),
            RegionLanguageMode.PlainText => null,
            _ => resolveFenceToken(token),
        };
    }

    /// <summary>
    /// Conservatively classifies <paramref name="content"/> and returns the exact name of an
    /// installed AvalonEdit built-in grammar, or <see langword="null"/> when the content is empty,
    /// ambiguous, prose, or does not clearly match one of the supported languages. See the type
    /// remarks for precedence and bounded-work guarantees.
    /// </summary>
    public static string? DetectBuiltIn(string content)
    {
        content ??= string.Empty;
        var sample = content.Length > MaxScanLength
            ? content.Substring(0, MaxScanLength)
            : content;
        if (string.IsNullOrWhiteSpace(sample))
            return null;

        // Tier-1: unmistakable document signatures, in precedence order.
        if (IsMatch(PhpSignature, sample))
            return Installed("PHP");
        if (IsMatch(XmlSignature, sample))
            return Installed("XML");
        if (IsMatch(HtmlSignature, sample))
            return Installed("HTML");

        // Tier-2: require exactly one matching language to stay conservative on ambiguity.
        string? winner = null;
        foreach (var language in Tier2)
        {
            if (!language.Matches(sample))
                continue;
            if (winner is not null)
                return null; // ambiguous overlap => no highlighting
            winner = language.Name;
        }

        return winner is null ? null : Installed(winner);
    }

    /// <summary>Returns <paramref name="name"/> only when that grammar is actually installed.</summary>
    private static string? Installed(string name) =>
        HighlightingManager.Instance.GetDefinition(name) is not null ? name : null;

    private static bool IsMatch(Regex regex, string input) => regex.IsMatch(input);

    private static Regex Make(string pattern, bool ignoreCase = false) =>
        new(pattern, SharedOptions | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));

    private sealed class LanguageMarkers
    {
        public LanguageMarkers(string name, Regex[] markers)
        {
            Name = name;
            _markers = markers;
        }

        public string Name { get; }

        private readonly Regex[] _markers;

        public bool Matches(string sample)
        {
            foreach (var marker in _markers)
            {
                if (IsMatch(marker, sample))
                    return true;
            }
            return false;
        }
    }
}
