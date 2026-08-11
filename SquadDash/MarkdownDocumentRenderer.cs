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
            // Use Padding (not Margin) for the bottom gap so that WPF selection
            // painting covers the space — Margin is outside the painted selection
            // region, which causes a visible break when selecting across paragraphs.
            Margin  = new Thickness(0),
            Padding = new Thickness(0, 0, 0, bottomMargin)
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
            if (TryReadFenceStart(trimmed, out var fenceLength)) {
                foreach (var block in BuildParagraphBlocks(paragraphLines))
                    yield return block;
                paragraphLines.Clear();

                index++;
                var codeLines = new List<string>();
                while (index < lines.Length && !IsFenceClose(lines[index].TrimStart(), fenceLength)) {
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
                FontSize = (double)Application.Current.Resources["FontSizeSmall"]
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
                var headingBold = new Bold();
                AppendInlineMarkdown(headingBold.Inlines, headingText);
                p.Inlines.Add(headingBold);
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
                    TryReadFenceStart(t, out _))
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
            Margin      = new Thickness(16, 0, 0, 0),
            Padding     = new Thickness(0, 1, 0, isLast ? 12 : 1),
            TextIndent  = -12,
            Tag         = $"{number}. {text}"
        };
        var markerRun = new Run($"{number}. ");
        markerRun.SetResourceReference(TextElement.ForegroundProperty, "ListMarkerText");
        p.Inlines.Add(markerRun);
        AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    private Paragraph BuildBulletParagraph(string text, bool isLast = false) {
        var p = new Paragraph {
            Margin      = new Thickness(16, 0, 0, 0),
            Padding     = new Thickness(0, 1, 0, isLast ? 12 : 1),
            TextIndent  = -12,
            Tag         = $"- {text}"
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

    internal static bool TryReadFenceStart(string trimmedLine, out int fenceLength) {
        fenceLength = CountLeadingBackticks(trimmedLine);
        return fenceLength >= 3;
    }

    internal static bool IsFenceClose(string trimmedLine, int openingFenceLength) {
        var fenceLength = CountLeadingBackticks(trimmedLine);
        if (fenceLength < openingFenceLength)
            return false;

        return string.IsNullOrWhiteSpace(trimmedLine[fenceLength..]);
    }

    private static int CountLeadingBackticks(string text) {
        var count = 0;
        while (count < text.Length && text[count] == '`')
            count++;
        return count;
    }

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
        var copyTip = new ToolTip { Content = "Copy", Placement = PlacementMode.Bottom };
        copyTip.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        copyTip.SetResourceReference(Control.BorderBrushProperty, "InputBorder");

        var copyBtn = new Button {
            Content             = "📋",
            ToolTip             = copyTip,
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
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
            bool copied = false;
            for (int attempt = 0; attempt < 3 && !copied; attempt++)
            {
                try { Clipboard.SetText(code); copied = true; }
                catch { System.Threading.Thread.Sleep(50); }
            }
            if (!copied) return;

            copyTip.Content = "Copied!";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) => { copyTip.Content = "Copy"; timer.Stop(); };
            timer.Start();
        };

        // Explicit context menu so right-click Copy works regardless of keyboard focus.
        // WPF's auto-generated ContextMenu evaluates ApplicationCommands.Copy.CanExecute
        // against the focused element at open time — if the TextBox loses focus to the
        // popup, Copy appears disabled even though selection is non-empty.
        var copyMenuItem = new MenuItem { Header = "Copy" };
        copyMenuItem.Click += (_, _) => {
            var text = textBox.SelectionLength > 0 ? textBox.SelectedText : code;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { Clipboard.SetText(text); break; }
                catch { System.Threading.Thread.Sleep(50); }
            }
        };
        var selectAllMenuItem = new MenuItem { Header = "Select All" };
        selectAllMenuItem.Click += (_, _) => textBox.SelectAll();
        var ctxMenu = new ContextMenu();
        ctxMenu.Items.Add(copyMenuItem);
        ctxMenu.Items.Add(new Separator());
        ctxMenu.Items.Add(selectAllMenuItem);
        // Update Copy header to reflect what will be copied before the menu opens.
        ctxMenu.Opened += (_, _) =>
            copyMenuItem.Header = textBox.SelectionLength > 0 ? "Copy Selection" : "Copy All";
        textBox.ContextMenu = ctxMenu;

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
                FontSize = (double)Application.Current.Resources["FontSizeBody"]
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
            var isDraft = string.Equals(routedQuickReply.RouteMode, "draft", StringComparison.OrdinalIgnoreCase);
            var button = TranscriptQuickReplyFactory.CreateButton(
                isDraft ? $"✏️ {option}" : option,
                _getFontSize(),
                new QuickReplyButtonPayload(
                    entry,
                    option,
                    routedQuickReply.RoutingInstruction,
                    routedQuickReply.ContinuationAgentLabel,
                    routedQuickReply.RouteMode),
                QuickReplyRoutePresentation.BuildButtonToolTip(
                    new QuickReplyRoutePresentation.RouteInfo(
                        routedQuickReply.RouteMode,
                        routedQuickReply.ContinuationAgentLabel,
                        routedQuickReply.Reason)),
                TranscriptQuickReplyFactory.ParseTone(routeDecision.Option.Tone));
            button.Click += (s, e) => { DismissKeyboardHint(); _onQuickReplyButtonClick(s, e); };
            panel.Children.Add(button);
        }

        stack.Children.Add(panel);
        return TranscriptQuickReplyFactory.CreateContainer(stack);
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

            if (TryReadWindowsEnvPath(text, i, out var pathEnd, out var envPath)) {
                flush(buffer.ToString());
                buffer.Clear();
                var capturedPath = envPath;
                var pathLink = new Hyperlink(new Run(envPath)) {
                    Tag = envPath,
                    ToolTip = $"Open in Explorer: {Environment.ExpandEnvironmentVariables(envPath)}"
                };
                pathLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                pathLink.Click += (_, _) => _onLinkClicked("app://open-path:" + capturedPath);
                inlines.Add(pathLink);
                i = pathEnd;
                continue;
            }

            if (TryReadPlanId(text, i, out var planIdEnd, out var planId)) {
                flush(buffer.ToString());
                buffer.Clear();
                var planUrl = $"app://open-plan:{Uri.EscapeDataString(planId)}";
                var planLink = new Hyperlink(new Run(planId)) {
                    Tag = planUrl,
                    ToolTip = $"Open plan visualizer: {planId}"
                };
                planLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                planLink.Click += (_, _) => _onLinkClicked(planUrl);
                inlines.Add(planLink);
                i = planIdEnd;
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
                } else if (TryReadBareUrl(codeText, 0, out var codeUrlEnd, out var codeUrl) && codeUrlEnd == codeText.Length) {
                    // Entire code span is a URL — render as a clickable link with code styling
                    var codeLink = new Hyperlink(new Run(codeText)) {
                        Tag = codeUrl,
                        FontFamily = new FontFamily("Consolas")
                    };
                    codeLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                    codeLink.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
                    codeLink.Click += (_, _) => _onLinkClicked(codeUrl);
                    inlines.Add(codeLink);
                } else if (TryReadWindowsEnvPath(codeText, 0, out var codePathEnd, out var codeEnvPath) && codePathEnd == codeText.Length) {
                    // Entire code span is a Windows env-var path — render as a clickable link with code styling
                    var capturedCodePath = codeEnvPath;
                    var codePathLink = new Hyperlink(new Run(codeText)) {
                        Tag = codeEnvPath,
                        FontFamily = new FontFamily("Consolas"),
                        ToolTip = $"Open in Explorer: {Environment.ExpandEnvironmentVariables(codeEnvPath)}"
                    };
                    codePathLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                    codePathLink.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
                    codePathLink.Click += (_, _) => _onLinkClicked("app://open-path:" + capturedCodePath);
                    inlines.Add(codePathLink);
                } else {
                    var codeRun = new Run(codeText) {
                        FontFamily = new FontFamily("Consolas")
                    };
                    codeRun.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
                    codeRun.SetResourceReference(TextElement.ForegroundProperty, "CodeText");
                    inlines.Add(codeRun);
                }
                // Append a color swatch if the entire code span is a CSS hex color
                if (TryReadColorHex(codeText, 0, out var codeSwatchEnd, out var codeSwatchColor) && codeSwatchEnd == codeText.Length) {
                    var swatchSize = Math.Round(_getFontSize() * 0.75);
                    var swatch = new System.Windows.Shapes.Rectangle {
                        Width               = swatchSize,
                        Height              = swatchSize,
                        Fill                = new SolidColorBrush(codeSwatchColor),
                        Stroke              = new SolidColorBrush(Color.FromArgb(100, 128, 128, 128)),
                        StrokeThickness     = 0.5,
                        Margin              = new Thickness(3, 0, 0, -2),
                        SnapsToDevicePixels = true,
                    };
                    inlines.Add(new InlineUIContainer(swatch) { BaselineAlignment = BaselineAlignment.Center });
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

            // Color hex swatch: #RRGGBB or #RGB
            if (text[i] == '#' && TryReadColorHex(text, i, out var hexEnd, out var swatchColor)) {
                flush(buffer.ToString()); buffer.Clear();
                inlines.Add(new Run(text[i..hexEnd]));
                var swatchSize = Math.Round(_getFontSize() * 0.75);
                var rect = new System.Windows.Shapes.Rectangle {
                    Width            = swatchSize,
                    Height           = swatchSize,
                    Fill             = new SolidColorBrush(swatchColor),
                    Stroke           = new SolidColorBrush(Color.FromArgb(100, 128, 128, 128)),
                    StrokeThickness  = 0.5,
                    Margin           = new Thickness(3, 0, 0, -2),
                    SnapsToDevicePixels = true,
                };
                inlines.Add(new InlineUIContainer(rect) { BaselineAlignment = BaselineAlignment.Center });
                i = hexEnd;
                continue;
            }

            buffer.Append(text[i++]);
        }

        flush(buffer.ToString());
    }

    // internal for unit testing via InternalsVisibleTo
    internal static bool TryReadColorHex(string text, int startIndex, out int nextIndex, out Color color) {
        nextIndex = startIndex;
        color = default;

        if (startIndex >= text.Length || text[startIndex] != '#')
            return false;

        // Don't match mid-word (e.g. "foo#bar")
        if (startIndex > 0 && char.IsLetterOrDigit(text[startIndex - 1]))
            return false;

        var pos = startIndex + 1;
        var count = 0;
        while (pos + count < text.Length && IsHexDigit(text[pos + count]))
            count++;

        if (count != 6 && count != 3)
            return false;

        // Don't match if followed by more alphanumeric chars (e.g. #D8C8B0FF has 8 digits)
        var afterPos = pos + count;
        if (afterPos < text.Length && char.IsLetterOrDigit(text[afterPos]))
            return false;

        var hex = text[pos..(pos + count)];
        if (count == 3) {
            var r3 = Convert.ToByte(new string(hex[0], 2), 16);
            var g3 = Convert.ToByte(new string(hex[1], 2), 16);
            var b3 = Convert.ToByte(new string(hex[2], 2), 16);
            color = Color.FromRgb(r3, g3, b3);
        } else {
            var r = Convert.ToByte(hex[0..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            color = Color.FromRgb(r, g, b);
        }

        nextIndex = afterPos;
        return true;

        static bool IsHexDigit(char c) =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    // internal for unit testing via InternalsVisibleTo
    internal static bool TryReadCommitHash(string text, int startIndex, out int nextIndex, out string hash) {
        nextIndex = startIndex;
        hash = string.Empty;

        // A hash must be a complete prose token, not merely a hex run after arbitrary
        // punctuation. This rejects plan IDs such as PLANSHIP-20260802 as well as
        // $abc1234 and abc1234%, while retaining ordinary prose delimiters.
        if (startIndex > 0 && !IsCommitStartBoundary(text[startIndex - 1]))
            return false;

        var end = startIndex;
        while (end < text.Length && IsHexWordChar(text[end]))
            end++;

        var length = end - startIndex;
        if (length < 7 || length > 40)
            return false;

        // End-boundary: reject both longer alphanumeric tokens and punctuation that
        // normally binds a value into another identifier.
        //
        // Notes on other edge cases:
        //   • Length range 7–40 is correct for SHA-1. Future SHA-256 hashes (64 chars)
        //     would be missed, but that is not a concern today.
        //   • Uppercase hex (A–F) is accepted deliberately; git hashes are normally
        //     lowercase but being permissive is safer.
        //   • Hex strings inside raw URLs may still be matched if the URL appears in
        //     plain text, because the scanner does not skip URL tokens. Lower priority.
        if (end < text.Length && !IsCommitEndBoundary(text[end]))
            return false;

        hash = text[startIndex..end];
        nextIndex = end;
        return true;

        static bool IsHexWordChar(char c) =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        static bool IsCommitStartBoundary(char c) =>
            char.IsWhiteSpace(c) || c is '(' or '[' or '{' or '<' or ':' or '/' or '\\' or '\'' or '"' or '`';

        static bool IsCommitEndBoundary(char c) =>
            char.IsWhiteSpace(c) || c is ')' or ']' or '}' or '>' or '.' or ',' or '!' or '?' or ';' or ':' or '/' or '\\' or '\'' or '"' or '`';
    }

    /// <summary>
    /// Detects a plan ID starting at <paramref name="startIndex"/>.
    /// Plan IDs follow the pattern: one or more uppercase word segments separated by hyphens,
    /// followed by an 8-digit date (YYYYMMDD), optionally followed by a hyphen and a numeric
    /// task suffix. Examples: <c>MODELPROF-20260810</c>, <c>ROUTEPROBE-20260728-001</c>.
    /// </summary>
    // internal for unit testing via InternalsVisibleTo
    internal static bool TryReadPlanId(string text, int startIndex, out int nextIndex, out string planId) {
        nextIndex = startIndex;
        planId = string.Empty;

        // Must be preceded by a word boundary (whitespace or opening punctuation)
        if (startIndex > 0 && !IsPlanIdStartBoundary(text[startIndex - 1]))
            return false;

        // Must start with an uppercase ASCII letter
        if (startIndex >= text.Length || text[startIndex] < 'A' || text[startIndex] > 'Z')
            return false;

        var end = startIndex;

        // Consume one or more uppercase word segments, each separated by a single hyphen.
        // A word segment is one or more uppercase letters or digits, starting with an uppercase letter.
        while (true) {
            // Consume uppercase letters and digits for one segment
            var segStart = end;
            while (end < text.Length && (text[end] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                end++;

            if (end == segStart) return false; // empty segment

            // After the last uppercase segment, we expect a hyphen then an 8-digit date
            if (end >= text.Length || text[end] != '-')
                return false;

            var hyphenPos = end;
            end++; // skip '-'

            // Check if the next 8 characters are digits (the date portion)
            if (end + 8 <= text.Length && IsEightDigitDate(text, end)) {
                end += 8;

                // Optional task suffix: -NNN (hyphen followed by one or more digits)
                if (end < text.Length && text[end] == '-') {
                    var suffixStart = end + 1;
                    var suffixEnd = suffixStart;
                    while (suffixEnd < text.Length && text[suffixEnd] >= '0' && text[suffixEnd] <= '9')
                        suffixEnd++;
                    if (suffixEnd > suffixStart) {
                        // Only consume the suffix if it ends at a word boundary
                        if (suffixEnd >= text.Length || IsPlanIdEndBoundary(text[suffixEnd]))
                            end = suffixEnd;
                    }
                }

                // Must end at a word boundary
                if (end < text.Length && !IsPlanIdEndBoundary(text[end]))
                    return false;

                planId = text[startIndex..end];
                nextIndex = end;
                return true;
            }

            // Not a date — this segment could be another uppercase prefix segment.
            // Continue scanning from after the hyphen (segStart for next word is current end).
            // But if the character after the hyphen is not an uppercase letter, it's not a plan ID.
            if (end >= text.Length || text[end] < 'A' || text[end] > 'Z')
                return false;
        }

        static bool IsEightDigitDate(string text, int pos) {
            for (var k = pos; k < pos + 8; k++) {
                if (text[k] < '0' || text[k] > '9') return false;
            }
            return true;
        }

        static bool IsPlanIdStartBoundary(char c) =>
            char.IsWhiteSpace(c) || c is '(' or '[' or '{' or '<' or ':' or '/' or '\\' or '\'' or '"' or '`';

        static bool IsPlanIdEndBoundary(char c) =>
            char.IsWhiteSpace(c) || c is ')' or ']' or '}' or '>' or '.' or ',' or '!' or '?' or ';' or ':' or '/' or '\\' or '\'' or '"' or '`';
    }

    /// <summary>
    /// Detects a bare http:// or https:// URL starting at <paramref name="startIndex"/>.
    /// URLs are terminated by whitespace or certain punctuation characters that commonly
    /// follow a URL in prose (e.g. "Go to https://example.com → next step").
    /// </summary>
    private static bool TryReadBareUrl(string text, int startIndex, out int nextIndex, out string url) {
        nextIndex = startIndex;
        url = string.Empty;

        // Must start with a recognised URL scheme
        if (!text.AsSpan(startIndex).StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.AsSpan(startIndex).StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !text.AsSpan(startIndex).StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) &&
            !text.AsSpan(startIndex).StartsWith("edge://", StringComparison.OrdinalIgnoreCase))
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
             !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals("chrome", StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals("edge", StringComparison.OrdinalIgnoreCase)))
            return false;

        url = candidate;
        nextIndex = end;
        return true;
    }

    private static bool IsUrlTerminator(char c) =>
        char.IsWhiteSpace(c) || c == '<' || c == '>' || c == '"' || c == '\'' || c == '→';

    /// <summary>
    /// Detects a Windows path that starts with a %-delimited environment variable prefix,
    /// e.g. <c>%AppData%\BetterVoice\trace.log</c> or <c>%LocalAppData%\Temp\out.txt</c>.
    /// The variable name must be followed by a backslash so that bare tokens like
    /// <c>%AppData%</c> alone do not match.
    /// Path characters are consumed until whitespace or common prose punctuation.
    /// </summary>
    private static bool TryReadWindowsEnvPath(string text, int startIndex, out int nextIndex, out string path) {
        nextIndex = startIndex;
        path = string.Empty;

        // Must start with %
        if (startIndex >= text.Length || text[startIndex] != '%')
            return false;

        // Find the closing %
        var varEnd = text.IndexOf('%', startIndex + 1);
        if (varEnd <= startIndex + 1) // need at least one char between %
            return false;

        // Variable name must be followed by a backslash
        if (varEnd + 1 >= text.Length || text[varEnd + 1] != '\\')
            return false;

        // Variable name must be non-empty alphanumeric+underscore (no spaces)
        var varName = text[(startIndex + 1)..varEnd];
        if (string.IsNullOrEmpty(varName) || !varName.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return false;

        // Consume the rest of the path (until whitespace or prose-terminating chars)
        var end = varEnd + 1;
        while (end < text.Length && !IsWindowsPathTerminator(text[end]))
            end++;

        // Trim trailing punctuation unlikely to be part of the path
        while (end > varEnd + 1 && IsTrailingPunctuation(text[end - 1]))
            end--;

        // Must have at least the backslash after the variable
        if (end <= varEnd + 1)
            return false;

        path = text[startIndex..end];
        nextIndex = end;
        return true;
    }

    private static bool IsWindowsPathTerminator(char c) =>
        char.IsWhiteSpace(c) || c == '<' || c == '>' || c == '"' || c == '\'' || c == '→' || c == '`';

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
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals("edge", StringComparison.OrdinalIgnoreCase))) {
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
        const string placeholder = "\x00PIPE\x00";
        return line
            .Trim()
            .Replace(@"\|", placeholder)
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim().Replace(placeholder, "|"))
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
        contextMenu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        var copyMenuItem = new MenuItem {
            Header = "Copy Table",
            Tag    = markdownTable
        };
        copyMenuItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
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
