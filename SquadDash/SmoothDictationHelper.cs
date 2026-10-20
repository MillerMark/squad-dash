namespace SquadDash;

using System;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;

/// <summary>
/// Smooth Dictation — removes sentence-break periods inserted by voice dictation
/// within a text selection or across the full text, and collapses back-to-back
/// duplicate words (e.g. "the the" → "the", "The the" → "The").
///
/// Rule 1: find every occurrence of ". [uppercase]" and replace it with " [lowercase]",
/// except when the uppercase letter is the pronoun "I" (i.e. "I" followed by a
/// non-letter character such as a space, apostrophe, comma, end of string, etc.).
///
/// Rule 2: find consecutive duplicate words separated only by whitespace (case-insensitive)
/// and keep only the first occurrence, preserving its original casing.
///
/// Trigger: Shift+Space when text is selected (keyboard); also available via right-click.
/// </summary>
internal static class SmoothDictationHelper
{
    // Matches a period + one-or-more whitespace chars + one uppercase letter.
    private static readonly Regex _sentenceBreak =
        new(@"\.\s+([A-Z])", RegexOptions.Compiled);

    // Matches a word (letters/digits/apostrophes) followed by whitespace and the same
    // word again (case-insensitive).  Group 1 = first word (keep); the whole match is
    // replaced by group 1 so the surrounding whitespace is trimmed to a single space.
    private static readonly Regex _duplicateWord =
        new(@"\b(\w[\w']*)\s+\1\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Applies smooth-dictation cleanup to <paramref name="input"/>.
    /// Returns the transformed string (or the original if no changes were made).
    /// </summary>
    public static string Apply(string input)
    {
        // Pass 1 — sentence-break smoothing (removes dictation periods, lowercases next word).
        string result = _sentenceBreak.Replace(input, m =>
        {
            char upper    = m.Groups[1].Value[0];
            int  afterPos = m.Index + m.Length;

            // Preserve the pronoun "I" — identified as a lone 'I' not followed by another letter.
            bool isPronounI = upper == 'I'
                && (afterPos >= input.Length || !char.IsLetter(input[afterPos]));

            if (isPronounI)
                return m.Value; // leave untouched

            // Drop the period; keep the whitespace; lowercase the first letter.
            // m.Value is e.g. ". Is" → whitespace is everything between '.' and the captured letter.
            string ws = m.Value[1..^1]; // skip leading '.' and trailing uppercase letter
            return ws + char.ToLowerInvariant(upper);
        });

        // Pass 2 — collapse duplicate words (runs last so sentence-break removal can't create
        // new duplicates that would be missed; loops in case of triple+ repetition).
        string prev;
        do
        {
            prev   = result;
            result = _duplicateWord.Replace(result, m => m.Groups[1].Value);
        } while (!string.Equals(result, prev, StringComparison.Ordinal));

        return result;
    }

    /// <summary>
    /// Applies smooth-dictation to the selected text in <paramref name="tb"/>.
    /// If nothing is selected, applies to the entire text.
    /// Returns true if any change was made.
    /// </summary>
    public static bool ApplyToTextBox(TextBox tb)
    {
        bool hasSelection = tb.SelectionLength > 0;
        string input  = hasSelection ? tb.SelectedText : tb.Text;
        string output = Apply(input);

        if (string.Equals(input, output, StringComparison.Ordinal))
            return false;

        int savedCaret = tb.CaretIndex;

        if (hasSelection)
        {
            int start  = tb.SelectionStart;
            tb.SelectedText = output;
            tb.Select(start, output.Length);
        }
        else
        {
            tb.Text = output;
            tb.CaretIndex = Math.Min(savedCaret, output.Length);
        }

        return true;
    }

    /// <summary>
    /// Applies smooth-dictation to the selected text in <paramref name="rtb"/> (RichTextBox).
    /// If nothing is selected, applies to the entire plain-text content.
    /// Returns true if any change was made.
    /// </summary>
    public static bool ApplyToRichTextBox(System.Windows.Controls.RichTextBox rtb)
    {
        bool hasSelection = rtb.GetSelectionLength() > 0;
        string input  = hasSelection ? rtb.GetSelectedText() : rtb.GetPlainText();
        string output = Apply(input);

        if (string.Equals(input, output, StringComparison.Ordinal))
            return false;

        if (hasSelection)
        {
            int start = rtb.GetSelectionStart();
            rtb.ReplaceSelection(output);
            rtb.SelectRange(start, output.Length);
        }
        else
        {
            rtb.SetPlainText(output);
        }

        return true;
    }

    /// <summary>
    /// Applies smooth-dictation to the selected text in <paramref name="editor"/>.
    /// If nothing is selected, applies to the entire text.
    /// Returns true if any change was made.
    /// </summary>
    public static bool ApplyToAvalonEdit(TextEditor editor)
    {
        if (editor.IsReadOnly)
            return false;
        var hasSelection = editor.SelectionLength > 0;
        var start = hasSelection ? editor.SelectionStart : 0;
        var length = hasSelection ? editor.SelectionLength : editor.Document.TextLength;
        var input = editor.Document.GetText(start, length);
        var output = Apply(input.ReplaceLineEndings("\n"));
        if (input == output)
            return false;
        var caret = editor.CaretOffset;
        using (editor.Document.RunUpdate())
            editor.Document.Replace(start, length, output);
        if (hasSelection)
            editor.Select(start, output.Length);
        else
            editor.Select(Math.Min(caret, output.Length), 0);
        return true;
    }
}
