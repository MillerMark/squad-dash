namespace SquadDash;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Creates a reusable "No items match the filter" empty-state panel shown when
/// a filter is active but yields zero results. Call <see cref="Build"/> once and
/// store the result; toggle <see cref="UIElement.Visibility"/> to show/hide it.
/// </summary>
internal static class FilterEmptyStateHelper
{
    /// <summary>
    /// Builds the empty-state panel containing a message label and a "Clear Filter" button.
    /// </summary>
    /// <param name="clearFilter">Invoked when the user clicks "Clear Filter".</param>
    /// <param name="message">Optional message override; defaults to "No items match the filter."</param>
    internal static UIElement Build(Action clearFilter, string? message = null)
    {
        var label = new TextBlock
        {
            Text                = message ?? "No items match the filter.",
            TextWrapping        = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment       = TextAlignment.Center,
            Margin              = new Thickness(8, 0, 8, 10),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var button = new Button { Content = "Clear Filter" };
        button.SetResourceReference(Button.StyleProperty,    "ThemedButtonStyle");
        button.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
        button.Click += (_, _) => clearFilter();

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(8, 20, 8, 8),
        };
        stack.Children.Add(label);
        stack.Children.Add(button);

        return stack;
    }

    /// <summary>
    /// Wraps <paramref name="listElement"/> in a <see cref="Grid"/> so that an overlay
    /// (e.g. the filter empty state) can be layered on top of it.
    /// Returns the grid; add the overlay as a second child of that grid.
    /// </summary>
    internal static Grid WrapInGrid(UIElement listElement)
    {
        var grid = new Grid();
        grid.Children.Add(listElement);
        return grid;
    }
}
