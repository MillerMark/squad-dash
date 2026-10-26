using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

internal static class PromptTextStyling
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PromptTextStyling),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsOverlayVisibleProperty = DependencyProperty.RegisterAttached(
        "IsOverlayVisible", typeof(bool), typeof(PromptTextStyling), new PropertyMetadata(true));

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(PromptTextAdorner), typeof(PromptTextStyling));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    public static bool GetIsOverlayVisible(DependencyObject target) => (bool)target.GetValue(IsOverlayVisibleProperty);
    public static void SetIsOverlayVisible(DependencyObject target, bool value) => target.SetValue(IsOverlayVisibleProperty, value);

    internal static bool IsApplyingFormatting(RichTextBox editor) =>
        editor.GetValue(AdornerProperty) is PromptTextAdorner { IsApplyingFormatting: true };

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not RichTextBox editor)
            throw new ArgumentException("Prompt text styling requires a RichTextBox.", nameof(target));

        if ((bool)e.NewValue)
        {
            editor.Loaded += OnLoaded;
            editor.Unloaded += OnUnloaded;
            if (editor.IsLoaded)
                Attach(editor);
        }
        else
        {
            editor.Loaded -= OnLoaded;
            editor.Unloaded -= OnUnloaded;
            Detach(editor);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((RichTextBox)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((RichTextBox)sender);

    private static void Attach(RichTextBox editor)
    {
        if (editor.GetValue(AdornerProperty) is PromptTextAdorner)
            return;
        var layer = AdornerLayer.GetAdornerLayer(editor)
            ?? throw new InvalidOperationException("Prompt text styling requires an AdornerLayer.");
        var adorner = new PromptTextAdorner(editor);
        editor.SetValue(AdornerProperty, adorner);
        layer.Add(adorner);
    }

    private static void Detach(RichTextBox editor)
    {
        if (editor.GetValue(AdornerProperty) is not PromptTextAdorner adorner)
            return;
        adorner.Dispose();
        (VisualTreeHelper.GetParent(adorner) as AdornerLayer)?.Remove(adorner);
        editor.ClearValue(AdornerProperty);
    }
}

/// <summary>
/// Token accents are paint only except for inline-code font families and heading sizes,
/// which use native layout so line heights, caret positions, and selection geometry agree.
/// </summary>
internal sealed class PromptTextAdorner : Adorner, IDisposable
{
    private static readonly FontFamily CodeFont = new("Consolas");
    private static readonly DependencyProperty HeadingBrushProperty = DependencyProperty.Register(
        "HeadingBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty HeadingDelimiterBrushProperty = DependencyProperty.Register(
        "HeadingDelimiterBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        "BorderBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty QuoteBrushProperty = DependencyProperty.Register(
        "QuoteBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(SystemColors.WindowTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty PlaceholderBrushProperty = DependencyProperty.Register(
        "PlaceholderBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(SystemColors.WindowTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty QuoteDelimiterBrushProperty = DependencyProperty.Register(
        "QuoteDelimiterBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty PlaceholderDelimiterBrushProperty = DependencyProperty.Register(
        "PlaceholderDelimiterBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty BoldDelimiterBrushProperty = DependencyProperty.Register(
        "BoldDelimiterBrush", typeof(Brush), typeof(PromptTextAdorner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private sealed record ParagraphTokens(
        string Text,
        IReadOnlyList<PromptTextToken> Tokens,
        IReadOnlyList<Glyph> Glyphs);
    private sealed class Glyph(
        TextPointer start,
        TextPointer end,
        PromptTextTokenKind kind,
        Brush? color,
        bool isQuoted,
        bool isPlaceholder,
        bool isHeading,
        PromptTextTokenKind? delimiter)
    {
        internal TextPointer Start { get; } = start;
        internal TextPointer End { get; } = end;
        internal PromptTextTokenKind Kind { get; } = kind;
        internal Brush? Color { get; } = color;
        internal bool IsQuoted { get; } = isQuoted;
        internal bool IsPlaceholder { get; } = isPlaceholder;
        internal bool IsHeading { get; } = isHeading;
        internal PromptTextTokenKind? Delimiter { get; } = delimiter;
        internal Rect DocumentRect { get; set; } = Rect.Empty;
    }
    // Geometry.Bounds is deceptively expensive for the GlyphRun-generated GeometryGroups used
    // here: WPF expands their child paths every time the property is queried. Keep the bounds that
    // were already calculated while reading the GlyphRun and translate them with the geometry.
    private sealed record Ink(Geometry Geometry, Rect Bounds, Brush Brush, double FontSize);
    private readonly record struct ViewportState(
        Rect Bounds,
        double HorizontalOffset,
        double VerticalOffset)
    {
        internal Rect DocumentBounds => new(
            Bounds.X + HorizontalOffset,
            Bounds.Y + VerticalOffset,
            Bounds.Width,
            Bounds.Height);
    }
    private readonly record struct FontLayout(
        FontFamily Family, double Size, FontStyle Style, FontWeight Weight,
        FontStretch Stretch, FlowDirection Direction, Thickness Padding, double PageWidth,
        Brush Foreground, Brush Background);

    private readonly RichTextBox _editor;
    private readonly Dictionary<Paragraph, ParagraphTokens> _paragraphs = [];
    private readonly List<Glyph> _glyphs = [];
    private readonly HashSet<Visual> _embeddedVisuals = [];
    private readonly HashSet<Paragraph> _geometryDirtyParagraphs = [];
    private Dictionary<int, List<Ink>>? _inkRows;
    private Rect _inkCoverage = Rect.Empty;
    private DispatcherOperation? _refresh;
    private DispatcherOperation? _inkRefresh;
    private bool _disposed;
    private bool _layoutDirty;
    private bool _fontRefreshPending = true;
    private bool _forceInkRefresh = true;
    private bool _inkDirty = true;
    private bool _selectionAffectsRender;
    private FontLayout _fontLayout;
    private double _layoutExtentHeight = double.NaN;
    private DrawingGroup? _retainedDrawing;
    private long _renderGeneration;
    private long _retainedGeneration = -1;
    internal bool IsApplyingFormatting { get; private set; }
    internal int InkCacheBuildCount { get; private set; }
    internal int GlyphCacheBuildCount { get; private set; }
    internal int GlyphRectQueryCount { get; private set; }
    internal int RenderCount { get; private set; }
    internal int DrawingBuildCount { get; private set; }

    internal PromptTextAdorner(RichTextBox editor) : base(editor)
    {
        _editor = editor;
        IsHitTestVisible = false;
        Focusable = false;
        // The adorner layer is outside the prompt's visibility subtree; hide paint, not styling state.
        SetBinding(VisibilityProperty, new Binding
        {
            Source = editor,
            Path = new PropertyPath(PromptTextStyling.IsOverlayVisibleProperty),
            Converter = new BooleanToVisibilityConverter(),
        });
        SetResourceReference(HeadingBrushProperty, "PromptHeadingText");
        SetResourceReference(HeadingDelimiterBrushProperty, "PromptHeadingDelimiter");
        SetResourceReference(BorderBrushProperty, "LabelText");
        SetResourceReference(QuoteBrushProperty, "PromptQuotedText");
        SetResourceReference(PlaceholderBrushProperty, "PromptPlaceholderText");
        SetResourceReference(QuoteDelimiterBrushProperty, "PromptQuoteDelimiter");
        SetResourceReference(PlaceholderDelimiterBrushProperty, "PromptPlaceholderDelimiter");
        SetResourceReference(BoldDelimiterBrushProperty, "PromptBoldDelimiter");
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        editor.TextChanged += OnTextChanged;
        editor.SizeChanged += OnSizeChanged;
        editor.LayoutUpdated += OnLayoutUpdated;
        editor.SelectionChanged += OnSelectionChanged;
        editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged), true);
        ScheduleRefresh();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == HeadingBrushProperty || e.Property == HeadingDelimiterBrushProperty ||
            e.Property == BorderBrushProperty || e.Property == QuoteBrushProperty ||
            e.Property == PlaceholderBrushProperty || e.Property == QuoteDelimiterBrushProperty ||
            e.Property == PlaceholderDelimiterBrushProperty || e.Property == BoldDelimiterBrushProperty)
        {
            // AffectsRender will schedule OnRender; advance the retained-drawing version so a
            // dynamic theme/resource replacement rebuilds it instead of replaying old brushes.
            _renderGeneration++;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _editor) || IsApplyingFormatting)
            return;
        // Keep the last complete frame only while layout determines whether geometry moved.
        // Geometry-stable edits can reuse it; OnLayoutUpdated discards it immediately when a
        // newline, wrap, or font change moves styled cells, preventing stale duplicate accents.
        _layoutDirty = true;
        if (_editor.CaretPosition.Paragraph is { } paragraph)
            _geometryDirtyParagraphs.Add(paragraph);
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_refresh is not null)
            return;
        _refresh = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refresh = null;
            RefreshTokens();
        });
    }

    internal void RefreshTokens()
    {
        if (_disposed)
            return;
        // Rebuild only this cheap ordered index. Cached paragraph glyph objects (and their
        // document rectangles) are retained; changed paragraphs replace just their own objects.
        _glyphs.Clear();
        _embeddedVisuals.Clear();
        var renderChanged = false;
        foreach (var block in _editor.Document.Blocks.OfType<BlockUIContainer>())
            if (block.Child is { } child)
                _embeddedVisuals.Add(child);
        var present = new HashSet<Paragraph>();
        // Deliberately never traverse BlockUIContainer children (the fenced region editors).
        foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>().ToArray())
        {
            present.Add(paragraph);
            foreach (var inline in paragraph.Inlines.OfType<InlineUIContainer>())
                if (inline.Child is { } child)
                    _embeddedVisuals.Add(child);
            var text = RichTextBoxExtensions.GetParagraphText(paragraph);
            if (!_paragraphs.TryGetValue(paragraph, out var cached) || cached.Text != text)
            {
                _geometryDirtyParagraphs.Add(paragraph);
                var oldGlyphs = cached?.Glyphs.Count ?? 0;
                var tokens = PromptTextTokenParser.Parse(text);
                ApplyTokenFonts(paragraph, text.Length, tokens);
                cached = new ParagraphTokens(text, tokens, BuildGlyphs(paragraph, text, tokens));
                _paragraphs[paragraph] = cached;
                // Native ink is only consumed by styled cells. A plain paragraph can grow without
                // touching the retained ink above or below it; layout validation below catches the
                // less common case where that growth wraps and moves later styled paragraphs.
                if (oldGlyphs > 0 || cached.Glyphs.Count > 0)
                {
                    renderChanged = true;
                    MarkInkCacheDirty(clearGlyphRects: false);
                }
            }
            else if (_fontRefreshPending)
            {
                ApplyTokenFonts(paragraph, cached.Text.Length, cached.Tokens);
            }
            _glyphs.AddRange(cached.Glyphs);
        }
        foreach (var paragraph in _paragraphs.Keys.Where(p => !present.Contains(p)).ToArray())
        {
            if (_paragraphs[paragraph].Glyphs.Count > 0)
            {
                renderChanged = true;
                MarkInkCacheDirty(clearGlyphRects: false);
            }
            _paragraphs.Remove(paragraph);
        }
        _fontRefreshPending = false;
        _layoutDirty = true;
        _selectionAffectsRender = DoesSelectionAffectRender();
        if (_inkDirty)
            ScheduleInkRefresh(deferForInput: true);
        if (renderChanged && !_inkDirty)
            InvalidateDrawing();
    }

    internal static double HeadingSizeScale(int level) => level switch
    {
        1 => 1.48,
        2 => 1.32,
        3 => 1.16,
        4 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private void ApplyTokenFonts(Paragraph paragraph, int length, IReadOnlyList<PromptTextToken> tokens)
    {
        var code = new bool[length];
        var sizes = new double[length];
        Array.Fill(sizes, _editor.FontSize);
        foreach (var token in tokens)
        {
            if (token.Kind == PromptTextTokenKind.InlineCodeContent)
                Array.Fill(code, true, token.Start, token.Length);
            else if (token.Kind is PromptTextTokenKind.HeadingMarker or PromptTextTokenKind.HeadingContent)
                Array.Fill(sizes, _editor.FontSize * HeadingSizeScale(
                    token.Kind == PromptTextTokenKind.HeadingMarker ? token.Length : token.HeadingLevel),
                    token.Start, token.Length);
        }

        var changes = new List<(TextRange Range, FontFamily Font, double Size)>();
        var offset = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is LineBreak)
                offset++;
            if (inline is not Run run)
                continue;
            for (var start = 0; start < run.Text.Length;)
            {
                var end = start + 1;
                while (end < run.Text.Length && code[offset + end] == code[offset + start] &&
                    sizes[offset + end] == sizes[offset + start])
                    end++;
                var font = code[offset + start] ? CodeFont : _editor.FontFamily;
                var size = sizes[offset + start];
                if (!run.FontFamily.Equals(font) || run.FontSize != size)
                    changes.Add((new TextRange(run.ContentStart.GetPositionAtOffset(start)!,
                        run.ContentStart.GetPositionAtOffset(end)!), font, size));
                start = end;
            }
            offset += run.Text.Length;
        }
        if (changes.Count == 0)
            return;

        IsApplyingFormatting = true;
        try
        {
            // No document rebuild or undo toggle. The prompt's shared history records text,
            // not these derived fonts. Native sizing also reserves the full heading line height.
            using (_editor.DeclareChangeBlock())
                foreach (var (range, font, size) in changes)
                {
                    range.ApplyPropertyValue(TextElement.FontFamilyProperty, font);
                    range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                }
        }
        finally
        {
            IsApplyingFormatting = false;
        }
    }

    private IReadOnlyList<Glyph> BuildGlyphs(
        Paragraph paragraph,
        string text,
        IReadOnlyList<PromptTextToken> tokens)
    {
        GlyphCacheBuildCount++;
        if (tokens.Count == 0)
            return [];
        var glyphs = new List<Glyph>();
        var length = text.Length;
        var boldChanges = new int[length + 1];
        var quoted = new bool[length];
        var placeholders = new bool[length];
        var delimiters = new PromptTextTokenKind?[length];
        var code = new bool[length];
        var headings = new bool[length];
        var colors = new Dictionary<int, Brush>();
        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case PromptTextTokenKind.QuotedContent:
                    Array.Fill(quoted, true, token.Start, token.Length);
                    break;
                case PromptTextTokenKind.Placeholder:
                    Array.Fill(placeholders, true, token.Start, token.Length);
                    break;
                case PromptTextTokenKind.QuoteDelimiter:
                case PromptTextTokenKind.PlaceholderDelimiter:
                case PromptTextTokenKind.BoldDelimiter:
                case PromptTextTokenKind.InlineCodeDelimiter:
                case PromptTextTokenKind.HeadingMarker:
                    Array.Fill(delimiters, token.Kind, token.Start, token.Length);
                    break;
                case PromptTextTokenKind.InlineCodeContent:
                    // Code wins even when surrounded by prose quotes, including its delimiters.
                    var codeStart = token.Start;
                    var codeEnd = token.Start + token.Length;
                    while (codeStart > 0 && text[codeStart - 1] == '`')
                        codeStart--;
                    while (codeEnd < length && text[codeEnd] == '`')
                        codeEnd++;
                    Array.Fill(code, true, codeStart, codeEnd - codeStart);
                    break;
                case PromptTextTokenKind.BoldContent:
                    boldChanges[token.Start]++;
                    boldChanges[token.Start + token.Length]--;
                    break;
                case PromptTextTokenKind.HexColor:
                    var brush = new SolidColorBrush(token.Color);
                    brush.Freeze();
                    colors[token.Start] = brush;
                    break;
                case PromptTextTokenKind.HeadingContent:
                    Array.Fill(headings, true, token.Start, token.Length);
                    break;
            }
        }
        for (var i = 1; i < boldChanges.Length; i++)
            boldChanges[i] += boldChanges[i - 1];

        var offset = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is LineBreak)
            {
                offset++;
                continue;
            }
            if (inline is not Run run)
                continue;
            for (var i = 0; i < run.Text.Length; i++)
            {
                var position = offset + i;
                var isQuoted = quoted[position] && !code[position];
                var isPlaceholder = placeholders[position] && !code[position];
                var isHeading = headings[position] && !code[position];
                var delimiter = delimiters[position];
                if (code[position] && delimiter != PromptTextTokenKind.InlineCodeDelimiter)
                    delimiter = null;
                var isColor = colors.TryGetValue(position, out var color);
                var kind = isColor ? PromptTextTokenKind.HexColor
                    : boldChanges[position] > 0 ? PromptTextTokenKind.BoldContent
                    : PromptTextTokenKind.QuotedContent;
                if (!isColor && boldChanges[position] == 0 &&
                    !isQuoted && !isPlaceholder && !isHeading && delimiter is null)
                    continue;
                var start = run.ContentStart.GetPositionAtOffset(i)!;
                glyphs.Add(new Glyph(start, start.GetPositionAtOffset(1)!, kind, color,
                    isQuoted, isPlaceholder, isHeading, delimiter));
            }
            offset += run.Text.Length;
        }
        return glyphs;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Height changes only reveal or hide document-coordinate geometry. Width changes
        // reflow the document and therefore invalidate every cached glyph position.
        if (e.WidthChanged)
        {
            _layoutDirty = true;
            _forceInkRefresh = true;
        }
        ScheduleInkRefresh();
        InvalidateDrawing();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var projectionChanged = e.HorizontalChange != 0 || e.VerticalChange != 0 ||
            e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0;
        if (!projectionChanged)
        {
            // Text edits also raise ScrollChanged for extent-only changes. Rebuilding the retained
            // drawing here would replay pre-layout positions; LayoutUpdated will either reuse the
            // cache or discard it once the new geometry is known.
            _layoutDirty = true;
            return;
        }
        // Scrolling changes only the projection from document to viewport coordinates.
        // Keep the expensive glyph outlines and coalesce any cache-extension work.
        ScheduleInkRefresh();
        InvalidateDrawing();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_layoutDirty || _refresh is not null)
            return;
        var affectsRender = DoesSelectionAffectRender();
        // Caret/selection moves wholly within plain text cannot change the adorner. Avoid
        // replaying every cached geometry merely because WPF moved its native caret.
        if (_selectionAffectsRender || affectsRender)
            InvalidateDrawing();
        _selectionAffectsRender = affectsRender;
    }

    private bool DoesSelectionAffectRender()
    {
        var selection = _editor.Selection;
        if (selection.IsEmpty)
        {
            var caret = _editor.CaretPosition;
            var paragraph = caret.Paragraph;
            if (paragraph is null || !_paragraphs.TryGetValue(paragraph, out var cached))
                return false;
            return cached.Glyphs.Any(glyph =>
                caret.CompareTo(glyph.Start) >= 0 && caret.CompareTo(glyph.End) <= 0);
        }
        return _glyphs.Any(glyph => selection.Start.CompareTo(glyph.End) < 0 &&
            selection.End.CompareTo(glyph.Start) > 0);
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.HighContrast))
            InvalidateDrawing();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        var fontLayout = new FontLayout(_editor.FontFamily, _editor.FontSize, _editor.FontStyle,
            _editor.FontWeight, _editor.FontStretch, _editor.FlowDirection, _editor.Padding,
            _editor.Document.PageWidth, _editor.Foreground, _editor.Background);
        // Font changes can move glyphs without changing the scroll extent. Text changes usually
        // do not move any styled cell at all; validate the retained document-coordinate cells and
        // keep their native-ink cache when the edit stayed on one unwrapped plain line.
        if (!_layoutDirty && fontLayout == _fontLayout)
            return;
        if (!Equals(fontLayout.Family, _fontLayout.Family) || fontLayout.Size != _fontLayout.Size)
        {
            _fontRefreshPending = true;
            ScheduleRefresh();
        }
        var viewport = GetViewportState();
        var extentHeight = GetExtentHeight();
        var globalLayoutChanged = _forceInkRefresh || fontLayout != _fontLayout;
        var extentDelta = double.IsNaN(_layoutExtentHeight)
            ? 0
            : extentHeight - _layoutExtentHeight;
        var extentChanged = Math.Abs(extentDelta) > 0.5;
        var editedGeometryChanged = !globalLayoutChanged && !extentChanged &&
            HasEditedParagraphGeometryChanged(viewport);
        var layoutChanged = globalLayoutChanged || extentChanged || editedGeometryChanged;
        _fontLayout = fontLayout;
        _layoutExtentHeight = extentHeight;
        _layoutDirty = false;
        _forceInkRefresh = false;
        if (layoutChanged)
            DiscardMovedGeometry(
                translateDownstream: extentChanged && !globalLayoutChanged,
                extentDelta);
        _geometryDirtyParagraphs.Clear();
        if (_inkDirty)
            ScheduleInkRefresh(deferForInput: true);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        RenderCount++;
        if (_retainedDrawing is not null && _retainedGeneration == _renderGeneration)
        {
            drawingContext.DrawDrawing(_retainedDrawing);
            return;
        }

        var drawing = new DrawingGroup();
        using (var retainedContext = drawing.Open())
            BuildDrawing(retainedContext);
        _retainedDrawing = drawing;
        _retainedGeneration = _renderGeneration;
        DrawingBuildCount++;
        drawingContext.DrawDrawing(drawing);
    }

    private void BuildDrawing(DrawingContext drawingContext)
    {
        if (_disposed || _glyphs.Count == 0)
            return;
        var viewport = GetViewportState();
        var bounds = viewport.Bounds;
        if (_inkRows is null || _inkDirty)
            ScheduleInkRefresh();

        Geometry clip = new RectangleGeometry(bounds);
        if (_editor.Selection.IsEmpty && _editor.CaretPosition.HasValidLayout)
        {
            var caret = _editor.CaretPosition.GetCharacterRect(_editor.CaretPosition.LogicalDirection);
            // Exclude the native caret from every overlay, including the taller swatch.
            if (!caret.IsEmpty)
                clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip,
                    new RectangleGeometry(new Rect(caret.Left - 1, caret.Top, 2, caret.Height)));
        }
        drawingContext.PushClip(clip);
        var borderPen = new Pen((Brush)GetValue(BorderBrushProperty), 0.5);
        var ink = _inkRows;
        var background = FindOpaqueBackground();
        var headingBrush = EnsureVisibleBrush((Brush)GetValue(HeadingBrushProperty), background);
        var headingDelimiterBrush = EnsureVisibleBrush(
            (Brush?)GetValue(HeadingDelimiterBrushProperty) ?? headingBrush, background);
        var quoteBrush = EnsureVisibleBrush((Brush)GetValue(QuoteBrushProperty), background);
        var placeholderBrush = EnsureVisibleBrush((Brush)GetValue(PlaceholderBrushProperty), background);
        var quoteDelimiterBrush = EnsureVisibleBrush(
            (Brush?)GetValue(QuoteDelimiterBrushProperty) ?? _editor.Foreground, background);
        var placeholderDelimiterBrush = EnsureVisibleBrush(
            (Brush?)GetValue(PlaceholderDelimiterBrushProperty) ?? placeholderBrush, background);
        var boldDelimiterBrush = EnsureVisibleBrush(
            (Brush?)GetValue(BoldDelimiterBrushProperty) ?? _editor.Foreground, background);
        var useProseColor = !SystemParameters.HighContrast && background is not null;
        var boldSpans = new List<Rect>();
        var quoteSpans = new List<Rect>();
        var boldQuoteSpans = new List<Rect>();
        var placeholderSpans = new List<Rect>();
        var boldPlaceholderSpans = new List<Rect>();
        var headingSpans = new List<Rect>();
        var boldHeadingSpans = new List<Rect>();
        var delimiterSpans = new Dictionary<(PromptTextTokenKind Kind, bool Bold), List<Rect>>();
        foreach (var glyph in _glyphs)
        {
            var rect = GetGlyphRect(glyph, viewport);
            if (rect.IsEmpty || rect.Bottom < bounds.Top || rect.Top > bounds.Bottom)
                continue;
            var selected = _editor.Selection.Start.CompareTo(glyph.End) < 0 &&
                _editor.Selection.End.CompareTo(glyph.Start) > 0;
            if (glyph.Color is not null)
            {
                // Leave selected glyphs to WPF, including its high-contrast selection rendering.
                if (!selected && background is not null && GetInk(ink, rect, viewport).Count > 0)
                {
                    drawingContext.PushClip(new RectangleGeometry(rect));
                    drawingContext.DrawRectangle(background, null, rect);
                    // Keep the superscript top-aligned and tuck it toward the following hex digits.
                    drawingContext.PushTransform(new ScaleTransform(0.84, 0.84, rect.Right, rect.Top));
                    DrawInk(drawingContext, ink, rect, viewport, false,
                        (glyph.IsQuoted || glyph.IsPlaceholder) && useProseColor
                            ? glyph.IsPlaceholder ? placeholderBrush : quoteBrush
                            : null);
                    drawingContext.Pop();
                    drawingContext.Pop();
                }
                drawingContext.DrawRectangle(glyph.Color, borderPen,
                    new Rect(rect.Left, rect.Bottom - 6, rect.Width, 6));
            }
            else if (glyph.Delimiter is { } delimiter && useProseColor && !selected)
            {
                var key = (delimiter, glyph.Kind == PromptTextTokenKind.BoldContent);
                if (!delimiterSpans.TryGetValue(key, out var spans))
                    delimiterSpans[key] = spans = [];
                AddBoldSpan(spans, rect);
            }
            else if ((glyph.IsQuoted || glyph.IsPlaceholder) && useProseColor && !selected)
            {
                var spans = glyph.IsPlaceholder
                    ? glyph.Kind == PromptTextTokenKind.BoldContent ? boldPlaceholderSpans : placeholderSpans
                    : glyph.Kind == PromptTextTokenKind.BoldContent ? boldQuoteSpans : quoteSpans;
                AddBoldSpan(spans, rect);
            }
            else if (glyph.IsHeading && useProseColor && !selected)
            {
                AddBoldSpan(glyph.Kind == PromptTextTokenKind.BoldContent ? boldHeadingSpans : headingSpans, rect);
            }
            else if (glyph.Kind == PromptTextTokenKind.BoldContent)
            {
                if (!selected)
                    AddBoldSpan(boldSpans, rect);
            }
        }
        // Bold tokens are indexed per character so selection can exclude individual cells, but
        // drawing the native ink once per character repeats the same GeometryGroup traversal for
        // every letter. Coalesce adjacent, unselected cells on each visual line and paint the ink
        // once per contiguous span instead.
        foreach (var span in boldSpans)
            DrawInk(drawingContext, ink, span, viewport, true);
        // Replace only unselected native ink, retaining its shaping and bold weight.
        DrawColoredSpans(quoteSpans, quoteBrush, false);
        DrawColoredSpans(boldQuoteSpans, quoteBrush, true);
        DrawColoredSpans(placeholderSpans, placeholderBrush, false);
        DrawColoredSpans(boldPlaceholderSpans, placeholderBrush, true);
        DrawColoredSpans(headingSpans, headingBrush, false);
        DrawColoredSpans(boldHeadingSpans, headingBrush, true);
        foreach (var (key, spans) in delimiterSpans)
        {
            var brush = key.Kind switch
            {
                PromptTextTokenKind.QuoteDelimiter => quoteDelimiterBrush,
                PromptTextTokenKind.PlaceholderDelimiter => placeholderDelimiterBrush,
                PromptTextTokenKind.HeadingMarker => headingDelimiterBrush,
                _ => boldDelimiterBrush,
            };
            DrawColoredSpans(spans, brush, key.Bold);
        }
        drawingContext.Pop();

        void DrawColoredSpans(List<Rect> spans, Brush brush, bool bold)
        {
            foreach (var span in spans)
            {
                DrawInk(drawingContext, ink, span, viewport, bold, brush, background);
            }
        }
    }

    private static void AddBoldSpan(List<Rect> spans, Rect rect)
    {
        const double layoutTolerance = 0.5;
        if (spans.Count > 0)
        {
            var previous = spans[^1];
            var sameLine = Math.Abs(previous.Top - rect.Top) <= layoutTolerance &&
                Math.Abs(previous.Height - rect.Height) <= layoutTolerance;
            var adjacent = rect.Left <= previous.Right + layoutTolerance &&
                rect.Right >= previous.Left - layoutTolerance;
            if (sameLine && adjacent)
            {
                spans[^1] = Rect.Union(previous, rect);
                return;
            }
        }
        spans.Add(rect);
    }

    private Rect GetGlyphRect(Glyph glyph, ViewportState viewport)
    {
        if (glyph.DocumentRect.IsEmpty)
        {
            if (!TryGetGlyphDocumentRect(glyph, viewport, out var documentRect))
                return Rect.Empty;
            glyph.DocumentRect = documentRect;
        }

        var rect = glyph.DocumentRect;
        rect.Offset(-viewport.HorizontalOffset, -viewport.VerticalOffset);
        return rect;
    }

    private static bool RectsMatch(Rect first, Rect second)
    {
        const double layoutTolerance = 0.5;
        return Math.Abs(first.Left - second.Left) <= layoutTolerance &&
            Math.Abs(first.Top - second.Top) <= layoutTolerance &&
            Math.Abs(first.Width - second.Width) <= layoutTolerance &&
            Math.Abs(first.Height - second.Height) <= layoutTolerance;
    }

    private bool TryGetGlyphDocumentRect(
        Glyph glyph,
        ViewportState viewport,
        out Rect documentRect)
    {
        GlyphRectQueryCount++;
        documentRect = Rect.Empty;
        if (!glyph.Start.HasValidLayout || !glyph.End.HasValidLayout)
            return false;
        var start = glyph.Start.GetCharacterRect(LogicalDirection.Forward);
        var end = glyph.End.GetCharacterRect(LogicalDirection.Backward);
        if (start.IsEmpty || end.IsEmpty)
            return false;
        var width = Math.Abs(end.Left - start.Left);
        if (width <= 0)
            return false;
        documentRect = new Rect(
            Math.Min(start.Left, end.Left) + viewport.HorizontalOffset,
            start.Top + viewport.VerticalOffset,
            width,
            start.Height);
        return true;
    }

    private bool HasEditedParagraphGeometryChanged(ViewportState viewport)
    {
        foreach (var paragraph in _geometryDirtyParagraphs)
        {
            if (!_paragraphs.TryGetValue(paragraph, out var cached) || cached.Glyphs.Count == 0)
                continue;
            if (GlyphMoved(cached.Glyphs[0]) ||
                (cached.Glyphs.Count > 1 && GlyphMoved(cached.Glyphs[^1])))
                return true;
        }
        return false;

        bool GlyphMoved(Glyph glyph) =>
            !glyph.DocumentRect.IsEmpty &&
            TryGetGlyphDocumentRect(glyph, viewport, out var current) &&
            !RectsMatch(glyph.DocumentRect, current);
    }

    private static Dictionary<int, List<Ink>> IndexInk(IEnumerable<Ink> ink)
    {
        var rows = new Dictionary<int, List<Ink>>();
        foreach (var item in ink)
        {
            var bounds = item.Bounds;
            if (bounds.IsEmpty)
                continue;
            for (var row = (int)Math.Floor(bounds.Top / 64); row <= (int)Math.Floor(bounds.Bottom / 64); row++)
            {
                if (!rows.TryGetValue(row, out var items))
                    rows[row] = items = [];
                items.Add(item);
            }
        }
        return rows;
    }

    private static void DrawInk(
        DrawingContext context,
        Dictionary<int, List<Ink>>? ink,
        Rect rect,
        ViewportState viewport,
        bool bold,
        Brush? foreground = null,
        Brush? background = null)
    {
        var items = GetInk(ink, rect, viewport);
        context.PushClip(new RectangleGeometry(rect));
        context.PushTransform(new TranslateTransform(-viewport.HorizontalOffset, -viewport.VerticalOffset));
        // Never erase native text outside the outlines actually available for replacement.
        // WPF can realize new lines between the scroll event and our deferred ink capture.
        if (background is not null)
            foreach (var item in items)
                context.DrawRectangle(background, null, item.Bounds);
        foreach (var item in items)
        {
            // Embolden native outlines without changing shaping, advances, or caret hit targets.
            var brush = foreground ?? item.Brush;
            context.DrawGeometry(brush, bold ? new Pen(brush, item.FontSize / 24) : null, item.Geometry);
        }
        context.Pop();
        context.Pop();
    }

    private static List<Ink> GetInk(Dictionary<int, List<Ink>>? ink, Rect rect, ViewportState viewport)
    {
        var result = new List<Ink>();
        if (ink is null)
            return result;
        var documentRect = rect;
        documentRect.Offset(viewport.HorizontalOffset, viewport.VerticalOffset);
        var drawn = new HashSet<Ink>();
        for (var row = (int)Math.Floor(documentRect.Top / 64); row <= (int)Math.Floor(documentRect.Bottom / 64); row++)
        {
            if (!ink.TryGetValue(row, out var items))
                continue;
            foreach (var item in items)
            {
                if (!item.Bounds.IntersectsWith(documentRect) || !drawn.Add(item))
                    continue;
                result.Add(item);
            }
        }
        return result;
    }

    private IEnumerable<Ink> ReadInk(Visual visual, Rect visibleBounds)
    {
        if (_embeddedVisuals.Contains(visual))
            yield break;
        var transform = visual.TransformToAncestor(_editor);
        var origin = transform.Transform(new Point());
        var x = transform.Transform(new Point(1, 0));
        var y = transform.Transform(new Point(0, 1));
        var matrix = new Matrix(x.X - origin.X, x.Y - origin.Y, y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y);
        foreach (var ink in ReadDrawing(VisualTreeHelper.GetDrawing(visual), matrix, visibleBounds))
            yield return ink;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                foreach (var ink in ReadInk(child, visibleBounds))
                    yield return ink;
        }
    }

    private static IEnumerable<Ink> ReadDrawing(Drawing? drawing, Matrix matrix, Rect visibleBounds)
    {
        if (drawing is GlyphRunDrawing { GlyphRun: { } run, ForegroundBrush: { } brush })
        {
            var localBounds = run.ComputeInkBoundingBox();
            if (localBounds.IsEmpty)
                yield break;
            localBounds.Offset(run.BaselineOrigin.X, run.BaselineOrigin.Y);
            var runBounds = new MatrixTransform(matrix).TransformBounds(localBounds);
            if (!runBounds.IntersectsWith(visibleBounds))
                yield break;
            var geometry = run.BuildGeometry().CloneCurrentValue();
            var transform = geometry.Transform?.Value ?? Matrix.Identity;
            transform.Append(matrix);
            geometry.Transform = new MatrixTransform(transform);
            // BuildGeometry carries GlyphRun positioning (including its baseline origin) that is
            // not always represented in ComputeInkBoundingBox when WPF splits a line into runs.
            // Pay for the accurate bounds walk once here; every scroll-frame lookup reuses it.
            yield return new Ink(geometry, geometry.Bounds, brush, run.FontRenderingEmSize);
        }
        else if (drawing is DrawingGroup group)
        {
            var transform = group.Transform?.Value ?? Matrix.Identity;
            transform.Append(matrix);
            foreach (var child in group.Children)
                foreach (var ink in ReadDrawing(child, transform, visibleBounds))
                    yield return ink;
        }
    }

    private ViewportState GetViewportState()
    {
        var bounds = new Rect(_editor.RenderSize);
        var horizontalOffset = 0.0;
        var verticalOffset = 0.0;
        if (_editor.Template.FindName("PART_ContentHost", _editor) is ScrollViewer scrollViewer)
        {
            horizontalOffset = scrollViewer.HorizontalOffset;
            verticalOffset = scrollViewer.VerticalOffset;
            if (scrollViewer.Template.FindName("PART_ScrollContentPresenter", scrollViewer) is FrameworkElement viewport)
                bounds.Intersect(viewport.TransformToAncestor(_editor).TransformBounds(new Rect(viewport.RenderSize)));
        }
        return new ViewportState(bounds, horizontalOffset, verticalOffset);
    }

    private double GetExtentHeight()
    {
        if (_editor.Template.FindName("PART_ContentHost", _editor) is ScrollViewer scrollViewer)
            return scrollViewer.ExtentHeight;
        return _editor.RenderSize.Height;
    }

    private bool EnsureInkCache(ViewportState viewport)
    {
        if (!_inkDirty && _inkRows is not null)
        {
            if (Contains(_inkCoverage, viewport.DocumentBounds))
                return false;
            // Reuse outlines for small scrolls only when all visible styled cells were captured.
            // An inflated requested read area is not proof that WPF realized those offscreen lines.
            var needsInk = _glyphs.Any(glyph =>
            {
                var rect = GetGlyphRect(glyph, viewport);
                rect.Intersect(viewport.Bounds);
                if (rect.IsEmpty)
                    return false;
                rect.Offset(viewport.HorizontalOffset, viewport.VerticalOffset);
                return !Contains(_inkCoverage, rect);
            });
            if (!needsInk)
                return false;
        }

        var editorCoverage = viewport.Bounds;
        editorCoverage.Inflate(32, Math.Max(128, viewport.Bounds.Height));
        var documentOffset = new Vector(viewport.HorizontalOffset, viewport.VerticalOffset);
        var visibleInk = ReadInk(_editor, editorCoverage)
            // Whitespace/control GlyphRuns can have layout bounds but no drawable outline.
            // Their built geometry reports Rect.Empty and must be discarded before translation.
            .Where(item => !item.Bounds.IsEmpty)
            .Select(item => TranslateInk(item, documentOffset));
        // Build off to the side and swap the complete index in one assignment. Geometry-stable
        // edits keep using the prior complete index while this deferred operation is pending;
        // geometry-changing edits discard it in OnLayoutUpdated to prevent ghost positions.
        var inkRows = IndexInk(visibleInk);
        _inkRows = inkRows;
        InkCacheBuildCount++;
        _inkCoverage = viewport.Bounds;
        _inkCoverage.Offset(documentOffset.X, documentOffset.Y);
        _inkDirty = false;
        return true;
    }

    private static Ink TranslateInk(Ink ink, Vector offset)
    {
        var geometry = ink.Geometry.CloneCurrentValue();
        var transform = geometry.Transform?.Value ?? Matrix.Identity;
        transform.Translate(offset.X, offset.Y);
        geometry.Transform = new MatrixTransform(transform);
        if (geometry.CanFreeze)
            geometry.Freeze();
        var bounds = OffsetInkBounds(ink.Bounds, offset);
        return ink with { Geometry = geometry, Bounds = bounds };
    }

    internal static Rect OffsetInkBounds(Rect bounds, Vector offset)
    {
        // Rect.Empty.Offset throws InvalidOperationException. Keep this defensive even though
        // EnsureInkCache filters empty ink so future callers cannot reintroduce the startup crash.
        if (!bounds.IsEmpty)
            bounds.Offset(offset.X, offset.Y);
        return bounds;
    }

    private static bool Contains(Rect outer, Rect inner) =>
        !outer.IsEmpty &&
        outer.Left <= inner.Left && outer.Top <= inner.Top &&
        outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

    internal static Brush EnsureVisibleBrush(Brush brush, Brush? background)
    {
        if (brush is not SolidColorBrush solid || background is not SolidColorBrush surface)
            return brush;

        const double minimumContrast = 3;
        var color = Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B);
        var backgroundLuminance = Luminance(surface.Color);
        double Contrast(Color foreground)
        {
            var luminance = Luminance(foreground);
            return (Math.Max(luminance, backgroundLuminance) + 0.05) /
                (Math.Min(luminance, backgroundLuminance) + 0.05);
        }

        if (Contrast(color) >= minimumContrast && solid.Color.A == 255 && solid.Opacity == 1)
            return brush;

        // Keep the normal palette untouched. Repair unsafe custom/tinted colors toward the
        // higher-contrast endpoint, using opaque RGB so alpha and brush opacity cannot compound.
        var endpoint = Contrast(Colors.Black) >= Contrast(Colors.White) ? 0 : 255;
        for (var step = 1; Contrast(color) < minimumContrast && step <= 255; step++)
        {
            var amount = step / 255.0;
            color = Color.FromRgb(
                (byte)Math.Round(solid.Color.R + (endpoint - solid.Color.R) * amount),
                (byte)Math.Round(solid.Color.G + (endpoint - solid.Color.G) * amount),
                (byte)Math.Round(solid.Color.B + (endpoint - solid.Color.B) * amount));
        }
        var visible = new SolidColorBrush(color);
        visible.Freeze();
        return visible;
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private void ScheduleInkRefresh(bool deferForInput = false)
    {
        if (_disposed || _inkRefresh is not null)
            return;
        // Text input gets precedence over the expensive native-ink capture. If keys arrive faster
        // than a capture can complete, ApplicationIdle naturally collapses those edits into one
        // rebuild. Scroll and resize still use Background so newly revealed content catches up
        // promptly when the user is not editing.
        var priority = deferForInput
            ? DispatcherPriority.ApplicationIdle
            : DispatcherPriority.Background;
        _inkRefresh = Dispatcher.BeginInvoke(priority, () =>
        {
            _inkRefresh = null;
            if (_disposed)
                return;
            if (EnsureInkCache(GetViewportState()))
                InvalidateDrawing();
        });
    }

    private void InvalidateDrawing()
    {
        _renderGeneration++;
        InvalidateVisual();
    }

    private void MarkInkCacheDirty(bool clearGlyphRects)
    {
        _inkDirty = true;
        if (clearGlyphRects)
            foreach (var glyph in _glyphs)
                glyph.DocumentRect = Rect.Empty;
    }

    private void DiscardMovedGeometry(bool translateDownstream, double extentDelta)
    {
        _inkDirty = true;
        _inkRows = null;
        _inkCoverage = Rect.Empty;
        _retainedDrawing = null;
        if (!translateDownstream || !TryGetFirstDirtyParagraphIndex(out var firstDirtyIndex))
        {
            foreach (var glyph in _glyphs)
                glyph.DocumentRect = Rect.Empty;
        }
        else
        {
            var index = 0;
            foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>())
            {
                if (_paragraphs.TryGetValue(paragraph, out var cached))
                {
                    if (_geometryDirtyParagraphs.Contains(paragraph))
                    {
                        foreach (var glyph in cached.Glyphs)
                            glyph.DocumentRect = Rect.Empty;
                    }
                    else if (index > firstDirtyIndex)
                    {
                        foreach (var glyph in cached.Glyphs)
                            if (!glyph.DocumentRect.IsEmpty)
                                glyph.DocumentRect = OffsetRect(glyph.DocumentRect, 0, extentDelta);
                    }
                }
                index++;
            }
        }
        InvalidateDrawing();
    }

    private bool TryGetFirstDirtyParagraphIndex(out int firstDirtyIndex)
    {
        firstDirtyIndex = int.MaxValue;
        var index = 0;
        foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>())
        {
            if (_geometryDirtyParagraphs.Contains(paragraph))
                firstDirtyIndex = Math.Min(firstDirtyIndex, index);
            index++;
        }
        return firstDirtyIndex != int.MaxValue;
    }

    private static Rect OffsetRect(Rect rect, double x, double y)
    {
        if (!rect.IsEmpty)
            rect.Offset(x, y);
        return rect;
    }

    private Brush? FindOpaqueBackground()
    {
        for (DependencyObject? current = _editor; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var brush = current switch
            {
                Control control => control.Background,
                Border border => border.Background,
                Panel panel => panel.Background,
                _ => null,
            };
            if (brush is SolidColorBrush { Color.A: 255, Opacity: 1 })
                return brush;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refresh?.Abort();
        _refresh = null;
        _inkRefresh?.Abort();
        _inkRefresh = null;
        _editor.TextChanged -= OnTextChanged;
        _editor.SizeChanged -= OnSizeChanged;
        _editor.LayoutUpdated -= OnLayoutUpdated;
        _editor.SelectionChanged -= OnSelectionChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _editor.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        IsApplyingFormatting = true;
        try
        {
            using (_editor.DeclareChangeBlock())
                foreach (var run in _editor.Document.Blocks.OfType<Paragraph>()
                    .SelectMany(p => p.Inlines).OfType<Run>().ToArray())
                {
                    run.ClearValue(TextElement.FontFamilyProperty);
                    run.ClearValue(TextElement.FontSizeProperty);
                }
        }
        finally
        {
            IsApplyingFormatting = false;
        }
        _paragraphs.Clear();
        _glyphs.Clear();
        _embeddedVisuals.Clear();
        _geometryDirtyParagraphs.Clear();
        _inkRows = null;
        _inkCoverage = Rect.Empty;
        _retainedDrawing = null;
    }
}
