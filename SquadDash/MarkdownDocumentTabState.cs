using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal sealed class MarkdownDocumentTabState {
    private MarkdownDocumentTabState(string tabTitle, string filePath, string text) {
        TabTitle = tabTitle;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        var stripped = StripFrontMatter(text, out var frontMatter);
        FrontMatter  = frontMatter;
        SavedText    = stripped;
        WorkingText  = stripped;

        WebBrowser = new WebBrowser();
        WebBrowser.Tag = this;
        FallbackViewer = new FlowDocumentScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        FallbackViewer.SetResourceReference(Control.BackgroundProperty, "TranscriptSurface");
        EditorTextBox = new RichTextBox {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Segoe UI Emoji"),
            FontSize = Application.Current?.Resources["FontSizeMedium"] is double fs ? fs : 12.0,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed
        };
        EditorTextBox.SetPlainText(stripped);
        EditorTextBox.Document.Resources[typeof(Paragraph)] = new Style(typeof(Paragraph))
        {
            Setters = { new Setter(Block.MarginProperty, new Thickness(0)) }
        };
        EditorTextBox.SetResourceReference(RichTextBox.BackgroundProperty, "InputSurface");
        EditorTextBox.SetResourceReference(RichTextBox.ForegroundProperty, "LabelText");
        EditorTextBox.SetResourceReference(RichTextBox.SelectionBrushProperty, "DocEditorSelectionBrush");
        EditorTextBox.SetResourceReference(RichTextBox.SelectionOpacityProperty, "DocEditorSelectionOpacity");
        
        // Force plain-text paste — RichTextBox defaults to rich paste which would preserve formatting
        DataObject.AddPastingHandler(EditorTextBox, (s, e) => {
            if (e.FormatToApply != DataFormats.UnicodeText && e.FormatToApply != DataFormats.Text) {
                if (Clipboard.ContainsText())
                    e.FormatToApply = DataFormats.UnicodeText;
                else
                    e.CancelCommand();
            }
        });

        PreviewHost = new Grid();
        PreviewHost.Children.Add(WebBrowser);
        PreviewHost.Children.Add(FallbackViewer);
    }

    public string TabTitle { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public string FrontMatter { get; set; } = string.Empty;
    public string SavedText { get; set; }
    public string WorkingText { get; set; }
    public bool IsDirty { get; set; }
    public WebBrowser WebBrowser { get; }
    public FlowDocumentScrollViewer FallbackViewer { get; }
    public RichTextBox EditorTextBox { get; }
    public Grid PreviewHost { get; }
    public TabItem? TabItem { get; set; }
    internal double? PendingScrollFraction { get; set; }
    internal bool IsReloadPending { get; set; }
    internal FileSystemWatcher? FileWatcher { get; set; }

    // ── Revision locks ─────────────────────────────────────────────────────────
    private readonly List<EditorRevisionLock> _lockedRanges = [];
    public bool HasLockedRanges => _lockedRanges.Count > 0;
    public IReadOnlyList<EditorRevisionLock> LockedRanges => _lockedRanges;
    public EditorRevisionLock AddRevisionLock(TextPointer start, TextPointer end) {
        var revLock = new EditorRevisionLock(start, end);
        _lockedRanges.Add(revLock);
        return revLock;
    }
    public void RemoveRevisionLock(EditorRevisionLock revLock) => _lockedRanges.Remove(revLock);

    public static MarkdownDocumentTabState Load(string tabTitle, string filePath) {
        var text = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        return new MarkdownDocumentTabState(tabTitle, filePath, text);
    }

    /// <summary>Creates a read-only tab state from an in-memory Markdown string (no backing file).</summary>
    public static MarkdownDocumentTabState FromContent(string tabTitle, string content) =>
        new MarkdownDocumentTabState(tabTitle, filePath: string.Empty, text: content);

    // Detects and strips a Jekyll/just-the-docs YAML frontmatter block (--- ... ---) from
    // the start of the text. The stripped block is returned via frontMatter; the remainder
    // is the return value. If no frontmatter is found, frontMatter is empty and the original
    // text is returned unchanged.
    private static readonly Regex s_frontMatterRegex = new(
        @"^---[ \t]*\r?\n[\s\S]*?\r?\n---[ \t]*\r?\n?",
        RegexOptions.Compiled);

    public static string StripFrontMatter(string rawText, out string frontMatter) {
        frontMatter = string.Empty;
        if (string.IsNullOrEmpty(rawText)) return rawText;
        var m = s_frontMatterRegex.Match(rawText);
        if (!m.Success) return rawText;
        frontMatter = m.Value;
        return rawText[m.Length..];
    }
}

/// <summary>Tracks one locked text range during an active AI revision.</summary>
internal sealed class EditorRevisionLock {
    public TextPointer Start { get; }
    public TextPointer End   { get; }
    public EditorRevisionLock(TextPointer start, TextPointer end) { Start = start; End = end; }
}
