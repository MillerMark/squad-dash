using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Extension methods for <see cref="RichTextBox"/> providing plain-text access compatible
/// with the integer-offset API previously offered by <see cref="System.Windows.Controls.TextBox"/>.
/// <para>
/// All offsets are in terms of the plain-text string returned by <see cref="GetPlainText"/>.
/// InlineUIContainer nodes (e.g. the Phase-3 pending-revision indicator) are excluded from
/// both character counting and text retrieval.
/// </para>
/// </summary>
internal static class RichTextBoxExtensions
{
    // ── Plain-text access ────────────────────────────────────────────────────

    /// Returns the editor content as a plain-text string with '\n' line endings.
    /// InlineUIContainer and other non-Run inlines are intentionally excluded so they
    /// never appear in the text written to disk.
    public static string GetPlainText(this RichTextBox rtb)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var block in rtb.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            if (!first) sb.Append('\n');
            first = false;
            foreach (var inline in para.Inlines)
            {
                if (inline is Run run)
                    sb.Append(run.Text);
                else if (inline is LineBreak)
                    sb.Append('\n'); // Shift+Enter produces a LineBreak within a paragraph
                // InlineUIContainer (e.g. RevisionPendingIndicator) intentionally skipped
            }
        }
        return sb.ToString();
    }

    /// Replaces the entire editor content with the supplied plain text.
    /// Clears all blocks and rebuilds from scratch; Document.PageWidth is preserved.
    public static void SetPlainText(this RichTextBox rtb, string? text)
    {
        rtb.Document.Blocks.Clear();
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
            rtb.Document.Blocks.Add(new Paragraph(new Run(line)));
    }

    // ── TextPointer ↔ char-offset conversions ─────────────────────────────────

    /// Converts a <see cref="TextPointer"/> to a zero-based plain-text character offset.
    public static int GetCharOffset(this RichTextBox rtb, TextPointer pointer)
    {
        // TextRange.Text uses \r\n for paragraph breaks; collapse to \n for plain-text counting.
        var range = new TextRange(rtb.Document.ContentStart, pointer);
        return range.Text.Replace("\r\n", "\n").Length;
    }

    /// Converts a zero-based plain-text character offset to a <see cref="TextPointer"/>.
    public static TextPointer GetTextPointerAt(this RichTextBox rtb, int charOffset)
    {
        int remaining = charOffset;
        bool first = true;
        foreach (var block in rtb.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            if (!first)
            {
                if (remaining == 0) return para.ContentStart;
                remaining--; // paragraph separator '\n'
            }
            first = false;
            foreach (var inline in para.Inlines)
            {
                if (inline is Run run)
                {
                    if (remaining <= run.Text.Length)
                        return run.ContentStart.GetPositionAtOffset(remaining) ?? run.ContentEnd;
                    remaining -= run.Text.Length;
                }
            }
        }
        return rtb.Document.ContentEnd;
    }

    // ── Caret ─────────────────────────────────────────────────────────────────

    public static int GetCaretOffset(this RichTextBox rtb)
        => rtb.GetCharOffset(rtb.CaretPosition);

    public static void SetCaretOffset(this RichTextBox rtb, int offset)
        => rtb.CaretPosition = rtb.GetTextPointerAt(offset);

    // ── Selection ─────────────────────────────────────────────────────────────

    public static int GetSelectionStart(this RichTextBox rtb)
        => rtb.GetCharOffset(rtb.Selection.Start);

    public static int GetSelectionLength(this RichTextBox rtb)
    {
        var start = rtb.GetCharOffset(rtb.Selection.Start);
        var end   = rtb.GetCharOffset(rtb.Selection.End);
        return end - start;
    }

    public static string GetSelectedText(this RichTextBox rtb)
        => rtb.Selection.Text.Replace("\r\n", "\n");

    /// Selects a range by plain-text offsets.
    public static void SelectRange(this RichTextBox rtb, int start, int length)
    {
        var startPtr = rtb.GetTextPointerAt(start);
        var endPtr   = rtb.GetTextPointerAt(start + length);
        rtb.Selection.Select(startPtr, endPtr);
    }

    /// Replaces the current selection with the supplied text.
    /// Equivalent to setting TextBox.SelectedText.
    public static void ReplaceSelection(this RichTextBox rtb, string text)
    {
        rtb.BeginChange();
        try   { rtb.Selection.Text = text; }
        finally { rtb.EndChange(); }
    }

    // ── Text length and substring ─────────────────────────────────────────────

    public static int GetTextLength(this RichTextBox rtb)
        => rtb.GetPlainText().Length;

    public static string GetSubstring(this RichTextBox rtb, int start, int length)
        => rtb.GetPlainText().Substring(start, length);

    // ── Geometry ──────────────────────────────────────────────────────────────

    /// Returns the bounding rect of a character at the given plain-text offset,
    /// in RichTextBox-local coordinates. Equivalent to TextBox.GetRectFromCharacterIndex.
    public static Rect GetRectFromOffset(this RichTextBox rtb, int offset)
    {
        var ptr = rtb.GetTextPointerAt(offset);
        return ptr.GetCharacterRect(LogicalDirection.Forward);
    }

    // ── Scrolling ─────────────────────────────────────────────────────────────

    /// Scrolls to bring the line containing the plain-text offset into view.
    /// Equivalent to calling ScrollToLine(GetLineIndexFromCharacterIndex(offset)) on TextBox.
    public static void ScrollToOffset(this RichTextBox rtb, int offset)
    {
        var ptr = rtb.GetTextPointerAt(offset);
        ptr.Paragraph?.BringIntoView();
    }
}
