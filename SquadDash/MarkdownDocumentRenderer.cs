using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Builds WPF FlowDocument blocks from markdown-flavoured response text and provides the
/// <c>AppendLine</c> / <c>AppendText</c> helpers that write to transcript threads.
/// Extracted from MainWindow to keep UI document-building concerns cohesive.
/// </summary>
internal sealed class MarkdownDocumentRenderer {
    // ── Timing constant (matches original MainWindow constant) ─────────────
    internal static readonly TimeSpan QuickReplyAgentContinuationWindow = TimeSpan.FromMinutes(3);

    // ── Injected dependencies ──────────────────────────────────────────────
    private readonly Func<double>                                           _getFontSize;
    private readonly Func<string?>                                          _getWorkspaceGitHubUrl;
    private readonly Action<string>                                         _onLinkClicked;
    private readonly Action<string, Exception>                              _onException;
    private readonly Func<TranscriptResponseEntry, TranscriptThreadState?>  _resolveContinuationThread;
    private readonly RoutedEventHandler                                     _onQuickReplyButtonClick;
    private readonly Action<TranscriptThreadState, string, bool>            _appendResponseSegment;
    private readonly Action<TranscriptThreadState>                          _scrollToEndIfAtBottom;
    private readonly Func<TranscriptThreadState>                            _getCoordinatorThread;

    // ── Owned state (read by MainWindow via properties) ────────────────────
    internal string[]                CurrentQuickReplyOptions { get; private set; } = [];
    internal TranscriptResponseEntry? LastQuickReplyEntry      { get; private set; }

    internal void ClearLastQuickReplyEntry() => LastQuickReplyEntry = null;

    private Run? _currentHintRun;

    internal void DismissKeyboardHint() {
        if (_currentHintRun is null) return;
        _currentHintRun.Text = string.Empty;
        _currentHintRun = null;
    }

    // ── Constructor ────────────────────────────────────────────────────────
    internal MarkdownDocumentRenderer(
        Func<double>                                           getFontSize,
        Func<string?>                                          getWorkspaceGitHubUrl,
        Action<string>                                         onLinkClicked,
        Action<string, Exception>                              onException,
        Func<TranscriptResponseEntry, TranscriptThreadState?>  resolveContinuationThread,
        RoutedEventHandler                                     onQuickReplyButtonClick,
        Action<TranscriptThreadState, string, bool>            appendResponseSegment,
        Action<TranscriptThreadState>                          scrollToEndIfAtBottom,
        Func<TranscriptThreadState>                            getCoordinatorThread) {
        _getFontSize               = getFontSize;
        _getWorkspaceGitHubUrl     = getWorkspaceGitHubUrl;
        _onLinkClicked             = onLinkClicked;
        _onException               = onException;
        _resolveContinuationThread = resolveContinuationThread;
        _onQuickReplyButtonClick   = onQuickReplyButtonClick;
        _appendResponseSegment     = appendResponseSegment;
        _scrollToEndIfAtBottom     = scrollToEndIfAtBottom;
        _getCoordinatorThread      = getCoordinatorThread;
    }

    // ── Transcript append helpers ──────────────────────────────────────────

    internal void AppendLine(string text, Brush? color = null) =>
        AppendLine(_getCoordinatorThread(), text, color);

    internal void AppendLine(TranscriptThreadState thread, string text, Brush? color = null) {
        if (thread.CurrentTurn is not null) {
            if (thread.CurrentTurn.ResponseTextBuilder.Length > 0)
                thread.CurrentTurn.ResponseTextBuilder.AppendLine();
            if (!string.IsNullOrEmpty(text))
                thread.CurrentTurn.ResponseTextBuilder.Append(text);
            _appendResponseSegment(thread, text, true);
            _scrollToEndIfAtBottom(thread);
            return;
        }

        var paragraph = CreateTranscriptParagraph();

        if (!string.IsNullOrEmpty(text)) {
            if (color is null) {
                AppendInlineMarkdown(paragraph.Inlines, text);
            }
            else {
                var run = new Run(text) {
                    Foreground = color
                };
                paragraph.Inlines.Add(run);
            }
        }

        thread.Document.Blocks.Add(paragraph);
        _scrollToEndIfAtBottom(thread);
    }

