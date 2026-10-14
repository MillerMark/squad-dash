using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

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
    internal const string EmbeddedCodeRegionCaretLandingTag = "embedded-code-region-caret-landing";

    internal static Paragraph CreateEmbeddedCodeRegionCaretLandingParagraph() =>
        new(new Run(string.Empty))
        {
            Margin = new Thickness(0),
            Tag = EmbeddedCodeRegionCaretLandingTag,
        };

    internal static bool IsEmptyEmbeddedCodeRegionCaretLandingParagraph(Block block) =>
        block is Paragraph para &&
        Equals(para.Tag, EmbeddedCodeRegionCaretLandingTag) &&
        GetParagraphText(para).Length == 0;

    // ── Plain-text access ────────────────────────────────────────────────────

    /// Returns the editor content as a plain-text string with '\n' line endings.
    /// InlineUIContainer and other non-Run inlines are intentionally excluded so they
    /// never appear in the text written to disk. Embedded code regions (see
    /// <see cref="EmbeddedCodeRegionBlock"/>, created by <see cref="EmbeddedCodeRegionManager"/>)
    /// are serialized back into their equivalent triple-backtick fenced form — this is the
    /// read-path half of the embedded-region serialization round-trip contract.
    public static string GetPlainText(this RichTextBox rtb)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var block in rtb.Document.Blocks)
        {
            if (IsEmptyEmbeddedCodeRegionCaretLandingParagraph(block))
                continue;

            string? blockText = block switch
            {
                Paragraph para => GetParagraphText(para),
                BlockUIContainer { Child: EmbeddedCodeRegionBlock region } => region.ToFencedText(),
                _ => null,
            };
            if (blockText is null) continue;
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(blockText);
        }
        return sb.ToString();
    }

    /// Returns the plain-text content of a single Paragraph ('\n' for each internal
    /// LineBreak, e.g. from Shift+Enter). Shared by <see cref="GetPlainText"/> and
    /// <see cref="EmbeddedCodeRegionManager"/>'s fence-scanning logic so both use the exact
    /// same paragraph-to-text rule.
    internal static string GetParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
                sb.Append(run.Text);
            else if (inline is LineBreak)
                sb.Append('\n'); // Shift+Enter produces a LineBreak within a paragraph
            // InlineUIContainer (e.g. RevisionPendingIndicator) intentionally skipped
        }
        return sb.ToString();
    }

    /// Replaces the entire editor content with the supplied plain text.
    /// Clears all blocks and rebuilds from scratch; Document.PageWidth is preserved.
    public static void SetPlainText(this RichTextBox rtb, string? text)
    {
        var undoWasEnabled = rtb.IsUndoEnabled;
        if (undoWasEnabled)
            rtb.IsUndoEnabled = false;

        try
        {
            rtb.Document.Blocks.Clear();
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
                rtb.Document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0) });
        }
        finally
        {
            if (undoWasEnabled)
                rtb.IsUndoEnabled = true;
        }
    }

    /// Appends plain text to the end of the editor content. Equivalent to TextBox.AppendText.
    public static void AppendPlainText(this RichTextBox rtb, string? text)
        => rtb.SetPlainText(rtb.GetPlainText() + (text ?? string.Empty));

    // ── TextPointer ↔ char-offset conversions ─────────────────────────────────

    /// Converts a <see cref="TextPointer"/> to a zero-based plain-text character offset.
    /// <para>
    /// Walks blocks the same way <see cref="GetPlainText"/> does (joining contributing
    /// blocks with '\n') so that an <see cref="EmbeddedCodeRegionBlock"/>'s serialized fence
    /// text counts toward the offset exactly like any other block's text would — a caret can
    /// never actually land "inside" the BlockUIContainer itself (it hosts a UIElement, not
    /// text), so a pointer found to be within one is treated as sitting at that block's edge.
    /// </para>
    public static int GetCharOffset(this RichTextBox rtb, TextPointer pointer)
    {
        var offset = 0;
        var first = true;
        foreach (var block in rtb.Document.Blocks)
        {
            if (IsEmptyEmbeddedCodeRegionCaretLandingParagraph(block))
            {
                if (pointer.CompareTo(block.ElementStart) >= 0 && pointer.CompareTo(block.ElementEnd) <= 0)
                    return offset;

                continue;
            }

            string? blockText = block switch
            {
                Paragraph => null, // handled specially below (needs partial-text support)
                BlockUIContainer { Child: EmbeddedCodeRegionBlock region } => region.ToFencedText(),
                _ => null,
            };

            if (block is not Paragraph && blockText is null) continue;

            var isWithinBlock = pointer.CompareTo(block.ElementStart) >= 0 && pointer.CompareTo(block.ElementEnd) <= 0;

            if (!first) offset += 1; // '\n' join, matching GetPlainText
            first = false;

            if (block is Paragraph para)
            {
                if (isWithinBlock)
                {
                    var range = new TextRange(para.ContentStart, pointer);
                    return offset + range.Text.Replace("\r\n", "\n").Length;
                }
                offset += GetParagraphText(para).Length;
            }
            else
            {
                // A BlockUIContainer hosts a single UIElement, not text — ContentStart and
                // ContentEnd typically collapse to the same position, so they can't
                // distinguish "before" from "after" the region. Use ElementEnd: a pointer at
                // or past it means the region has been fully passed (count its full fenced
                // length); anything before that (realistically only ElementStart) means the
                // pointer sits right before the region.
                if (isWithinBlock)
                    return pointer.CompareTo(block.ElementEnd) >= 0 ? offset + blockText!.Length : offset;
                offset += blockText!.Length;
            }
        }
        return offset;
    }

    /// Converts a zero-based plain-text character offset to a <see cref="TextPointer"/>.
    /// Embedded code regions (<see cref="BlockUIContainer"/> hosting an
    /// <see cref="EmbeddedCodeRegionBlock"/>) are treated as an opaque run of
    /// <c>region.ToFencedText().Length</c> characters — an offset landing anywhere within
    /// that span resolves to a boundary immediately before/after the region, since the
    /// region's own content lives in a separate nested TextBox, not in this RichTextBox's
    /// TextPointer space.
    /// <para>
    /// Uses the <see cref="BlockUIContainer"/>'s own ElementStart/ElementEnd as the
    /// before/after boundary for a region — this keeps the offset math exactly symmetric
    /// with <see cref="GetCharOffset"/> (which classifies pointers the same way), so the two
    /// methods round-trip consistently. Callers that need to actually *assign* the resulting
    /// pointer to <c>RichTextBox.CaretPosition</c> right at a region boundary should be aware
    /// that WPF does not always keep the caret at a BlockUIContainer's own element boundary
    /// when that container hosts a focusable child control (the region's content TextBox);
    /// see <see cref="EmbeddedCodeRegionManager.RenderSegments"/> for the caller-side nudge
    /// that works around this when restoring caret position after a rebuild.
    /// </para>
    public static TextPointer GetTextPointerAt(this RichTextBox rtb, int charOffset)
    {
        int remaining = charOffset;
        var first = true;

        foreach (var block in rtb.Document.Blocks)
        {
            if (IsEmptyEmbeddedCodeRegionCaretLandingParagraph(block))
                continue;

            if (block is Paragraph para)
            {
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
            else if (block is BlockUIContainer { Child: EmbeddedCodeRegionBlock region })
            {
                if (!first)
                {
                    if (remaining == 0) return block.ElementStart;
                    remaining--; // paragraph separator '\n'
                }
                first = false;

                var fencedLength = region.ToFencedText().Length;
                if (remaining <= fencedLength)
                    return remaining == 0 ? block.ElementStart : block.ElementEnd;
                remaining -= fencedLength;
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

    public static bool ReplaceSelectionWithoutUndo(this RichTextBox rtb, string text)
    {
        if (rtb.Selection.IsEmpty)
            return false;

        var undoWasEnabled = rtb.IsUndoEnabled;
        if (undoWasEnabled)
            rtb.IsUndoEnabled = false;

        try
        {
            rtb.Selection.Text = text;
            return true;
        }
        finally
        {
            if (undoWasEnabled)
                rtb.IsUndoEnabled = true;
        }
    }

    /// <summary>
    /// Deletes the active selection while undo is disabled. This avoids WPF's default
    /// undo-unit XAML clone path for embedded UI elements hosted in the document.
    /// </summary>
    public static bool DeleteSelectionWithoutUndo(this RichTextBox rtb) =>
        rtb.ReplaceSelectionWithoutUndo(string.Empty);

    public static bool ContainsEmbeddedCodeRegion(this RichTextBox rtb) =>
        rtb.Document.Blocks.OfType<BlockUIContainer>()
            .Any(block => block.Child is EmbeddedCodeRegionBlock);

    /// <summary>
    /// When the caret (an empty/collapsed selection) sits immediately adjacent to an
    /// embedded code region's <see cref="BlockUIContainer"/> in the given direction,
    /// gives focus to that region's content TextBox. This enables arrow-key navigation
    /// to enter the region. Returns <see langword="true"/> if a region was found and focused,
    /// <see langword="false"/> otherwise.
    /// </summary>
    public static bool TryFocusAdjacentEmbeddedCodeRegion(this RichTextBox rtb, LogicalDirection direction)
    {
        if (!rtb.Selection.IsEmpty)
        {
            SquadDashTrace.Write(
                "UI",
                $"EMBEDDED_CODE_HOST_ARROW exit reason=selection-not-empty direction={direction} selectionLength={GetSelectionLengthForTrace(rtb)} caretOffset={GetCaretOffsetForTrace(rtb)}");
            return false;
        }

        SquadDashTrace.Write(
            "UI",
            $"EMBEDDED_CODE_HOST_ARROW enter direction={direction} caretOffset={GetCaretOffsetForTrace(rtb)} focused={DescribeInputObject(Keyboard.FocusedElement)}");
        var block = FindAdjacentEmbeddedCodeRegion(rtb.Document, rtb.CaretPosition, direction);
        if (block?.Child is not EmbeddedCodeRegionBlock region)
        {
            SquadDashTrace.Write("UI", $"EMBEDDED_CODE_HOST_ARROW exit reason=no-adjacent-region direction={direction} caretOffset={GetCaretOffsetForTrace(rtb)}");
            return false;
        }

        region.FocusContent();
        SquadDashTrace.Write("UI", $"EMBEDDED_CODE_HOST_ARROW exit reason=focused-region direction={direction} caretOffset={GetCaretOffsetForTrace(rtb)} focused={DescribeInputObject(Keyboard.FocusedElement)}");
        return true;
    }

    /// <summary>
    /// Pure adjacency check, separated from <see cref="TryDeleteAdjacentEmbeddedCodeRegion"/>
    /// so the detection rule is directly unit-testable with an explicit <see cref="TextPointer"/>
    /// — bypassing <c>RichTextBox.CaretPosition</c>'s own setter, which only reliably
    /// normalizes a position adjacent to a focusable-content <see cref="BlockUIContainer"/>
    /// once the control is actually focused inside a rendered window.
    /// </summary>
    internal static BlockUIContainer? FindAdjacentEmbeddedCodeRegion(
        FlowDocument document, TextPointer caret, LogicalDirection direction)
    {
        foreach (var block in document.Blocks.OfType<BlockUIContainer>())
        {
            if (block.Child is not EmbeddedCodeRegionBlock)
                continue;

            var isAdjacent = direction == LogicalDirection.Forward
                ? caret.CompareTo(block.ElementStart) == 0
                : caret.CompareTo(block.ElementEnd) == 0;
            if (isAdjacent)
                return block;
        }
        return null;
    }

    private static string GetCaretOffsetForTrace(RichTextBox rtb)
    {
        try
        {
            return rtb.GetCaretOffset().ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string GetSelectionLengthForTrace(RichTextBox rtb)
    {
        try
        {
            return rtb.GetSelectionLength().ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string DescribeInputObject(object? value)
    {
        if (value is null)
            return "(null)";

        var typeName = value.GetType().Name;
        return value is FrameworkElement { Name.Length: > 0 } element
            ? $"{typeName}#{element.Name}"
            : typeName;
    }

    /// <summary>
    /// When the caret (an empty/collapsed selection) sits immediately adjacent to an
    /// embedded code region's <see cref="BlockUIContainer"/> in the given direction,
    /// removes that block directly via <c>Document.Blocks.Remove</c> instead of letting the
    /// Delete/Backspace key fall through to WPF's native block-boundary deletion handling —
    /// which is not reliable around a <see cref="BlockUIContainer"/> hosting a nested
    /// interactive control and can crash. Returns <see langword="false"/> (no-op) when the
    /// selection isn't empty or the caret isn't adjacent to a region in that direction, so
    /// normal key handling can take over.
    /// </summary>
    public static bool TryDeleteAdjacentEmbeddedCodeRegion(this RichTextBox rtb, LogicalDirection direction)
    {
        if (!rtb.Selection.IsEmpty)
            return false;

        var block = FindAdjacentEmbeddedCodeRegion(rtb.Document, rtb.CaretPosition, direction);
        if (block is null)
            return false;

        var undoWasEnabled = rtb.IsUndoEnabled;
        if (undoWasEnabled)
            rtb.IsUndoEnabled = false;
        try
        {
            rtb.Document.Blocks.Remove(block);
        }
        finally
        {
            if (undoWasEnabled)
                rtb.IsUndoEnabled = true;
        }
        return true;
    }

    public static bool SelectionContainsEmbeddedCodeRegion(this RichTextBox rtb)
    {
        if (rtb.Selection.IsEmpty)
            return false;

        foreach (var block in rtb.Document.Blocks.OfType<BlockUIContainer>())
        {
            if (block.Child is not EmbeddedCodeRegionBlock)
                continue;

            if (rtb.Selection.Start.CompareTo(block.ContentEnd) < 0
                && rtb.Selection.End.CompareTo(block.ContentStart) > 0)
                return true;
        }

        return false;
    }

    // ── Text length and substring ─────────────────────────────────────────────

    public static int GetTextLength(this RichTextBox rtb)
        => rtb.GetPlainText().Length;

    /// Returns the number of paragraph lines, counting embedded LineBreak-created lines
    /// as part of the same paragraph (matching TextBox.LineCount's '\n' counting semantics
    /// closely enough for the "is multi-line" checks that consume it).
    public static int GetLineCount(this RichTextBox rtb)
        => rtb.GetPlainText().Count(c => c == '\n') + 1;

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

    // ── Paste enforcement ─────────────────────────────────────────────────────

    /// <summary>
    /// Pure decision logic behind "force plain-text paste" handlers registered via
    /// <see cref="DataObject.AddPastingHandler"/> on plain-text RichTextBox editors
    /// (e.g. PromptTextBox, MarkdownDocumentTabState.EditorTextBox). Separated from the
    /// actual <see cref="Clipboard"/> access so it can be unit tested without requiring
    /// a live OS clipboard.
    /// </summary>
    /// <param name="formatToApply">The format WPF is about to apply, from
    /// <c>DataObjectPastingEventArgs.FormatToApply</c>.</param>
    /// <param name="clipboardContainsText">Whether the clipboard currently contains a
    /// text representation, from <c>Clipboard.ContainsText()</c>.</param>
    /// <returns>The format that should be applied (always a plain-text format), or
    /// <see langword="null"/> if the paste should be cancelled entirely.</returns>
    public static string? ResolvePlainTextPasteFormat(string formatToApply, bool clipboardContainsText)
    {
        if (formatToApply == DataFormats.UnicodeText || formatToApply == DataFormats.Text)
            return formatToApply;
        return clipboardContainsText ? DataFormats.UnicodeText : null;
    }
}
