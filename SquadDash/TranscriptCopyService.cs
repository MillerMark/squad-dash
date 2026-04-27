using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Builds clipboard text from a <see cref="RichTextBox"/> selection that may span
/// heterogeneous transcript elements (plain text, thinking blocks, tool entries,
/// quick reply buttons, code blocks).
///
/// WPF's built-in copy leaves <see cref="BlockUIContainer"/> nodes empty. This service
/// walks the FlowDocument block tree for the selection range, extracting native text
/// from <see cref="Paragraph"/> nodes and delegating to <see cref="ICopyable"/>
/// implementors (or structural pattern-matching) for container nodes.
/// </summary>
internal static class TranscriptCopyService {

    /// <summary>
    /// Builds a plain-text string representing the current selection of
    /// <paramref name="richTextBox"/>. Returns <c>null</c> if the selection is empty.
    /// </summary>
    public static string? BuildSelectionText(RichTextBox richTextBox) {
        var selection = richTextBox.Selection;
        if (selection.IsEmpty)
            return null;

        var selStart = selection.Start;
        var selEnd   = selection.End;
        var sb       = new StringBuilder();

        AppendBlocks(sb, richTextBox.Document.Blocks, selStart, selEnd);

        var result = sb.ToString().TrimEnd();
        return result.Length == 0 ? null : result;
    }

    // ── Block walk ────────────────────────────────────────────────────────────

    private static void AppendBlocks(
        StringBuilder sb,
        BlockCollection blocks,
        TextPointer selStart,
        TextPointer selEnd) {

        foreach (var block in blocks) {
            if (!OverlapsSelection(block, selStart, selEnd))
                continue;

            switch (block) {
                case Paragraph p:
                    AppendParagraphText(sb, p, selStart, selEnd);
                    sb.AppendLine();
                    break;

                case BlockUIContainer buc:
                    if (TryAppendBlockUIContainer(sb, buc))
                        sb.AppendLine();
                    break;

                case Section s:
                    // Sections are structural groupings — recurse without adding extra newlines.
                    AppendBlocks(sb, s.Blocks, selStart, selEnd);
                    break;
            }
        }
    }

    private static void AppendParagraphText(
        StringBuilder sb,
        Paragraph p,
        TextPointer selStart,
        TextPointer selEnd) {

        // Clamp range to the intersection of the paragraph and the selection.
        var rangeStart = p.ContentStart.CompareTo(selStart) < 0 ? selStart : p.ContentStart;
        var rangeEnd   = p.ContentEnd.CompareTo(selEnd)   > 0 ? selEnd   : p.ContentEnd;

        if (rangeStart.CompareTo(rangeEnd) >= 0)
            return;

        sb.Append(new TextRange(rangeStart, rangeEnd).Text);
    }

    private static bool TryAppendBlockUIContainer(StringBuilder sb, BlockUIContainer container) {
        // Priority 1: ICopyable tag — model object knows how to render itself.
        if (container.Tag is ICopyable copyable) {
            var text = copyable.GetCopyText();
            if (!string.IsNullOrEmpty(text))
                sb.Append(text);
            return true;
        }

        // Priority 2: markdown table — StackPanel with Tag = preformatted markdown text.
        if (container.Child is StackPanel sp && sp.Tag is string tableText && tableText.Length > 0) {
            sb.Append(tableText);
            return true;
        }

        // Priority 3: code block TextBox — identified by Tag = "codeblock".
        if (container.Child is TextBox { Tag: "codeblock" } codeBox) {
            sb.Append(codeBox.Text);
            return true;
        }

        // Unknown container type — silently skip so future additions don't crash.
        return false;
    }

    private static bool OverlapsSelection(TextElement element, TextPointer start, TextPointer end) {
        // BlockUIContainer has ContentStart == ContentEnd (no text nodes — it hosts a UIElement).
        // Use ElementStart/ElementEnd, which correctly bracket the block in the outer document tree.
        var blockStart = element is BlockUIContainer ? element.ElementStart : element.ContentStart;
        var blockEnd   = element is BlockUIContainer ? element.ElementEnd   : element.ContentEnd;
        return blockStart.CompareTo(end) < 0 && blockEnd.CompareTo(start) > 0;
    }

    // ── Inline text helpers (also used by ICopyable implementors) ─────────────

    /// <summary>
    /// Walks an <see cref="InlineCollection"/> and returns the concatenated plain text,
    /// honoring <see cref="Run"/>, <see cref="LineBreak"/>, and nested <see cref="Span"/> nodes.
    /// </summary>
    internal static string ExtractInlineText(InlineCollection inlines) {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
            AppendInlineText(sb, inline);
        return sb.ToString();
    }

    internal static void AppendInlineText(StringBuilder sb, Inline inline) {
        switch (inline) {
            case Run run:
                sb.Append(run.Text);
                break;

            case LineBreak:
                sb.AppendLine();
                break;

            case Span span:
                foreach (var child in span.Inlines)
                    AppendInlineText(sb, child);
                break;
        }
    }
}

// ── Quick reply copy data ─────────────────────────────────────────────────────

/// <summary>
/// Holds the option labels for a quick reply block so that <see cref="TranscriptCopyService"/>
/// can include them in a clipboard copy operation.
/// Attached as <c>Tag</c> on the quick reply <see cref="System.Windows.Documents.BlockUIContainer"/>.
/// </summary>
internal sealed record QuickReplyCopyData(
    IReadOnlyList<string> OptionLabels,
    string? CaptionText) : ICopyable {

    public string GetCopyText() {
        var labels = OptionLabels
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        return labels.Count == 0
            ? string.Empty
            : "[" + string.Join(" | ", labels) + "]";
    }
}
