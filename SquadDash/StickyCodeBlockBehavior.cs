using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

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

    private sealed class StickyCodeBlockState
    {
        private readonly FrameworkElement _container;
        private readonly FrameworkElement _button;
        private readonly TranslateTransform _translateTransform = new();
        private ScrollViewer? _scrollViewer;

        internal StickyCodeBlockState(FrameworkElement container, FrameworkElement button)
        {
            _container = container;
            _button = button;
            _button.RenderTransform = _translateTransform;
            _container.Loaded += OnLoaded;
            _container.Unloaded += OnUnloaded;
            _container.SizeChanged += OnLayoutChanged;
            _button.SizeChanged += OnLayoutChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BindToAncestorScrollViewer();
            UpdatePosition();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer.SizeChanged -= OnLayoutChanged;
            }
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => UpdatePosition();

        private void OnLayoutChanged(object? sender, EventArgs e) => UpdatePosition();

        private void BindToAncestorScrollViewer()
        {
            var scrollViewer = FindAncestorScrollViewer(_container);
            if (ReferenceEquals(scrollViewer, _scrollViewer))
                return;

            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer.SizeChanged -= OnLayoutChanged;
            }

            _scrollViewer = scrollViewer;
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
                _scrollViewer.SizeChanged += OnLayoutChanged;
            }
        }

        private void UpdatePosition()
        {
            if (!_container.IsLoaded || _button.ActualHeight <= 0)
                return;

            BindToAncestorScrollViewer();
            if (_scrollViewer is null)
            {
                _button.Visibility = Visibility.Visible;
                _translateTransform.Y = 0;
                return;
            }

            Rect containerBounds;
            try
            {
                containerBounds = _container.TransformToAncestor(_scrollViewer)
                    .TransformBounds(new Rect(0, 0, _container.ActualWidth, _container.ActualHeight));
            }
            catch
            {
                return;
            }

            var viewportHeight = _scrollViewer.ViewportHeight;
            if (viewportHeight <= 0)
                viewportHeight = _scrollViewer.ActualHeight;

            if (containerBounds.Bottom <= 0 || containerBounds.Top >= viewportHeight)
            {
                _button.Visibility = Visibility.Collapsed;
                return;
            }

            _button.Visibility = Visibility.Visible;

            var buttonHeight = _button.ActualHeight;
            var minViewportY = containerBounds.Top;
            var maxViewportY = containerBounds.Bottom - buttonHeight;
            if (maxViewportY < minViewportY)
                maxViewportY = minViewportY;

            var desiredViewportY = 0.0;
            desiredViewportY = Math.Max(desiredViewportY, minViewportY);
            desiredViewportY = Math.Min(desiredViewportY, maxViewportY);

            _translateTransform.Y = desiredViewportY - containerBounds.Top;
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
        {
            DependencyObject? current = start;
            while (current is not null)
            {
                if (current is ScrollViewer scrollViewer)
                    return scrollViewer;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
