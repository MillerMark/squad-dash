using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Adjusts voice-recognized text before it is inserted at the cursor so that
/// it reads naturally in context.
///
/// This class is the single, growing home for all voice-insertion intelligence.
/// All non-private methods are <c>internal</c> so the NUnit test suite can
/// exercise each heuristic directly without needing integration-level fixtures.
///
/// Add new heuristics here — do NOT spread voice-adjustment logic across callers.
/// </summary>
internal static class VoiceInsertionHeuristics
{
    // Words that must never be lowercased regardless of context.
    // "I" (first-person pronoun) is the canonical example: it has only one
    // uppercase letter but must stay capitalised.
    private static readonly HashSet<string> PreservedWords =
        new(StringComparer.Ordinal) { "I" };

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Applies all active heuristics and returns the adjusted text to insert.
    /// </summary>
    /// <param name="leftContext">
    ///   The text that already exists to the LEFT of the insertion caret
    ///   (i.e. <c>promptText[..caretIndex]</c>).  Used to determine whether
    ///   the incoming phrase is continuing a sentence mid-stream.
    /// </param>
    /// <param name="incomingText">
    ///   The raw text returned by the speech recogniser.
    /// </param>
    public static string Apply(string leftContext, string incomingText)
    {
        if (string.IsNullOrEmpty(incomingText)) return incomingText;

        var result = incomingText;

        // 1. Mid-sentence continuation: lowercase first word unless it is a
        //    special-case token (acronym, CamelCase, pronoun "I").
        if (IsSentenceContinuation(leftContext))
            result = LowercaseFirstWordIfNotSpecial(result);

        // 2. Conservative trailing-punctuation corrections.
        result = ApplyTrailingPunctuationFixes(result);

        return result;
    }

    // ── Heuristics (internal for unit testability) ────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="leftContext"/> ends in a way
    /// that suggests the incoming voice text is continuing an existing sentence
    /// rather than starting a new one.
    ///
    /// <para>Continuation signals (last non-whitespace character):</para>
    /// <list type="bullet">
    ///   <item>Lowercase letter a–z</item>
    ///   <item>Comma <c>,</c></item>
    ///   <item>Open parenthesis <c>(</c></item>
    ///   <item>Semicolon <c>;</c></item>
    ///   <item>Hyphen or en/em dash <c>-</c> <c>–</c> <c>—</c></item>
    /// </list>
    ///
    /// <para>NOT continuation (sentence-ending or ambiguous):</para>
    /// <list type="bullet">
    ///   <item>Period, exclamation mark, question mark, colon</item>
    ///   <item>Uppercase letter (already at a sentence boundary)</item>
    ///   <item>Digit</item>
    ///   <item>Empty / whitespace-only context</item>
    /// </list>
    /// </summary>
    internal static bool IsSentenceContinuation(string leftContext)
    {
        if (string.IsNullOrEmpty(leftContext)) return false;

        // Walk backwards to skip trailing whitespace.
        for (var i = leftContext.Length - 1; i >= 0; i--)
        {
            var ch = leftContext[i];
            if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n') continue;

            return char.IsLower(ch)
                   || ch == ','
                   || ch == '('
                   || ch == ';'
                   || ch == '-'
                   || ch == '\u2013'  // en dash
                   || ch == '\u2014'; // em dash
        }

        return false; // empty or all-whitespace context
    }

    /// <summary>
    /// Lowercases the first character of <paramref name="text"/> unless the
    /// first word qualifies as a <em>special-case</em> token (see
    /// <see cref="IsSpecialCaseWord"/>).
    /// </summary>
    internal static string LowercaseFirstWordIfNotSpecial(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var spaceIdx  = text.IndexOf(' ');
        var firstWord = spaceIdx < 0 ? text : text[..spaceIdx];

        if (IsSpecialCaseWord(firstWord)) return text;

        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="word"/> must NOT be lowercased
    /// during sentence continuation.
    ///
    /// A word is special if:
    /// <list type="bullet">
    ///   <item>It is in the <see cref="PreservedWords"/> set (e.g. <c>"I"</c>).</item>
    ///   <item>It contains two or more uppercase letters — indicating an acronym
    ///         (<c>API</c>, <c>URL</c>) or a CamelCase identifier
    ///         (<c>JavaScript</c>, <c>iPhone</c>).</item>
    /// </list>
    /// </summary>
    internal static bool IsSpecialCaseWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        if (PreservedWords.Contains(word)) return true;

        var upperCount = 0;
        foreach (var ch in word)
            if (char.IsUpper(ch) && ++upperCount >= 2)
                return true;

        return false;
    }

    /// <summary>
    /// Applies conservative trailing-punctuation corrections.  Only rules
    /// whose precision is extremely high are included here; prefer omitting a
    /// borderline rule entirely over introducing frequent false positives.
    ///
    /// <para>Current rules:</para>
    /// <list type="bullet">
    ///   <item>
    ///     Ends with the word <c>"this"</c> (optionally followed by a period) →
    ///     replace / append a colon.
    ///     Example: <c>"it looks like this."</c> → <c>"it looks like this:"</c>
    ///   </item>
    /// </list>
    /// </summary>
    internal static string ApplyTrailingPunctuationFixes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (TryMatchTrailingWord(text, "this", out var stemLen))
            return text[..stemLen] + "this:";

        return text;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether <paramref name="text"/> ends with <paramref name="targetWord"/>
    /// (case-insensitive) optionally followed by a single period, and that the
    /// match is at a word boundary.
    ///
    /// If matched, <paramref name="stemLength"/> is the index at which
    /// <paramref name="targetWord"/> begins in <paramref name="text"/> so the
    /// caller can reconstruct the corrected string.
    /// </summary>
    private static bool TryMatchTrailingWord(string text, string targetWord, out int stemLength)
    {
        // Strip optional trailing period.
        var core = text.EndsWith('.') ? text[..^1] : text;

        if (core.EndsWith(targetWord, StringComparison.OrdinalIgnoreCase))
        {
            var wordStart = core.Length - targetWord.Length;
            // Confirm word boundary: start of string, or preceded by non-letter.
            if (wordStart == 0 || !char.IsLetter(core[wordStart - 1]))
            {
                stemLength = wordStart;
                return true;
            }
        }

        stemLength = 0;
        return false;
    }
}
