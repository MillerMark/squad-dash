using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Static helpers that apply markdown formatting to a <see cref="TextBox"/>.
/// Shared by the documentation source panel (MainWindow) and the standalone
/// <see cref="MarkdownDocumentWindow"/> source editor so both surfaces offer the
/// same editing capabilities.
/// </summary>
internal static class MarkdownEditorCommands
{
    // Matches optional leading whitespace + bullet marker at the start of a line.
    // Group "sym"  — unordered marker (- * +)
    // Group "num"  — ordered list number (digits before the dot)
    private static readonly Regex ListBulletRegex = new(
        @"^(?<indent>[ \t]*)(?:(?<sym>[-*+]) |(?<num>\d+)\. )",
        RegexOptions.Compiled);

    /// <summary>
    /// Handles the Enter key in a markdown TextBox: if the caret is at the end
    /// of a bullet list line, inserts a new line that continues the list.
    /// Unordered bullets (- * +) are repeated; ordered numbers are incremented.
    /// Returns <see langword="true"/> if the keystroke was handled (caller should
    /// set <c>e.Handled = true</c>).
    /// </summary>
    internal static bool ContinueListOnEnter(TextBox box)
    {
        var text  = box.Text;
        var caret = box.CaretIndex;

        if (caret == 0) return false;

        // Only continue when caret is at end of its line.
        var atEndOfLine = caret == text.Length || text[caret] == '\n' || text[caret] == '\r';
        if (!atEndOfLine) return false;

        // Find the start of the current line (character after last '\n').
        var lineStart = text.LastIndexOf('\n', caret - 1) + 1;
        var lineText  = text[lineStart..caret];

        var m = ListBulletRegex.Match(lineText);
        if (!m.Success) return false;

        // If the line is nothing but the bullet prefix (no content typed yet),
        // escape the list: remove the prefix and leave an empty line.
        if (lineText.Length == m.Length) {
            box.Text       = text[..lineStart] + text[caret..];
            box.CaretIndex = lineStart;
            return true;
        }

        var indent = m.Groups["indent"].Value;
        string nextPrefix;
        if (m.Groups["num"].Success && int.TryParse(m.Groups["num"].Value, out int n))
            nextPrefix = indent + (n + 1) + ". ";
        else
            nextPrefix = indent + m.Groups["sym"].Value + " ";

        var insertion  = "\n" + nextPrefix;
        box.Text       = text[..caret] + insertion + text[caret..];
        box.CaretIndex = caret + insertion.Length;
        return true;
    }

    /// <summary>
    /// RichTextBox overload of <see cref="ContinueListOnEnter(TextBox)"/>.
    /// </summary>
    internal static bool ContinueListOnEnter(RichTextBox box)
    {
        var text  = box.GetPlainText();
        var caret = box.GetCaretOffset();

        if (caret == 0) return false;

        var atEndOfLine = caret == text.Length || text[caret] == '\n' || text[caret] == '\r';
        if (!atEndOfLine) return false;

        var lineStart = text.LastIndexOf('\n', caret - 1) + 1;
        var lineText  = text[lineStart..caret];

        var m = ListBulletRegex.Match(lineText);
        if (!m.Success) return false;

        // Escape the list when the line has no content beyond the bullet prefix.
        if (lineText.Length == m.Length) {
            box.SetPlainText(text[..lineStart] + text[caret..]);
            box.SetCaretOffset(lineStart);
            return true;
        }

        var indent = m.Groups["indent"].Value;
        string nextPrefix;
        if (m.Groups["num"].Success && int.TryParse(m.Groups["num"].Value, out int n))
            nextPrefix = indent + (n + 1) + ". ";
        else
            nextPrefix = indent + m.Groups["sym"].Value + " ";

        var insertion = "\n" + nextPrefix;
        box.SetPlainText(text[..caret] + insertion + text[caret..]);
        box.SetCaretOffset(caret + insertion.Length);
        return true;
    }

