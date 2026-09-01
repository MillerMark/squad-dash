using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Hosts a code block action in the window's adorner layer rather than inside the
/// FlowDocument. The action follows the visible top-right corner of the code block,
/// so FlowDocument clipping and a code TextBox's horizontal scrolling cannot move it.
/// </summary>
internal static class StickyCodeBlockBehavior
{
    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(StickyCodeBlockState),
            typeof(StickyCodeBlockBehavior),
            new PropertyMetadata(null));

    internal static void Attach(FrameworkElement codeBlockContainer, FrameworkElement stickyButton)
    {
        if (codeBlockContainer.GetValue(StateProperty) is StickyCodeBlockState)
            return;

        codeBlockContainer.SetValue(StateProperty, new StickyCodeBlockState(codeBlockContainer, stickyButton));
    }

    internal readonly record struct StickyLayout(bool IsVisible, Rect ButtonBounds, Rect ClipBounds);

    /// <summary>
    /// Calculates the action bounds in the same coordinate space as the code block and
    /// viewport. Keeping this math independent from WPF layout makes the scroll-edge
    /// behavior deterministic and unit-testable.
    /// </summary>
    internal static StickyLayout CalculateLayout(
        Rect codeBlockBounds,
        Rect viewportBounds,
        Size buttonSize,
        Thickness inset)
    {
        var visibleBounds = Rect.Intersect(codeBlockBounds, viewportBounds);
        if (visibleBounds.IsEmpty || visibleBounds.Width <= 0 || visibleBounds.Height <= 0 ||
            buttonSize.Width <= 0 || buttonSize.Height <= 0)
        {
            return new StickyLayout(false, Rect.Empty, Rect.Empty);
        }

        // Anchor to the visible right edge, not the code TextBox's horizontally-scrolled
        // content. Near an edge the viewport clip progressively hides the action instead
        // of allowing it to escape the code block.
        var x = Math.Min(
            visibleBounds.Right - inset.Right - buttonSize.Width,
            codeBlockBounds.Right - inset.Right - buttonSize.Width);
        x = Math.Max(x, codeBlockBounds.Left + inset.Left);

        var preferredY = Math.Max(
            codeBlockBounds.Top + inset.Top,
            viewportBounds.Top + inset.Top);
        var lastContainedY = codeBlockBounds.Bottom - inset.Bottom - buttonSize.Height;
        var y = Math.Min(preferredY, lastContainedY);

        return new StickyLayout(
            true,
            new Rect(new Point(x, y), buttonSize),
            visibleBounds);
    }

    private sealed class StickyCodeBlockState
    {
        private readonly FrameworkElement _container;
        private readonly FrameworkElement _button;
        private readonly CopyButton? _copyButton;
        private readonly Thickness _inset;
        private ScrollViewer? _scrollViewer;
        private FrameworkElement? _viewport;
        private AdornerLayer? _adornerLayer;
        private StickyCodeButtonAdorner? _adorner;

        internal StickyCodeBlockState(FrameworkElement container, FrameworkElement button)
        {
            _container = container;
            _button = button;
            _copyButton = button as CopyButton ?? FindDescendantCopyButton(button);
            _inset = button.Margin;
            _button.Margin = new Thickness(0);

            _container.Loaded += OnLoaded;
            _container.Unloaded += OnUnloaded;
            _container.SizeChanged += OnLayoutChanged;
            _button.SizeChanged += OnLayoutChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BindToAncestorScrollViewer();

            // BlockUIContainer.Loaded can precede the final adorner layer and button
            // measure pass. Deferring one dispatcher turn makes attachment reliable.
            _container.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (!_container.IsLoaded)
                        return;

                    TryAttachAdorner();
                    RefreshPosition();
                }));
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnbindFromScrollViewer();
            RemoveAdorner();
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            _copyButton?.DismissFeedback();
            RefreshPosition();
        }

        private void OnLayoutChanged(object? sender, EventArgs e) => RefreshPosition();

        private void TryAttachAdorner()
        {
            if (_adorner is not null)
                return;

            var layer = AdornerLayer.GetAdornerLayer(_container);
            if (layer is null)
                return;

            _adorner = new StickyCodeButtonAdorner(_container, _button, ResolveViewport, _inset);
            _adornerLayer = layer;
            layer.Add(_adorner);
        }

        private void RemoveAdorner()
        {
            if (_adornerLayer is not null && _adorner is not null)
            {
                try { _adornerLayer.Remove(_adorner); }
                catch (InvalidOperationException) { }
                // Release the button from the adorner's visual collection so it can be
                // re-parented to a new adorner without an ArgumentException.
                _adorner.ReleaseButton();
            }

            _adorner = null;
            _adornerLayer = null;
        }

        private void RefreshPosition()
        {
            if (!_container.IsLoaded)
                return;

            BindToAncestorScrollViewer();
            TryAttachAdorner();
            _adorner?.InvalidateMeasure();
            _adorner?.InvalidateArrange();
            if (_adornerLayer is not null)
            {
                try { _adornerLayer.Update(_container); }
                catch (InvalidOperationException) { }
            }
        }

        private FrameworkElement? ResolveViewport() => _viewport ?? _scrollViewer;

        private void BindToAncestorScrollViewer()
        {
            var (scrollViewer, viewport) = FindAncestorScrollViewer(_container);
            if (ReferenceEquals(scrollViewer, _scrollViewer) && ReferenceEquals(viewport, _viewport))
                return;

            UnbindFromScrollViewer();
            _scrollViewer = scrollViewer;
            _viewport = viewport;
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
                _scrollViewer.SizeChanged += OnLayoutChanged;
            }
            if (_viewport is not null && !ReferenceEquals(_viewport, _scrollViewer))
                _viewport.SizeChanged += OnLayoutChanged;
        }

        private void UnbindFromScrollViewer()
        {
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer.SizeChanged -= OnLayoutChanged;
            }
            if (_viewport is not null && !ReferenceEquals(_viewport, _scrollViewer))
                _viewport.SizeChanged -= OnLayoutChanged;

            _scrollViewer = null;
            _viewport = null;
        }

        private static (ScrollViewer? ScrollViewer, FrameworkElement? Viewport)
            FindAncestorScrollViewer(DependencyObject start)
        {
            FrameworkElement? viewport = null;
            DependencyObject? current = start;
            while (current is not null)
            {
                if (current is ScrollContentPresenter presenter)
                    viewport ??= presenter;

                if (current is ScrollViewer scrollViewer)
                    return (scrollViewer, viewport ?? scrollViewer);

                current = VisualTreeHelper.GetParent(current);
            }

            return (null, null);
        }

        private static CopyButton? FindDescendantCopyButton(DependencyObject root)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is CopyButton copyButton)
                    return copyButton;

                var nestedMatch = FindDescendantCopyButton(child);
                if (nestedMatch is not null)
                    return nestedMatch;
            }

            return null;
        }
    }

    private sealed class StickyCodeButtonAdorner : Adorner
    {
        private readonly FrameworkElement _button;
        private readonly Func<FrameworkElement?> _resolveViewport;
        private readonly Thickness _inset;
        private readonly VisualCollection _visuals;

        internal StickyCodeButtonAdorner(
            FrameworkElement adornedElement,
            FrameworkElement button,
            Func<FrameworkElement?> resolveViewport,
            Thickness inset)
            : base(adornedElement)
        {
            _button = button;
            _resolveViewport = resolveViewport;
            _inset = inset;
            _visuals = new VisualCollection(this) { _button };
        }

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index) => _visuals[index];

        internal void ReleaseButton()
        {
            if (_visuals.Contains(_button))
                _visuals.Remove(_button);
        }

        // Only the child button should receive input; the transparent remainder of the
        // adorner must not interfere with code selection or the TextBox scrollbar.
        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

        protected override Size MeasureOverride(Size constraint)
        {
            // Hidden elements still participate in measure. Collapsed elements do not,
            // which can permanently strand the button at DesiredSize=(0,0) when the
            // FlowDocument performs an early zero-sized layout during hydration.
            if (_button.Visibility == Visibility.Collapsed)
                _button.Visibility = Visibility.Hidden;
            _button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var viewport = _resolveViewport();
            if (viewport is null || !viewport.IsVisible || finalSize.Width <= 0 || finalSize.Height <= 0)
            {
                HideButton();
                return finalSize;
            }

            try
            {
                var viewportBounds = viewport.TransformToVisual(AdornedElement)
                    .TransformBounds(new Rect(new Point(0, 0), viewport.RenderSize));
                var layout = CalculateLayout(
                    new Rect(new Point(0, 0), finalSize),
                    viewportBounds,
                    _button.DesiredSize,
                    _inset);

                if (!layout.IsVisible)
                {
                    HideButton();
                    return finalSize;
                }

                _button.Visibility = Visibility.Visible;
                Clip = new RectangleGeometry(layout.ClipBounds);
                _button.Arrange(layout.ButtonBounds);
            }
            catch (InvalidOperationException)
            {
                HideButton();
            }

            return finalSize;
        }

        private void HideButton()
        {
            _button.Visibility = Visibility.Hidden;
            Clip = null;
        }
    }
}
