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
            var text = box.SelectedText;
            var md   = $"`{text}`";
            box.SelectedText    = md;
            box.SelectionStart  = selStart;
            box.SelectionLength = md.Length;
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
            var text = box.GetSelectedText();
            var md   = $"`{text}`";
            box.SelectRange(selStart, selLen);
            box.ReplaceSelection(md);
            box.SelectRange(selStart, md.Length);
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
}