    /// <summary>
    /// Handles Shift+Enter in a TextBox: if the current line starts with a bullet or numbered
    /// list prefix, inserts a new continued line at the caret (unordered bullet repeated;
    /// ordered number incremented). If the line contains only the prefix with no further
    /// content, the prefix is removed and the caret is placed at the start of the now-empty
    /// line (list escape). Returns <see langword="true"/> if handled.
    /// </summary>
    internal static bool ContinueListOnShiftEnter(TextBox box)
    {
        var text  = box.Text;
        var caret = box.CaretIndex;

        var lineStart = caret == 0 ? 0 : (text.LastIndexOf('\n', caret - 1) + 1);
        var lineEnd   = text.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        var lineText  = text[lineStart..lineEnd];

        var m = ListBulletRegex.Match(lineText);
        if (!m.Success) return false;

        // Escape: the line is nothing but the prefix — strip it and park the caret there.
        if (lineText.Length == m.Length) {
            box.Text       = text[..lineStart] + text[(lineStart + m.Length)..];
            box.CaretIndex = lineStart;
            return true;
        }

        var indent = m.Groups["indent"].Value;
        string nextPrefix;
        if (m.Groups["num"].Success && int.TryParse(m.Groups["num"].Value, out int n))
            nextPrefix = indent + (n + 1) + ". ";
        else
            nextPrefix = indent + m.Groups["sym"].Value + " ";

        var insertion  = "\n" + nextPrefix;
        box.Text       = text[..caret] + insertion + text[caret..];
        box.CaretIndex = caret + insertion.Length;
        return true;
    }

    /// <summary>
    /// Duplicates the current line and moves the caret to the start of the new copy.
    /// Uses <c>SelectedText</c> insertion so the operation participates in the undo stack
    /// as a single step.
    /// </summary>
    internal static void DuplicateLine(TextBox box)
    {
        var text  = box.Text.Replace("\r\n", "\n").Replace("\r", "\n");
        var caret = box.CaretIndex;

        var lineStart = caret == 0 ? 0 : (text.LastIndexOf('\n', caret - 1) + 1);
        var lineEnd   = text.IndexOf('\n', caret);
        if (lineEnd < 0) lineEnd = text.Length;

        var line = text[lineStart..lineEnd];

        // Insert via SelectedText so the action joins the undo stack as one step
        // rather than replacing box.Text wholesale (which clears undo history).
        box.SelectionStart  = lineEnd;
        box.SelectionLength = 0;
        box.SelectedText    = "\n" + line;
        box.CaretIndex      = lineEnd + 1; // start of the new duplicate line
    }

