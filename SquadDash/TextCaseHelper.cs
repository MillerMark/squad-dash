using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Detects and cycles text through Title Case → PascalCase → Sentence case → UPPERCASE → kebab-case → preserve_underscores.
/// </summary>
internal static class TextCaseHelper
{
    internal enum TextCase { None, TitleCase, SentenceCase, UpperCase, PascalCase, KebabCase, UnderscoreCase }

    /// <summary>
    /// Detects which case the text matches, or <see cref="TextCase.None"/> if it matches none.
    /// </summary>
    internal static TextCase DetectCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return TextCase.None;

        // UPPERCASE: every letter is uppercase, at least one letter present (checked before PascalCase)
        if (text.Any(char.IsLetter) && text.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            return TextCase.UpperCase;

        // KebabCase: no whitespace, contains '-', all non-dash chars are lowercase
        if (!text.Any(char.IsWhiteSpace) && text.Contains('-')
            && text.All(c => c == '-' || !char.IsLetter(c) || char.IsLower(c)))
            return TextCase.KebabCase;

        // UnderscoreCase: no whitespace, contains '_', not also KebabCase
        if (!text.Any(char.IsWhiteSpace) && text.Contains('_'))
            return TextCase.UnderscoreCase;

        // PascalCase: no spaces, first char uppercase, at least one more uppercase after the first char
        if (!text.Contains(' ') && text.Length > 1 && char.IsUpper(text[0]) && text[1..].Any(char.IsUpper))
            return TextCase.PascalCase;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return TextCase.None;

        // Title Case: every word's first letter is uppercase, all other letters lowercase
        if (words.All(w => {
            var firstLetter = w.FirstOrDefault(char.IsLetter);
            if (firstLetter == default(char)) return true;   // no letters in this word — OK
            return char.IsUpper(firstLetter)
                && w.SkipWhile(c => !char.IsLetter(c))  // skip leading non-letters
                     .Skip(1)                            // skip the first letter itself
                     .All(c => !char.IsLetter(c) || char.IsLower(c));
        }))
            return TextCase.TitleCase;

        // Sentence case: first word starts with uppercase letter (rest lowercase letters), all other words fully lowercase
        var firstWordFirstLetter = words[0].FirstOrDefault(char.IsLetter);
        bool firstOk = firstWordFirstLetter != default(char)
                       && char.IsUpper(firstWordFirstLetter)
                       && words[0].SkipWhile(c => !char.IsLetter(c))
                                   .Skip(1)
                                   .All(c => !char.IsLetter(c) || char.IsLower(c));
        bool restLower = words.Skip(1).All(w => w.All(c => !char.IsLetter(c) || char.IsLower(c)));
        if (firstOk && restLower) return TextCase.SentenceCase;

        return TextCase.None;
    }

