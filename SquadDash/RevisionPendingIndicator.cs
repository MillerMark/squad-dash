using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// An animated adorner-based spinner positioned at the end of the user's selection
/// while a Revise-with-AI request is in flight.  Drawn entirely via OnRender —
/// does not touch the FlowDocument or the undo stack.
/// </summary>
internal sealed class RevisionPendingIndicator : Adorner
{
    private const double FallbackTimeoutSeconds = 130;
    private const double AnimationIntervalMs = 80;   // 8 frames × 80ms ≈ 640ms per revolution

    private static readonly double[] SpokeOpacities = { 1.0, 0.75, 0.55, 0.4, 0.3, 0.2, 0.15, 0.1 };
    private const int SpokeCount = 8;
    private const double InnerRadius = 2.5;
    private const double OuterRadius = 5.5;
    private const double StrokeWidth = 1.3;

    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _fallbackTimer;
    private readonly TextPointer _position;
    private int _animationPhase;
    private bool _removed;

    private RevisionPendingIndicator(RichTextBox rtb, TextPointer position) : base(rtb)
    {
        _position = position;
        IsHitTestVisible = false;
        ToolTip = "AI is revising this selection";

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationIntervalMs) };
        _animationTimer.Tick += (_, _) =>
        {
            _animationPhase = (_animationPhase + 1) % SpokeCount;
            InvalidateVisual();
        };
        _animationTimer.Start();

        _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FallbackTimeoutSeconds) };
        _fallbackTimer.Tick += (_, _) => Detach();
        _fallbackTimer.Start();
    }

    internal static RevisionPendingIndicator? Attach(RichTextBox rtb, TextPointer position)
    {
        try
        {
            var layer = AdornerLayer.GetAdornerLayer(rtb);
            if (layer is null) return null;

            var indicator = new RevisionPendingIndicator(rtb, position);
            layer.Add(indicator);
            return indicator;
        }
        catch { return null; }
    }

    internal void Detach()
    {
        if (_removed) return;
        _removed = true;

        _animationTimer.Stop();
        _fallbackTimer.Stop();

        try
        {
            AdornerLayer.GetAdornerLayer(AdornedElement as RichTextBox)?.Remove(this);
        }
        catch { }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_removed) return;

        try
        {
            var rect = _position.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) return;

            var cx = rect.Right + 10.0;
            var cy = rect.Top + rect.Height / 2.0;

            var baseColor = GetColor("ActionLinkText", Colors.SteelBlue);

            for (int i = 0; i < SpokeCount; i++)
            {
                // "behind" = how many steps this spoke trails the bright tip
                var behind = (SpokeCount + _animationPhase - i) % SpokeCount;
                var opacity = SpokeOpacities[behind];
                var angleDeg = i * (360.0 / SpokeCount);
                var rad = angleDeg * Math.PI / 180.0;

                var lineColor = Color.FromArgb((byte)(opacity * 230), baseColor.R, baseColor.G, baseColor.B);
                var pen = new Pen(new SolidColorBrush(lineColor), StrokeWidth);
                pen.Freeze();

                dc.DrawLine(pen,
                    new Point(cx + Math.Cos(rad) * InnerRadius, cy + Math.Sin(rad) * InnerRadius),
                    new Point(cx + Math.Cos(rad) * OuterRadius,  cy + Math.Sin(rad) * OuterRadius));
            }
        }
        catch { /* TextPointer becomes invalid if document is rebuilt — skip silently */ }
    }

    private static Color GetColor(string key, Color fallback)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush b) return b.Color;
        return fallback;
    }
}
