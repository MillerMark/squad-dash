using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace SquadDash;

/// <summary>
/// A Track subclass that enforces a minimum thumb length.
/// WPF's built-in Track computes the thumb size purely proportionally and ignores
/// the Thumb's MinHeight/MinWidth during ArrangeOverride — this class fixes that.
/// </summary>
public class MinThumbTrack : Track
{
    public static readonly DependencyProperty MinThumbLengthProperty =
        DependencyProperty.Register(
            nameof(MinThumbLength),
            typeof(double),
            typeof(MinThumbTrack),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinThumbLength
    {
        get => (double)GetValue(MinThumbLengthProperty);
        set => SetValue(MinThumbLengthProperty, value);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        // Let WPF's Track do all the normal proportional layout + direction handling.
        Size result = base.ArrangeOverride(arrangeSize);

        if (Thumb == null || MinThumbLength <= 0)
            return result;

        bool isVertical = Orientation == System.Windows.Controls.Orientation.Vertical;
        double currentSize = isVertical ? Thumb.RenderSize.Height : Thumb.RenderSize.Width;
        double trackLength = isVertical ? arrangeSize.Height : arrangeSize.Width;
        double minLength = Math.Min(MinThumbLength, trackLength);

        if (currentSize >= minLength)
            return result;

        // Thumb is smaller than the minimum. Read its current visual position from
        // the layout pass that base just completed, then re-arrange with a larger size.
        Point thumbOrigin = Thumb.TranslatePoint(new Point(0, 0), this);
        double thumbStart = isVertical ? thumbOrigin.Y : thumbOrigin.X;

        // Keep the thumb within the track.
        double maxStart = Math.Max(0, trackLength - minLength);
        thumbStart = Math.Max(0, Math.Min(thumbStart, maxStart));
        double thumbEnd = thumbStart + minLength;

        if (isVertical)
        {
            DecreaseRepeatButton?.Arrange(new Rect(0, 0, arrangeSize.Width, thumbStart));
            Thumb.Arrange(new Rect(0, thumbStart, arrangeSize.Width, minLength));
            IncreaseRepeatButton?.Arrange(new Rect(0, thumbEnd, arrangeSize.Width,
                Math.Max(0, trackLength - thumbEnd)));
        }
        else
        {
            DecreaseRepeatButton?.Arrange(new Rect(0, 0, thumbStart, arrangeSize.Height));
            Thumb.Arrange(new Rect(thumbStart, 0, minLength, arrangeSize.Height));
            IncreaseRepeatButton?.Arrange(new Rect(thumbEnd, 0,
                Math.Max(0, trackLength - thumbEnd), arrangeSize.Height));
        }

        return result;
    }
}
