using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;
public class SimpleMarkdownViewer : Control {
    // Matches: ![optional-width:alt](path) optional-trailing-text
    // Group 1 = width hint (digits), Group 2 = alt, Group 3 = path, Group 4 = trailing text
    private static readonly Regex ImageLineRegex = new(
        @"^!\[(?:(\d+):)?([^\]]*)\]\(([^)]+)\)\s*(.*)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Optional callback that resolves an image path (as written in markdown) to an
    /// <see cref="ImageSource"/>.  Supports both file-backed <see cref="BitmapImage"/> and
    /// vector <see cref="DrawingImage"/> resources.  When null, image tags are rendered as
    /// plain text.
    /// </summary>
    public Func<string, ImageSource?>? ImageResolver { get; set; }
    private static readonly DependencyPropertyKey DocumentPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Document), typeof(FlowDocument), typeof(SimpleMarkdownViewer), new FrameworkPropertyMetadata());

    public static readonly DependencyProperty DocumentProperty = DocumentPropertyKey.DependencyProperty;

    public FlowDocument Document {
        get { return (FlowDocument)GetValue(DocumentProperty); }
        protected set { SetValue(DocumentPropertyKey, value); }
    }

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register("Markdown", typeof(string), typeof(SimpleMarkdownViewer), new FrameworkPropertyMetadata(null, MarkdownPropertyChangedCallback));

    private static void MarkdownPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is SimpleMarkdownViewer simpleMarkdownViewer)
            simpleMarkdownViewer.MarkdownChanged(e);
    }

    void MarkdownChanged(DependencyPropertyChangedEventArgs e) {
        RefreshDocument();
    }

    public string Markdown {
        // IMPORTANT: To maintain parity between setting a property in XAML and procedural code, do not touch the getter and setter inside this dependency property!
        get {
            return (string)GetValue(MarkdownProperty);
        }
        set {
            SetValue(MarkdownProperty, value);
        }
    }

    public double FontScaleFactor => FontSize / 16;

    bool scaledHeadingStyles;

    void ScaleHeadingStylesIfNeeded() {
        if (scaledHeadingStyles)
            return;
        scaledHeadingStyles = true;
        if (TryFindResource(Styles.Heading1FontSizeKey) is double heading1FontSize)
            Resources[Styles.Heading1FontSizeKey] = heading1FontSize * FontScaleFactor;
        if (TryFindResource(Styles.Heading2FontSizeKey) is double heading2FontSize)
            Resources[Styles.Heading2FontSizeKey] = heading2FontSize * FontScaleFactor;
        if (TryFindResource(Styles.Heading3FontSizeKey) is double heading3FontSize)
            Resources[Styles.Heading3FontSizeKey] = heading3FontSize * FontScaleFactor;
        if (TryFindResource(Styles.Heading4FontSizeKey) is double heading4FontSize)
            Resources[Styles.Heading4FontSizeKey] = heading4FontSize * FontScaleFactor;
        if (TryFindResource(Styles.Heading5FontSizeKey) is double heading5FontSize)
            Resources[Styles.Heading5FontSizeKey] = heading5FontSize * FontScaleFactor;
        if (TryFindResource(Styles.Heading6FontSizeKey) is double heading6FontSize)
            Resources[Styles.Heading6FontSizeKey] = heading6FontSize * FontScaleFactor;
    }

    void SetHeadingStyle(Paragraph paragraph, ref string cleanParagraphText) {
        if (cleanParagraphText == null)
            return;
        int headingStyle = 0;
        while (cleanParagraphText.Length > 0 && cleanParagraphText[0] == '#') {
            headingStyle++;
            cleanParagraphText = cleanParagraphText.Substring(1).Trim();
        }
        if (headingStyle == 0)
            return;

        ScaleHeadingStylesIfNeeded();
        switch (headingStyle) {
            case 1:
                SetStyle(paragraph, Styles.Heading1StyleKey);
                break;
            case 2:
                SetStyle(paragraph, Styles.Heading2StyleKey);
                break;
            case 3:
                SetStyle(paragraph, Styles.Heading3StyleKey);
                break;
            case 4:
                SetStyle(paragraph, Styles.Heading4StyleKey);
                break;
            case 5:
                SetStyle(paragraph, Styles.Heading5StyleKey);
                break;
            case 6:
                SetStyle(paragraph, Styles.Heading6StyleKey);
                break;
        }
    }

    Block? AddParagraph(FlowDocument flowDocument, string paragraphText, Block? lastBlock, Paragraph? continuationParagraph = null) {
        string cleanParagraphText = paragraphText.Trim();
        if (string.IsNullOrWhiteSpace(cleanParagraphText))
            return lastBlock;

        // ── Inline image: ![width:alt](path) trailing text ────────────────────
        if (ImageResolver is not null) {
            var m = ImageLineRegex.Match(cleanParagraphText);
            if (m.Success) {
                var path         = m.Groups[3].Value.Trim();
                var trailingText = m.Groups[4].Value.Trim();
                var imageWidth   = (int.TryParse(m.Groups[1].Value, out var w) ? w : 48) * FontScaleFactor;
                var imgSource    = ImageResolver(path);

                if (imgSource is not null) {
                    var img = new System.Windows.Controls.Image {
                        Source            = imgSource,
                        Width             = imageWidth,
                        Stretch           = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Top,
                    };

                    if (string.IsNullOrWhiteSpace(trailingText)) {
                        // Standalone image — center it horizontally.
                        var centered = new StackPanel {
                            Orientation         = Orientation.Vertical,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        };
                        centered.Children.Add(img);
                        var centeredBlock = new BlockUIContainer(centered) {
                            Margin = new Thickness(0, 4, 0, 8)
                        };
                        flowDocument.Blocks.Add(centeredBlock);
                        return centeredBlock;
                    }

                    // Image left, text right.
                    img.Margin = new Thickness(0, 0, 12, 0);
                    var textBlock = new TextBlock {
                        // WrapWithOverflow: wraps at word boundaries only — never splits a word
                        // mid-character even when the available width is very narrow.
                        TextWrapping      = TextWrapping.WrapWithOverflow,
                        VerticalAlignment = VerticalAlignment.Top,
                    };
                    foreach (var inline in ParseInlines(trailingText))
                        textBlock.Inlines.Add(inline);
                    var dock = new DockPanel { LastChildFill = true };
                    DockPanel.SetDock(img, Dock.Left);
                    dock.Children.Add(img);
                    dock.Children.Add(textBlock);
                    var dockBlock = new BlockUIContainer(dock) {
                        Margin = new Thickness(0, 2, 0, 8)
                    };
                    flowDocument.Blocks.Add(dockBlock);
                    return dockBlock;
                }

                // Image failed to load — fall through with just the trailing text.
                if (string.IsNullOrWhiteSpace(trailingText))
                    return lastBlock;
                cleanParagraphText = trailingText;
            }
        }

        const string listItemStart = "* ";
        if (paragraphText.StartsWith(listItemStart)) {
            string listItemStr = paragraphText.Substring(listItemStart.Length);
            Block? listItemContents = AddParagraph(flowDocument, listItemStr, null);
            if (listItemContents is Paragraph paragraphContents) {
                ListItem listItem = new ListItem(paragraphContents);
                SetStyle(listItem, Styles.ListItemKey);
                List parentList;
                if (lastBlock is List list) {
                    list.ListItems.Add(listItem);
                    parentList = list;
                }
                else {
                    parentList = new List(listItem);
                    ScaleListMargin();

                    SetStyle(parentList, Styles.ListKey);
                    flowDocument.Blocks.Add(parentList);
                }
                return parentList;
            }
        }

        Paragraph paragraph = new Paragraph();

        // ── [vspace:N], [indent], [center] prefix modifiers ───────────────────
        double? marginTop  = null;
        double? marginLeft = null;
        bool isCentered = false;
        double? sizeMultiplier = null;
        int? linesCount = null;
        bool modified = true;
        while (modified) {
            modified = false;
            if (cleanParagraphText.StartsWith("[center]", StringComparison.OrdinalIgnoreCase)) {
                isCentered = true;
                cleanParagraphText = cleanParagraphText[8..].TrimStart();
                modified = true;
            }
            if (cleanParagraphText.StartsWith("[indent]", StringComparison.OrdinalIgnoreCase)) {
                marginLeft = FontSize * 2.0;
                cleanParagraphText = cleanParagraphText[8..].TrimStart();
                modified = true;
            }
            var vm = VSpaceRegex.Match(cleanParagraphText);
            if (vm.Success && double.TryParse(vm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var factor)) {
                marginTop = FontSize * factor;
                cleanParagraphText = cleanParagraphText[vm.Length..].TrimStart();
                modified = true;
            }
            var sm = SizeRegex.Match(cleanParagraphText);
            if (sm.Success && double.TryParse(sm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var multiplier) && multiplier > 0) {
                sizeMultiplier = multiplier;
                cleanParagraphText = cleanParagraphText[sm.Length..].TrimStart();
                modified = true;
            }
            var lm = LinesRegex.Match(cleanParagraphText);
            if (lm.Success && int.TryParse(lm.Groups[1].Value, out var n) && n > 1) {
                linesCount = n;
                cleanParagraphText = cleanParagraphText[lm.Length..].TrimStart();
                modified = true;
            }
        }
        if (marginTop.HasValue || marginLeft.HasValue) {
            double top = marginTop ?? 0;
            if (top < 0) {
                // WPF Block.Margin rejects negative values. Pull up by reducing the
                // previous block's bottom margin instead (clamped to 0).
                if (lastBlock is Paragraph prevPara) {
                    var pm = prevPara.Margin;
                    prevPara.Margin = new Thickness(pm.Left, pm.Top, pm.Right,
                        Math.Max(0, pm.Bottom + top));
                }
                top = 0;
            }
            paragraph.Margin = new Thickness(marginLeft ?? 0, top, 0, 0);
        }
        if (isCentered)
            paragraph.TextAlignment = TextAlignment.Center;
        if (sizeMultiplier.HasValue)
            paragraph.FontSize = FontSize * sizeMultiplier.Value;

        if (linesCount.HasValue)
        {
            string plainForMeasure = InlineMarkupRegex.Replace(cleanParagraphText, m =>
            {
                var s = m.Value;
                if (s.StartsWith("**") && s.EndsWith("**") && s.Length > 4) return s[2..^2];
                if (s.StartsWith('*')  && s.EndsWith('*')  && s.Length > 2)  return s[1..^1];
                return s;
            });

            double effectiveFontSize = sizeMultiplier.HasValue ? FontSize * sizeMultiplier.Value : FontSize;
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double pixelsPerDip = 1.0;
            try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch { /* not yet in visual tree — 1.0 is a safe logical-unit fallback */ }
            if (pixelsPerDip <= 0) pixelsPerDip = 1.0;

            var ft = new FormattedText(
                plainForMeasure,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                effectiveFontSize,
                Brushes.Black,
                pixelsPerDip);

            double singleLineWidth = ft.WidthIncludingTrailingWhitespace;
            if (singleLineWidth > 0)
            {
                // Paragraph does not expose MaxWidth; use a TextBlock inside a
                // BlockUIContainer so we can constrain the wrapping width directly.
                var tb = new System.Windows.Controls.TextBlock {
                    TextWrapping      = System.Windows.TextWrapping.Wrap,
                    MaxWidth          = singleLineWidth / linesCount.Value,
                    FontSize          = effectiveFontSize,
                    FontFamily        = FontFamily,
                    HorizontalAlignment = isCentered
                        ? HorizontalAlignment.Center
                        : HorizontalAlignment.Left,
                };
                if (isCentered) tb.TextAlignment = TextAlignment.Center;
                foreach (var inline in ParseInlines(cleanParagraphText))
                    tb.Inlines.Add(inline);

                var container = new BlockUIContainer(tb) { Margin = paragraph.Margin };
                flowDocument.Blocks.Add(container);
                return container;
            }
        }

        // ── <br> continuation: append to existing paragraph ───────────────────
        if (continuationParagraph is not null) {
            continuationParagraph.Inlines.Add(new LineBreak());
            foreach (var inline in ParseInlines(cleanParagraphText))
                continuationParagraph.Inlines.Add(inline);
            return continuationParagraph;
        }

        SetHeadingStyle(paragraph, ref cleanParagraphText);

        foreach (Inline inline in ParseInlines(cleanParagraphText))
            paragraph.Inlines.Add(inline);

        flowDocument.Blocks.Add(paragraph);
        return paragraph;
    }

    // Splits on **bold** and *italic* spans (bold checked first so ** isn't consumed as two *)
    private static readonly Regex InlineMarkupRegex = new(
        @"(\*\*[^*]+?\*\*|\*[^*]+?\*)",
        RegexOptions.Compiled);

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        foreach (string part in InlineMarkupRegex.Split(text))
        {
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                yield return new Bold(new Run(part[2..^2]));
            else if (part.StartsWith('*') && part.EndsWith('*') && part.Length > 2)
                yield return new Italic(new Run(part[1..^1]));
            else
                yield return new Run(part);
        }
    }

    private void ScaleListMargin() {
        if (TryFindResource(Styles.ListMarginKey) is Thickness listMargin)
            Resources[Styles.ListMarginKey] = new Thickness(listMargin.Left * FontScaleFactor, 0, 0, 0);
    }

    // Matches [vspace:N] or [vspace:-N] at the start of a paragraph (N may be decimal)
    private static readonly Regex VSpaceRegex = new(
        @"^\[vspace:(-?\d+(?:\.\d+)?)\]",
        RegexOptions.Compiled);

    // Matches [size:N] at the start of a paragraph (N is a positive multiplier, e.g. 1.2)
    private static readonly Regex SizeRegex = new(
        @"^\[size:(\d+(?:\.\d+)?)\]",
        RegexOptions.Compiled);

    // Matches [lines:N] at the start of a paragraph (N is a positive integer, e.g. 2)
    private static readonly Regex LinesRegex = new(
        @"^\[lines:(\d+)\]",
        RegexOptions.Compiled);

    FlowDocument CreateFlowDocumentFromMarkdown() {
        FlowDocument flowDocument = new FlowDocument();
        SquadDashTrace.Write(TraceCategory.UI, $"[Callout] CreateFlowDocumentFromMarkdown: this.FontSize={FontSize:F1} (before assign)");
        flowDocument.FontSize = FontSize;
        SquadDashTrace.Write(TraceCategory.UI, $"[Callout] CreateFlowDocumentFromMarkdown: flowDocument.FontSize={flowDocument.FontSize:F1} (after local assign, before SetStyle)");
        SetStyle(flowDocument, Styles.DocumentStyleKey);
        SquadDashTrace.Write(TraceCategory.UI, $"[Callout] CreateFlowDocumentFromMarkdown: flowDocument.FontSize={flowDocument.FontSize:F1} (after SetStyle)");

        string[] lines = ConvertEscapedCharacters().Split('\n');
        Block? lastBlock = null;
        Paragraph? brParagraph = null; // non-null when previous line ended with <br>

        foreach (string line in lines) {
            string processLine = line;
            bool hasBr = processLine.TrimEnd().EndsWith("<br>", StringComparison.OrdinalIgnoreCase);
            if (hasBr)
                processLine = processLine.TrimEnd()[..^4];

            lastBlock = AddParagraph(flowDocument, processLine, lastBlock, brParagraph);
            brParagraph = hasBr && lastBlock is Paragraph p ? p : null;
        }

        return flowDocument;
    }

    private string ConvertEscapedCharacters() {
        const string encodedDoubleSlash = "$eScApElItErAl$";
        string encodedEscapes = Markdown.Replace("\\\\", encodedDoubleSlash);
        string converted = encodedEscapes.Replace("\\n", "\n");
        return converted.Replace(encodedDoubleSlash, "\\");
    }

    private static void SetStyle(FrameworkContentElement element, object styleKey) {
        element.SetResourceReference(FrameworkContentElement.StyleProperty, styleKey);
    }

    void RefreshDocument() {
        Document = CreateFlowDocumentFromMarkdown();
    }

    public SimpleMarkdownViewer() {
    }
}