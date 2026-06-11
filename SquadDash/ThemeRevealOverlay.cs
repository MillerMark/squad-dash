using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>
/// Developer-only hover-inspect overlay. Activated via Developer > Theme Reveal.
/// Shows named theme color tokens (Background, Foreground, BorderBrush, Fill, Stroke)
/// on the hovered element, walking up the visual tree up to 3 ancestry levels.
/// Left-click or Escape dismisses.
/// </summary>
internal sealed class ThemeRevealOverlay
{
    private static readonly DependencyProperty[] _colorDps = BuildColorDps();

    private static DependencyProperty[] BuildColorDps()
    {
        var seen = new HashSet<DependencyProperty>(ReferenceEqualityComparer.Instance);
        var list = new List<DependencyProperty>();
        void Add(DependencyProperty dp) { if (seen.Add(dp)) list.Add(dp); }
        Add(TextBlock.ForegroundProperty);
        Add(Control.ForegroundProperty);
        Add(Control.BackgroundProperty);
        Add(Border.BackgroundProperty);
        Add(Panel.BackgroundProperty);
        Add(Border.BorderBrushProperty);
        Add(Control.BorderBrushProperty);
        Add(Shape.FillProperty);
        Add(Shape.StrokeProperty);
        return list.ToArray();
    }

    private static readonly Dictionary<DependencyProperty, string> _dpNames = BuildDpNames();

    private static Dictionary<DependencyProperty, string> BuildDpNames()
    {
        var d = new Dictionary<DependencyProperty, string>(ReferenceEqualityComparer.Instance);
        void Add(DependencyProperty dp, string name) => d.TryAdd(dp, name);
        Add(TextBlock.ForegroundProperty,  "Foreground");
        Add(Control.ForegroundProperty,    "Foreground");
        Add(Control.BackgroundProperty,    "Background");
        Add(Border.BackgroundProperty,     "Background");
        Add(Panel.BackgroundProperty,      "Background");
        Add(Border.BorderBrushProperty,    "BorderBrush");
        Add(Control.BorderBrushProperty,   "BorderBrush");
        Add(Shape.FillProperty,            "Fill");
        Add(Shape.StrokeProperty,          "Stroke");
        return d;
    }

    private Window?  _owner;
    private Cursor?  _savedCursor;
    private Popup?   _popup;
    private StackPanel? _rowsPanel;
    private FrameworkElement? _lastElement;

    // Last-computed token list so Ctrl+C can copy it.
    private List<(string Prop, string Key, Color Color, string SourceLabel)> _lastTokens = new();

    /// <summary>True while the overlay is active on a window.</summary>
    public bool IsActive => _owner is not null;

    public void Activate(Window owner)
    {
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            Deactivate();

        if (_owner is not null)
            return;

        _owner = owner;
        _savedCursor = owner.Cursor;
        owner.Cursor = AnnotationCursors.EyedropperTool;

        EnsurePopup();
        _lastElement = null;

        InputManager.Current.PostProcessInput += OnPostProcessInput;
    }

    public void Deactivate()
    {
        if (_owner is not null)
        {
            InputManager.Current.PostProcessInput -= OnPostProcessInput;

            try { _owner.Cursor = _savedCursor; } catch { }
            _owner       = null;
            _savedCursor = null;
        }

        if (_popup is not null)
            _popup.IsOpen = false;

        _lastElement = null;
    }