    internal void AppendText(string text) =>
        AppendText(_getCoordinatorThread(), text);

    internal void AppendText(TranscriptThreadState thread, string text) {
        if (string.IsNullOrEmpty(text))
            return;

        thread.CurrentTurn?.ResponseTextBuilder.Append(text);
        _appendResponseSegment(thread, text, false);
        _scrollToEndIfAtBottom(thread);
    }

    internal static void AppendParagraphText(
        Paragraph paragraph,
        string? text,
        Brush? color = null,
        bool startOnNewLine = false) {
        if (paragraph is null || string.IsNullOrEmpty(text))
            return;

        if (startOnNewLine && paragraph.Inlines.Count > 0)
            paragraph.Inlines.Add(new LineBreak());

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('\n');

        for (var index = 0; index < segments.Length; index++) {
            if (index > 0)
                paragraph.Inlines.Add(new LineBreak());

            if (segments[index].Length == 0)
                continue;

            var run = new Run(segments[index]);
            if (color is not null)
                run.Foreground = color;

            paragraph.Inlines.Add(run);
        }
    }

    // ── Paragraph factory ──────────────────────────────────────────────────

    internal Paragraph CreateTranscriptParagraph(double bottomMargin = 6) {
        return new Paragraph {
            Margin = new Thickness(0, 0, 0, bottomMargin)
        };
    }

    /// <summary>Returns a heading font size proportional to the base transcript size.</summary>
    internal static double HeadingFontSize(int level, double baseFontSize) =>
        level == 1 ? baseFontSize * (18.0 / 14.0)
        : level == 2 ? baseFontSize * (16.0 / 14.0)
        : baseFontSize; // H3+ = same as body; bold weight provides the visual distinction

    // ── Response block builder ─────────────────────────────────────────────

    internal IEnumerable<Block> BuildResponseBlocks(
        TranscriptResponseEntry entry,
        string responseText,
        bool allowQuickReplies) {
        var quickReplyOptions = Array.Empty<QuickReplyOptionMetadata>();
        if (QuickReplyOptionParser.TryExtractWithMetadata(responseText, out var cleanedResponseText, out var extractedOptions)) {
            responseText = cleanedResponseText;
            if (allowQuickReplies)
                quickReplyOptions = extractedOptions;
        }

        var normalized = responseText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var paragraphLines = new List<string>();

        for (var index = 0; index < lines.Length; index++) {
            var line = lines[index];
            var trimmed = line.TrimStart();

            // Code fence
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                foreach (var block in BuildParagraphBlocks(paragraphLines))
                    yield return block;
                paragraphLines.Clear();

                index++;
                var codeLines = new List<string>();
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) {
                    codeLines.Add(lines[index]);
                    index++;
                }

                yield return BuildCodeBlock(string.Join("\n", codeLines));
                continue;
            }

            // Table
            if (TryReadMarkdownTable(lines, index, out var nextIndex, out var tableLines)) {
                foreach (var block in BuildParagraphBlocks(paragraphLines))
                    yield return block;
                paragraphLines.Clear();
                yield return BuildMarkdownTable(tableLines);
                index = nextIndex;
                continue;
            }

