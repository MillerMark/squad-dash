using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Detects and cycles text through Title Case → Sentence case → UPPERCASE → camelCase.
/// </summary>
internal static class TextCaseHelper
{
    internal enum TextCase { None, TitleCase, SentenceCase, UpperCase, CamelCase }

    /// <summary>
    /// Detects which case the text matches, or <see cref="TextCase.None"/> if it matches none.
    /// </summary>
    internal static TextCase DetectCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return TextCase.None;

        // camelCase: no spaces, first char lowercase, at least one uppercase letter present
        if (!text.Contains(' ') && char.IsLower(text[0]) && text.Any(char.IsUpper))
            return TextCase.CamelCase;

        // UPPERCASE: every letter is uppercase, at least one letter present
        if (text.Any(char.IsLetter) && text.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            return TextCase.UpperCase;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return TextCase.None;

        // Title Case: every word starts with uppercase, rest of letters lowercase
        if (words.All(w => w.Length > 0 && char.IsUpper(w[0])
                           && w.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c))))
            return TextCase.TitleCase;

        // Sentence case: first word starts uppercase (rest lowercase), all other words fully lowercase
        bool firstOk = words[0].Length > 0
                       && char.IsUpper(words[0][0])
                       && words[0].Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c));
        bool restLower = words.Skip(1).All(w => w.All(c => !char.IsLetter(c) || char.IsLower(c)));
        if (firstOk && restLower) return TextCase.SentenceCase;

        return TextCase.None;
    }

    /// <summary>Every word's first letter uppercased, remaining letters lowercased (split on spaces).</summary>
    internal static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        bool capitalizeNext = true;
        foreach (char c in text)
        {
            if (c == ' ')
            {
                capitalizeNext = true;
                sb.Append(c);
            }
            else if (capitalizeNext && char.IsLetter(c))
            {
                sb.Append(char.ToUpper(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(char.ToLower(c));
                capitalizeNext = false;
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

    /// <summary>Split on spaces/underscores/hyphens; first word all-lowercase, subsequent words title-capped; joined with no separator.</summary>
    internal static string ToCamelCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var words = Regex.Split(text, @"[\s_\-]+")
                         .Where(w => w.Length > 0)
                         .ToArray();
        if (words.Length == 0) return text;
        var sb = new StringBuilder();
        sb.Append(words[0].ToLower());
        for (int i = 1; i < words.Length; i++)
        {
            sb.Append(char.ToUpper(words[i][0]));
            if (words[i].Length > 1)
                sb.Append(words[i][1..].ToLower());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Detects the current case and returns the text transformed to the next case in the cycle:
    /// Title Case → Sentence case → UPPERCASE → camelCase → (back to) Title Case.
    /// If the text matches no known case, starts from Title Case.
    /// </summary>
    internal static string CycleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return DetectCase(text) switch
        {
            TextCase.TitleCase    => ToSentenceCase(text),
            TextCase.SentenceCase => ToUpperCase(text),
            TextCase.UpperCase    => ToCamelCase(text),
            TextCase.CamelCase    => ToTitleCase(text),
            _                     => ToTitleCase(text),
        };
    }
}
