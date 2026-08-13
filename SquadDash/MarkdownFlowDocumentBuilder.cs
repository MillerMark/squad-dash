using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;
internal static class MarkdownFlowDocumentBuilder {
    private static readonly Brush DefaultForegroundBrush    = new SolidColorBrush(Color.FromRgb(0x32, 0x2A, 0x23));
    private static readonly Brush DefaultQuoteFillBrush     = new SolidColorBrush(Color.FromRgb(0xF6, 0xF1, 0xE8));
    private static readonly Brush DefaultQuoteBorderBrush   = new SolidColorBrush(Color.FromRgb(0xD5, 0xCA, 0xBA));
    private static readonly Brush DefaultTableBorderBrush   = new SolidColorBrush(Color.FromArgb(0x38, 0x40, 0x40, 0x40));
    private static readonly Brush DefaultTableHeaderBrush   = new SolidColorBrush(Color.FromArgb(0x18, 0x40, 0x40, 0x40));

    private static Brush Res(string key, Brush fallback) =>
        Application.Current?.Resources[key] as Brush ?? fallback;

    public static FlowDocument Build(string markdown, double baseFontSize = 0) =>
        BuildWithMapCore(markdown, out _, baseFontSize, styleCodeIdentifiers: false);

    /// <summary>
    /// Builds Inbox Markdown and automatically gives camelCase/PascalCase identifiers the same
    /// treatment as explicit backtick code spans. Other Markdown consumers retain their existing
    /// rendering through <see cref="Build"/>.
    /// </summary>
    public static FlowDocument BuildInbox(string markdown, double baseFontSize = 0) =>
        BuildWithMapCore(markdown, out _, baseFontSize, styleCodeIdentifiers: true);

    /// <summary>
    /// Adds Inbox-style inline content to an existing WPF text surface. Explicit backtick spans
    /// and camelCase/PascalCase identifiers receive the standard code-reference treatment.
    /// </summary>
    internal static void AddInboxInlineText(InlineCollection inlines, string text) =>
        AddInlineText(inlines, text, styleCodeIdentifiers: true);

    /// <summary>
    /// Builds a <see cref="FlowDocument"/> from <paramref name="markdown"/> and also returns,
    /// for each block in document order, the 0-based (StartLine, EndLine) range in the
    /// normalised input that produced it.  Use this to map rendered blocks back to source.
    /// </summary>
    public static FlowDocument BuildWithMap(string markdown, out List<(int StartLine, int EndLine)> blockLineRanges, double baseFontSize = 0) =>
        BuildWithMapCore(markdown, out blockLineRanges, baseFontSize, styleCodeIdentifiers: false);

    private static FlowDocument BuildWithMapCore(
        string markdown,
        out List<(int StartLine, int EndLine)> blockLineRanges,
        double baseFontSize,
        bool styleCodeIdentifiers) {
        blockLineRanges = new List<(int, int)>();

        if (baseFontSize <= 0)
            baseFontSize = Application.Current?.Resources["FontSizeMedium"] as double? ?? 13.0;

        var foreground   = Res("LabelText",          DefaultForegroundBrush);
        var quoteFill    = Res("QuoteSurface",        DefaultQuoteFillBrush);
        var quoteBorder  = Res("QuoteBorder",         DefaultQuoteBorderBrush);
        var tableRule    = Res("TableRule",           DefaultTableBorderBrush);
        var tableHeader  = Res("TableHeaderSurface",  DefaultTableHeaderBrush);

        _ = quoteBorder; // declared for future use; suppress unused-variable warning

        var document = new FlowDocument {
            FontFamily    = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize      = baseFontSize,
            Foreground    = foreground,
            Background    = Brushes.Transparent,   // let the viewer's background show through the page
            PagePadding   = new Thickness(18),
            TextAlignment = TextAlignment.Left     // left align text; never use full justification
        };

        var lines = Normalize(markdown).Split('\n');

        for (var index = 0; index < lines.Length; index++) {
            var startIndex = index;
            var line = lines[index];
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed)) {
                // Use a minimal spacer instead of a full-height empty paragraph.
                document.Blocks.Add(new Paragraph { Margin = new Thickness(0), LineHeight = 6 });
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                var codeLines = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) {
                    codeLines.Add(lines[index]);
                    index++;
                }