    private static readonly HashSet<string> _minorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "but", "or", "nor", "for", "so", "yet",
        "as", "at", "by", "in", "of", "on", "to", "up", "via", "vs"
    };

    /// <summary>
    /// AP/Chicago-style title case: first and last words always capitalised; interior minor words
    /// (a, an, the, and, but, or, nor, for, so, yet, as, at, by, in, of, on, to, up, via, vs)
    /// are left lowercase.  Inter-word separators (spaces, hyphens, underscores) are preserved.
    /// </summary>
    internal static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Split on word-separator runs, capturing the separators so we can reconstruct them.
        var tokens = Regex.Split(text, @"([\s\-_]+)");

        bool IsSep(string t) => t.Length == 0 || Regex.IsMatch(t, @"^[\s\-_]+$");

        var wordIndices = Enumerable.Range(0, tokens.Length)
            .Where(i => !IsSep(tokens[i]))
            .ToList();

        if (wordIndices.Count == 0) return text;

        int firstIdx = wordIndices[0];
        int lastIdx  = wordIndices[^1];

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (IsSep(token)) { sb.Append(token); continue; }

            bool isFirst = i == firstIdx;
            bool isLast  = i == lastIdx;
            string letters = new string(token.Where(char.IsLetter).ToArray()).ToLower();

            if (isFirst || isLast || !_minorWords.Contains(letters))
                sb.Append(CapitalizeWord(token));
            else
                sb.Append(token.ToLower());
        }
        return sb.ToString();
    }

    private static string CapitalizeWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        var sb = new StringBuilder(word.Length);
        bool capitalizeNext = true;
        foreach (char c in word)
        {
            if (char.IsLetter(c))
            {
                sb.Append(capitalizeNext ? char.ToUpper(c) : char.ToLower(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>First letter of first word uppercased; everything else lowercased.</summary>
    internal static string ToSentenceCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        bool firstLetter = true;
        foreach (char c in text)
        {
            if (firstLetter && char.IsLetter(c))
            {
                sb.Append(char.ToUpper(c));
                firstLetter = false;
            }
            else
            {
                sb.Append(char.ToLower(c));
            }
        }
        return sb.ToString();
    }

    /// <summary>All letters uppercased.</summary>
    internal static string ToUpperCase(string text) => text.ToUpper();

    /// <summary>
    /// Split on spaces/underscores/hyphens; every word title-capped; joined with no separator.
    /// E.g. "hello world" → "HelloWorld".
    /// </summary>
    internal static string ToPascalCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var words = Regex.Split(text, @"[\s_\-]+")
                         .Where(w => w.Length > 0)
                         .ToArray();
        if (words.Length == 0) return text;
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            sb.Append(char.ToUpper(word[0]));
            if (word.Length > 1)
                sb.Append(word[1..].ToLower());
        }
        return sb.ToString();
    }

    /// <summary>
    /// All letters lowercased; spaces and underscores replaced by a single dash.
    /// E.g. "Hello World" → "hello-world".
    /// </summary>
    internal static string ToKebabCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var normalized = Regex.Replace(text, @"[\s_]+", "-");
        return normalized.ToLower();
    }

    /// <summary>
    /// Spaces replaced with underscores; letter case preserved exactly.
    /// E.g. "Hello World" → "Hello_World".
    /// </summary>
    internal static string ToUnderscorePreserveCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"\s+", "_");
    }

    /// <summary>
    /// Returns the case variants in dynamic cycle order starting from the next case to apply.
    /// The canonical order is: Title Case, PascalCase, Sentence case, UPPERCASE, kebab-case, underscore_case.
    /// If the input matches one of these, that case is moved to the end so the user cycles through
    /// all 5 other cases before returning to the original. Always returns exactly 6 items.
    /// </summary>
    internal static List<string> ComputeOrderedVariants(string text)
    {
        var canonical = new List<string>
        {
            ToTitleCase(text),
            ToPascalCase(text),
            ToSentenceCase(text),
            ToUpperCase(text),
            ToKebabCase(text),
            ToUnderscorePreserveCase(text)
        };

        int detectedIndex = DetectCase(text) switch
        {
            TextCase.TitleCase      => 0,
            TextCase.PascalCase     => 1,
            TextCase.SentenceCase   => 2,
            TextCase.UpperCase      => 3,
            TextCase.KebabCase      => 4,
            TextCase.UnderscoreCase => 5,
            _                       => -1,
        };

        if (detectedIndex >= 0)
        {
            var moved = canonical[detectedIndex];
            canonical.RemoveAt(detectedIndex);
            canonical.Add(moved);
        }

        return canonical;
    }

    /// <summary>
    /// Deprecated alias for <see cref="ComputeOrderedVariants"/>. Use that method directly.
    /// </summary>
    internal static List<string> ComputeVariants(string text) => ComputeOrderedVariants(text);

    /// <summary>
    /// Always returns 0. <see cref="ComputeOrderedVariants"/> already places the correct
    /// first-press variant at index 0, so callers should use <c>_cycleIndex = 0</c> directly.
    /// Kept for source compatibility.
    /// </summary>
    internal static int GetFirstVariantIndex(string text) => 0;

    /// <summary>
    /// Detects the current case and returns the text transformed to the next case in the cycle:
    /// Title Case → PascalCase → Sentence case → UPPERCASE → kebab-case → preserve_underscores → (back to) Title Case.
    /// If the text matches no known case, starts from Title Case.
    /// </summary>
    internal static string CycleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return DetectCase(text) switch
        {
            TextCase.TitleCase      => ToPascalCase(text),
            TextCase.PascalCase     => ToSentenceCase(text),
            TextCase.SentenceCase   => ToUpperCase(text),
            TextCase.UpperCase      => ToKebabCase(text),
            TextCase.KebabCase      => ToUnderscorePreserveCase(text),
            TextCase.UnderscoreCase => ToTitleCase(text),
            _                       => ToTitleCase(text),
        };
    }
}
