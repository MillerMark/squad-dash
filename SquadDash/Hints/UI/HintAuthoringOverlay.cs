using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SquadDash.Hints;

namespace SquadDash;

/// <summary>
/// Thin service that manages Hint Authoring Mode (F6 in developer mode).
///
/// When active:
///   • Hovering over any control highlights it with a dashed blue adorner and
///     shows the resolved control ID as a tooltip label.
///   • Clicking a control opens <see cref="HintAuthoringWindow"/> pre-filled
///     with the control's ID; normal click propagation is suppressed.
///   • Controls whose resolved ID starts with <c>PART_</c> are skipped.
///
/// Deactivated by pressing F6 or Escape, or by calling <see cref="Deactivate"/>.
/// </summary>
internal sealed class HintAuthoringOverlay
{
    private Window? _owner;
    private string  _workspaceRoot = string.Empty;
    private Action<bool>? _setActiveIndicator;

    private FrameworkElement? _hoveredElement;
    private HintHighlightAdorner? _activeAdorner;
    private UIElement? _adornerTarget;

    public bool IsActive => _owner is not null;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public void Activate(Window owner, string workspaceRoot, Action<bool>? setActiveIndicator = null)
    {
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            Deactivate();
        if (_owner is not null)
            return;

        _owner              = owner;
        _workspaceRoot      = workspaceRoot;
        _setActiveIndicator = setActiveIndicator;

        InputManager.Current.PostProcessInput += OnPostProcessInput;
        owner.PreviewMouseLeftButtonDown += OnOwnerPreviewClick;
        _setActiveIndicator?.Invoke(true);
    }

    public void Deactivate()
    {
        if (_owner is null) return;

        InputManager.Current.PostProcessInput -= OnPostProcessInput;
        _owner.PreviewMouseLeftButtonDown -= OnOwnerPreviewClick;
        RemoveAdorner();
        _hoveredElement = null;

        _setActiveIndicator?.Invoke(false);
        _setActiveIndicator = null;
        _owner = null;
    }

    // ── Input handling ───────────────────────────────────────────────────────

    private void OnPostProcessInput(object sender, ProcessInputEventArgs e)
    {
        try
        {
            if (_owner is null) return;

            // Escape → deactivate (F6 is handled in MainWindow.OnGlobalPreProcessInput)
            if (e.StagingItem.Input is KeyEventArgs keyArgs
                && keyArgs.RoutedEvent == Keyboard.KeyUpEvent
                && keyArgs.Key == Key.Escape
                && Keyboard.Modifiers == ModifierKeys.None)
            {
                Deactivate();
                keyArgs.Handled = true;
                return;
            }

            // Mouse move → update highlight adorner
            if (e.StagingItem.Input is MouseEventArgs mouseArgs
                && mouseArgs.RoutedEvent == Mouse.MouseMoveEvent)
            {
                UpdateHighlight(mouseArgs);
            }
        }
        catch
        {
            // Never crash on an input handler
        }
    }

    // ── Hover highlight ──────────────────────────────────────────────────────

    private void UpdateHighlight(MouseEventArgs mouseArgs)
    {
        if (_owner is null) return;

        var pos = mouseArgs.GetPosition(_owner);
        var hit = VisualTreeHelper.HitTest(_owner, pos);

        if (hit?.VisualHit is not FrameworkElement fe)
        {
            RemoveAdorner();
            _hoveredElement = null;
            return;
        }

        var named = FindInterestingElement(fe);
        if (named is null)
        {
            RemoveAdorner();
            _hoveredElement = null;
            return;
        }

        var controlId = HintControlResolver.ResolveControlId(named);

        // Skip PART_ internals
        if (controlId?.StartsWith("PART_", StringComparison.Ordinal) == true)
            return;

        if (ReferenceEquals(named, _hoveredElement))
            return;

        _hoveredElement = named;
        UpdateAdorner(named, controlId ?? "(no name)");
    }

    /// <summary>
    /// Walks up from <paramref name="start"/> to find the nearest named
    /// FrameworkElement, stopping at a Window boundary.
    /// Falls back to <paramref name="start"/> itself if nothing is named.
    /// </summary>
    private static FrameworkElement? FindInterestingElement(FrameworkElement start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                return fe;
            if (current is Window)
                break;
            current = VisualTreeHelper.GetParent(current);
        }
        return start;
    }

    private void UpdateAdorner(FrameworkElement target, string label)
    {
        RemoveAdorner();

        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;

        _activeAdorner  = new HintHighlightAdorner(target, label);
        _adornerTarget  = target;
        layer.Add(_activeAdorner);
    }

    private void RemoveAdorner()
    {
        if (_activeAdorner is null || _adornerTarget is null) return;

        var layer = AdornerLayer.GetAdornerLayer(_adornerTarget);
        layer?.Remove(_activeAdorner);
        _activeAdorner = null;
        _adornerTarget = null;
    }

    // ── Click intercept (must be on tunneling Preview event to suppress) ─────

    private void OnOwnerPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (_hoveredElement is null) return;
        e.Handled = true;
        OpenAuthoringWindow(_hoveredElement);
    }

    // ── Authoring window ─────────────────────────────────────────────────────

    private void OpenAuthoringWindow(FrameworkElement target)
    {
        var controlId = HintControlResolver.ResolveControlId(target) ?? target.GetType().Name;
        var window    = new HintAuthoringWindow();
        window.Initialize(controlId, _workspaceRoot);
        window.Owner = _owner;
        window.Show();
    }

    // ── Adorner ──────────────────────────────────────────────────────────────

    private sealed class HintHighlightAdorner : Adorner
    {
        private static readonly Pen _borderPen;
        private static readonly SolidColorBrush _labelBg;

        static HintHighlightAdorner()
        {
            // Blue accent matching QueueTabActiveBorder (#1E88E5)
            var blue = Color.FromRgb(0x1E, 0x88, 0xE5);
            var borderBrush = new SolidColorBrush(blue);
            borderBrush.Freeze();
            _borderPen = new Pen(borderBrush, 2) { DashStyle = DashStyles.Dash };
            _borderPen.Freeze();

            _labelBg = new SolidColorBrush(Color.FromArgb(210, 0x1E, 0x88, 0xE5));
            _labelBg.Freeze();
        }

        private readonly string _label;

        public HintHighlightAdorner(UIElement element, string label) : base(element)
        {
            IsHitTestVisible = false;
            _label = label;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;

            dc.DrawRectangle(null, _borderPen,
                new Rect(1, 1, Math.Max(0, w - 2), Math.Max(0, h - 2)));

            var typeface  = new Typeface("Segoe UI");
            var pixelsDip = VisualTreeHelper.GetDpi(AdornedElement).PixelsPerDip;
            var ft = new FormattedText(
                _label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                Brushes.White,
                pixelsDip);

            var labelRect = new Rect(0, h, ft.Width + 6, ft.Height + 4);
            dc.DrawRectangle(_labelBg, null, labelRect);
            dc.DrawText(ft, new Point(3, h + 2));
        }
    }
}
