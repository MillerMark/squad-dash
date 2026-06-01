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
using System.Windows.Threading;
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
        TextBlock.ForegroundProperty,   // == Control.ForegroundProperty (same DP via AddOwner)
        Control.BackgroundProperty,
        Border.BackgroundProperty,
        Border.BorderBrushProperty,
        Control.BorderBrushProperty,
        Panel.BackgroundProperty,
        Shape.FillProperty,
        Shape.StrokeProperty,
        TextBlock.FontSizeProperty,     // == Control.FontSizeProperty (same DP via AddOwner)
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
    private DispatcherTimer? _copyFeedbackTimer;
    private string? _line1SavedText;

    // Diagnostic log file — cleared on each Activate(), appended during the session.
    private static readonly string _diagLogPath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SquadDash_UiReveal_diag.txt");

    /// <summary>True while the overlay is active on a window.</summary>
    public bool IsActive => _owner is not null;

    public void Activate(Window owner)
    {
        // If already active on a different window, detach first.
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            Deactivate();

        if (_owner is not null)
            return; // already active on this window

        // Start fresh diagnostic log each time the overlay is activated.
        try { System.IO.File.WriteAllText(_diagLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] Activate() called — owner={owner.GetType().Name}\r\n"); } catch { }

        _owner = owner;
        EnsurePopup();
        _lastElement = null;

        // PostProcessInput fires AFTER the input event has been fully dispatched,
        // so OriginalSource is set correctly for both main-window elements and
        // elements inside floating ToolTip / Popup HWNDs.
        InputManager.Current.PostProcessInput += OnPostProcessInput;

        // PreProcessInput is used for key detection (same pattern as the global F12 handler)
        // because PostProcessInput may see the event after a focused control has already
        // handled it, and the RoutedEvent on the args may differ between the two stages.
        InputManager.Current.PreProcessInput += OnPreProcessInputForKeys;

        DiagLog("Activate() complete — PreProcessInput and PostProcessInput subscribed");
    }

    internal void Deactivate()
    {
        if (_owner is not null)
        {
            InputManager.Current.PostProcessInput -= OnPostProcessInput;
            InputManager.Current.PreProcessInput -= OnPreProcessInputForKeys;
            _owner = null;
        }

        if (_popup is not null)
            _popup.IsOpen = false;

        RemoveHighlight();
        _lastElement = null;
    }

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

            // Ctrl+C or Ctrl+Insert → handled via OnPreProcessInputForKeys (PreProcessInput).

            // Only handle plain mouse-move events (not button, not wheel).
            if (e.StagingItem.Input is not MouseEventArgs mouse
                || e.StagingItem.Input is MouseButtonEventArgs
                || e.StagingItem.Input is MouseWheelEventArgs)
                return;

            // At PostProcessInput, OriginalSource is the actual hit-tested element.
            var source = (mouse.OriginalSource ?? mouse.Source) as DependencyObject;
            FrameworkElement? element = null;
            var current = source;
            while (current is not null)
            {
                if (current is FrameworkElement fe) { element = fe; break; }
                current = VisualTreeHelper.GetParent(current);
            }

            if (element is null) return;

            // WPF ToolTip popups are IsHitTestVisible=false, so mouse events always
            // hit the trigger element beneath the tooltip, not the tooltip contents.
            // Check if the hovered element (or an ancestor) has an open ToolTip and,
            // if so, inspect the tooltip's visual tree instead.
            var openTip = FindOpenToolTip(element);
            if (openTip is not null)
            {
                if (ReferenceEquals(openTip, _lastElement)) return;
                _lastElement = openTip;
                UpdatePopupContentForTooltip(openTip);
                RemoveHighlight(); // no AdornerLayer inside tooltip HWND
            }
            else
            {
                if (ReferenceEquals(element, _lastElement)) return;
                _lastElement = element;
                UpdatePopupContent(element);
                ApplyHighlight(element);
            }

            // Position popup near the cursor — but when a tooltip is being inspected,
            // offset outside the tooltip so the reveal info isn't hidden behind it.
            if (openTip is not null)
            {
                PositionPopupBesideTooltip(openTip);
            }
            else
            {
                // Use Win32 GetCursorPos — reliable regardless of which HWND has the mouse.
                PositionPopup(NativeMethods.GetCursorScreenPos());
            }

            _popup!.IsOpen = true;
        }
        catch { /* never throw from overlay */ }
    }

    private static ToolTip? FindOpenToolTip(FrameworkElement element)
    {
        var current = (DependencyObject)element;
        for (int depth = 0; depth < 10 && current is not null; depth++)
        {
            if (current is FrameworkElement fe && fe.ToolTip is ToolTip { IsOpen: true } tip)
                return tip;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// PreProcessInput handler — fires before the event is dispatched to the element tree,
    /// so key intercepts work even when a focused RichTextBox would normally consume Ctrl+C.
    /// </summary>
    private void OnPreProcessInputForKeys(object sender, PreProcessInputEventArgs e)
    {
        try
        {
            if (e.StagingItem.Input is not KeyEventArgs keyArgs) return;

            // Only log/act on PreviewKeyDown — ignore KeyUp, KeyDown (bubbling pass), etc.
            if (keyArgs.RoutedEvent != Keyboard.PreviewKeyDownEvent) return;

            var mods = Keyboard.Modifiers;

            // Log every key seen while the overlay is active so we know the handler fires.
            if (_owner is not null)
            {
                var diagMsg = $"PreviewKeyDown: key={keyArgs.Key} mods={mods} handled={keyArgs.Handled} " +
                              $"_lastElement={(_lastElement is null ? "null" : _lastElement.GetType().Name)}";
                DiagLog(diagMsg);
                ShowDiagInOverlay($"{keyArgs.Key} mods:{mods}");
            }

            if (_owner is null || _lastElement is null)
            {
                if (_owner is not null)
                    DiagLog("  → skipped: _lastElement is null (no hover yet)");
                return;
            }

            bool isCopy =
                (keyArgs.Key == Key.C
                    && mods.HasFlag(ModifierKeys.Control)
                    && !mods.HasFlag(ModifierKeys.Shift)
                    && !mods.HasFlag(ModifierKeys.Alt))
                || (keyArgs.Key == Key.Insert
                    && mods.HasFlag(ModifierKeys.Control)
                    && !mods.HasFlag(ModifierKeys.Shift)
                    && !mods.HasFlag(ModifierKeys.Alt));

            if (!isCopy) return;

            DiagLog("  → COPY TRIGGERED — building clipboard text");
            var text = BuildClipboardText(_lastElement);
            DiagLog($"  → BuildClipboardText: {(string.IsNullOrEmpty(text) ? "EMPTY" : $"{text.Length} chars")}");

            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    Clipboard.SetText(text);
                    DiagLog("  → Clipboard.SetText: OK");
                }
                catch (Exception clipEx)
                {
                    DiagLog($"  → Clipboard.SetText THREW: {clipEx.Message}");
                }
                ShowCopyFeedback();
            }
            keyArgs.Handled = true;
        }
        catch (Exception ex)
        {
            DiagLog($"OnPreProcessInputForKeys THREW: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void DiagLog(string message)
    {
        // Route through the app's trace system so output appears in the live Trace panel
        // under the "UI" category checkbox, as well as in the trace.log file.
        SquadDashTrace.Write("UI", $"[UiReveal] {message}");
        // Keep the separate temp file as a backup (useful if app hasn't set workspace yet).
        try
        {
            System.IO.File.AppendAllText(_diagLogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }

    /// <summary>Shows a brief debug string in line2 of the overlay without disrupting normal display.</summary>
    private void ShowDiagInOverlay(string text)
    {
        if (_line4 is null) return;
        _line4.Text = $"[diag] {text}";
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

    private void PositionPopup(Point screenPixels)
    {
        // AbsolutePoint popup offsets are in WPF logical (96-dpi) units.
        if (_popup is null || _owner is null) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_owner);
        _popup.HorizontalOffset = screenPixels.X / dpi.DpiScaleX + 16;
        _popup.VerticalOffset   = screenPixels.Y / dpi.DpiScaleY + 16;
    }

    /// <summary>
    /// When inspecting an open ToolTip, position the reveal popup just outside the
    /// tooltip rect so neither overlaps the other.  Uses the tooltip HWND rect via
    /// Win32 GetWindowRect for reliability; falls back to Win32 cursor position.
    /// </summary>
    private void PositionPopupBesideTooltip(ToolTip tip)
    {
        if (_popup is null || _owner is null) return;
        try
        {
            // Use the HWND rect — reliable even when PointToScreen would throw.
            var tipRect = NativeMethods.TryGetVisualHwndScreenRect(tip);

            // Also account for DPI: GetWindowRect returns physical pixels; WPF
            // AbsolutePoint popup offsets are in logical (96-dpi) units.
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_owner);
            double dpiX = dpi.DpiScaleX;
            double dpiY = dpi.DpiScaleY;

            if (tipRect is null || tipRect.Value.Width == 0)
            {
                // Fallback: use Win32 cursor pos (works even when mouse is in a foreign HWND).
                var cur = NativeMethods.GetCursorScreenPos();
                _popup.HorizontalOffset = cur.X / dpiX + 16;
                _popup.VerticalOffset   = cur.Y / dpiY + 16;
                return;
            }

            var r = tipRect.Value;
            // Convert from physical pixels to WPF logical units.
            double tipLeft   = r.Left   / dpiX;
            double tipTop    = r.Top    / dpiY;
            double tipRight  = r.Right  / dpiX;
            double tipBottom = r.Bottom / dpiY;

            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;

            const double revealW = 390;
            const double revealH = 130;
            const double gap     = 8;

            double x, y;

            if (tipRight + gap + revealW <= screenW)
            {
                x = tipRight + gap;
                y = tipTop;
            }
            else if (tipLeft - gap - revealW >= 0)
            {
                x = tipLeft - gap - revealW;
                y = tipTop;
            }
            else
            {
                x = tipLeft;
                y = tipBottom + gap;
            }

            if (y + revealH > screenH) y = Math.Max(0, screenH - revealH);

            _popup.HorizontalOffset = x;
            _popup.VerticalOffset   = y;
        }
        catch
        {
            try
            {
                var cur = NativeMethods.GetCursorScreenPos();
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_owner);
                _popup.HorizontalOffset = cur.X / dpi.DpiScaleX + 16;
                _popup.VerticalOffset   = cur.Y / dpi.DpiScaleY + 16;
            }
            catch { }
        }
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

    /// <summary>
    /// Shows a summary of all DynamicResource keys used anywhere in the tooltip's
    /// visual tree — this is what the user cares about when inspecting themed tooltips.
    /// </summary>
    private void UpdatePopupContentForTooltip(ToolTip tip)
    {
        if (_line1 is null) return;
        try
        {
            var contentType = tip.Content?.GetType().Name ?? "ToolTip";
            _line1.Text = $"ToolTip → {contentType}";

            // Collect every DynamicResource key from every element in the tooltip tree.
            var keys = new List<string>();
            CollectAllResourceKeysInTree(tip, keys);
            var distinct = keys.Distinct().ToList();

            if (_line2 is not null)
            {
                _line2.Text = distinct.Count > 0 ? "◈ " + string.Join(", ", distinct) : string.Empty;
                _line2.Visibility = string.IsNullOrEmpty(_line2.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (_line3 is not null) { _line3.Text = string.Empty; _line3.Visibility = Visibility.Collapsed; }
            if (_line4 is not null) { _line4.Text = string.Empty; _line4.Visibility = Visibility.Collapsed; }
        }
        catch { if (_line1 is not null) _line1.Text = "(error reading tooltip)"; }
    }

    private static void CollectAllResourceKeysInTree(DependencyObject node, List<string> keys)
    {
        if (node is FrameworkElement fe)
            CollectResourceKeys(fe, keys, prefix: null);

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            CollectAllResourceKeysInTree(VisualTreeHelper.GetChild(node, i), keys);
    }

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
    // Clipboard copy
    // -------------------------------------------------------------------------

    private void ShowCopyFeedback()
    {
        if (_line1 is null) return;
        _line1SavedText ??= _line1.Text;
        _line1SavedText = _line1.Text; // capture current before overwriting
        _line1.Text = "✓ Copied to clipboard";

        if (_copyFeedbackTimer is null)
        {
            _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _copyFeedbackTimer.Tick += (_, _) =>
            {
                _copyFeedbackTimer.Stop();
                if (_line1 is not null && _line1SavedText is not null)
                    _line1.Text = _line1SavedText;
                _line1SavedText = null;
            };
        }

        _copyFeedbackTimer.Stop();
        _copyFeedbackTimer.Start();
    }

    private static readonly Dictionary<DependencyProperty, string> _dpDisplayNames = new()
    {
        { TextBlock.ForegroundProperty,    "Foreground"   },  // Control.ForegroundProperty is the same DP
        { Control.BackgroundProperty,      "Background"   },
        { Border.BackgroundProperty,       "Background"   },
        { Border.BorderBrushProperty,      "BorderBrush"  },
        { Control.BorderBrushProperty,     "BorderBrush"  },
        { Panel.BackgroundProperty,        "Background"   },
        { Shape.FillProperty,              "Fill"         },
        { Shape.StrokeProperty,            "Stroke"       },
        { TextBlock.FontSizeProperty,      "FontSize"     },  // Control.FontSizeProperty is the same DP
        { FrameworkElement.StyleProperty,  "Style"        },
    };

    private static string BuildClipboardText(FrameworkElement element)
    {
        // sb is outside the try so partial output is preserved even if an exception fires.
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine("[SquadDash UI Reveal]");

            if (element is ToolTip tip)
            {
                sb.AppendLine($"Type: ToolTip");
                var contentType = tip.Content?.GetType().Name ?? "(none)";
                sb.AppendLine($"ContentType: {contentType}");

                var keys = new List<string>();
                CollectAllResourceKeysInTree(tip, keys);
                var distinct = keys.Distinct().ToList();
                if (distinct.Count > 0)
                    sb.AppendLine($"ResourceKeys: {string.Join(", ", distinct)}");
            }
            else
            {
                // Build the ancestor chain from Window → ... → element.
                var chain = BuildAncestorChain(element);

                sb.AppendLine();
                sb.AppendLine("--- Ancestor Chain (Window → Element) ---");
                for (int i = 0; i < chain.Count; i++)
                {
                    var node = chain[i];
                    var indent = new string(' ', i * 2);
                    var marker = i == chain.Count - 1 ? "▶ " : "  ";
                    sb.AppendLine($"{indent}{marker}{node}");
                }

                // Detail block for the hovered element
                sb.AppendLine();
                sb.AppendLine("--- Hovered Element ---");
                sb.AppendLine($"Type: {element.GetType().Name}");
                sb.AppendLine($"Name: {GetElementLabel(element)}");

                var ownEntries = CollectResourceKeyEntries(element);
                if (ownEntries.Count > 0)
                {
                    sb.AppendLine("Properties:");
                    foreach (var (prop, key) in ownEntries)
                        sb.AppendLine($"  {prop}: {key}");
                }

                var styleKey = GetStyleKey(element);
                if (styleKey is not null)
                    sb.AppendLine($"Style: {styleKey}");

                var dcLine = BuildDataContextLine(element);
                if (!string.IsNullOrEmpty(dcLine))
                    sb.AppendLine($"DataContext: {dcLine.Replace("◈ dc: ", "")}");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("UI", $"[UiReveal] BuildClipboardText THREW {ex.GetType().Name}: {ex.Message}");
            var partial = sb.ToString().TrimEnd();
            return partial.Length > 0 ? partial + "\n[partial — " + ex.GetType().Name + "]" : string.Empty;
        }
    }

    /// <summary>
    /// Walks the visual tree from <paramref name="element"/> up to the root Window,
    /// then reverses so the list runs Window → ... → element.
    /// Each entry is a short label: "TypeName \"x:Name\"" or just "TypeName".
    /// </summary>
    private static List<string> BuildAncestorChain(FrameworkElement element)
    {
        var chain = new List<string>();
        DependencyObject? current = element;
        while (current is not null)
        {
            chain.Add(DescribeNode(current));
            if (current is Window) break;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { break; }
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Returns a short descriptive label for a visual tree node, including:
    /// - The type name
    /// - The x:Name (if any, via FrameworkElement.Name or FrameworkContentElement.Name)
    /// - Whether it implements ILiveElementLocator / IFixtureLoader / IReplayableUiAction
    ///   (proxy for the "IHaveDisplayName" concept — any named interface on the DataContext)
    /// </summary>
    private static string DescribeNode(DependencyObject node)
    {
        var typeName = node.GetType().Name;

        // Try to get the x:Name
        string? name = null;
        if (node is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            name = fe.Name;
        else if (node is System.Windows.FrameworkContentElement fce && !string.IsNullOrEmpty(fce.Name))
            name = fce.Name;

        var label = name is not null ? $"{typeName} \"{name}\"" : typeName;

        // Annotate DataContext interfaces if interesting
        if (node is FrameworkElement fe2 && fe2.DataContext is not null)
        {
            var dc = fe2.DataContext;
            var ifaces = new List<string>();
            if (dc is ILiveElementLocator)  ifaces.Add(nameof(ILiveElementLocator));
            if (dc is IFixtureLoader)        ifaces.Add(nameof(IFixtureLoader));
            if (dc is IReplayableUiAction)   ifaces.Add(nameof(IReplayableUiAction));
            if (ifaces.Count > 0)
                label += $" [dc:{dc.GetType().Name}({string.Join(",", ifaces)})]";
        }

        return label;
    }

    /// <summary>Returns the display label for a FrameworkElement (name or "(unnamed)").</summary>
    private static string GetElementLabel(FrameworkElement element)
        => string.IsNullOrEmpty(element.Name) ? "(unnamed)" : element.Name;

    private static List<(string Prop, string Key)> CollectResourceKeyEntries(FrameworkElement element)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>();
        foreach (var dp in _dpsToCheck)
        {
            if (dp == FrameworkElement.StyleProperty) continue;
            try
            {
                var value = element.ReadLocalValue(dp);
                if (value == DependencyProperty.UnsetValue) continue;
                if (!value.GetType().Name.Contains("ResourceReference", StringComparison.Ordinal)) continue;

                var keyProp = value.GetType().GetProperty("ResourceKey",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public   |
                    System.Reflection.BindingFlags.NonPublic);
                if (keyProp?.GetValue(value) is string key && !string.IsNullOrEmpty(key))
                {
                    var propName = _dpDisplayNames.TryGetValue(dp, out var n) ? n : dp.Name;
                    var entryKey = $"{propName}:{key}";
                    if (seen.Add(entryKey))
                        result.Add((propName, key));
                }
            }
            catch { }
        }
        return result;
    }

    private static string? GetStyleKey(FrameworkElement element)
    {
        try
        {
            var value = element.ReadLocalValue(FrameworkElement.StyleProperty);
            if (value == DependencyProperty.UnsetValue || value is not Style style) return null;
            var keyProp = style.GetType().GetProperty("ResourceKey",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public   |
                System.Reflection.BindingFlags.NonPublic);
            if (keyProp?.GetValue(style) is string key && !string.IsNullOrEmpty(key))
                return key;
        }
        catch { }
        return null;
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