    /// <summary>
    /// RichTextBox overload of <see cref="DuplicateLine(TextBox)"/>.
    /// Inserts a new <see cref="Paragraph"/> after the current one inside a
    /// <c>BeginChange</c>/<c>EndChange</c> block so the whole operation is a
    /// single undo step (avoids the full-document replace that <c>SetPlainText</c>
    /// would cause, which wipes undo history).
    /// </summary>
    internal static void DuplicateLine(RichTextBox box)
    {
        var currentPara = box.CaretPosition?.Paragraph;
        if (currentPara is null) return;

        // Collect the line text from all Run inlines (skips InlineUIContainer nodes).
        var lineText = string.Concat(
            currentPara.Inlines.OfType<Run>().Select(r => r.Text));

        box.BeginChange();
        try
        {
            var newPara = new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(lineText))
            { Margin = new System.Windows.Thickness(0) };

            box.Document.Blocks.InsertAfter(currentPara, newPara);
            box.CaretPosition = newPara.ContentStart;
        }
        finally { box.EndChange(); }
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static (string[] lines, int rangeStart, int rangeEnd) GetSelectedLines(
        string text, int selStart, int selLen)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        int rangeEnd   = selLen > 0 ? selStart + selLen : selStart;
        int lineStart  = selStart == 0 ? 0 : (text.LastIndexOf('\n', selStart - 1) + 1);
        int lineEnd    = text.IndexOf('\n', rangeEnd);
        if (lineEnd < 0) lineEnd = text.Length;
        var lines = text[lineStart..lineEnd].Split('\n');
        return (lines, lineStart, lineEnd);
    }

    private static void ReplaceLines(TextBox box, string replacement, int rangeStart, int rangeEnd)
    {
        var text = box.Text.Replace("\r\n", "\n").Replace("\r", "\n");
        box.Text            = text[..rangeStart] + replacement + text[rangeEnd..];
        box.SelectionStart  = rangeStart;
        box.SelectionLength = replacement.Length;
    }

    private static void ReplaceLines(RichTextBox box, string text, string replacement, int rangeStart, int rangeEnd)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        box.SetPlainText(text[..rangeStart] + replacement + text[rangeEnd..]);
        box.SelectRange(rangeStart, replacement.Length);
    }

    /// <summary>
    /// Converts each selected line to a bullet list item (<c>- </c>).
    /// If no text is selected, the current line is converted.
    /// Any existing list marker is replaced rather than doubled.
    /// </summary>
    internal static void ApplyBulletList(TextBox box)
    {
        var (lines, rangeStart, rangeEnd) = GetSelectedLines(box.Text, box.SelectionStart, box.SelectionLength);
        var updated = lines.Select(l =>
        {
            var m      = ListBulletRegex.Match(l);
            var indent = m.Success ? m.Groups["indent"].Value : "";
            var body   = m.Success ? l[m.Length..] : l;
            return $"{indent}- {body}";
        }).ToArray();
        ReplaceLines(box, string.Join("\n", updated), rangeStart, rangeEnd);
    }

    /// <summary>
    /// Converts each selected line to a numbered list item (1. 2. 3. …).
    /// If no text is selected, the current line is converted.
    /// Any existing list marker is replaced.
    /// </summary>
    internal static void ApplyNumberedList(TextBox box)
    {
        var (lines, rangeStart, rangeEnd) = GetSelectedLines(box.Text, box.SelectionStart, box.SelectionLength);
        var updated = lines.Select((l, i) =>
        {
            var m      = ListBulletRegex.Match(l);
            var indent = m.Success ? m.Groups["indent"].Value : "";
            var body   = m.Success ? l[m.Length..] : l;
            return $"{indent}{i + 1}. {body}";
        }).ToArray();
        ReplaceLines(box, string.Join("\n", updated), rangeStart, rangeEnd);
    }

    /// <summary>RichTextBox overload of <see cref="ApplyBulletList(TextBox)"/>.</summary>
    internal static void ApplyBulletList(RichTextBox box)
    {
        var text = box.GetPlainText();
        var (lines, rangeStart, rangeEnd) = GetSelectedLines(text, box.GetSelectionStart(), box.GetSelectionLength());
        var updated = lines.Select(l =>
        {
            var m      = ListBulletRegex.Match(l);
            var indent = m.Success ? m.Groups["indent"].Value : "";
            var body   = m.Success ? l[m.Length..] : l;
            return $"{indent}- {body}";
        }).ToArray();
        ReplaceLines(box, text, string.Join("\n", updated), rangeStart, rangeEnd);
    }

    /// <summary>RichTextBox overload of <see cref="ApplyNumberedList(TextBox)"/>.</summary>
    internal static void ApplyNumberedList(RichTextBox box)
    {
        var text = box.GetPlainText();
        var (lines, rangeStart, rangeEnd) = GetSelectedLines(text, box.GetSelectionStart(), box.GetSelectionLength());
        var updated = lines.Select((l, i) =>
        {
            var m      = ListBulletRegex.Match(l);
            var indent = m.Success ? m.Groups["indent"].Value : "";
            var body   = m.Success ? l[m.Length..] : l;
            return $"{indent}{i + 1}. {body}";
        }).ToArray();
        ReplaceLines(box, text, string.Join("\n", updated), rangeStart, rangeEnd);
    }

    internal static void ApplyBold(TextBox box)
    {
        var selStart = box.SelectionStart;
        var selLen   = box.SelectionLength;

        if (selLen > 0)
        {
            var selected       = box.SelectedText;
            var trimmed        = selected.TrimEnd(' ');
            var trailingSpaces = selected[trimmed.Length..];
            box.SelectedText    = $"**{trimmed}**{trailingSpaces}";
            box.SelectionStart  = selStart;
            box.SelectionLength = trimmed.Length + 4;
        }
        else
        {
            var caret = box.CaretIndex;
            box.Text       = box.Text.Insert(caret, "****");
            box.CaretIndex = caret + 2;
        }
    }

    internal static void ApplyItalic(TextBox box)
    {
        var selStart = box.SelectionStart;
        var selLen   = box.SelectionLength;

        if (selLen > 0)
        {
            var selected       = box.SelectedText;
            var trimmed        = selected.TrimEnd(' ');
            var trailingSpaces = selected[trimmed.Length..];
            box.SelectedText    = $"*{trimmed}*{trailingSpaces}";
            box.SelectionStart  = selStart;
            box.SelectionLength = trimmed.Length + 2;
        }
        else
        {
            var caret = box.CaretIndex;
            box.Text       = box.Text.Insert(caret, "**");
            box.CaretIndex = caret + 1;
        }
    }

    internal static void InsertLink(TextBox box)
    {
        var selStart = box.SelectionStart;
        var selLen   = box.SelectionLength;

        if (selLen > 0)
        {
            var text = box.SelectedText;
            var md   = $"[{text}](url)";
            box.SelectedText    = md;
            box.SelectionStart  = selStart;
            box.SelectionLength = md.Length;
        }
        else
        {
            var caret = box.CaretIndex;
            const string md = "[text](url)";
            box.Text        = box.Text.Insert(caret, md);
            box.SelectionStart  = caret;
            box.SelectionLength = md.Length;
        }
    }

    internal static void InsertTable(TextBox box)
    {
        var caret = box.CaretIndex;
        const string table =
            "| Column 1 | Column 2 | Column 3 |\n" +
            "|----------|----------|----------|\n" +
            "| Cell     | Cell     | Cell     |";
        box.Text        = box.Text.Insert(caret, table);
        box.CaretIndex  = caret + table.Length;
    }

    internal static void InsertInlineCode(TextBox box)
    {
        var selStart = box.SelectionStart;
        var selLen   = box.SelectionLength;

        if (selLen > 0)
        {
            var raw            = box.SelectedText;
            var trimmed        = raw.TrimEnd(' ');
            var trailingSpaces = raw[trimmed.Length..];
            var md             = $"`{trimmed}`{trailingSpaces}";
            box.SelectedText    = md;
            box.SelectionStart  = selStart;
            box.SelectionLength = trimmed.Length + 2;
        }
        else
        {
            var caret = box.CaretIndex;
            box.Text        = box.Text.Insert(caret, "``");
            box.CaretIndex  = caret + 1;
        }
    }

    internal static void InsertHorizontalRule(TextBox box)
    {
        var caret = box.CaretIndex;
        var text  = box.Text;

        // Determine if caret is already at the start of a line (or beginning of text)
        var atLineStart = caret == 0 || text[caret - 1] == '\n';
        // Determine if caret is already at the end of a line (or end of text)
        var atLineEnd = caret == text.Length || text[caret] == '\n';

        var prefix = atLineStart ? "" : "\n";
        var suffix = atLineEnd   ? "\n" : "\n\n";
        var insertion = $"{prefix}---{suffix}";
        box.Text       = text.Insert(caret, insertion);
        box.CaretIndex = caret + insertion.Length;
    }

    internal static void InsertCodeBlock(TextBox box)
    {
        var selStart = box.SelectionStart;
        var selLen   = box.SelectionLength;

        if (selLen > 0)
        {
            var text = box.SelectedText;
            var md   = $"\n```\n{text}\n```\n";
            box.SelectedText    = md;
            box.SelectionStart  = selStart;
            box.SelectionLength = md.Length;
        }
        else
        {
            var caret = box.CaretIndex;
            const string fence = "\n```\n\n```\n";
            box.Text        = box.Text.Insert(caret, fence);
            box.CaretIndex  = caret + 5;
        }
    }

    internal static void ApplyBold(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();

        if (selLen > 0)
        {
            var selected       = box.GetSelectedText();
            var trimmed        = selected.TrimEnd(' ');
            var trailingSpaces = selected[trimmed.Length..];
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection($"**{trimmed}**{trailingSpaces}");
            box.SelectRange(selStart, trimmed.Length + 4);
        }
        else
        {
            var caret = box.GetCaretOffset();
            var text  = box.GetPlainText();
            box.SetPlainText(text.Insert(caret, "****"));
            box.SetCaretOffset(caret + 2);
        }
    }

    internal static void ApplyItalic(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();

        if (selLen > 0)
        {
            var selected       = box.GetSelectedText();
            var trimmed        = selected.TrimEnd(' ');
            var trailingSpaces = selected[trimmed.Length..];
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection($"*{trimmed}*{trailingSpaces}");
            box.SelectRange(selStart, trimmed.Length + 2);
        }
        else
        {
            var caret = box.GetCaretOffset();
            var text  = box.GetPlainText();
            box.SetPlainText(text.Insert(caret, "**"));
            box.SetCaretOffset(caret + 1);
        }
    }

    internal static void InsertLink(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();

        if (selLen > 0)
        {
            var text = box.GetSelectedText();
            var md   = $"[{text}](url)";
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection(md);
            box.SelectRange(selStart, md.Length);
        }
        else
        {
            var caret = box.GetCaretOffset();
            const string md = "[text](url)";
            box.SetPlainText(box.GetPlainText().Insert(caret, md));
            box.SelectRange(caret, md.Length);
        }
    }

    internal static void InsertTable(RichTextBox box)
    {
        var caret = box.GetCaretOffset();
        const string table =
            "| Column 1 | Column 2 | Column 3 |\n" +
            "|----------|----------|----------|\n" +
            "| Cell     | Cell     | Cell     |";
        box.SetPlainText(box.GetPlainText().Insert(caret, table));
        box.SetCaretOffset(caret + table.Length);
    }

    internal static void InsertInlineCode(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();

        if (selLen > 0)
        {
            var raw            = box.GetSelectedText();
            var trimmed        = raw.TrimEnd(' ');
            var trailingSpaces = raw[trimmed.Length..];
            var md             = $"`{trimmed}`{trailingSpaces}";
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection(md);
            box.SelectRange(selStart, trimmed.Length + 2);
        }
        else
        {
            var caret = box.GetCaretOffset();
            box.SetPlainText(box.GetPlainText().Insert(caret, "``"));
            box.SetCaretOffset(caret + 1);
        }
    }

    internal static void InsertHorizontalRule(RichTextBox box)
    {
        var caret = box.GetCaretOffset();
        var text  = box.GetPlainText();

        var atLineStart = caret == 0 || text[caret - 1] == '\n';
        var atLineEnd   = caret == text.Length || text[caret] == '\n';

        var prefix    = atLineStart ? "" : "\n";
        var suffix    = atLineEnd   ? "\n" : "\n\n";
        var insertion = $"{prefix}---{suffix}";
        box.SetPlainText(text.Insert(caret, insertion));
        box.SetCaretOffset(caret + insertion.Length);
    }

    internal static void InsertCodeBlock(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();

        if (selLen > 0)
        {
            var text = box.GetSelectedText();
            var md   = $"\n```\n{text}\n```\n";
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection(md);
            box.SelectRange(selStart, md.Length);
        }
        else
        {
            var caret = box.GetCaretOffset();
            const string fence = "\n```\n\n```\n";
            box.SetPlainText(box.GetPlainText().Insert(caret, fence));
            box.SetCaretOffset(caret + 5);
        }
    }

    // ── Selection embedding ───────────────────────────────────────────────────

    internal static bool ApplyInlineCodeOrFence(TextBox box)
    {
        var selStart = box.SelectionStart;
        var selLen   = box.SelectionLength;
        if (selLen == 0) return false;

        var raw = box.SelectedText;
        if (raw.Contains('\n'))
        {
            var (leading, core, trailing) = SplitBoundaryBlanks(raw);
            var result = $"{leading}```\n{core}\n```{trailing}";
            box.SelectedText    = result;
            box.SelectionStart  = selStart;
            box.SelectionLength = result.Length;
            return true;
        }

        var trimmed        = raw.Trim(' ');
        var leadingSpaces  = raw[..(raw.Length - raw.TrimStart(' ').Length)];
        var trailingSpaces = raw[raw.TrimEnd(' ').Length..];
        var step1 = $"{leadingSpaces}`{trimmed}`{trailingSpaces}";

        // Step 1: wrap in backticks; selection covers inner content only.
        box.SelectedText    = step1;
        box.SelectionStart  = selStart + leadingSpaces.Length + 1;
        box.SelectionLength = trimmed.Length;

        // Step 2 (multi-word only): camelCase-transform the inner content.
        // Runs as a second undo record so Ctrl+Z steps back through both changes.
        if (trimmed.Contains(' '))
        {
            var camel = ToCamelCaseIdentifier(trimmed);
            box.SelectedText = camel;
        }

        return true;
    }

    internal static bool ApplyInlineQuote(TextBox box)
    {
        var selLen = box.SelectionLength;
        if (selLen == 0) return false;

        var raw = box.SelectedText;
        if (raw.Contains('\n')) return false;

        var selStart       = box.SelectionStart;
        var trimmed        = raw.Trim(' ');
        var leadingSpaces  = raw[..(raw.Length - raw.TrimStart(' ').Length)];
        var trailingSpaces = raw[raw.TrimEnd(' ').Length..];
        var result         = $"{leadingSpaces}\"{trimmed}\"{trailingSpaces}";
        // Step 1: replace selection with the pressed char — creates undo record 1
        box.SelectedText = "\"";
        box.Select(selStart, 1);
        // Step 2: replace with full wrap — creates undo record 2
        box.SelectedText    = result;
        box.SelectionStart  = selStart + leadingSpaces.Length;
        box.SelectionLength = 1 + trimmed.Length + 1;
        return true;
    }

    internal static bool ApplyInlineCodeOrFence(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();
        if (selLen == 0) return false;

        var raw = box.GetSelectedText();
        if (raw.Contains('\n'))
        {
            var (leading, core, trailing) = SplitBoundaryBlanks(raw);
            var result = $"{leading}```\n{core}\n```{trailing}";
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection(result);
            box.SelectRange(selStart, result.Length);
            return true;
        }

        var trimmed        = raw.Trim(' ');
        var leadingSpaces  = raw[..(raw.Length - raw.TrimStart(' ').Length)];
        var trailingSpaces = raw[raw.TrimEnd(' ').Length..];
        var step1 = $"{leadingSpaces}`{trimmed}`{trailingSpaces}";

        // Step 1: wrap in backticks; selection covers inner content only.
        box.SelectRange(selStart, selLen);
        box.ReplaceSelection(step1);
        box.SelectRange(selStart + leadingSpaces.Length + 1, trimmed.Length);

        // Step 2 (multi-word only): camelCase-transform the inner content.
        if (trimmed.Contains(' '))
        {
            var camel = ToCamelCaseIdentifier(trimmed);
            box.ReplaceSelection(camel);
        }

        return true;
    }

    internal static bool ApplyInlineQuote(RichTextBox box)
    {
        var selStart = box.GetSelectionStart();
        var selLen   = box.GetSelectionLength();
        if (selLen == 0) return false;

        var raw = box.GetSelectedText();
        if (raw.Contains('\n')) return false;

        var trimmed        = raw.Trim(' ');
        var leadingSpaces  = raw[..(raw.Length - raw.TrimStart(' ').Length)];
        var trailingSpaces = raw[raw.TrimEnd(' ').Length..];
        var result         = $"{leadingSpaces}\"{trimmed}\"{trailingSpaces}";
        // Step 1: replace selection with the pressed char — creates undo record 1
        box.SelectRange(selStart, selLen);
        box.ReplaceSelection("\"");
        box.SelectRange(selStart, 1);
        // Step 2: replace with full wrap — creates undo record 2
        box.ReplaceSelection(result);
        box.SelectRange(selStart + leadingSpaces.Length, 1 + trimmed.Length + 1);
        return true;
    }

    internal static bool ApplyInlineParens(TextBox box, string pressedChar)
    {
        var selLen = box.SelectionLength;
        if (selLen == 0) return false;

        var raw = box.SelectedText;
        if (raw.Contains('\n')) return false;

        var selStart       = box.SelectionStart;
        var trimmed        = raw.Trim(' ');
        var leadingSpaces  = raw[..(raw.Length - raw.TrimStart(' ').Length)];
        var trailingSpaces = raw[raw.TrimEnd(' ').Length..];
        var result         = $"{leadingSpaces}({trimmed}){trailingSpaces}";
        // Step 1: replace selection with the pressed char — creates undo record 1
        box.SelectedText = pressedChar;
        box.Select(selStart, pressedChar.Length);
        // Step 2: replace with full wrap — creates undo record 2
        box.SelectedText    = result;
        box.SelectionStart  = selStart + leadingSpaces.Length;
        box.SelectionLength = 1 + trimmed.Length + 1;
        return true;
    }

    internal static bool ApplyInlineParens(RichTextBox box, string pressedChar)
    {
        var selLen = box.GetSelectionLength();
        if (selLen == 0) return false;

        var raw = box.GetSelectedText();
        if (raw.Contains('\n')) return false;

        var selStart       = box.GetSelectionStart();
        var trimmed        = raw.Trim(' ');
        var leadingSpaces  = raw[..(raw.Length - raw.TrimStart(' ').Length)];
        var trailingSpaces = raw[raw.TrimEnd(' ').Length..];
        var result         = $"{leadingSpaces}({trimmed}){trailingSpaces}";
        // Step 1: replace selection with the pressed char — creates undo record 1
        box.SelectRange(selStart, selLen);
        box.ReplaceSelection(pressedChar);
        box.SelectRange(selStart, pressedChar.Length);
        // Step 2: replace with full wrap — creates undo record 2
        box.ReplaceSelection(result);
        box.SelectRange(selStart + leadingSpaces.Length, 1 + trimmed.Length + 1);
        return true;
    }

    /// <summary>
    /// Splits <paramref name="raw"/> into leading blank lines, core content, and
    /// trailing blank lines. A "blank line" is one containing only whitespace.
    /// The three parts reconstruct the original: <c>leading + core + trailing == normalized</c>.
    /// Leading/trailing parts include their terminating/leading newline so they can be
    /// placed directly outside the fence without adding extra blank lines.
    /// </summary>
    private static (string Leading, string Core, string Trailing) SplitBoundaryBlanks(string raw)
    {
        raw = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = raw.Split('\n');
        int lo = 0, hi = lines.Length - 1;
        while (lo <= hi && string.IsNullOrWhiteSpace(lines[lo])) lo++;
        while (hi >= lo && string.IsNullOrWhiteSpace(lines[hi])) hi--;

        if (lo > hi) // entire selection was blank lines
            return (raw, string.Empty, string.Empty);

        var leading  = lo > 0               ? string.Join("\n", lines[..lo])          + "\n" : string.Empty;
        var core     = string.Join("\n", lines[lo..(hi + 1)]);
        var trailing = hi < lines.Length - 1 ? "\n" + string.Join("\n", lines[(hi + 1)..]) : string.Empty;
        return (leading, core, trailing);
    }

    private static string ToCamelCaseIdentifier(string raw)
    {
        if (!raw.Contains(' ') && !raw.Contains('\t')) return raw;

        // Normalize "dot EXTENSION" at end → ".extension"
        var normalized = Regex.Replace(raw.Trim(),
            @"\s+[Dd][Oo][Tt]\s+([A-Za-z]{1,6})\s*$",
            m => "." + m.Groups[1].Value.ToLower());

        // Split by whitespace
        var parts = normalized.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return raw;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;
            if (i == 0)
            {
                sb.Append(part.ToLower());
            }
            else if (part[0] == '.')
            {
                // Already a .extension segment — keep as-is (already lowercased)
                sb.Append(part);
            }
            else
            {
                sb.Append(char.ToUpper(part[0]));
                sb.Append(part[1..].ToLower());
            }
        }
        return sb.ToString();
    }
}
