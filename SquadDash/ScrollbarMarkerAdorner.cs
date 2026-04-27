using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Draws proportional match-position tick marks on the vertical scroll bar track.
/// </summary>
internal sealed class ScrollbarMarkerAdorner : Adorner
{
    private IReadOnlyList<double> _positions = []; // 0.0 .. 1.0 fractions of total doc height

    public ScrollbarMarkerAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    /// <param name="positions">Each value is a fraction 0..1 of the total scrollable document height where a match lives.</param>
    public void SetPositions(IReadOnlyList<double> positions)
    {
        _positions = positions;
        InvalidateVisual();
    }

    public void Clear()
    {
        _positions = [];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_positions.Count == 0) return;

        var brush = GetBrush("SearchHighlightCurrent", Color.FromArgb(220, 255, 179, 0));
        var h = RenderSize.Height;

        foreach (var pos in _positions)
        {
            var y = pos * h;
            dc.DrawRectangle(brush, null, new Rect(0, Math.Clamp(y, 0, h - 2), RenderSize.Width, 2));
        }
    }

    private static Brush GetBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources[key] is Brush b) return b;
        return new SolidColorBrush(fallback);
    }
}
