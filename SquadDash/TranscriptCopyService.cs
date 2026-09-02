using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
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

    /// <summary>
    /// Returns selected text from a nested selectable document viewer, such as a recovery card.
    /// Capturing this before its context menu takes focus keeps right-click Copy reliable.
    /// </summary>
    public static string? BuildSelectionText(FlowDocumentScrollViewer viewer) {
        var selection = viewer.Selection;
        if (selection.IsEmpty)
            return null;

        var result = selection.Text.TrimEnd();
        return result.Length == 0 ? null : result;
    }

    /// <summary>Builds readable text for a whole embedded transcript card.</summary>
    internal static string? BuildCardText(BlockUIContainer container) {
        if (TranscriptCardLayout.GetCopyable(container) is { } copyable) {
            var copyText = copyable.GetCopyText().TrimEnd();
            return copyText.Length == 0 ? null : copyText;
        }

        if (container.Child is StackPanel { Tag: string tableText } && tableText.Length > 0)
            return tableText;
        if (container.Child is TextBox { Tag: "codeblock" } codeBox)
            return string.IsNullOrEmpty(codeBox.Text) ? null : codeBox.Text;
        return null;
    }

    internal static string? BuildVisualCardText(UIElement card) {
        if (FindDocumentViewer(card) is { Document: { } document }) {
            var documentText = new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd();
            if (documentText.Length > 0)
                return documentText;
        }

        var lines = new List<string>();
        AppendVisibleCardText(lines, card);
        var result = string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line))).TrimEnd();
        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// Builds a markdown-faithful string for the current selection, using the original
    /// markdown source stored on each <see cref="Paragraph.Tag"/> by the renderer.
    /// Falls back to plain-text extraction for blocks without a stored source.
    /// Block elements are separated by blank lines so the note renders correctly.
    /// Returns <c>null</c> if the selection is empty.
    /// </summary>
    public static string? BuildSelectionMarkdown(RichTextBox richTextBox) {
        var selection = richTextBox.Selection;
        if (selection.IsEmpty)
            return null;

        var selStart = selection.Start;
        var selEnd   = selection.End;
        var sb       = new StringBuilder();

        AppendBlocksMarkdown(sb, richTextBox.Document.Blocks, selStart, selEnd);

        var result = sb.ToString().TrimEnd();
        return result.Length == 0 ? null : result;
    }

    // ── Block walk (markdown) ─────────────────────────────────────────────

    private static void AppendBlocksMarkdown(
        StringBuilder sb,
        BlockCollection blocks,
        TextPointer selStart,
        TextPointer selEnd) {

        var lastWasListItem = false;
        foreach (var block in blocks) {
            if (!OverlapsSelection(block, selStart, selEnd))
                continue;

            switch (block) {
                case Paragraph p: {
                    var markdownSrc = p.Tag as string;
                    var isListItem  = IsListItemTag(markdownSrc);

                    // Consecutive list items must be adjacent lines — no blank line separator.
                    // Every other block transition gets a blank line for readability.
                    if (sb.Length > 0 && !(isListItem && lastWasListItem))
                        sb.AppendLine();

                    if (markdownSrc is { Length: > 0 })
                        sb.AppendLine(markdownSrc);
                    else
                        AppendParagraphText(sb, p, selStart, selEnd);

                    lastWasListItem = isListItem;
                    break;
                }

                case BlockUIContainer buc: {
                    if (sb.Length > 0) sb.AppendLine();
                    // Reuse the same extraction logic as plain-text copy (already returns
                    // markdown-friendly content for code blocks, tables, and quick replies).
                    if (TryAppendBlockUIContainer(sb, buc))
                        sb.AppendLine();
                    lastWasListItem = false;
                    break;
                }

                case Section s:
                    AppendBlocksMarkdown(sb, s.Blocks, selStart, selEnd);
                    lastWasListItem = false;
                    break;
            }
        }
    }

    private static bool IsListItemTag(string? tag) {
        if (tag is null) return false;
        if (tag.StartsWith("- ", StringComparison.Ordinal) ||
            tag.StartsWith("* ", StringComparison.Ordinal))
            return true;
        var dotIdx = tag.IndexOf(". ", StringComparison.Ordinal);
        return dotIdx > 0 && dotIdx <= 3 && tag[..dotIdx].All(char.IsDigit);
    }

    // ── Block walk (plain text) ────────────────────────────────────────────────

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
        var text = BuildCardText(container);
        if (text is null)
            return false;
        sb.Append(text);
        return true;
    }

    internal static FlowDocumentScrollViewer? FindCardDocumentViewer(BlockUIContainer container) =>
        FindDocumentViewer(container.Child);

    private static FlowDocumentScrollViewer? FindDocumentViewer(UIElement element) {
        if (element is FlowDocumentScrollViewer viewer)
            return viewer;
        if (element is Border { Child: UIElement borderChild })
            return FindDocumentViewer(borderChild);
        if (element is ContentControl { Content: UIElement content })
            return FindDocumentViewer(content);
        if (element is Panel panel) {
            foreach (UIElement child in panel.Children)
                if (FindDocumentViewer(child) is { } nested)
                    return nested;
        }
        return null;
    }

    private static void AppendVisibleCardText(List<string> lines, UIElement element) {
        if (element.Visibility != System.Windows.Visibility.Visible)
            return;
        switch (element) {
            case TextBlock textBlock:
                var text = ExtractInlineText(textBlock.Inlines);
                if (string.IsNullOrWhiteSpace(text))
                    text = textBlock.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    lines.Add(text.TrimEnd());
                return;
            case TextBox textBox when !string.IsNullOrWhiteSpace(textBox.Text):
                lines.Add(textBox.Text.TrimEnd());
                return;
            case Button:
                return; // Action labels are controls, not card narrative.
            case Border { Child: UIElement child }:
                AppendVisibleCardText(lines, child);
                return;
            case ContentControl { Content: UIElement content }:
                AppendVisibleCardText(lines, content);
                return;
            case Panel panel:
                foreach (UIElement childElement in panel.Children)
                    AppendVisibleCardText(lines, childElement);
                return;
        }
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
