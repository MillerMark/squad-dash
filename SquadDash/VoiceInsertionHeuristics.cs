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
    /// <param name="rightContext">
    ///   The text that exists to the RIGHT of the insertion caret
    ///   (i.e. <c>promptText[caretIndex..]</c>).  Used to detect whether the
    ///   insertion point is mid-sentence (so trailing periods should be
    ///   stripped).  Pass <c>""</c> or omit when appending at the end.
    /// </param>
    public static string Apply(string leftContext, string incomingText, string rightContext = "")
    {
        if (string.IsNullOrEmpty(incomingText)) return incomingText;

        var result = incomingText;

        // 1. Mid-sentence continuation (left side): lowercase first word unless
        //    it is a special-case token (acronym, CamelCase, pronoun "I").
        if (IsSentenceContinuation(leftContext))
            result = LowercaseFirstWordIfNotSpecial(result);

        // 2. Mid-sentence insertion (right side): strip any trailing period when
        //    the character immediately to the right of the caret is a lowercase
        //    letter or a closing parenthesis — inserting a sentence-ending period
        //    mid-sentence produces broken punctuation.
        if (IsRightContextMidSentence(rightContext))
            result = StripTrailingPeriods(result);

        // 2b. Right context starts with punctuation (.  ,  ;  !  ?  :): the inserted
        //     text's trailing sentence-ending punctuation would double up, so strip it.
        //     Example: caret before ", which" — inserting "hello." → "hello".
        if (IsRightContextStartsWithPunctuation(rightContext))
            result = StripTrailingSentencePunctuation(result);

        // 3. Conservative trailing-punctuation corrections (e.g. "this." → "this:").
        result = ApplyTrailingPunctuationFixes(result);

        // 4. Right context starts with any non-whitespace character other than ')':
        //    append a space so the inserted text doesn't run into whatever follows.
        //    The ')' exception lets voice fill the inside of "()" without leaving a
        //    trailing space just before the close paren.
        //    Examples:
        //      caret before "years"  → "score and twenty " → "four score and twenty years ago"
        //      caret before "(note)" → "see " → "see (note)"
        //      caret before ")"      → "done"  → "(done)"  — no trailing space
        if (IsRightContextRequiresTrailingSpace(rightContext) && !result.EndsWith(' '))
            result += ' ';

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
    ///   <item>Close double-quote <c>"</c> (straight or curly) preceded by a
    ///         lowercase letter — e.g. <c>word" </c> indicates we are still inside
    ///         the surrounding sentence.</item>
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

            // Close-quote after lowercase: e.g. `word" ` — we're still inside a
            // surrounding sentence, so the incoming voice text should continue lowercase.
            if ((ch == '"' || ch == '\u201D') && i > 0 && char.IsLower(leftContext[i - 1]))
                return true;

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
    ///   <item>It is a first-person contraction starting with <c>I'</c>
    ///         (e.g. <c>"I'm"</c>, <c>"I've"</c>, <c>"I'll"</c>, <c>"I'd"</c>) —
    ///         the pronoun must stay capitalised even mid-sentence.</item>
    ///   <item>It contains two or more uppercase letters — indicating an acronym
    ///         (<c>API</c>, <c>URL</c>) or a CamelCase identifier
    ///         (<c>JavaScript</c>, <c>iPhone</c>).</item>
    /// </list>
    /// </summary>
    internal static bool IsSpecialCaseWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        if (PreservedWords.Contains(word)) return true;

        // First-person contractions: I'm, I've, I'll, I'd, I'd, etc.
        if (word.Length >= 3 && word[0] == 'I' && word[1] == '\'')
            return true;

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
            return text[..stemLen] + "this: ";

        return text;
    }

    /// <summary>
    /// Returns <c>true</c> when the first non-whitespace character of
    /// <paramref name="rightContext"/> indicates the caret is mid-sentence —
    /// specifically a lowercase letter or a closing parenthesis <c>)</c>.
    /// When true, the caller should strip trailing periods from the inserted text.
    /// </summary>
    internal static bool IsRightContextMidSentence(string rightContext)
    {
        if (string.IsNullOrEmpty(rightContext)) return false;

        foreach (var ch in rightContext)
        {
            if (ch == ' ' || ch == '\t') continue;
            return char.IsLower(ch) || ch == ')';
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the character immediately to the right of the
    /// caret (i.e. <c>rightContext[0]</c>) is a letter — meaning the inserted
    /// text will run directly into the next word unless a trailing space is added.
    /// </summary>
    internal static bool IsRightContextStartsWithLetter(string rightContext) =>
        rightContext.Length > 0 && char.IsLetter(rightContext[0]);

    /// <summary>
    /// Returns <c>true</c> when the character immediately to the right of the
    /// caret is any non-whitespace character other than <c>')'</c> or a
    /// punctuation mark that naturally hugs the preceding word
    /// (<c>. , ; ! ? :</c>).
    /// When true, a trailing space should be appended to the inserted text so
    /// it doesn't run into the next token.
    /// The <c>')'</c> exception prevents a spurious space inside empty parens
    /// like <c>"(|)"</c> → <c>"(done)"</c> rather than <c>"(done )"</c>.
    /// The punctuation exception prevents <c>"word ,rest"</c> — punctuation
    /// attaches to the word on its left, not the word on its right.
    /// </summary>
    internal static bool IsRightContextRequiresTrailingSpace(string rightContext) =>
        rightContext.Length > 0
        && rightContext[0] != ')'
        && ".,;!?:".IndexOf(rightContext[0]) < 0
        && !char.IsWhiteSpace(rightContext[0]);

    /// <summary>
    /// Returns <c>true</c> when the first character of <paramref name="rightContext"/>
    /// is a punctuation mark that would cause the inserted text's trailing punctuation
    /// to double up: <c>. , ; ! ? :</c>.
    /// </summary>
    internal static bool IsRightContextStartsWithPunctuation(string rightContext) =>
        rightContext.Length > 0 && ".,;!?:".IndexOf(rightContext[0]) >= 0;

    /// <summary>
    /// Removes one trailing sentence-ending punctuation character (<c>.</c>, <c>!</c>,
    /// or <c>?</c>) from <paramref name="text"/>.  Used when the right context already
    /// opens with punctuation, making the inserted punctuation redundant.
    /// </summary>
    internal static string StripTrailingSentencePunctuation(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var last = text[^1];
        return last == '.' || last == '!' || last == '?' ? text[..^1] : text;
    }

    /// <summary>
    /// Removes one trailing period from <paramref name="text"/>.
    /// Used when the caret is mid-sentence (right context signals continuation).
    /// </summary>
    internal static string StripTrailingPeriods(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.EndsWith('.') ? text[..^1] : text;
    }



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
        // Trim trailing whitespace (speech recognizers sometimes append a space).
        var trimmed = text.TrimEnd();

        // Strip optional trailing period.
        var core = trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;

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
