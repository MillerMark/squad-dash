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

    /// <summary>
    /// Enumerates the <see cref="EmbeddedCodeRegionBlock"/> instances hosted in
    /// <paramref name="rtb"/>'s document (each inside a <see cref="BlockUIContainer"/>). Used by
    /// <c>MainWindow.ApplyTheme</c> to push the active theme palette onto every region's
    /// AvalonEdit editor, whose colors do not follow WPF DynamicResource.
    /// </summary>
    internal static IEnumerable<EmbeddedCodeRegionBlock> EnumerateEmbeddedRegions(RichTextBox rtb)
    {
        foreach (var block in rtb.Document.Blocks)
        {
            if (block is BlockUIContainer { Child: EmbeddedCodeRegionBlock region })
                yield return region;
        }
    }

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
            DisposeEmbeddedRegions(rtb);
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
                    // Count only the same Run/LineBreak content GetParagraphText serializes,
                    // not TextRange's paragraph terminators or embedded UI placeholders.
                    foreach (var inline in para.Inlines)
                    {
                        if (pointer.CompareTo(inline.ElementStart) <= 0)
                            break;
                        if (inline is Run run)
                        {
                            if (pointer.CompareTo(run.ContentStart) <= 0)
                                break;
                            if (pointer.CompareTo(run.ContentEnd) < 0)
                                return offset + run.ContentStart.GetOffsetToPosition(pointer);
                            offset += run.Text.Length;
                        }
                        else if (inline is LineBreak && pointer.CompareTo(inline.ElementEnd) >= 0)
                            offset++;
                    }
                    return offset;
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
                    else if (inline is LineBreak lineBreak)
                    {
                        if (remaining == 0)
                            return lineBreak.ElementStart;
                        remaining--; // Shift+Enter contributes one '\n', just like GetParagraphText.
                        if (remaining == 0)
                            return lineBreak.ElementEnd;
                    }
                }
                // A freshly emptied Paragraph need not contain a Run.
                if (remaining == 0)
                    return para.ContentEnd;
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

    /// <summary>
    /// Serializes the current selection to plain text using the SAME block-walk contract as
    /// <see cref="GetPlainText"/>: every embedded code region whose
    /// <see cref="BlockUIContainer"/> the selection intersects contributes its FULL fenced
    /// text (<see cref="EmbeddedCodeRegionBlock.ToFencedText"/>, with the variable-length
    /// backtick outer fence), plain paragraphs contribute only the selected portion of their
    /// text, and contributing blocks are joined with '\n' exactly as
    /// <see cref="GetPlainText"/> / <see cref="GetCharOffset"/> do.
    /// <para>
    /// This is the copy/cut counterpart of the <see cref="GetPlainText"/> read path. WPF's
    /// default clipboard serialization only walks the text <see cref="Run"/>s of a selection
    /// and silently drops any <see cref="BlockUIContainer"/>, which strips an embedded region
    /// out of the copied text. A region is an atomic unit — it can never be partially copied —
    /// so the instant the selection touches one it is emitted as its whole fenced form, which
    /// mirrors how <see cref="GetCharOffset"/> treats a region as one indivisible
    /// fenced-length block. <paramref name="containsRegion"/> reports whether any region was
    /// intersected so callers can leave WPF's normal plain-text copy untouched (Shift+Enter
    /// line breaks, etc.) when the selection contains no region.
    /// </para>
    /// </summary>
    public static string GetSelectionFencedText(this RichTextBox rtb, out bool containsRegion)
    {
        containsRegion = false;
        var selection = rtb.Selection;
        if (selection is null || selection.IsEmpty)
            return string.Empty;

        var selStart = selection.Start;
        var selEnd = selection.End;

        var sb = new StringBuilder();
        var first = true;
        foreach (var block in rtb.Document.Blocks)
        {
            if (IsEmptyEmbeddedCodeRegionCaretLandingParagraph(block))
                continue;

            // Open-interval overlap between the selection and this block's element range.
            var overlapsSelection =
                selStart.CompareTo(block.ElementEnd) < 0 && selEnd.CompareTo(block.ElementStart) > 0;

            if (block is BlockUIContainer { Child: EmbeddedCodeRegionBlock region })
            {
                if (!overlapsSelection) continue;

                containsRegion = true;
                if (!first) sb.Append('\n');
                first = false;
                sb.Append(region.ToFencedText());
                continue;
            }

            if (block is not Paragraph para || !overlapsSelection)
                continue;

            // Respect a partial selection at the leading/trailing paragraph by clamping to the
            // intersection of the selection and this paragraph's content.
            var rangeStart = selStart.CompareTo(para.ContentStart) > 0 ? selStart : para.ContentStart;
            var rangeEnd = selEnd.CompareTo(para.ContentEnd) < 0 ? selEnd : para.ContentEnd;
            var clampedText = rangeStart.CompareTo(rangeEnd) < 0
                ? new TextRange(rangeStart, rangeEnd).Text.Replace("\r\n", "\n").Replace("\r", "\n")
                : string.Empty;

            // A paragraph contributes a line only when it carries selected text or is a
            // fully-selected (possibly blank) line whose '\n' join must be preserved. One
            // merely touched at a boundary (empty clamp, not fully contained) is skipped so we
            // never inject a stray blank line.
            var fullyContained =
                selStart.CompareTo(para.ElementStart) <= 0 && selEnd.CompareTo(para.ElementEnd) >= 0;
            if (clampedText.Length == 0 && !fullyContained)
                continue;

            if (!first) sb.Append('\n');
            first = false;
            sb.Append(clampedText);
        }

        return sb.ToString();
    }

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

        DisposeEmbeddedRegionsIntersectingSelection(rtb);

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

        if (block.Child is EmbeddedCodeRegionBlock region)
            region.DisposeRegionResources();

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

    internal static void DisposeEmbeddedRegions(this RichTextBox rtb)
    {
        foreach (var region in rtb.Document.Blocks
                     .OfType<BlockUIContainer>()
                     .Select(block => block.Child)
                     .OfType<EmbeddedCodeRegionBlock>()
                     .ToList())
        {
            region.DisposeRegionResources();
        }
    }

    private static void DisposeEmbeddedRegionsIntersectingSelection(RichTextBox rtb)
    {
        if (rtb.Selection.IsEmpty)
            return;

        var selStart = rtb.Selection.Start;
        var selEnd = rtb.Selection.End;
        foreach (var region in rtb.Document.Blocks
                     .OfType<BlockUIContainer>()
                     .Where(block => block.Child is EmbeddedCodeRegionBlock
                                     && selStart.CompareTo(block.ContentEnd) < 0
                                     && selEnd.CompareTo(block.ContentStart) > 0)
                     .Select(block => (EmbeddedCodeRegionBlock)block.Child)
                     .ToList())
        {
            region.DisposeRegionResources();
        }
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

    /// <summary>
    /// The outcome of <see cref="DecideRegionEntry"/>: the embedded region an arrow key should
    /// enter and the caret position within it. START (0) for forward keys (Down/Right), END
    /// (<c>Region.Content.Length</c>) for backward keys (Up/Left) — EXCEPT the column-preserving
    /// special case where Up is pressed at the START of the host line below the region, which
    /// enters at the START of the region's LAST line (see <paramref name="caretAtLineStart"/>).
    /// </summary>
    internal readonly record struct RegionEntryDecision(EmbeddedCodeRegionBlock Region, int TargetCaretPosition);

    /// <summary>
    /// Offset of the START of the LAST line of <paramref name="content"/> — the index just after
    /// the final <c>\n</c>, or 0 when the content is single-line (or empty). Used by the Change-1
    /// "Up from start-of-line-below enters at start of the region's last line" case.
    /// </summary>
    private static int StartOfLastLineOffset(string content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;
        var lastNewline = content.LastIndexOf('\n');
        return lastNewline < 0 ? 0 : lastNewline + 1;
    }

    /// <summary>
    /// Pure, direction-aware entry decision — the deterministic core of smart arrow-key
    /// navigation, separated from any live <see cref="TextPointer"/>/focus state so it is
    /// directly unit-testable (mirroring <see cref="FindAdjacentEmbeddedCodeRegion"/> and
    /// <c>EmbeddedCaretNavMath.Decide</c>).
    /// <para>
    /// Given the document's block list, the index of the block the caret sits on, and the
    /// arrow key, decides which adjacent region (if any) should be entered and where to place
    /// the caret. The rule is STRICTLY directional: a forward key (Down/Right) may ONLY enter
    /// the region immediately <em>below</em> the caret block, and a backward key (Up/Left) may
    /// ONLY enter the region immediately <em>above</em> it. This is what prevents a Down/Right
    /// press on the line below a region from jumping UP into the region above it (and the
    /// symmetric Up/Left case).
    /// </para>
    /// </summary>
    internal static RegionEntryDecision? DecideRegionEntry(
        IReadOnlyList<Block> blocks, int caretBlockIndex, Key key, bool caretAtLineStart = false)
    {
        if (caretBlockIndex < 0 || caretBlockIndex >= blocks.Count)
            return null;

        var forward = key is Key.Down or Key.Right;
        var backward = key is Key.Up or Key.Left;
        if (!forward && !backward)
            return null;

        // Entry is only from a text (host) line adjacent to the region; if the caret is on the
        // region's own container the caret is already inside, so there is no entry to make.
        if (blocks[caretBlockIndex] is not Paragraph)
            return null;

        var neighborIndex = forward ? caretBlockIndex + 1 : caretBlockIndex - 1;
        if (neighborIndex < 0 || neighborIndex >= blocks.Count)
            return null;

        if (blocks[neighborIndex] is not BlockUIContainer { Child: EmbeddedCodeRegionBlock region })
            return null;

        // Change 1 (column-preserving Up): when the caret is at the START of the host line below
        // the region and Up is pressed, enter at the START of the region's LAST line (column 0)
        // instead of its END. Every other backward case (Left, or mid-line Up) still enters at END.
        int target;
        if (forward)
            target = 0;
        else if (key is Key.Up && caretAtLineStart)
            target = StartOfLastLineOffset(region.Content);
        else
            target = region.Content.Length;

        return new RegionEntryDecision(region, target);
    }

    /// <summary>
    /// Direction-aware entry decision that accounts for the fragile line-prediction used by the
    /// live caret, separated as a pure function so every entry case is directly unit-testable.
    /// <para>
    /// VERTICAL and HORIZONTAL arrows have DIFFERENT correct entry semantics and are split here:
    /// </para>
    /// <para>
    /// HORIZONTAL keys (Left/Right) enter ONLY at the exact text boundary adjacent to the region.
    /// <paramref name="caretAtParagraphBoundary"/> is the boundary-adjacency fact the caller
    /// supplies: the caret is at the END of its paragraph for Right, or the START of its paragraph
    /// for Left — i.e. the next insertion position in the travel direction genuinely leaves the
    /// host paragraph and crosses into the region. A horizontal move that stays within the
    /// paragraph's text must NOT enter. When at the boundary, the strictly directional
    /// <see cref="DecideRegionEntry"/> enters the region BELOW (Right, at START) or ABOVE (Left, at
    /// END) the caret block.
    /// </para>
    /// <para>
    /// VERTICAL forward (Down) decides on the PREDICTED landing block (<paramref name="caretAfterIndex"/>)
    /// FIRST: the downward line prediction is reliable when it lands back on a host paragraph, and
    /// gating on it means a within-paragraph move does not prematurely enter the region below.
    /// When that prediction instead lands on the region's own non-text <c>BlockUIContainer</c> (the
    /// "dead zone" — when the caret starts at the START of the line directly above the region), the
    /// predicted-block rule finds nothing and we fall back to deciding on the block the caret is
    /// actually ON (<paramref name="caretBeforeIndex"/>). This lets Down enter from ANYWHERE on the
    /// adjacent line (a single line-step crosses the region from any column), while the strict
    /// directional invariant holds because forward <see cref="DecideRegionEntry"/> only ever enters
    /// the region at <c>caretBeforeIndex + 1</c>.
    /// </para>
    /// <para>
    /// VERTICAL backward (Up) decides on the block the caret is on BEFORE the press
    /// (<paramref name="caretBeforeIndex"/>). The Up prediction walks ACROSS the region and lands
    /// on the wrong side, so it cannot be trusted to find the region above; entry still requires
    /// the press to actually leave the current paragraph (the predicted landing, when known,
    /// differs from the current block) so a within-paragraph Up on a wrapped host line does not
    /// jump into the region.
    /// </para>
    /// </summary>
    internal static RegionEntryDecision? DecideRegionEntryFromPrediction(
        IReadOnlyList<Block> blocks, int caretBeforeIndex, int caretAfterIndex, Key key,
        bool caretAtParagraphBoundary, bool caretAtLineStart = false)
    {
        var forward = key is Key.Down or Key.Right;
        var backward = key is Key.Up or Key.Left;
        if (!forward && !backward)
            return null;

        // HORIZONTAL (Left/Right): enter ONLY when the caret is at the paragraph boundary in the
        // travel direction (Right → paragraph END, Left → paragraph START). A horizontal move that
        // stays within the paragraph's own text must NOT cross into an adjacent region.
        if (key is Key.Left or Key.Right)
        {
            if (!caretAtParagraphBoundary)
                return null;

            // DecideRegionEntry is strictly directional: Right enters the region BELOW the caret
            // block at START; Left enters the region ABOVE at END.
            return DecideRegionEntry(blocks, caretBeforeIndex, key);
        }

        if (forward)
            // VERTICAL Down — Primary: the predicted landing is a host paragraph directly above a
            // region. Fallback (entry from the START of the line above): the predicted landing fell
            // onto the region's own dead-zone container, so decide on the block the caret is ON so
            // Down enters from anywhere on the line above. The strict directional invariant holds
            // because forward DecideRegionEntry only ever enters the region at caretBeforeIndex + 1.
            return DecideRegionEntry(blocks, caretAfterIndex, key)
                ?? DecideRegionEntry(blocks, caretBeforeIndex, key);

        // VERTICAL Up: the predicted landing is unusable for finding the region above, but it is a
        // reliable "did the press leave the current paragraph?" signal. When the prediction is
        // known (>= 0) and stays on the same block, this was a within-paragraph move — do not
        // enter. Otherwise (left the paragraph, or prediction unavailable because it walked off
        // across the region) enter the region immediately above at END.
        if (caretAfterIndex >= 0 && caretAfterIndex == caretBeforeIndex)
            return null;

        return DecideRegionEntry(blocks, caretBeforeIndex, key, caretAtLineStart);
    }

    /// <summary>
    /// Returns the index of the first block whose element range contains <paramref name="caret"/>,
    /// or -1 if none. First-match-wins mirrors the boundary semantics of the original
    /// caret-paragraph scan (a position on a block boundary resolves to the earlier block).
    /// </summary>
    private static int FindCaretBlockIndex(IReadOnlyList<Block> blocks, TextPointer caret)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (caret.CompareTo(block.ElementStart) >= 0 && caret.CompareTo(block.ElementEnd) <= 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// For a HORIZONTAL arrow, returns whether the caret sits at the paragraph boundary in the
    /// travel direction (Right → END of its paragraph, Left → START), meaning the next insertion
    /// position would leave the host paragraph and can cross into an adjacent region. Returns
    /// <see langword="false"/> for vertical keys (they do not use boundary-adjacency).
    /// </summary>
    private static bool ComputeHorizontalEntryBoundary(TextPointer caret, Key key)
    {
        if (key is not (Key.Left or Key.Right))
            return false;

        var paragraph = caret.Paragraph;
        var direction = key == Key.Right ? LogicalDirection.Forward : LogicalDirection.Backward;
        var neighbor = caret.GetNextInsertionPosition(direction);

        // At the paragraph boundary when there is no enclosing paragraph, no further insertion
        // position in the travel direction, or the next insertion position belongs to a different
        // paragraph (i.e. the move leaves this paragraph's text).
        return paragraph is null || neighbor is null || !ReferenceEquals(neighbor.Paragraph, paragraph);
    }

    /// <summary>
    /// For a VERTICAL Up arrow, returns whether the caret sits at the START of its host paragraph
    /// (column 0) — the trigger for the Change-1 column-preserving entry. Mirrors the boundary
    /// detection in <see cref="ComputeHorizontalEntryBoundary"/> but for the backward/vertical path:
    /// the caret is at the paragraph start when there is no enclosing paragraph, no insertion
    /// position backward, or the previous insertion position belongs to a different paragraph.
    /// </summary>
    private static bool ComputeCaretAtLineStart(TextPointer caret)
    {
        var paragraph = caret.Paragraph;
        var neighbor = caret.GetNextInsertionPosition(LogicalDirection.Backward);
        return paragraph is null || neighbor is null || !ReferenceEquals(neighbor.Paragraph, paragraph);
    }

    private static BlockUIContainer? FindContainerForRegion(FlowDocument document, EmbeddedCodeRegionBlock region)
    {
        foreach (var block in document.Blocks.OfType<BlockUIContainer>())
        {
            if (ReferenceEquals(block.Child, region))
                return block;
        }
        return null;
    }

    /// <summary>
    /// Detects if the caret is on a region's paragraph line (the "dead zone") — positioned
    /// before or after a BlockUIContainer that hosts an EmbeddedCodeRegionBlock, but not
    /// actually inside the region's editor. This state occurs after pressing an arrow key
    /// that would land on the region's line without entering the region.
    /// <para>
    /// Direction-aware: a forward key (Down/Right) only matches a region BELOW the caret block
    /// and a backward key (Up/Left) only matches a region ABOVE it, so a press can never select
    /// the region on the wrong side of the travel direction.
    /// </para>
    /// Returns the region if found, null otherwise. Used to implement smart arrow-key
    /// navigation that enters the region directly instead of landing in the dead zone.
    /// </summary>
    internal static EmbeddedCodeRegionBlock? FindRegionOnCaretParagraph(FlowDocument document, TextPointer caret, Key key)
    {
        if (caret is null)
            return null;

        var blocksList = document.Blocks.ToList();
        var caretIndex = FindCaretBlockIndex(blocksList, caret);
        return DecideRegionEntry(blocksList, caretIndex, key)?.Region;
    }

    /// <summary>
    /// Determines the direction of arrow key navigation and attempts to navigate smartly into
    /// an embedded region. Detects if the caret would land in the dead zone (on a region's
    /// paragraph line but outside the TextBox) and instead routes focus directly into the
    /// region with correct caret positioning.
    /// 
    /// Returns true if navigation was handled (keystroke was intercepted), false if the
    /// default RichTextBox behavior should take over.
    /// </summary>
    public static bool TryNavigateIntoEmbeddedRegionWithDirection(
        this RichTextBox rtb, Key key, TextPointer? caretBefore, TextPointer? caretAfter)
    {
        if (!rtb.Selection.IsEmpty || caretBefore is null)
            return false;

        // Only handle arrow keys
        if (key is not (Key.Up or Key.Down or Key.Left or Key.Right))
            return false;

        var forward = key is Key.Down or Key.Right;

        // Resolve the block the caret is on BEFORE the press and (when predictable) the block it
        // would land on AFTER. Entry-from-above is decided on the predicted landing; entry-from-
        // below is decided on the current block because the backward prediction walks ACROSS the
        // region's non-text BlockUIContainer and resolves to the wrong side.
        var blocksList = rtb.Document.Blocks.ToList();
        var caretBeforeIndex = FindCaretBlockIndex(blocksList, caretBefore);
        var caretAfterIndex = caretAfter is null ? -1 : FindCaretBlockIndex(blocksList, caretAfter);

        // Horizontal entry is gated on a boundary-adjacency fact rather than the predicted landing
        // block: Right may only cross into the region below when the caret is at the END of its
        // paragraph; Left only when it is at the START. We detect that by asking whether the next
        // insertion position in the travel direction still lands inside the same paragraph — if it
        // leaves the paragraph (or there is none), the caret is at the boundary. Vertical keys
        // ignore this flag and keep their from-anywhere-on-the-adjacent-line behavior.
        var caretAtParagraphBoundary = ComputeHorizontalEntryBoundary(caretBefore, key);

        // Vertical Up at the START of the host line below the region preserves the column: enter at
        // the START of the region's last line rather than its END (Change 1). Only meaningful for Up.
        var caretAtLineStart = key is Key.Up && ComputeCaretAtLineStart(caretBefore);

        var decision = DecideRegionEntryFromPrediction(
            blocksList, caretBeforeIndex, caretAfterIndex, key, caretAtParagraphBoundary, caretAtLineStart);
        if (decision is null)
            return false;

        var region = decision.Value.Region;

        // Guard against entering a region on the WRONG side of the travel direction: forward keys
        // may only enter a region positioned AFTER caretBefore, backward keys only a region
        // positioned BEFORE caretBefore. (Preserves the strict directional invariant.)
        var container = FindContainerForRegion(rtb.Document, region);
        if (container is not null)
        {
            var onCorrectSide = forward
                ? container.ElementStart.CompareTo(caretBefore) >= 0
                : container.ElementEnd.CompareTo(caretBefore) <= 0;
            if (!onCorrectSide)
            {
                SquadDashTrace.Write(
                    "UI",
                    $"EMBEDDED_CODE_SMART_NAV reject reason=region-wrong-side key={key} forward={forward} caretBefore-offset={GetCaretOffsetForTraceHelper(rtb, caretBefore)} caretAfter-offset={GetCaretOffsetForTraceHelper(rtb, caretAfter)}");
                return false;
            }
        }

        var targetPosition = decision.Value.TargetCaretPosition;

        SquadDashTrace.Write(
            "UI",
            $"EMBEDDED_CODE_SMART_NAV enter key={key} forward={forward} caretBefore-offset={GetCaretOffsetForTraceHelper(rtb, caretBefore)} caretAfter-offset={GetCaretOffsetForTraceHelper(rtb, caretAfter)} targetPosition={targetPosition}");

        // Focus the region at the calculated position
        region.FocusContentAtPosition(targetPosition);

        SquadDashTrace.Write(
            "UI",
            $"EMBEDDED_CODE_SMART_NAV exit key={key} focused={DescribeInputObject(Keyboard.FocusedElement)}");

        return true;
    }

    private static string GetCaretOffsetForTraceHelper(RichTextBox rtb, TextPointer? pointer)
    {
        if (pointer is null)
            return "(null)";
        try
        {
            return rtb.GetCharOffset(pointer).ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
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

    internal static void PreparePlainTextPaste(DataObjectPastingEventArgs e, bool clipboardContainsText)
    {
        var format = ResolvePlainTextPasteFormat(e.FormatToApply, clipboardContainsText);
        if (format is null)
        {
            e.CancelCommand();
            return;
        }

        if (e.DataObject.GetData(format) is string text)
        {
            // Normalize before WPF creates document breaks, not after text/offsets are stored.
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            e.DataObject = new DataObject(format, normalized);
        }
        e.FormatToApply = format;
    }

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
