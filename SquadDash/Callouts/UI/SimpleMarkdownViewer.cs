using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Reflection.Metadata;

namespace SquadDash;
public class SimpleMarkdownViewer : Control {
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

    Block AddParagraph(FlowDocument flowDocument, string paragraphText, Block lastBlock) {
        string cleanParagraphText = paragraphText.Trim();
        if (string.IsNullOrWhiteSpace(cleanParagraphText))
            return lastBlock;

        const string listItemStart = "* ";
        if (paragraphText.StartsWith(listItemStart)) {
            string listItemStr = paragraphText.Substring(listItemStart.Length);
            Block listItemContents = AddParagraph(flowDocument, listItemStr, null);
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

        SetHeadingStyle(paragraph, ref cleanParagraphText);

        const string boldDelimiter = "**";
        bool bold = false;
        string[] textRuns = cleanParagraphText.Split(new string[] { boldDelimiter }, StringSplitOptions.None);

        foreach (string text in textRuns) {
            Inline textRun = new Run() { Text = text };
            if (bold)
                textRun = new Bold(textRun);
            paragraph.Inlines.Add(textRun);
            bold = !bold;
        }

        flowDocument.Blocks.Add(paragraph);
        return paragraph;
    }

    private void ScaleListMargin() {
        if (TryFindResource(Styles.ListMarginKey) is Thickness listMargin)
            Resources[Styles.ListMarginKey] = new Thickness(listMargin.Left * FontScaleFactor, 0, 0, 0);
    }

    FlowDocument CreateFlowDocumentFromMarkdown() {
        FlowDocument flowDocument = new FlowDocument();
        flowDocument.FontSize = FontSize;
        SetStyle(flowDocument, Styles.DocumentStyleKey);

        string[] paragraphs = ConvertEscapedCharacters().Split('\n');
        Block lastBlock = null;
        foreach (string paragraph in paragraphs)
            lastBlock = AddParagraph(flowDocument, paragraph, lastBlock);

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