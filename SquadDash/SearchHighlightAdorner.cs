using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Adorner that renders search-match highlight rectangles directly over a
/// <see cref="RichTextBox"/> without touching its <see cref="FlowDocument"/>.
/// Handles multi-line matches and scroll-offset tracking automatically.
/// </summary>
internal sealed class SearchHighlightAdorner : Adorner
{
    private readonly RichTextBox _rtb;
    private List<(TextPointer Start, TextPointer End, string Text)> _matches = [];
    private int _currentIndex = -1;

    public SearchHighlightAdorner(RichTextBox richTextBox) : base(richTextBox)
    {
        _rtb = richTextBox;
        IsHitTestVisible = false;

        richTextBox.SizeChanged += (_, _) => InvalidateVisual();

        // Subscribe to the internal ScrollViewer so highlights reposition on scroll.
        richTextBox.Loaded += (_, _) => SubscribeToScrollViewer();
        if (richTextBox.IsLoaded)
            SubscribeToScrollViewer();
    }

    private void SubscribeToScrollViewer()
    {
        var sv = FindScrollViewer(_rtb);
        if (sv is not null)
            sv.ScrollChanged += (_, _) => InvalidateVisual();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;
            var found = FindScrollViewer(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>Sets all match TextPointer pairs and the index of the current (navigated) match.</summary>
    public void SetMatches(IReadOnlyList<(TextPointer Start, TextPointer End, string Text)> matches, int currentIndex)
    {
        _matches = [.. matches];
        _currentIndex = currentIndex;
        InvalidateVisual();
    }

    /// <summary>Updates only the current-match index without rebuilding the match list.</summary>
    public void UpdateCurrentIndex(int currentIndex)
    {
        _currentIndex = currentIndex;
        InvalidateVisual();
    }

    /// <summary>Clears all highlights.</summary>
    public void Clear()
    {
        _matches = [];
        _currentIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Triggers a re-render pass — call after new document content is appended.</summary>
    public void InvalidateHighlights() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        if (_matches.Count == 0)
            return;

        // Clip all drawing to the adorner's own bounds so nothing bleeds outside the RichTextBox.
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));

        var normalBrush      = GetBrush("SearchHighlight",            Color.FromArgb(180, 255, 213,  79));
        var currentBrush     = GetBrush("SearchHighlightCurrent",     Color.FromArgb(230, 255, 143,   0));
        var textBrush        = GetBrush("SearchHighlightText",        Color.FromRgb( 18,  13,  0));
        var textBrushCurrent = GetBrush("SearchHighlightTextCurrent", Color.FromRgb( 49,  34,  0));

        var renderBounds = new Rect(RenderSize);

        for (var i = 0; i < _matches.Count; i++)
        {
            var (start, end, matchText) = _matches[i];
            if (start is null || end is null) continue;

            var isCurrent = i == _currentIndex;
            var brush     = isCurrent ? currentBrush : normalBrush;
            var opacity   = 1.0; // always fully opaque — transparency causes original text to bleed through

            var usedTextBrush = isCurrent ? textBrushCurrent : textBrush;

            try
            {
                DrawMatch(dc, start, end, matchText, brush, opacity, renderBounds, usedTextBrush);
            }
            catch
            {
                // TextPointer becomes invalid when the document is rebuilt — skip silently.
            }
        }

        dc.Pop(); // pop clip
    }

    private void DrawMatch(
        DrawingContext dc,
        TextPointer start,
        TextPointer end,
        string matchText,
        Brush brush,
        double opacity,
        Rect renderBounds,
        Brush textBrush)
    {
        var startRect = start.GetCharacterRect(LogicalDirection.Forward);
        var endRect   = end.GetCharacterRect(LogicalDirection.Backward);

        if (startRect.IsEmpty || endRect.IsEmpty)
            return;

        if (Math.Abs(startRect.Top - endRect.Top) < 2.0)
        {
            // Single line: one rectangle spanning the match.
            var highlightRect = new Rect(startRect.Left, startRect.Top,
                Math.Max(2, endRect.Right - startRect.Left),
                Math.Max(2, startRect.Height));
            DrawHighlightRect(dc, brush, opacity, highlightRect, renderBounds);
            DrawMatchText(dc, matchText, highlightRect, textBrush);
        }
        else
        {
            // Multi-line: three zones — draw text only on the first line for simplicity.
            var firstLineRect = new Rect(startRect.Left, startRect.Top,
                Math.Max(2, RenderSize.Width - startRect.Left), startRect.Height);
            DrawHighlightRect(dc, brush, opacity, firstLineRect, renderBounds);
            DrawMatchText(dc, matchText, firstLineRect, textBrush);

            // Middle block between the first and last line.
            var midTop    = startRect.Bottom;
            var midBottom = endRect.Top;
            if (midBottom - midTop > 1.0)
                DrawHighlightRect(dc, brush, opacity,
                    new Rect(0, midTop, RenderSize.Width, midBottom - midTop),
                    renderBounds);

            // Last line: from left edge to the match end.
            DrawHighlightRect(dc, brush, opacity,
                new Rect(0, endRect.Top, Math.Max(2, endRect.Right), endRect.Height),
                renderBounds);
        }
    }

    private void DrawMatchText(
        DrawingContext dc,
        string matchText,
        Rect highlightRect,
        Brush textBrush)
    {
        if (highlightRect.IsEmpty || string.IsNullOrEmpty(matchText)) return;

        // Read font properties directly from the RichTextBox so the overlay text
        // matches the current zoom level and typeface (Ctrl+scroll changes FontSize).
        var fontSize   = _rtb.FontSize;
        var fontFamily = _rtb.FontFamily;
        var fontWeight = _rtb.FontWeight;
        var fontStyle  = _rtb.FontStyle;

        var typeface = new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
        var dpi      = VisualTreeHelper.GetDpi(_rtb).PixelsPerDip;

        var ft = new FormattedText(
            matchText,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textBrush,
            dpi);

        dc.DrawText(ft, new Point(highlightRect.Left, highlightRect.Top));
    }

    private static void DrawHighlightRect(
        DrawingContext dc, Brush brush, double opacity, Rect rect, Rect renderBounds)
    {
        if (!rect.IntersectsWith(renderBounds))
            return;
        dc.PushOpacity(opacity);
        dc.DrawRectangle(brush, null, rect);
        dc.Pop();
    }

    private static Brush GetBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources[key] is Brush b)
            return b;
        return new SolidColorBrush(fallback);
    }
}
