namespace SquadDash;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

/// <summary>
/// Shared helper for building priority indicator icons used across the Tasks panel,
/// Inbox panel, and task editor. All four priority levels share the same shapes and
/// theme resource keys everywhere in the app.
/// </summary>
internal static class PriorityIndicatorHelper
{
    /// <summary>
    /// Maps a priority emoji to the canonical priority string used by
    /// <see cref="BuildIndicator"/>.
    /// </summary>
    internal static string EmojiToPriority(string emoji) => emoji switch
    {
        "⚫" => "critical",
        "🔴" => "high",
        "🟡" => "mid",
        "🟢" => "low",
        "🔵" => "low",   // legacy blue alias for low
        _    => "mid"
    };

    /// <summary>
    /// Builds a themed priority indicator icon for the given <paramref name="priority"/>
    /// level. All indicators reserve the same layout footprint (FontSizeBody × FontSizeBody
    /// plus a 4 px right margin) so adjacent text starts at a consistent horizontal offset.
    /// <c>RenderTransform</c> (not <c>LayoutTransform</c>) is used so the visual scale does
    /// not collapse the layout footprint.
    /// </summary>
    /// <param name="priority">One of "low", "mid", "high", "critical" (case-insensitive).</param>
    /// <param name="opacity">Visual opacity; use 0.4 for already-read items, 1.0 otherwise.</param>
    internal static UIElement BuildIndicator(string priority, double opacity = 1.0)
    {
        switch ((priority ?? "mid").ToLowerInvariant())
        {
            case "low":
            {
                // Chevron-down shape
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M18.3333333333333,17.6666666666667C30.6666666666666,5.66666666666666,30.6666666666666,5.66666666666666,30.6666666666666,5.66666666666666C32.6666666666666,3.66666666666666,31.3333333333333,0.333333333333329,28.6666666666666,0.333333333333329C3.66666666666663,0.333333333333329,3.66666666666663,0.333333333333329,3.66666666666663,0.333333333333329C0.666666666666629,0.333333333333329,-0.666666666666686,3.66666666666666,1.33333333333331,5.66666666666666C14,17.6666666666667,14,17.6666666666667,14,17.6666666666667C15,19,17,19,18.3333333333333,17.6666666666667z"),
                    StrokeThickness = 1.333,
                };
                path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "PriorityLow");
                var canvas = new Canvas { Width = 32, Height = 19 };
                canvas.Children.Add(path);
                var viewbox = new Viewbox
                {
                    Stretch               = Stretch.Uniform,
                    VerticalAlignment     = VerticalAlignment.Center,
                    HorizontalAlignment   = HorizontalAlignment.Center,
                    Margin                = new Thickness(0, 0, 4, 0),
                    Opacity               = opacity,
                    Child                 = canvas,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new ScaleTransform(0.8, 0.8),
                };
                viewbox.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                viewbox.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return viewbox;
            }

            case "high":
            {
                // Triangle / warning shape
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M13.6666666666666,1.66666666666666C0,25,0,25,0,25C-1.66666666666669,27.6666666666667,0.666666666666629,31.3333333333333,4,31.3333333333333C30.6666666666666,31.3333333333333,30.6666666666666,31.3333333333333,30.6666666666666,31.3333333333333C34,31.3333333333333,36,27.6666666666667,34.3333333333333,25C21,1.66666666666666,21,1.66666666666666,21,1.66666666666666C19.3333333333333,-1,15.3333333333333,-1,13.6666666666666,1.66666666666666z"),
                };
                path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "PriorityHigh");
                var canvas = new Canvas { Width = 35, Height = 31 };
                canvas.Children.Add(path);
                var viewbox = new Viewbox
                {
                    Stretch               = Stretch.Uniform,
                    VerticalAlignment     = VerticalAlignment.Center,
                    HorizontalAlignment   = HorizontalAlignment.Center,
                    Margin                = new Thickness(0, 0, 4, 0),
                    Opacity               = opacity,
                    Child                 = canvas,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new ScaleTransform(0.95, 0.95),
                };
                viewbox.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                viewbox.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return viewbox;
            }

            case "critical":
            {
                // Diamond shape
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 17.5,0 L 35,17.5 L 17.5,35 L 0,17.5 Z"),
                };
                path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "PriorityCritical");
                var canvas = new Canvas { Width = 35, Height = 35 };
                canvas.Children.Add(path);
                var viewbox = new Viewbox
                {
                    Stretch               = Stretch.Uniform,
                    VerticalAlignment     = VerticalAlignment.Center,
                    HorizontalAlignment   = HorizontalAlignment.Center,
                    Margin                = new Thickness(0, 0, 4, 0),
                    Opacity               = opacity,
                    Child                 = canvas,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new ScaleTransform(1.035, 1.035),
                };
                viewbox.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                viewbox.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return viewbox;
            }

            default: // "mid" and anything unrecognised — circle/dot
            {
                var dot = new Ellipse
                {
                    VerticalAlignment     = VerticalAlignment.Center,
                    HorizontalAlignment   = HorizontalAlignment.Center,
                    Margin                = new Thickness(0, 0, 4, 0),
                    Opacity               = opacity,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new ScaleTransform(0.72, 0.72),
                };
                dot.SetResourceReference(Ellipse.FillProperty,             "PriorityMid");
                dot.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                dot.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return dot;
            }
        }
    }
}