    internal void CopyToClipboard()
    {
        if (_lastTokens.Count == 0) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Theme Reveal]");
            foreach (var (prop, key, color, sourceLabel) in _lastTokens)
            {
                var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                var src = string.IsNullOrEmpty(sourceLabel) ? string.Empty : $"  ({sourceLabel})";
                sb.AppendLine($"{prop} → {key}  {hex}{src}");
            }
            System.Windows.Clipboard.SetText(sb.ToString().TrimEnd());
        }
        catch { }
    }

    private void OnPreProcessInputForKeys(object sender, PreProcessInputEventArgs e)
    {
        // Key handling is delegated to MainWindow.OnGlobalPreProcessInput which
        // fires first (registered at app startup). This method is kept as a stub.
    }

    // -------------------------------------------------------------------------
    // Input handling
    // -------------------------------------------------------------------------

    private void OnPostProcessInput(object sender, ProcessInputEventArgs e)
    {
        try
        {
            if (_owner is null) return;

            // Left-click anywhere → dismiss.
            if (e.StagingItem.Input is MouseButtonEventArgs
                { ChangedButton: MouseButton.Left, ButtonState: MouseButtonState.Pressed })
            {
                Deactivate();
                return;
            }

            // Only handle plain mouse-move events.
            if (e.StagingItem.Input is not MouseEventArgs mouse
                || e.StagingItem.Input is MouseButtonEventArgs
                || e.StagingItem.Input is MouseWheelEventArgs)
                return;

            var source = (mouse.OriginalSource ?? mouse.Source) as DependencyObject;
            FrameworkElement? element = null;
            var current = source;
            while (current is not null)
            {
                if (current is FrameworkElement fe) { element = fe; break; }
                current = VisualTreeHelper.GetParent(current);
            }

            if (element is null) return;
            if (ReferenceEquals(element, _lastElement)) return;

            _lastElement = element;
            UpdatePopupContent(element);
            PositionPopup(NativeMethods.GetCursorScreenPos());
            _popup!.IsOpen = true;
        }
        catch { /* never throw from overlay */ }
    }

    // -------------------------------------------------------------------------
    // Popup construction
    // -------------------------------------------------------------------------

    private void EnsurePopup()
    {
        if (_popup is not null) return;

        _rowsPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };

        var border = new Border
        {
            Child           = _rowsPanel,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
        };
        border.SetResourceReference(Border.BackgroundProperty,  "PopupSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "PopupBorder");

        _popup = new Popup
        {
            Child              = border,
            AllowsTransparency = true,
            StaysOpen          = true,
            Placement          = PlacementMode.AbsolutePoint,
            IsHitTestVisible   = false,
        };
    }

    private void PositionPopup(Point screenPixels)
    {
        if (_popup is null || _owner is null) return;
        var dpi = VisualTreeHelper.GetDpi(_owner);
        _popup.HorizontalOffset = screenPixels.X / dpi.DpiScaleX + 16;
        _popup.VerticalOffset   = screenPixels.Y / dpi.DpiScaleY + 16;
    }

    private void UpdatePopupContent(FrameworkElement element)
    {
        if (_rowsPanel is null) return;
        _rowsPanel.Children.Clear();

        var allTokens = CollectAllLevelTokens(element);
        _lastTokens = allTokens;

        if (allTokens.Count == 0)
        {
            var empty = new TextBlock
            {
                Text      = "(no theme tokens)",
                FontStyle = FontStyles.Italic,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            _rowsPanel.Children.Add(empty);
            return;
        }

        foreach (var (prop, key, color, sourceLabel) in allTokens)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

            // Color swatch
            var swatch = new Rectangle
            {
                Width  = 12,
                Height = 12,
                Fill   = new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(swatch);

            // "Background → RosterPanelSurface  (Border)"
            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

            label.Text = string.IsNullOrEmpty(sourceLabel)
                ? $"{prop} → {key}"
                : $"{prop} → {key}  ({sourceLabel})";

            row.Children.Add(label);
            _rowsPanel.Children.Add(row);
        }
    }

    // -------------------------------------------------------------------------
    // Token collection helpers
    // -------------------------------------------------------------------------

    private static List<(string Prop, string Key, Color Color, string SourceLabel)> CollectAllLevelTokens(
        FrameworkElement element)
    {
        var allTokens = new List<(string Prop, string Key, Color Color, string SourceLabel)>();
        var seenKeys  = new HashSet<string>();

        var current = (DependencyObject)element;
        int levels  = 0;

        while (current is not null && levels < 20)
        {
            if (current is FrameworkElement fe)
            {
                var tokens = CollectThemeTokens(fe);
                if (tokens.Count > 0)
                {
                    var sourceLabel = levels == 0 ? string.Empty : DescribeElement(fe);
                    foreach (var t in tokens)
                    {
                        // Deduplicate by property name only — the innermost (first-found)
                        // assignment wins; a parent's Background cannot override a child's.
                        if (seenKeys.Add(t.Prop))
                            allTokens.Add((t.Prop, t.Key, t.Color, sourceLabel));
                    }

                    levels++;
                    if (levels >= 3) break;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return allTokens;
    }

    private static List<(string Prop, string Key, Color Color)> CollectThemeTokens(FrameworkElement element)
    {
        var result  = new List<(string, string, Color)>();
        var seenDup = new HashSet<string>();

        foreach (var dp in _colorDps)
        {
            try
            {
                var value = element.ReadLocalValue(dp);
                if (value == DependencyProperty.UnsetValue) continue;
                if (!value.GetType().Name.Contains("ResourceReference", System.StringComparison.Ordinal)) continue;

                var keyProp = value.GetType().GetProperty("ResourceKey",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public   |
                    System.Reflection.BindingFlags.NonPublic);

                if (keyProp?.GetValue(value) is not string key || string.IsNullOrEmpty(key)) continue;

                var propName = _dpNames.TryGetValue(dp, out var n) ? n : dp.Name;
                var dedupKey = $"{propName}:{key}";
                if (!seenDup.Add(dedupKey)) continue;

                var color = ResolveColor(key);
                result.Add((propName, key, color));
            }
            catch { }
        }

        return result;
    }

    private static Color ResolveColor(string key)
    {
        try
        {
            if (Application.Current.TryFindResource(key) is SolidColorBrush brush)
                return brush.Color;
        }
        catch { }
        return Colors.Transparent;
    }

    private static string DescribeElement(FrameworkElement fe)
    {
        var typeName = fe.GetType().Name;
        return string.IsNullOrEmpty(fe.Name)
            ? typeName
            : $"{typeName} \"{fe.Name}\"";
    }
}
