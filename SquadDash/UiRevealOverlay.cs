using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SquadDash.Screenshots;

namespace SquadDash;

/// <summary>
/// Developer-only hover-inspect overlay. Activated via Developer > UI Reveal.
/// Shows element name, type, DynamicResource keys, style key, and DataContext hints.
/// Click anywhere to deactivate.
/// </summary>
internal sealed class UiRevealOverlay
{
    private static readonly DependencyProperty[] _dpsToCheck = new[]
    {
        TextBlock.ForegroundProperty,
        Control.ForegroundProperty,
        Control.BackgroundProperty,
        Border.BackgroundProperty,
        Border.BorderBrushProperty,
        Control.BorderBrushProperty,
        Panel.BackgroundProperty,
        Shape.FillProperty,
        Shape.StrokeProperty,
        TextBlock.FontSizeProperty,
        Control.FontSizeProperty,
        FrameworkElement.StyleProperty,
    };

    private Window? _owner;
    private Popup? _popup;
    private TextBlock? _line1;
    private TextBlock? _line2;
    private TextBlock? _line3;
    private TextBlock? _line4;
    private FrameworkElement? _lastElement;
    private HighlightAdorner? _activeAdorner;
    private UIElement? _activeAdornerTarget;

    /// <summary>True while the overlay is active on a window.</summary>
    public bool IsActive => _owner is not null;

    public void Activate(Window owner)
    {
        // If already active on a different window, detach first.
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            Deactivate();

        if (_owner is not null)
            return; // already active on this window

        _owner = owner;
        EnsurePopup();
        _lastElement = null;

        owner.PreviewMouseMove  += OnPreviewMouseMove;
        owner.PreviewMouseDown  += OnPreviewMouseDown;
    }

    internal void Deactivate()
    {
        if (_owner is not null)
        {
            _owner.PreviewMouseMove -= OnPreviewMouseMove;
            _owner.PreviewMouseDown -= OnPreviewMouseDown;
            _owner = null;
        }

        if (_popup is not null)
            _popup.IsOpen = false;

        RemoveHighlight();
        _lastElement = null;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (_owner is null) return;
            var pos = e.GetPosition(_owner);
            var hit = VisualTreeHelper.HitTest(_owner, pos)?.VisualHit;

            FrameworkElement? element = null;
            var current = hit;
            while (current is not null)
            {
                if (current is FrameworkElement fe)
                {
                    element = fe;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            if (element is null || ReferenceEquals(element, _lastElement))
                return;

            _lastElement = element;
            UpdatePopupContent(element);

            // Convert window-relative position to screen coordinates for AbsolutePoint popup
            var windowPos  = e.GetPosition(_owner);
            var screenPos  = _owner.PointToScreen(windowPos);
            PositionPopup(screenPos);
            _popup!.IsOpen = true;

            ApplyHighlight(element);
        }
        catch { /* never throw from overlay */ }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            e.Handled = true;
            Deactivate();
        }
        catch { }
    }

    // -------------------------------------------------------------------------
    // Popup construction
    // -------------------------------------------------------------------------

