using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SquadDash;

/// <summary>
/// Pure, UI-free policy engine that decides which available grammar
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
/// <b>Classifier precedence and limits (see <see cref="Detect"/>):</b> detection is
/// deterministic and bounded. Only the first <see cref="MaxScanLength"/> characters are examined,
/// using parser-backed checks for JSON/XML and non-backtracking regexes for language signals.
/// Unmistakable document signatures win immediately. Otherwise a weighted marker scan runs across
/// built-in and bundled languages; a grammar is returned only when its score clears the confidence
/// threshold and no other language also clears that bar. Zero matches, weak matches, close overlaps,
/// prose, or empty input all return <see langword="null"/>. Every candidate is additionally
/// verified against the caller's available grammar set. TypeScript-specific signals disambiguate
/// its shared JavaScript/CSS markers; unrelated languages still require a single clear winner.
/// </para>
/// </remarks>
public static class RegionGrammarResolver
{
    /// <summary>Upper bound on the number of leading characters the classifier inspects.</summary>
    public const int MaxScanLength = 8192;

    internal const string ErrorMessageGrammarName = "Error message";

    private const int MinimumScore = 4;
    private const RegexOptions SharedOptions =
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly XmlReaderSettings XmlReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        ConformanceLevel = ConformanceLevel.Fragment,
    };

    // Document signatures take precedence over markers in embedded scripts.
    private static readonly Regex PhpSignature = Make(@"<\?(php|=)", ignoreCase: true);
    private static readonly Regex PatchSignature = Make(
        @"(?:^|\n)\s*(diff --git\b|Index:\s+\S+|---\s+[ab]/[^\r\n]+|@@\s+-\d+(,\d+)?\s+\+\d+(,\d+)?\s+@@)");
    private static readonly Regex AspXhtmlSignature = Make(
        @"<%@\s*(Page|Control|Master|Register|Import)\b|<asp:[A-Za-z]",
        ignoreCase: true);
    private static readonly Regex HtmlSignature = Make(
        @"<!DOCTYPE\s+html|<(html|head|body|div|span|table|ul|ol|li|p|a|h[1-6]|section|article|main|script|style|button|form|input)\b|</(html|head|body|div|span|table|ul|ol|li|p|a|h[1-6]|section|article|main|script|style|button|form)>",
        ignoreCase: true);
    private static readonly Regex XmlDeclarationSignature = Make(@"^\s*<\?xml\b", ignoreCase: true);
    private static readonly Regex MarkupRoot = Make(
        @"\A[\s\uFEFF]*(?:<\?xml\b[^?]*\?>\s*|<!--(?:[^-]|-[^-])*-->\s*)*<(?<name>[A-Za-z_][\w.:-]*)(?<attributes>(?:\s+[A-Za-z_][\w.:-]*\s*=\s*(?:""[^""]*""|'[^']*'))*)\s*/?(?:>|$)");
    private static readonly Regex MarkupAttribute = Make(
        @"(?<name>[A-Za-z_][\w.:-]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')");
    private static readonly Regex BashSignature = Make(
        @"^#![ \t]*/(bin/bash|usr/bin/bash|usr/bin/env[ \t]+(-S[ \t]+)?bash)([ \t]|\r?\n|$)");
    private static readonly Regex TsqlSpecificMarker = Make(
        @"\bDECLARE\s+@\w+|\bCREATE\s+(PROCEDURE|PROC)\b|(?:^|\n)[ \t]*GO[ \t]*(\r?\n|$)|\[[A-Za-z_][\w ]*\]",
        ignoreCase: true);
    private static readonly Regex YamlBlockRoot = Make(@"(?:^|\n)[A-Za-z_][\w.-]*:[ \t]*(#[^\r\n]*)?\r?\n");
    private static readonly Regex RustStandardLibraryImport = Make(
        @"(?:^|\n)[ \t]*(pub[ \t]+)?use[ \t]+std::");
    private static readonly Regex DotNetStackFrameLine = Make(
        @"(?m:^[ \t]+at[ \t]+(?:[\p{L}_<>$][\p{L}\p{N}_<>`$+|,\[\]-]*\.)+\.?[\p{L}_<>$][\p{L}\p{N}_<>`$+|,\[\]-]*\([^\r\n()]*\)(?:[ \t]+in[ \t]+[^ \t\r\n][^\r\n]*:line[ \t]+[0-9]+)?[ \t]*\r?$)");
    // Score language markers after unmistakable document signatures.
    private static readonly LanguageMarkers[] ScoredLanguages =
    {
        new("C#", new[]
        {
            WeightedMarker(@"\busing\s+System\b", 4),
            WeightedMarker(@"\bnamespace\s+[A-Za-z_][\w.]*\s*[;{]", 4),
            WeightedMarker(@"\bpublic\s+static\s+void\s+Main\s*\(\s*string\[\]", 4),
            WeightedMarker(@"\busing\s+[A-Za-z_][\w.]*\s*;", 3),
            WeightedMarker(@"\{\s*(get|set|init)\s*;", 4),
            WeightedMarker(@"\bevent\s+[A-Za-z_][\w.]*\??\s+[A-Za-z_]\w*\s*;", 4),
            WeightedMarker(@"\bEventHandler\?", 4),
            WeightedMarker(@"\b(public|internal|protected|private)\s+(sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+[A-Za-z_]\w*\s*\{\s*\}", 4),
            WeightedMarker(@"\b(public|internal|protected|private)\s+(sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+[A-Za-z_]\w*", 2),
            WeightedMarker(@"\b(public|private|protected|internal)\s+(sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|ref\s+)*(record|struct|interface)\b", 4),
            WeightedMarker(@"\b(public|private|protected|internal)\s+(async\s+|static\s+|virtual\s+|override\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|unsafe\s+)*[A-Za-z_][\w<>,\[\]?]*\s+[A-Z]\w*\s*\([^;{}]*\)\s*(=>|\{)", 3),
            WeightedMarker(@"\basync\s+Task(<|\b)", 4),
            WeightedMarker(@"\bCancellationToken\b", 3),
            WeightedMarker(@"\bI(ReadOnly)?(List|Dictionary|Collection|Enumerable)<", 3),
            WeightedMarker(@"\bConsole\.(Write|WriteLine)\b", 4),
        }),
        new("Java", new[]
        {
            WeightedMarker(@"\bimport\s+java\.", 4),
            WeightedMarker(@"\bSystem\.out\.print", 4),
            WeightedMarker(@"\bpublic\s+static\s+void\s+main\s*\(\s*String", 4),
            WeightedMarker(@"\bpackage\s+[a-z][\w.]*\s*;", 3),
            WeightedMarker(@"\b@Override\b", 3),
        }),
        new("C++", new[]
        {
            WeightedMarker(@"#\s*include\s*<", 4),
            WeightedMarker(@"\bstd::", 4),
            WeightedMarker(@"\bcout\s*<<", 4),
            WeightedMarker(@"\btemplate\s*<", 4),
            WeightedMarker(@"\b(int|void|auto)\s+main\s*\(", 2),
        }),
        new("PowerShell", new[]
        {
            WeightedMarker(@"\b(Write|Get|Set|New|Remove|Invoke|Start|Stop|Import|Export)-[A-Z][A-Za-z]+", 4),
            WeightedMarker(@"\[CmdletBinding", 4),
            WeightedMarker(@"\$PSScriptRoot\b", 4),
            WeightedMarker(@"\bparam\s*\(\s*\$", 4),
            WeightedMarker(@"\$_\.", 3),
        }),
        new("TypeScript", new[]
        {
            WeightedMarker(@"(?:^|\n)[ \t]*(export\s+)?(declare\s+)?interface\s+[A-Za-z_$][\w$]*\s*\{", 3),
            WeightedMarker(@"\)\s*:\s*[A-Za-z_$][\w$<>, \t\[\]|]*\s*=>", 4),
            WeightedMarker(@"(?:^|\n)[ \t]*(export\s+)?type\s+[A-Za-z_$][\w$]*\s*=", 4),
            WeightedMarker(@"(?:^|\n)[ \t]*(export\s+)?(const\s+)?enum\s+[A-Za-z_$][\w$]*\s*\{", 3),
            WeightedMarker(@":\s*(string|number|boolean)\b", 2),
            WeightedMarker(@"[(,]\s*[A-Za-z_$][\w$]*\??\s*:\s*(string|number|boolean)\b", 4),
            WeightedMarker(@"\b(const|let)\s+[A-Za-z_$][\w$]*\s*:\s*[A-Za-z_$][\w$]*", 4),
        }),
        new("JavaScript", new[]
        {
            WeightedMarker(@"\bconsole\.log\s*\(", 4),
            WeightedMarker(@"\bmodule\.exports\b", 4),
            WeightedMarker(@"\brequire\s*\(\s*['""]", 4),
            WeightedMarker(@"\bdocument\.(getElementById|querySelector)", 4),
            WeightedMarker(@"\bexport\s+default\b", 4),
            WeightedMarker(@"\bimport\s+[A-Za-z_$][\w$]*(\s*,\s*\{[^}]*\}|\s+from)?\s+from\s+['""]", 4),
            WeightedMarker(@"\bconst\s+[A-Za-z_$][\w$]*\s*=\s*(async\s*)?\([^)]*\)\s*=>", 4),
            WeightedMarker(@"\bfunction\s+[A-Za-z_$][\w$]*\s*\(", 3),
        }),
        new("CSS", new[]
        {
            WeightedMarker(@"@media\b", 4, ignoreCase: true),
            WeightedMarker(@"@import\b", 4, ignoreCase: true),
            WeightedMarker(@"[.#]?[-\w]+\s*\{[^{}]*:[^{}]+;[^{}]*\}", 4),
            WeightedMarker(@"--[-\w]+\s*:", 2),
        }),
        new("Python", new[]
        {
            WeightedMarker(@"(?:^|\n)\s*(async\s+)?def\s+[A-Za-z_]\w*\s*\([^)]*\)\s*:", 4),
            WeightedMarker(@"(?:^|\n)\s*class\s+[A-Za-z_]\w*(\([^)]*\))?\s*:", 4),
            WeightedMarker(@"if\s+__name__\s*==\s*['""]__main__['""]\s*:", 4),
            WeightedMarker(@"(?:^|\n)\s*from\s+[A-Za-z_][\w.]*\s+import\s+", 3),
            WeightedMarker(@"(?:^|\n)\s*import\s+[A-Za-z_][\w.]*", 2),
            WeightedMarker(@"\bself\.", 2),
        }),
        new("YAML", new[]
        {
            new WeightedLanguageMarker(YamlBlockRoot, 2),
            WeightedMarker(@"(?:^|\n)[ ]+[A-Za-z_][\w.-]*:[ \t]+[^ \t\r\n][^\r\n]*", 2),
            WeightedMarker(@"(?:^|\n)[ ]+-[ \t]+[^ \t\r\n][^\r\n]*", 2),
        }),
        new("SQL", new[]
        {
            WeightedMarker(@"\bSELECT\s+[\s\S]{1,240}\s+FROM\s+[\[\]\w.]+(\s+(WHERE|JOIN|ORDER|GROUP)\b|;)", 4, ignoreCase: true),
            WeightedMarker(@"\bDECLARE\s+@\w+", 4, ignoreCase: true),
            WeightedMarker(@"\bCREATE\s+(PROCEDURE|PROC|FUNCTION|VIEW|TABLE|TRIGGER)\b", 4, ignoreCase: true),
            WeightedMarker(@"\bINSERT\s+INTO\b", 4, ignoreCase: true),
            WeightedMarker(@"\bUPDATE\s+[\[\]\w.]+\s+SET\b", 4, ignoreCase: true),
            WeightedMarker(@"(?:^|\n)[ \t]*GO[ \t]*(\r?\n|$)", 4, ignoreCase: true),
            WeightedMarker(@"\bJOIN\s+[\[\]\w.]+\s+ON\b", 2, ignoreCase: true),
        }),
        new("Go", new[]
        {
            WeightedMarker(@"(?:^|\n)[ \t]*package[ \t]+[A-Za-z_]\w*[ \t]*(\r?\n|$)", 2),
            WeightedMarker(@"(?:^|\n)[ \t]*func[ \t]+(\([^)\r\n]+\)[ \t]+)?[A-Za-z_]\w*[ \t]*\([^)\r\n]*\)[ \t]*(\([^)\r\n]+\)|[\w*\[\].]+)?[ \t]*\{", 4),
            WeightedMarker(@"(?:^|\n)[ \t]*import[ \t]*\(", 2),
            WeightedMarker(@"\b[A-Za-z_]\w*[ \t]*:=", 2),
            WeightedMarker(@"\bfmt\.(Print|Printf|Println|Sprintf|Errorf)[ \t]*\(", 2),
            WeightedMarker(@"(?:^|\n)[ \t]*(go|defer)[ \t]+[A-Za-z_][\w.]*[ \t]*\(", 2),
        }),
        new("Rust", new[]
        {
            WeightedMarker(@"(?:^|\n)[ \t]*(pub(\([^)\r\n]+\))?[ \t]+)?(async[ \t]+|unsafe[ \t]+)?fn[ \t]+[A-Za-z_]\w*[ \t]*(<[^>\r\n]+>)?[ \t]*\([^)\r\n]*\)[ \t]*(->[ \t]*[^{;\r\n]+)?[ \t]*\{", 4),
            WeightedMarker(@"(?:^|\n)[ \t]*(pub[ \t]+)?use[ \t]+[A-Za-z_]\w*::", 4),
            WeightedMarker(@"\blet[ \t]+mut[ \t]+[A-Za-z_]\w*[ \t]*[:=]", 3),
            WeightedMarker(@"(?:^|\n)[ \t]*impl[ \t]+[A-Za-z_]\w*", 3),
            WeightedMarker(@"\b(println|eprintln|print|format|vec|assert|assert_eq|panic)![ \t]*[(\[]", 2),
        }),
        new("Bash", new[]
        {
            WeightedMarker(@"(?:^|\n)[ \t]*(if|elif)[ \t]+\[\[?[ \t]+[^\r\n]+\]\]?[ \t]*;[ \t]*then\b", 4),
            WeightedMarker(@"(?:^|\n)[ \t]*(fi|done|esac)[ \t]*(\r?\n|$)", 2),
            WeightedMarker(@"(?:^|\n)[ \t]*function[ \t]+[A-Za-z_]\w*[ \t]*(\([ \t]*\))?[ \t]*\{", 2),
            WeightedMarker(@"(?:^|\n)[ \t]*(echo|printf)[ \t]+[^\r\n]*\$(\{[A-Za-z_]\w*\}|[A-Za-z_]\w*)", 2),
            WeightedMarker(@"(?:^|\n)[ \t]*for[ \t]+[A-Za-z_]\w*[ \t]+in[ \t]+[^\r\n]+;[ \t]*do\b", 4),
        }),
        // Each structural family scores once; repeated list items or emphasized prose alone cannot win.
        new("Markdown", new[]
        {
            WeightedMarker(@"(?:^|\n)[ ]{0,3}#{1,6}[ \t]+\S", 2),
            WeightedMarker(@"(?:^|\n)[ ]{0,3}(```+|~~~+)[^\r\n]*(\r?\n|$)", 2),
            WeightedMarker(@"(?:^|\n)[ ]{0,3}([-+*]|[0-9]+\.)[ \t]+\S", 1),
            WeightedMarker(@"\[[^\]\r\n]+\]\((https?://|[./#])[^\s)]+\)", 1),
            WeightedMarker(@"\*\*[^*\r\n]+\*\*", 1),
        }),
        new("VB", new[]
        {
            WeightedMarker(@"(?:^|\n)\s*Imports\s+[A-Za-z_][\w.]*", 4, ignoreCase: true),
            WeightedMarker(@"(?:^|\n)\s*(Public\s+|Private\s+|Friend\s+|Protected\s+)?Class\s+[A-Za-z_]\w*[\s\S]{0,500}(?:^|\n)\s*End\s+Class\b", 4, ignoreCase: true),
            WeightedMarker(@"(?:^|\n)\s*(Public\s+|Private\s+|Friend\s+|Protected\s+)?(Sub|Function)\s+[A-Za-z_]\w*\s*\([^)]*\)[\s\S]{0,500}(?:^|\n)\s*End\s+(Sub|Function)\b", 4, ignoreCase: true),
            WeightedMarker(@"(?:^|\n)\s*Dim\s+[A-Za-z_]\w*\s+As\s+[A-Za-z_]\w*", 3, ignoreCase: true),
            WeightedMarker(@"(?:^|\n)\s*End\s+(Class|Module|Sub|Function|If)\b", 2, ignoreCase: true),
        }),
        new("TeX", new[]
        {
            WeightedMarker(@"\\documentclass(\[[^\]]*\])?\{[^}]+\}", 4),
            WeightedMarker(@"\\begin\{document\}", 4),
            WeightedMarker(@"\\usepackage(\[[^\]]*\])?\{[^}]+\}", 3),
            WeightedMarker(@"\\(chapter|section|subsection|begin|end)\{[^}]+\}", 2),
        }),
        new("PHP", new[]
        {
            WeightedMarker(@"\b(public|private|protected)\s+function\s+[A-Za-z_]\w*\s*\(", 3, ignoreCase: true),
            WeightedMarker(@"\bfunction\s+[A-Za-z_]\w*\s*\([^)]*\$[A-Za-z_]\w*", 4, ignoreCase: true),
            WeightedMarker(@"\$this->[A-Za-z_]\w*", 4),
            WeightedMarker(@"(?:^|\n)\s*(echo|print)\s+['""]", 2, ignoreCase: true),
            WeightedMarker(@"\bnamespace\s+[A-Za-z_\\][\w\\]*\s*;", 3, ignoreCase: true),
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
    public static string? DetectBuiltIn(string content) =>
        Detect(content, name => HighlightingManager.Instance.GetDefinition(name) is not null);

    /// <summary>
    /// Classifies content using the caller's grammar registry, without loading definitions or
    /// touching UI state. Standard SQL prefers SQL, with TSQL retained for dialect-specific syntax
    /// or callers that only have the built-in TSQL definition.
    /// </summary>
    public static string? Detect(string content, Func<string, bool> isGrammarAvailable)
    {
        ArgumentNullException.ThrowIfNull(isGrammarAvailable);
        string? Available(string name) => isGrammarAvailable(name) ? name : null;

        content ??= string.Empty;
        var sample = content.Length > MaxScanLength
            ? content.Substring(0, MaxScanLength)
            : content;
        if (string.IsNullOrWhiteSpace(sample))
            return null;

        // Unmistakable document signatures, in precedence order.
        if (IsJsonDocument(sample))
            return Available("Json");
        if (IsMatch(PatchSignature, sample))
            return Available("Patch");
        if (IsMatch(PhpSignature, sample))
            return Available("PHP");
        if (IsMatch(AspXhtmlSignature, sample))
            return Available("ASP/XHTML");
        if (IsXamlRoot(sample))
            return Available("XAML") ?? Available("XML");
        if (IsMatch(HtmlSignature, sample))
            return Available("HTML");
        if (IsMatch(XmlDeclarationSignature, sample) || IsXmlDocument(sample))
            return Available("XML");
        if (IsMatch(BashSignature, sample))
            return Available("Bash");
        var hasDotNetStackFrame = IsMatch(DotNetStackFrameLine, sample);
        // Frame callables and arguments are data, not independent source-language signals.
        var scoringSample = hasDotNetStackFrame
            ? DotNetStackFrameLine.Replace(sample, "\n")
            : sample;
        var errorCandidate = hasDotNetStackFrame
            ? Available(ErrorMessageGrammarName)
            : null;
        var sqlGrammar = IsMatch(TsqlSpecificMarker, scoringSample) || !isGrammarAvailable("SQL") ? "TSQL" : "SQL";
        // Rust imports share std:: with C++; remove only that import prefix from C++ scoring.
        // Independent C++ markers elsewhere must still make a mixed sample ambiguous.
        var cppSample = RustStandardLibraryImport.Replace(scoringSample, "\n");
        var scores = ScoredLanguages
            .Where(language => language.Name != "YAML" ||
                (!scoringSample.Contains('{') && !scoringSample.Contains('}') && IsMatch(YamlBlockRoot, scoringSample)))
            .Select(language => new Candidate(
                language.Name == "SQL" ? sqlGrammar : language.Name,
                language.Score(language.Name == "C++" ? cppSample : scoringSample)))
            .ToArray();
        // Typed declarations can also match CSS blocks and ordinary JavaScript expressions.
        // Collapse those shared signals before enforcing ambiguity across unrelated languages.
        var hasTypeScript = scores.Any(candidate => candidate.Name == "TypeScript" && candidate.Score >= MinimumScore);
        // Scored markers: require a clear winner to stay conservative on ambiguity.
        var candidates = scores
            .Where(candidate => !hasTypeScript || candidate.Name is not ("CSS" or "JavaScript"))
            .Where(candidate => candidate.Score >= MinimumScore && isGrammarAvailable(candidate.Name))
            .Concat(errorCandidate is null ? Array.Empty<Candidate>() : new[] { new Candidate(errorCandidate, MinimumScore) })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
            return null;
        if (candidates.Length > 1)
            return null;

        return candidates[0].Name;
    }

    private static bool IsMatch(Regex regex, string input) => regex.IsMatch(input);

    private static Regex Make(string pattern, bool ignoreCase = false) =>
        new(pattern, SharedOptions | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));

    private static WeightedLanguageMarker WeightedMarker(string pattern, int weight, bool ignoreCase = false) =>
        new(Make(pattern, ignoreCase), weight);

    private static bool IsJsonDocument(string sample)
    {
        var trimmed = sample.Trim();
        if (trimmed.Length < 2 ||
            !((trimmed[0] == '{' && trimmed[^1] == '}') ||
              (trimmed[0] == '[' && trimmed[^1] == ']')))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(trimmed, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsXmlDocument(string sample)
    {
        var trimmed = sample.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '<' || trimmed[^1] != '>')
            return false;
        if (trimmed.StartsWith("<!", StringComparison.Ordinal) ||
            trimmed.StartsWith("<?", StringComparison.Ordinal))
            return false;

        try
        {
            using var reader = XmlReader.Create(new StringReader(trimmed), XmlReaderSettings);
            var elementCount = 0;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                    elementCount++;
            }
            return elementCount > 0;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsXamlRoot(string sample)
    {
        // Inspect only the opening element, not XAML examples in prose, comments or attribute values.
        // A complete document is not required while a user is editing a fragment.
        var root = MarkupRoot.Match(sample);
        if (!root.Success)
            return false;

        var name = root.Groups["name"].Value;
        var attributes = MarkupAttribute.Matches(root.Groups["attributes"].Value);
        string? AttributeValue(string attribute) => attributes
            .FirstOrDefault(match => match.Groups["name"].Value == attribute)?.Groups["value"].Value;
        var separator = name.IndexOf(':');
        var namespaceAttribute = separator < 0 ? "xmlns" : "xmlns:" + name[..separator];
        var rootNamespace = AttributeValue(namespaceAttribute);
        if (rootNamespace is "http://schemas.microsoft.com/winfx/2006/xaml/presentation" or
            "http://schemas.microsoft.com/winfx/2006/xaml" or "http://schemas.microsoft.com/winfx/2009/xaml")
            return true;
        var xNamespace = AttributeValue("xmlns:x");
        if ((xNamespace is null or "http://schemas.microsoft.com/winfx/2006/xaml" or
            "http://schemas.microsoft.com/winfx/2009/xaml") &&
            (string.IsNullOrEmpty(rootNamespace) || rootNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) &&
            attributes.Any(attribute => attribute.Groups["name"].Value is "x:Class" or "x:Name" or "x:Key"))
            return true;
        if (!string.IsNullOrEmpty(rootNamespace))
            return false;

        return name is "Window" or "UserControl" or "ResourceDictionary" or "Application" or "Page" or
            "Grid" or "StackPanel" or "DockPanel" or "WrapPanel" or "TextBlock" or "TextBox" or
            "DataTemplate" or "ControlTemplate";
    }

    private readonly record struct WeightedLanguageMarker(Regex Regex, int Weight);

    private readonly record struct Candidate(string Name, int Score);

    private sealed class LanguageMarkers
    {
        public LanguageMarkers(string name, WeightedLanguageMarker[] markers)
        {
            Name = name;
            _markers = markers;
        }

        public string Name { get; }

        private readonly WeightedLanguageMarker[] _markers;

        public int Score(string sample)
        {
            var score = 0;
            foreach (var marker in _markers)
            {
                if (IsMatch(marker.Regex, sample))
                    score += marker.Weight;
            }
            return score;
        }
    }
}