            paragraphLines.Add(line);
        }

        foreach (var block in BuildParagraphBlocks(paragraphLines))
            yield return block;

        if (quickReplyOptions.Length > 0) {
            yield return BuildQuickReplyBlock(entry, quickReplyOptions);
            var hintParagraph = CreateTranscriptParagraph(bottomMargin: 6);
            var hintRun = new Run("Press \u201c[\u201d to respond with the keyboard.") {
                FontSize = 11
            };
            hintRun.SetResourceReference(TextElement.ForegroundProperty, "KeyboardHintText");
            hintParagraph.Inlines.Add(hintRun);
            _currentHintRun = hintRun;
            yield return hintParagraph;
        }
    }

    // ── Paragraph block builder ────────────────────────────────────────────

    private IEnumerable<Paragraph> BuildParagraphBlocks(List<string> lines) {
        if (lines.Count == 0)
            yield break;

        var i = 0;
        while (i < lines.Count) {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Blank line — flush nothing, just skip
            if (string.IsNullOrWhiteSpace(trimmed)) {
                i++;
                continue;
            }

            // Heading
            if (trimmed.StartsWith("#", StringComparison.Ordinal)) {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                var headingText = trimmed[level..].TrimStart();
                var p = CreateTranscriptParagraph(bottomMargin: level <= 2 ? 6 : 4);
                p.FontSize = HeadingFontSize(level, _getFontSize());
                p.Tag = new string('#', level) + " " + headingText;
                p.Inlines.Add(new Run(headingText) { FontWeight = FontWeights.Bold });
                yield return p;
                i++;
                continue;
            }

            // Blockquote
            if (trimmed.StartsWith("> ", StringComparison.Ordinal)) {
                var quoteLines = new List<string>();
                while (i < lines.Count && lines[i].TrimStart().StartsWith("> ", StringComparison.Ordinal)) {
                    quoteLines.Add(lines[i].TrimStart()[2..]);
                    i++;
                }
                yield return BuildBlockquote(string.Join("\n", quoteLines));
                continue;
            }

            // Bullet list
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) {
                var bullets = new List<string>();
                while (i < lines.Count) {
                    var t = lines[i].TrimStart();
                    if (!t.StartsWith("- ", StringComparison.Ordinal) && !t.StartsWith("* ", StringComparison.Ordinal))
                        break;
                    bullets.Add(t[2..]);
                    i++;
                }
                for (var b = 0; b < bullets.Count; b++)
                    yield return BuildBulletParagraph(bullets[b], isLast: b == bullets.Count - 1);
                continue;
            }

            // Numbered list
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\. ")) {
                var items = new List<(int Num, string Text)>();
                while (i < lines.Count) {
                    var t = lines[i].TrimStart();
                    var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d+)\. (.*)");
                    if (!m.Success) break;
                    items.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value));
                    i++;
                }
                for (var n = 0; n < items.Count; n++)
                    yield return BuildNumberedListParagraph(items[n].Num, items[n].Text, isLast: n == items.Count - 1);
                continue;
            }

            // Plain paragraph — collect until blank line or block-level element
            var paragraphLines = new List<string>();
            while (i < lines.Count) {
                var t = lines[i].TrimStart();
                if (string.IsNullOrWhiteSpace(t))
                    break;
                if (t.StartsWith("#", StringComparison.Ordinal) ||
                    t.StartsWith("> ", StringComparison.Ordinal) ||
                    t.StartsWith("- ", StringComparison.Ordinal) ||
                    t.StartsWith("* ", StringComparison.Ordinal) ||
                    t.StartsWith("```", StringComparison.Ordinal))
                    break;
                paragraphLines.Add(lines[i]);
                i++;
            }

            if (paragraphLines.Count > 0) {
                foreach (var pl in paragraphLines) {
                    var p = CreateTranscriptParagraph(bottomMargin: 4);
                    p.Tag = pl;
                    AppendInlineMarkdown(p.Inlines, pl.TrimStart());
                    yield return p;
                }
            }
        }
    }

    // ── List item paragraph builders ───────────────────────────────────────

    private Paragraph BuildNumberedListParagraph(int number, string text, bool isLast = false) {
        var p = new Paragraph {
            Margin = new Thickness(16, 1, 0, isLast ? 12 : 1),
            TextIndent = -12,
            Tag = $"{number}. {text}"
        };
        var markerRun = new Run($"{number}. ");
        markerRun.SetResourceReference(TextElement.ForegroundProperty, "ListMarkerText");
        p.Inlines.Add(markerRun);
        AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    private Paragraph BuildBulletParagraph(string text, bool isLast = false) {
        var p = new Paragraph {
            Margin = new Thickness(16, 1, 0, isLast ? 12 : 1),
            TextIndent = -12,
            Tag = $"- {text}"
        };
        var markerRun = new Run("• ");
        markerRun.SetResourceReference(TextElement.ForegroundProperty, "ListMarkerText");
        p.Inlines.Add(markerRun);
        AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    private Paragraph BuildBlockquote(string text) {
        var p = new Paragraph {
            Margin = new Thickness(12, 2, 0, 8),
            Padding = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Tag = string.Join("\n", text.Split('\n').Select(l => $"> {l}"))
        };
        p.SetResourceReference(Block.BorderBrushProperty, "QuoteBorder");
        p.SetResourceReference(Block.BackgroundProperty, "QuoteSurface");
        p.SetResourceReference(TextElement.ForegroundProperty, "BlockquoteBodyText");
        AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    // ── Code block ─────────────────────────────────────────────────────────

    private Block BuildCodeBlock(string code) {
        var textBox = new TextBox {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = _getFontSize() * 0.9,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8),
            Tag = "codeblock"
        };
        textBox.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        textBox.SetResourceReference(Control.ForegroundProperty, "CodeText");

        // ── Copy button with "Copied!" feedback ──────────────────────────
        var copiedTip = new ToolTip {
            Content   = "Copied!",
            Placement = PlacementMode.Bottom,
        };

        var copyBtn = new Button {
            Content             = "📋",
            ToolTip             = copiedTip,
            FontSize            = 13,
            Width               = 26,
            Height              = 22,
            Padding             = new Thickness(0),
            Margin              = new Thickness(4, 2, 4, 2),
            BorderThickness     = new Thickness(0),
            Background          = Brushes.Transparent,
            Cursor              = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        copyBtn.SetResourceReference(Control.ForegroundProperty, "SubtleText");
        copyBtn.SetResourceReference(Control.StyleProperty, "TranscriptInlineButtonStyle");

        copyBtn.Click += (_, _) => {
            try { Clipboard.SetText(code); }
            catch { /* clipboard contention — ignore */ }

            copiedTip.PlacementTarget = copyBtn;
            copiedTip.IsOpen          = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) => { copiedTip.IsOpen = false; timer.Stop(); };
            timer.Start();
        };

        // ── Header row: copy button pinned right ─────────────────────────
        var header = new DockPanel { LastChildFill = false };
        header.SetResourceReference(DockPanel.BackgroundProperty, "CodeSurface");
        DockPanel.SetDock(copyBtn, Dock.Right);
        header.Children.Add(copyBtn);

        // ── Outer container ───────────────────────────────────────────────
        var container = new StackPanel();
        container.Children.Add(header);
        container.Children.Add(textBox);

        return new BlockUIContainer(container) { Margin = new Thickness(0, 2, 0, 10) };
    }

    // ── Quick-reply block ──────────────────────────────────────────────────

    internal sealed record QuickReplyButtonPayload(
        TranscriptResponseEntry Entry,
        string Option,
        string? RoutingInstruction,
        string? ContinuationAgentLabel,
        string? RouteMode);

    private Block BuildQuickReplyBlock(TranscriptResponseEntry entry, IReadOnlyList<QuickReplyOptionMetadata> options) {
        CurrentQuickReplyOptions = options.Select(option => option.Label).ToArray();
        LastQuickReplyEntry = entry;

        var routeDecisions = options
            .Select(option => (Option: option, Decision: BuildQuickReplyRouting(entry, option)))
            .ToArray();
        var captionText = QuickReplyRoutePresentation.BuildCaption(
            routeDecisions.Select(item => new QuickReplyRoutePresentation.RouteInfo(
                item.Decision.RouteMode,
                item.Decision.ContinuationAgentLabel,
                item.Decision.Reason)).ToArray());
        var stack = new StackPanel {
            Orientation = Orientation.Vertical
        };

        if (!string.IsNullOrWhiteSpace(captionText)) {
            var caption = new TextBlock {
                Text = captionText,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 12
            };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "AgentRoleText");
            stack.Children.Add(caption);
        }

        var panel = new WrapPanel {
            Margin = new Thickness(0, 2, 0, 0),
            Orientation = Orientation.Horizontal
        };

        foreach (var routeDecision in routeDecisions) {
            var option = routeDecision.Option.Label;
            var routedQuickReply = routeDecision.Decision;
            var button = new Button {
                Content = option,
                Tag = new QuickReplyButtonPayload(
                    entry,
                    option,
                    routedQuickReply.RoutingInstruction,
                    routedQuickReply.ContinuationAgentLabel,
                    routedQuickReply.RouteMode),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 4, 10, 4),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                MinHeight = 28,
                ToolTip = QuickReplyRoutePresentation.BuildButtonToolTip(
                    new QuickReplyRoutePresentation.RouteInfo(
                        routedQuickReply.RouteMode,
                        routedQuickReply.ContinuationAgentLabel,
                        routedQuickReply.Reason))
            };
            if (Application.Current.TryFindResource("QuickReplyButtonStyle") is Style quickReplyStyle)
                button.Style = quickReplyStyle;
            button.Click += (s, e) => { DismissKeyboardHint(); _onQuickReplyButtonClick(s, e); };
            panel.Children.Add(button);
        }

        stack.Children.Add(panel);
        return new BlockUIContainer(stack) { Margin = new Thickness(0, 2, 0, 10) };
    }

    // ── Inline markdown ────────────────────────────────────────────────────

    internal void AppendInlineMarkdown(InlineCollection inlines, string text) {
        if (string.IsNullOrEmpty(text))
            return;

        var i = 0;
        var buffer = new StringBuilder();

        Action<string> flush = segment => {
            if (segment.Length > 0)
                inlines.Add(new Run(segment));
        };

        while (i < text.Length) {
            if (text[i] == '\\' && i + 1 < text.Length) {
                buffer.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (TryReadMarkdownLink(text, i, out var nextIndex, out var label, out var target) &&
                TryCreateMarkdownHyperlink(label, target, out var hyperlink)) {
                flush(buffer.ToString());
                buffer.Clear();
                inlines.Add(hyperlink);
                i = nextIndex;
                continue;
            }

            if (TryReadBareUrl(text, i, out var urlEnd, out var url)) {
                flush(buffer.ToString());
                buffer.Clear();
                var urlLink = new Hyperlink(new Run(url)) {
                    Tag = url
                };
                urlLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                urlLink.Click += (_, _) => _onLinkClicked(url);
                inlines.Add(urlLink);
                i = urlEnd;
                continue;
            }

            var workspaceGitHubUrl = _getWorkspaceGitHubUrl();
            if (!string.IsNullOrWhiteSpace(workspaceGitHubUrl) &&
                TryReadCommitHash(text, i, out var hashEnd, out var hash)) {
                flush(buffer.ToString());
                buffer.Clear();
                var commitUrl = $"{workspaceGitHubUrl}/commit/{hash}";
                var commitLink = new Hyperlink(new Run(hash)) {
                    Tag = commitUrl
                };
                commitLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                commitLink.Click += (_, _) => _onLinkClicked(commitUrl);
                inlines.Add(commitLink);
                i = hashEnd;
                continue;
            }

            // Bold: **...**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*') {
                flush(buffer.ToString()); buffer.Clear();
                i += 2;
                var end = text.IndexOf("**", i, StringComparison.Ordinal);
                if (end < 0) { buffer.Append("**"); continue; }
                var boldText = text[i..end];
                var bold = new Bold();
                AppendInlineMarkdown(bold.Inlines, boldText);
                inlines.Add(bold);
                i = end + 2;
                continue;
            }

            // Inline code: `...`
            if (text[i] == '`') {
                flush(buffer.ToString()); buffer.Clear();
                i++;
                var end = text.IndexOf('`', i);
                if (end < 0) { buffer.Append('`'); continue; }
                var codeText = text[i..end];
                var codeGitHubUrl = _getWorkspaceGitHubUrl();
                if (!string.IsNullOrWhiteSpace(codeGitHubUrl) &&
                    TryReadCommitHash(codeText, 0, out var codeHashEnd, out var codeHash) && codeHashEnd == codeText.Length) {
                    var commitUrl = $"{codeGitHubUrl}/commit/{codeHash}";
                    var commitLink = new Hyperlink(new Run(codeHash)) {
                        Tag = commitUrl,
                        FontFamily = new FontFamily("Consolas")
                    };
                    commitLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                    commitLink.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
                    commitLink.Click += (_, _) => _onLinkClicked(commitUrl);
                    inlines.Add(commitLink);
                } else {
                    var codeRun = new Run(codeText) {
                        FontFamily = new FontFamily("Consolas")
                    };
                    codeRun.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
                    codeRun.SetResourceReference(TextElement.ForegroundProperty, "CodeText");
                    inlines.Add(codeRun);
                }
                i = end + 1;
                continue;
            }

            // Italic: *...* (not **)
            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*')) {
                flush(buffer.ToString()); buffer.Clear();
                i++;
                var end = i;
                while (end < text.Length && !(text[end] == '*' && (end + 1 >= text.Length || text[end + 1] != '*')))
                    end++;
                if (end >= text.Length) { buffer.Append('*'); continue; }
                var italic = new Italic();
                AppendInlineMarkdown(italic.Inlines, text[i..end]);
                inlines.Add(italic);
                i = end + 1;
                continue;
            }

            buffer.Append(text[i++]);
        }

        flush(buffer.ToString());
    }

    // internal for unit testing via InternalsVisibleTo
    internal static bool TryReadCommitHash(string text, int startIndex, out int nextIndex, out string hash) {
        nextIndex = startIndex;
        hash = string.Empty;

        // Use char.IsLetterOrDigit for both boundary checks so that hex substrings
        // embedded inside longer words (e.g. "succeeded" → "cceeded") are rejected.
        // IsHexWordChar alone would pass the start-boundary when the preceding char is a
        // non-hex letter such as 'u', which caused the original false-positive.
        if (startIndex > 0 && char.IsLetterOrDigit(text[startIndex - 1]))
            return false;

        var end = startIndex;
        while (end < text.Length && IsHexWordChar(text[end]))
            end++;

        var length = end - startIndex;
        if (length < 7 || length > 40)
            return false;

        // End-boundary: reject if the hash is a prefix of a longer alphanumeric token
        // (e.g. "abcdef123g" must not match because 'g' follows immediately).
        // Previously this checked IsHexWordChar, which was dead code — the while loop
        // already consumed all hex chars so text[end] could never be hex. Switching to
        // char.IsLetterOrDigit makes this check meaningful for non-hex trailing chars.
        //
        // Notes on other edge cases:
        //   • Length range 7–40 is correct for SHA-1. Future SHA-256 hashes (64 chars)
        //     would be missed, but that is not a concern today.
        //   • Uppercase hex (A–F) is accepted deliberately; git hashes are normally
        //     lowercase but being permissive is safer.
        //   • Hex strings inside raw URLs may still be matched if the URL appears in
        //     plain text, because the scanner does not skip URL tokens. Lower priority.
        if (end < text.Length && char.IsLetterOrDigit(text[end]))
            return false;

        hash = text[startIndex..end];
        nextIndex = end;
        return true;

        static bool IsHexWordChar(char c) =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    /// <summary>
    /// Detects a bare http:// or https:// URL starting at <paramref name="startIndex"/>.
    /// URLs are terminated by whitespace or certain punctuation characters that commonly
    /// follow a URL in prose (e.g. "Go to https://example.com → next step").
    /// </summary>
    private static bool TryReadBareUrl(string text, int startIndex, out int nextIndex, out string url) {
        nextIndex = startIndex;
        url = string.Empty;

        // Must start with http:// or https://
        if (!text.AsSpan(startIndex).StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.AsSpan(startIndex).StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Must not be preceded by a non-whitespace character (avoid matching mid-word)
        if (startIndex > 0 && !char.IsWhiteSpace(text[startIndex - 1]))
            return false;

        var end = startIndex;
        while (end < text.Length && !IsUrlTerminator(text[end]))
            end++;

        // Trim trailing punctuation that is unlikely to be part of the URL
        while (end > startIndex && IsTrailingPunctuation(text[end - 1]))
            end--;

        if (end <= startIndex + 7) // must have at least something after the scheme
            return false;

        var candidate = text[startIndex..end];
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return false;

        url = candidate;
        nextIndex = end;
        return true;
    }

    private static bool IsUrlTerminator(char c) =>
        char.IsWhiteSpace(c) || c == '<' || c == '>' || c == '"' || c == '\'' || c == '→';

    private static bool IsTrailingPunctuation(char c) =>
        c == '.' || c == ',' || c == ')' || c == ']' || c == '!' || c == '?' || c == ';' || c == ':';

    private static bool TryReadMarkdownLink(
        string text,
        int startIndex,
        out int nextIndex,
        out string label,
        out string target) {
        nextIndex = startIndex;
        label = string.Empty;
        target = string.Empty;

        if (startIndex >= text.Length || text[startIndex] != '[')
            return false;

        var labelEnd = text.IndexOf("](", startIndex, StringComparison.Ordinal);
        if (labelEnd <= startIndex + 1)
            return false;

        var targetEnd = text.IndexOf(')', labelEnd + 2);
        if (targetEnd <= labelEnd + 2)
            return false;

        label = text[(startIndex + 1)..labelEnd];
        target = text[(labelEnd + 2)..targetEnd];
        nextIndex = targetEnd + 1;
        return true;
    }

    private bool TryCreateMarkdownHyperlink(string label, string target, out Hyperlink hyperlink) {
        hyperlink = null!;
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(target))
            return false;

        var trimmedTarget = target.Trim();
        var trimmedLabel  = label.Trim();

        if (trimmedTarget.StartsWith("thread:", StringComparison.OrdinalIgnoreCase)) {
            hyperlink = new Hyperlink(new Run(trimmedLabel)) {
                Tag = trimmedTarget[7..]
            };
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            hyperlink.Click += (_, _) => _onLinkClicked(trimmedTarget[7..]);
            return true;
        }

        if (trimmedTarget.StartsWith("app://", StringComparison.OrdinalIgnoreCase)) {
            hyperlink = new Hyperlink(new Run(trimmedLabel));
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            hyperlink.Click += (_, _) => _onLinkClicked(trimmedTarget);
            return true;
        }

        if (Uri.TryCreate(trimmedTarget, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))) {
            hyperlink = new Hyperlink(new Run(trimmedLabel)) {
                Tag = absoluteUri.AbsoluteUri
            };
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            hyperlink.Click += (_, _) => _onLinkClicked(absoluteUri.AbsoluteUri);
            return true;
        }

        return false;
    }

    // ── Markdown table ─────────────────────────────────────────────────────

    internal bool TryReadMarkdownTable(
        IReadOnlyList<string> lines,
        int startIndex,
        out int lastIndex,
        out List<string[]> rows) {
        rows = [];
        lastIndex = startIndex;

        if (startIndex + 1 >= lines.Count)
            return false;

        if (!IsMarkdownTableRow(lines[startIndex]) || !IsMarkdownTableSeparator(lines[startIndex + 1]))
            return false;

        for (var index = startIndex; index < lines.Count; index++) {
            if (!IsMarkdownTableRow(lines[index]))
                break;

            if (index != startIndex + 1)
                rows.Add(ParseMarkdownTableRow(lines[index]));

            lastIndex = index;
        }

        return rows.Count >= 1;
    }

    private static bool IsMarkdownTableRow(string line) {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
    }

    private static bool IsMarkdownTableSeparator(string line) {
        if (!IsMarkdownTableRow(line))
            return false;

        return ParseMarkdownTableRow(line)
            .All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' '));
    }

    private static string[] ParseMarkdownTableRow(string line) {
        return line
            .Trim()
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();
    }

    internal Block BuildMarkdownTable(IReadOnlyList<string[]> rows) {
        var columnCount = rows.Max(row => row.Length);
        var tablePanel = new StackPanel {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetIsSharedSizeScope(tablePanel, true);
        var markdownTable = BuildMarkdownTableText(rows);
        tablePanel.Tag = markdownTable; // used by DataObjectCopying to include table in selection copy
        var contextMenu  = new ContextMenu();
        var copyMenuItem = new MenuItem {
            Header = "Copy Table",
            Tag    = markdownTable
        };
        copyMenuItem.Click += CopyTableMenuItem_Click;
        contextMenu.Items.Add(copyMenuItem);
        tablePanel.ContextMenu = contextMenu;

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++) {
            var rowGrid = new Grid {
                HorizontalAlignment = HorizontalAlignment.Left
            };

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition {
                    Width = GridLength.Auto,
                    SharedSizeGroup = $"TranscriptTableColumn{columnIndex}"
                });
            }

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) {
                var cellText = columnIndex < rows[rowIndex].Length ? rows[rowIndex][columnIndex] : string.Empty;
                var textBlock = new TextBlock {
                    TextWrapping = TextWrapping.NoWrap,
                };
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TableCellText");

                if (rowIndex == 0)
                    textBlock.FontWeight = FontWeights.SemiBold;

                AppendTextRuns(textBlock.Inlines, cellText);

                var cellBorder = new Border {
                    BorderThickness = new Thickness(0.6),
                    Background      = Brushes.Transparent,
                    Padding         = new Thickness(8, 5, 8, 5),
                    Child           = textBlock
                };
                cellBorder.SetResourceReference(Border.BorderBrushProperty, "TableRule");
                if (rowIndex == 0)
                    cellBorder.SetResourceReference(Border.BackgroundProperty, "TableHeaderSurface");
                Grid.SetColumn(cellBorder, columnIndex);
                rowGrid.Children.Add(cellBorder);
            }

            tablePanel.Children.Add(rowGrid);
        }

        return new BlockUIContainer(tablePanel) {
            Margin = new Thickness(0, 2, 0, 14)
        };
    }

    private void CopyTableMenuItem_Click(object sender, RoutedEventArgs e) {
        if (sender is not MenuItem { Tag: string markdownTable })
            return;

        try {
            Clipboard.SetText(markdownTable);
        }
        catch (Exception ex) {
            _onException("Copy Table", ex);
        }
    }

    private static string BuildMarkdownTableText(IReadOnlyList<string[]> rows) {
        if (rows.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine(BuildMarkdownTableRow(rows[0]));
        builder.AppendLine(BuildMarkdownTableSeparator(rows[0].Length));

        for (var index = 1; index < rows.Count; index++)
            builder.AppendLine(BuildMarkdownTableRow(rows[index]));

        return builder.ToString().TrimEnd();
    }

    private static string BuildMarkdownTableRow(IReadOnlyList<string> cells) {
        return "| " + string.Join(" | ", cells) + " |";
    }

    private static string BuildMarkdownTableSeparator(int columnCount) {
        return "| " + string.Join(" | ", Enumerable.Repeat("---", columnCount)) + " |";
    }

    private void AppendTextRuns(InlineCollection inlines, string? text) {
        if (string.IsNullOrEmpty(text))
            return;

        AppendInlineMarkdown(inlines, text);
    }

    // ── Quick-reply routing ────────────────────────────────────────────────

    internal sealed record QuickReplyRoutingDecision(
        string? RoutingInstruction,
        string? ContinuationAgentLabel,
        string? RouteMode,
        string? Reason);

    private QuickReplyRoutingDecision BuildQuickReplyRouting(TranscriptResponseEntry entry, QuickReplyOptionMetadata option) {
        var trimmedOption = option.Label.Trim();
        if (string.IsNullOrWhiteSpace(trimmedOption))
            return new QuickReplyRoutingDecision(null, null, null, null);

        var targetThread = _resolveContinuationThread(entry);
        var agentHandle  = GetQuickReplyAgentHandle(targetThread);
        if (targetThread is null || string.IsNullOrWhiteSpace(agentHandle))
            return new QuickReplyRoutingDecision(null, null, option.RouteMode, option.Reason);

        var agentLabel = ResolveQuickReplyAgentLabel(targetThread);
        var routingInstruction = "Route this quick-reply follow-up to @" + agentHandle.Trim() +
                                 ". Have that agent continue from their most recent work on this task, follow their charter, and carry out the user's selected next step: " +
                                 trimmedOption;
        return new QuickReplyRoutingDecision(routingInstruction, agentLabel, option.RouteMode, option.Reason);
    }

    internal static bool CanRouteQuickReplyToAgent(TranscriptThreadState? thread) {
        if (thread is null || thread.Kind != TranscriptThreadKind.Agent || thread.IsPlaceholderThread)
            return false;

        return !string.IsNullOrWhiteSpace(thread.AgentName) ||
               !string.IsNullOrWhiteSpace(thread.AgentId);
    }

    internal static string? GetQuickReplyAgentHandle(TranscriptThreadState? thread) {
        if (thread is null)
            return null;

        if (!string.IsNullOrWhiteSpace(thread.AgentName))
            return thread.AgentName.Trim();
        if (!string.IsNullOrWhiteSpace(thread.AgentId))
            return thread.AgentId.Trim();

        return null;
    }

    internal static string? ResolveQuickReplyAgentLabel(TranscriptThreadState? thread) {
        if (thread is null)
            return null;

        if (!string.IsNullOrWhiteSpace(thread.AgentDisplayName))
            return thread.AgentDisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(thread.AgentName))
            return AgentNameHumanizer.Humanize(thread.AgentName);
        if (!string.IsNullOrWhiteSpace(thread.AgentId))
            return AgentNameHumanizer.Humanize(thread.AgentId);

        return null;
    }
}
