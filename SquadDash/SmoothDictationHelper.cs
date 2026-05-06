namespace SquadDash;

using System;
using System.Text.RegularExpressions;
using System.Windows.Controls;

/// <summary>
/// Smooth Dictation — removes sentence-break periods inserted by voice dictation
/// within a text selection or across the full text.
///
/// Rule: find every occurrence of ". [uppercase]" and replace it with " [lowercase]",
/// except when the uppercase letter is the pronoun "I" (i.e. "I" followed by a
/// non-letter character such as a space, apostrophe, comma, end of string, etc.).
///
/// Trigger: Shift+Space when text is selected (keyboard); also available via right-click.
/// </summary>
internal static class SmoothDictationHelper
{
    // Matches a period + one-or-more whitespace chars + one uppercase letter.
    private static readonly Regex _sentenceBreak =
        new(@"\.\s+([A-Z])", RegexOptions.Compiled);

    /// <summary>
    /// Applies smooth-dictation cleanup to <paramref name="input"/>.
    /// Returns the transformed string (or the original if no changes were made).
    /// </summary>
    public static string Apply(string input)
    {
        return _sentenceBreak.Replace(input, m =>
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
}
