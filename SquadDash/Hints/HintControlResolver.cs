using System.Windows;
using System.Windows.Media;

namespace SquadDash.Hints;

/// <summary>
/// Resolves a stable string identity for a WPF element, used to match
/// <see cref="HintDefinition.TargetControlId"/> values at display time.
/// </summary>
internal static class HintControlResolver {
    /// <summary>
    /// Walks up the visual tree from <paramref name="element"/> and returns the
    /// first stable identifier found, using the following priority order:
    /// <list type="number">
    ///   <item>DataContext implements <see cref="IHaveAgentName"/> → AgentName</item>
    ///   <item>DataContext implements <see cref="INamedControl"/> → ControlName</item>
    ///   <item><see cref="FrameworkElement.Name"/> — non-empty and not starting with <c>PART_</c></item>
    /// </list>
    /// The walk stops at a <see cref="Window"/> root. Returns <c>null</c> if nothing is found.
    /// </summary>
    public static string? ResolveControlId(DependencyObject element) {
        DependencyObject? current = element;
        while (current is not null) {
            if (current is FrameworkElement fe) {
                var dc = fe.DataContext;
                if (dc is IHaveAgentName agentNamed)
                    return agentNamed.AgentName;
                if (dc is INamedControl namedControl)
                    return namedControl.ControlName;
                var name = fe.Name;
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("PART_", StringComparison.Ordinal))
                    return name;
            }
            if (current is Window) break;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