                document.Blocks.Add(BuildCodeBlock(string.Join(Environment.NewLine, codeLines)));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (TryReadTable(lines, ref index, out var tableRows)) {
                document.Blocks.Add(BuildTable(tableRows, tableRule, tableHeader, baseFontSize, styleCodeIdentifiers));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)) {
                document.Blocks.Add(BuildHeading(trimmed, baseFontSize, styleCodeIdentifiers));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal)) {
                document.Blocks.Add(BuildQuote(trimmed[2..].Trim(), quoteFill, foreground, styleCodeIdentifiers));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) {
                // Collect list items, merging indented continuation lines into the preceding item.
                var currentItem = new System.Text.StringBuilder(trimmed[2..].Trim());
                var listItems   = new List<string>();
                while (index + 1 < lines.Length) {
                    var nextRaw = lines[index + 1];
                    var next    = nextRaw.Trim();
                    if (next.StartsWith("- ", StringComparison.Ordinal) || next.StartsWith("* ", StringComparison.Ordinal)) {
                        // New sibling list item.
                        listItems.Add(currentItem.ToString());
                        currentItem = new System.Text.StringBuilder(next[2..].Trim());
                        index++;
                    } else if (!string.IsNullOrWhiteSpace(next) &&
                               nextRaw.Length > 0 && char.IsWhiteSpace(nextRaw[0])) {
                        // Indented continuation of the current item — join as a single line.
                        currentItem.Append(' ').Append(next);
                        index++;
                    } else {
                        break;
                    }
                }
                listItems.Add(currentItem.ToString());

                document.Blocks.Add(BuildList(listItems, styleCodeIdentifiers));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (IsOrderedListItem(trimmed)) {
                var dotIdx = trimmed.IndexOf(". ", StringComparison.Ordinal);
                var currentItem = new System.Text.StringBuilder(trimmed[(dotIdx + 2)..].Trim());
                var listItems   = new List<string>();
                while (index + 1 < lines.Length) {
                    var nextRaw = lines[index + 1];
                    var next    = nextRaw.Trim();
                    if (IsOrderedListItem(next)) {
                        listItems.Add(currentItem.ToString());
                        var nextDotIdx = next.IndexOf(". ", StringComparison.Ordinal);
                        currentItem = new System.Text.StringBuilder(next[(nextDotIdx + 2)..].Trim());
                        index++;
                    } else if (!string.IsNullOrWhiteSpace(next) &&
                               nextRaw.Length > 0 && char.IsWhiteSpace(nextRaw[0])) {
                        currentItem.Append(' ').Append(next);
                        index++;
                    } else if (string.IsNullOrWhiteSpace(next) &&
                               index + 2 < lines.Length &&
                               IsOrderedListItem(lines[index + 2].Trim())) {
                        // Blank line between ordered list items (loose list) — skip the blank
                        // and continue collecting so all items end up in one sequential list.
                        listItems.Add(currentItem.ToString());
                        index += 2; // skip blank, land on next item line
                        var lookaheadRaw = lines[index];
                        var lookahead    = lookaheadRaw.Trim();
                        var nextDotIdx   = lookahead.IndexOf(". ", StringComparison.Ordinal);
                        currentItem = new System.Text.StringBuilder(lookahead[(nextDotIdx + 2)..].Trim());
                    } else {
                        break;
                    }
                }
                listItems.Add(currentItem.ToString());
                document.Blocks.Add(BuildOrderedList(listItems, styleCodeIdentifiers));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            if (IsHorizontalRule(trimmed)) {
                document.Blocks.Add(new BlockUIContainer(new Border {
                    Height = 1,
                    Margin = new Thickness(0),
                    Background = tableRule
                }));
                blockLineRanges.Add((startIndex, index));
                continue;
            }

            // Standard markdown: consecutive non-blank plain-text lines form one paragraph.
            // Only a blank line creates a paragraph break.
            var paraLines = new System.Text.StringBuilder(trimmed);
            while (index + 1 < lines.Length) {
                var nextTrimmed = lines[index + 1].Trim();
                if (string.IsNullOrWhiteSpace(nextTrimmed)) break;
                if (IsSpecialLine(nextTrimmed)) break;
                paraLines.Append(' ').Append(nextTrimmed);
                index++;
            }
            document.Blocks.Add(BuildParagraph(paraLines.ToString(), styleCodeIdentifiers));
            blockLineRanges.Add((startIndex, index));
        }

        return document;
    }

    /// <summary>Returns true for lines that start a new markdown block and must not be
    /// absorbed into the preceding paragraph during soft-wrap joining.</summary>
    private static bool IsSpecialLine(string trimmed) =>
        trimmed.StartsWith('#')        ||
        trimmed.StartsWith("- ",  StringComparison.Ordinal) ||
        trimmed.StartsWith("* ",  StringComparison.Ordinal) ||
        trimmed.StartsWith("> ",  StringComparison.Ordinal) ||
        trimmed.StartsWith("```", StringComparison.Ordinal) ||
        IsHorizontalRule(trimmed)      ||
        IsOrderedListItem(trimmed);

    private static bool IsOrderedListItem(string trimmed) {
        var dotIdx = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dotIdx <= 0) return false;
        return trimmed[..dotIdx].All(char.IsAsciiDigit);
    }

    private static string Normalize(string markdown) {
        return (markdown ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private static Paragraph BuildHeading(string line, double baseFontSize, bool styleCodeIdentifiers) {
        var level = line.TakeWhile(character => character == '#').Count();
        var text = line[level..].Trim();
        var size = level switch {
            1 => baseFontSize * (24.0 / 14.0),
            2 => baseFontSize * (20.0 / 14.0),
            3 => baseFontSize * (17.0 / 14.0),
            _ => baseFontSize * (15.0 / 14.0)
        };

        var paragraph = new Paragraph {
            Margin = new Thickness(0, level == 1 ? 4 : 10, 0, 6),
        };
        if (styleCodeIdentifiers)
        {
            paragraph.FontSize = size;
            paragraph.FontWeight = FontWeights.SemiBold;
            AddInlineText(paragraph.Inlines, text, styleCodeIdentifiers: true);
        }
        else
        {
            paragraph.Inlines.Add(new Run(text)
            {
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
            });
        }
        return paragraph;
    }

    private static Paragraph BuildParagraph(string text, bool styleCodeIdentifiers) {
        var paragraph = new Paragraph {
            Margin = new Thickness(0, 0, 0, 10)
        };
        AddInlineText(paragraph.Inlines, text, styleCodeIdentifiers);
        return paragraph;
    }

    private static Block BuildQuote(string text, Brush quoteFill, Brush foreground, bool styleCodeIdentifiers) {
        // Use a flow Paragraph (not BlockUIContainer) so the text is included in
        // FlowDocumentScrollViewer selection/copy operations.
        var paragraph = new Paragraph {
            Background = quoteFill,
            Padding    = new Thickness(12, 8, 12, 8),
            Margin     = new Thickness(0, 2, 0, 10),
            Foreground = foreground,
        };
        AddInlineText(paragraph.Inlines, text, styleCodeIdentifiers);
        return paragraph;
    }

    private static List BuildList(IEnumerable<string> items, bool styleCodeIdentifiers) {
        var list = new List {
            Margin = new Thickness(16, 0, 0, 10),
            MarkerStyle = TextMarkerStyle.Disc
        };

        foreach (var item in items) {
            var paragraph = new Paragraph {
                Margin = new Thickness(0, 0, 0, 4)
            };
            AddInlineText(paragraph.Inlines, item, styleCodeIdentifiers);
            list.ListItems.Add(new ListItem(paragraph));
        }

        return list;
    }

    private static List BuildOrderedList(IEnumerable<string> items, bool styleCodeIdentifiers) {
        var list = new List {
            MarkerStyle = TextMarkerStyle.Decimal,
            Margin      = new Thickness(24, 2, 0, 2),
            Padding     = new Thickness(4, 0, 0, 0),
        };
        foreach (var item in items) {
            var paragraph = new Paragraph {
                Margin = new Thickness(0, 1, 0, 1)
            };
            AddInlineText(paragraph.Inlines, item, styleCodeIdentifiers);
            list.ListItems.Add(new ListItem(paragraph));
        }
        return list;
    }

    private static Block BuildCodeBlock(string code) {
        // Use a flow Paragraph (not BlockUIContainer wrapping a TextBox) so the text
        // participates in FlowDocumentScrollViewer selection and is included when the
        // user copies a selection that spans the code block.
        var paragraph = new Paragraph {
            Padding    = new Thickness(12, 10, 12, 10),
            Margin     = new Thickness(0, 2, 0, 10),
            FontFamily = new FontFamily("Consolas"),
        };
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "CodeText");

        var codeLines = code.Split('\n');
        for (var i = 0; i < codeLines.Length; i++) {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(codeLines[i]));
        }
        return paragraph;
    }

    private static bool TryReadTable(string[] lines, ref int index, out List<string[]> rows) {
        rows = new List<string[]>();

        if (!IsTableRow(lines[index]))
            return false;

        if (index + 1 >= lines.Length || !IsTableSeparator(lines[index + 1]))
            return false;

        rows.Add(ParseTableRow(lines[index]));
        index++;

        while (index + 1 < lines.Length && IsTableRow(lines[index + 1])) {
            rows.Add(ParseTableRow(lines[index + 1]));
            index++;
        }

        return rows.Count > 0;
    }

    private static Block BuildTable(
        IReadOnlyList<string[]> rows,
        Brush tableRule,
        Brush tableHeader,
        double baseFontSize,
        bool styleCodeIdentifiers) {
        var columnCount = rows.Max(row => row.Length);

        var foreground = Res("LabelText", DefaultForegroundBrush);
        var fontSize   = baseFontSize;

        var grid = new Grid {
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var r = 0; r < rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++) {
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) {
                var text = columnIndex < rows[rowIndex].Length ? rows[rowIndex][columnIndex] : string.Empty;

                var tb = new TextBlock {
                    TextWrapping      = TextWrapping.Wrap,
                    MaxWidth          = 500,
                    TextAlignment     = TextAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground        = foreground,
                    FontSize          = fontSize,
                };
                if (styleCodeIdentifiers)
                    AddInlineText(tb.Inlines, text, styleCodeIdentifiers: true);
                else
                    tb.Text = text;

                var cell = new Border {
                    BorderBrush     = tableRule,
                    BorderThickness = new Thickness(0, 0, 0.5, 0.5),
                    Padding         = new Thickness(8, 5, 8, 5),
                    Background      = rowIndex == 0 ? tableHeader : Brushes.Transparent,
                    Child           = tb,
                };

                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        var outerBorder = new Border {
            BorderBrush         = tableRule,
            BorderThickness     = new Thickness(0.5, 0.5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child               = grid,
        };

        return new BlockUIContainer(outerBorder) {
            Margin = new Thickness(0, 2, 0, 12),
        };
    }

    private static bool IsTableRow(string line) {
        var trimmed = line.Trim();
        return trimmed.StartsWith("|", StringComparison.Ordinal) &&
               trimmed.EndsWith("|", StringComparison.Ordinal) &&
               trimmed.Count(character => character == '|') >= 2;
    }

    private static bool IsTableSeparator(string line) {
        if (!IsTableRow(line))
            return false;

        var cells = ParseTableRow(line);
        return cells.All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' '));
    }

    private static string[] ParseTableRow(string line) {
        const string placeholder = "\x00PIPE\x00";
        return line
            .Trim()
            .Replace(@"\|", placeholder)
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim().Replace(placeholder, "|"))
            .ToArray();
    }

    private static bool IsHorizontalRule(string line) {
        return line.Length >= 3 && line.All(character => character is '-' or '_' or '*');
    }

    // Colored-circle emoji that WPF cannot render from font glyphs — replaced with drawn ellipses.
    private static readonly Dictionary<string, Color> CircleEmojiColors = new() {
        { "🔴", Color.FromRgb(0xE5, 0x39, 0x35) },
        { "🟠", Color.FromRgb(0xF4, 0x51, 0x1E) },
        { "🟡", Color.FromRgb(0xFF, 0xB3, 0x00) },
        { "🟢", Color.FromRgb(0x43, 0xA0, 0x47) },
        { "🔵", Color.FromRgb(0x1E, 0x88, 0xE5) },
        { "🟣", Color.FromRgb(0x8E, 0x24, 0xAA) },
        { "⚫", Color.FromRgb(0x21, 0x21, 0x21) },
        { "⚪", Color.FromRgb(0xDD, 0xDD, 0xDD) },
        { "🟤", Color.FromRgb(0x6D, 0x4C, 0x41) },
    };

    private static void AddInlineText(
        InlineCollection inlines,
        string text,
        bool styleCodeIdentifiers) {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('`');

        for (var index = 0; index < segments.Length; index++) {
            if (segments[index].Length == 0)
                continue;

            if (index % 2 == 1) {
                // Inside backtick code span — emit as-is in monospace.
                inlines.Add(CreateInlineCodeRun(segments[index]));
                continue;
            }

            // Outside code span — split on colored-circle emoji and draw them as Ellipse.
            AddTextWithCircleEmoji(inlines, segments[index], styleCodeIdentifiers);
        }
    }

    // Matches **bold**, __bold__, *italic*, _italic_ — bold patterns listed first so ** beats *.
    private static readonly System.Text.RegularExpressions.Regex BoldItalicRegex = new(
        @"\*\*(.+?)\*\*|__(.+?)__|(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)|(?<!_)_(?!_)(.+?)(?<!_)_(?!_)",
        System.Text.RegularExpressions.RegexOptions.Singleline);

    private static readonly System.Text.RegularExpressions.Regex CodeIdentifierRegex = new(
        @"(?<![\p{L}\p{N}_])(?=[\p{L}\p{N}_.]*[a-z0-9][A-Z])_?[\p{L}][\p{L}\p{N}_]*(?:\.[\p{L}\p{N}_]+)*(?![\p{L}\p{N}_])",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void AddFormattedRuns(
        InlineCollection inlines,
        string text,
        bool styleCodeIdentifiers) {
        if (string.IsNullOrEmpty(text)) return;
        int pos = 0;
        foreach (System.Text.RegularExpressions.Match m in BoldItalicRegex.Matches(text)) {
            if (m.Index > pos)
                AddPlainRuns(inlines, text[pos..m.Index], styleCodeIdentifiers);
            if (m.Groups[1].Success || m.Groups[2].Success)
                AddPlainRuns(
                    inlines,
                    m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value,
                    styleCodeIdentifiers,
                    FontWeights.Bold,
                    null);
            else
                AddPlainRuns(
                    inlines,
                    m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value,
                    styleCodeIdentifiers,
                    null,
                    FontStyles.Italic);
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            AddPlainRuns(inlines, text[pos..], styleCodeIdentifiers);
    }

    private static void AddPlainRuns(
        InlineCollection inlines,
        string text,
        bool styleCodeIdentifiers,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null) {
        if (string.IsNullOrEmpty(text)) return;
        if (!styleCodeIdentifiers) {
            inlines.Add(CreateRun(text, fontWeight, fontStyle));
            return;
        }

        var position = 0;
        foreach (System.Text.RegularExpressions.Match match in CodeIdentifierRegex.Matches(text)) {
            if (match.Index > position)
                inlines.Add(CreateRun(text[position..match.Index], fontWeight, fontStyle));
            inlines.Add(CreateInlineCodeRun(match.Value, fontWeight, fontStyle));
            position = match.Index + match.Length;
        }
        if (position < text.Length)
            inlines.Add(CreateRun(text[position..], fontWeight, fontStyle));
    }

    private static Run CreateRun(string text, FontWeight? fontWeight, FontStyle? fontStyle) {
        var run = new Run(text);
        if (fontWeight is not null) run.FontWeight = fontWeight.Value;
        if (fontStyle is not null) run.FontStyle = fontStyle.Value;
        return run;
    }

    private static Run CreateInlineCodeRun(
        string text,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null) {
        var run = CreateRun(text, fontWeight, fontStyle);
        run.FontFamily = new FontFamily("Consolas");
        run.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
        run.SetResourceReference(TextElement.ForegroundProperty, "CodeText");
        return run;
    }

    private static void AddTextWithCircleEmoji(
        InlineCollection inlines,
        string text,
        bool styleCodeIdentifiers) {
        // Walk through the string, splitting out any known circle emoji.
        var remaining = text;
        while (remaining.Length > 0) {
            // Find the earliest emoji occurrence.
            var earliestIdx = -1;
            var earliestEmoji = string.Empty;
            foreach (var emoji in CircleEmojiColors.Keys) {
                var idx = remaining.IndexOf(emoji, StringComparison.Ordinal);
                if (idx >= 0 && (earliestIdx < 0 || idx < earliestIdx)) {
                    earliestIdx  = idx;
                    earliestEmoji = emoji;
                }
            }

            if (earliestIdx < 0) {
                // No more emoji — emit the rest with bold/italic formatting.
                AddFormattedRuns(inlines, remaining, styleCodeIdentifiers);
                break;
            }

            // Emit text before the emoji with bold/italic formatting.
            if (earliestIdx > 0)
                AddFormattedRuns(inlines, remaining[..earliestIdx], styleCodeIdentifiers);

            // Emit the emoji as a drawn circle.
            var color  = CircleEmojiColors[earliestEmoji];
            var brush  = new SolidColorBrush(color);
            var ellipse = new System.Windows.Shapes.Ellipse {
                Width   = 11,
                Height  = 11,
                Fill    = brush,
                Margin  = new Thickness(0, 0, 2, -1),
            };
            inlines.Add(new InlineUIContainer(ellipse));

            remaining = remaining[(earliestIdx + earliestEmoji.Length)..];
        }
    }
}