    private void EnsurePopup()
    {
        if (_popup is not null) return;

        _line1 = new TextBlock { FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        _line2 = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 340, FontStyle = FontStyles.Normal };
        _line3 = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 340, FontStyle = FontStyles.Normal };
        _line4 = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 340, FontStyle = FontStyles.Italic };

        _line1.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        _line2.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _line3.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _line4.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var stack = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
        stack.Children.Add(_line1);
        stack.Children.Add(_line2);
        stack.Children.Add(_line3);
        stack.Children.Add(_line4);

        var border = new Border
        {
            Child          = stack,
            BorderThickness = new Thickness(1),
            CornerRadius   = new CornerRadius(4),
        };
        border.SetResourceReference(Border.BackgroundProperty,   "PopupSurface");
        border.SetResourceReference(Border.BorderBrushProperty,  "PopupBorder");

        _popup = new Popup
        {
            Child             = border,
            AllowsTransparency = true,
            StaysOpen          = true,
            Placement          = PlacementMode.AbsolutePoint,
            IsHitTestVisible   = false,
        };
    }

    private void PositionPopup(Point screenPos)
    {
        if (_popup is null) return;
        _popup.HorizontalOffset = screenPos.X + 16;
        _popup.VerticalOffset   = screenPos.Y + 16;
    }

    private void ApplyHighlight(FrameworkElement element)
    {
        try
        {
            RemoveHighlight();
            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(element);
            if (layer is null) return;
            var adorner = new HighlightAdorner(element);
            layer.Add(adorner);
            _activeAdorner       = adorner;
            _activeAdornerTarget = element;
        }
        catch { }
    }

    private void RemoveHighlight()
    {
        try
        {
            if (_activeAdorner is not null && _activeAdornerTarget is not null)
            {
                var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(_activeAdornerTarget);
                layer?.Remove(_activeAdorner);
            }
        }
        catch { }
        finally
        {
            _activeAdorner       = null;
            _activeAdornerTarget = null;
        }
    }

    // -------------------------------------------------------------------------
    // Content building
    // -------------------------------------------------------------------------

    private void UpdatePopupContent(FrameworkElement element)
    {
        if (_line1 is null) return;

        try
        {
            // Line 1: name + type
            var name     = string.IsNullOrEmpty(element.Name) ? "(unnamed)" : element.Name;
            var typeName = element.GetType().Name;
            _line1.Text  = $"{name} ({typeName})";

            // Lines 2–4
            if (_line2 is not null)
            {
                _line2.Text       = BuildResourceKeysLine(element);
                _line2.Visibility = string.IsNullOrEmpty(_line2.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (_line3 is not null)
            {
                _line3.Text       = BuildStyleKeyLine(element);
                _line3.Visibility = string.IsNullOrEmpty(_line3.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (_line4 is not null)
            {
                _line4.Text       = BuildDataContextLine(element);
                _line4.Visibility = string.IsNullOrEmpty(_line4.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        catch { _line1.Text = "(error reading element info)"; }
    }

    private static string BuildResourceKeysLine(FrameworkElement element)
    {
        try
        {
            var keys = new List<string>();
            CollectResourceKeys(element, keys, prefix: null);

            // Walk up to 3 ancestors
            var ancestor = VisualTreeHelper.GetParent(element) as DependencyObject;
            for (int depth = 0; depth < 3 && ancestor is not null; depth++)
            {
                if (ancestor is FrameworkElement fe)
                    CollectResourceKeys(fe, keys, prefix: "↑");
                ancestor = VisualTreeHelper.GetParent(ancestor);
            }

            var deduped = keys.Distinct().ToList();
            return deduped.Count == 0 ? string.Empty : "◈ " + string.Join(", ", deduped);
        }
        catch { return string.Empty; }
    }

    private static void CollectResourceKeys(FrameworkElement element, List<string> keys, string? prefix)
    {
        foreach (var dp in _dpsToCheck)
        {
            try
            {
                if (dp == FrameworkElement.StyleProperty) continue; // handled separately

                var value = element.ReadLocalValue(dp);
                if (value == DependencyProperty.UnsetValue) continue;

                var typeName = value.GetType().Name;
                if (!typeName.Contains("ResourceReference", StringComparison.Ordinal)) continue;

                var keyProp = value.GetType().GetProperty("ResourceKey",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public   |
                    System.Reflection.BindingFlags.NonPublic);

                if (keyProp?.GetValue(value) is string key && !string.IsNullOrEmpty(key))
                    keys.Add(prefix is null ? key : $"{prefix} {key}");
            }
            catch { }
        }
    }

    private static string BuildStyleKeyLine(FrameworkElement element)
    {
        try
        {
            var value = element.ReadLocalValue(FrameworkElement.StyleProperty);
            if (value == DependencyProperty.UnsetValue) return string.Empty;
            if (value is not Style style) return string.Empty;

            // Style.ResourceKey is not a public property; try reflection then TargetType name fallback.
            var keyProp = style.GetType().GetProperty("ResourceKey",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public   |
                System.Reflection.BindingFlags.NonPublic);
            if (keyProp?.GetValue(style) is string key && !string.IsNullOrEmpty(key))
                return $"◈ style: {key}";
        }
        catch { }
        return string.Empty;
    }

    private static string BuildDataContextLine(FrameworkElement element)
    {
        try
        {
            var dc = element.DataContext;
            if (dc is null) return string.Empty;

            var dcType = dc.GetType();
            var ifaces = new List<string>();
            if (dc is ILiveElementLocator)  ifaces.Add(nameof(ILiveElementLocator));
            if (dc is IFixtureLoader)        ifaces.Add(nameof(IFixtureLoader));
            if (dc is IReplayableUiAction)   ifaces.Add(nameof(IReplayableUiAction));

            if (ifaces.Count == 0) return string.Empty;
            return $"◈ dc: {dcType.Name} ({string.Join(", ", ifaces)})";
        }
        catch { return string.Empty; }
    }

    // -------------------------------------------------------------------------
    // Highlight adorner — 1px black outer + 1px white inner, both 50% opaque
    // -------------------------------------------------------------------------

    private sealed class HighlightAdorner : System.Windows.Documents.Adorner
    {
        private static readonly Pen _outerPen = MakePen(0, 0, 0);
        private static readonly Pen _innerPen = MakePen(255, 255, 255);

        private static Pen MakePen(byte r, byte g, byte b)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(128, r, g, b)), 1.0);
            pen.Freeze();
            return pen;
        }

        public HighlightAdorner(UIElement element) : base(element)
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            // outer black rect (0.5px inset so the stroke falls on a pixel boundary)
            dc.DrawRectangle(null, _outerPen,
                new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)));
            // inner white rect (1.5px inset)
            dc.DrawRectangle(null, _innerPen,
                new Rect(1.5, 1.5, Math.Max(0, w - 3), Math.Max(0, h - 3)));
        }
    }
}
