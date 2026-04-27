namespace SquadDash;

/// <summary>
/// Implemented by data-context objects whose WPF visual tree representations
/// (e.g. DataTemplate roots) share a common <c>x:Name</c> across all instances.
/// <see cref="VisualTreeEdgeAnalyzer"/> checks this interface on an element's
/// <see cref="System.Windows.FrameworkElement.DataContext"/> when no <c>x:Name</c>
/// is present or when the name is not sufficiently unique, allowing templated items
/// such as agent cards to report a stable, instance-specific identifier.
/// </summary>
public interface IHaveUniqueName
{
    /// <summary>
    /// Returns a stable, kebab-case identifier for this instance.
    /// Must be non-null and non-empty.
    /// Example return values: <c>"orion-vale"</c>, <c>"lyra-morn"</c>.
    /// </summary>
    string UniqueName { get; }
}
