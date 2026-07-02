using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Depth-first visual-tree search helpers shared across MainWindow, GuidedTourController,
/// and any other component that needs to locate WPF elements beyond a single NameScope.
/// </summary>
public static class VisualTreeSearch
{
    /// <summary>
    /// Returns the first descendant of <typeparamref name="T"/> found by depth-first
    /// visual-tree traversal, or <c>null</c> if none exists.
    /// </summary>
    public static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var result = FindChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Returns the first descendant of <typeparamref name="T"/> that satisfies
    /// <paramref name="predicate"/>, found by depth-first visual-tree traversal,
    /// or <c>null</c>.
    /// </summary>
    public static T? FindChild<T>(DependencyObject parent, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && predicate(typedChild)) return typedChild;
            var result = FindChild<T>(child, predicate);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Returns the first <see cref="FrameworkElement"/> descendant whose
    /// <see cref="FrameworkElement.Name"/> equals <paramref name="name"/>,
    /// found by depth-first visual-tree traversal, or <c>null</c>.
    /// <para>
    /// Supports an index-selector suffix: <c>"ControlName[N]"</c> finds the element named
    /// <c>ControlName</c> and then returns the Nth (0-based) item container (for an
    /// <see cref="ItemsControl"/>) or the Nth visual child (for a <see cref="Panel"/>).
    /// If N exceeds the available count the last available item is returned instead.
    /// </para>
    /// </summary>
    public static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        // Index-selector: "SomeName[N]"
        int bracketOpen = name.IndexOf('[');
        if (bracketOpen > 0 && name.EndsWith(']'))
        {
            var baseName  = name[..bracketOpen];
            var indexStr  = name[(bracketOpen + 1)..^1];
            if (int.TryParse(indexStr, out int index) && index >= 0)
            {
                var baseElement = FindByNameCore(root, baseName);
                if (baseElement is ItemsControl ic)
                {
                    int clampedIc = Math.Min(index, ic.Items.Count - 1);
                    if (clampedIc >= 0)
                        return ic.ItemContainerGenerator.ContainerFromIndex(clampedIc) as FrameworkElement;
                    return null;
                }
                if (baseElement is Panel panel && panel.Children.Count > 0)
                {
                    // For panels that mix tab Borders with non-tab elements (e.g. QueueTabStrip
                    // which interleaves priority-feedback TextBlocks and hint labels), index into
                    // only the Border children so that [N] reliably means "the Nth tab" regardless
                    // of how many decorative non-Border children are interspersed.
                    var borders = panel.Children.OfType<Border>().ToList();
                    if (borders.Count > 0)
                    {
                        int clamped = Math.Min(index, borders.Count - 1);
                        return borders[clamped];
                    }
                    int clampedPanel = Math.Min(index, panel.Children.Count - 1);
                    return panel.Children[clampedPanel] as FrameworkElement;
                }
            }
            return null;
        }

        return FindByNameCore(root, name);
    }

    private static FrameworkElement? FindByNameCore(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            var result = FindByNameCore(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Returns the <em>last</em> <see cref="FrameworkElement"/> descendant whose
    /// <see cref="FrameworkElement.Name"/> equals <paramref name="name"/>, found by
    /// collecting all matches via depth-first visual-tree traversal and returning the
    /// final entry, or <c>null</c> if no match exists.
    /// </summary>
    public static FrameworkElement? FindLastByName(DependencyObject root, string name)
    {
        var all = new List<FrameworkElement>();
        CollectByName(root, name, all);
        return all.Count > 0 ? all[all.Count - 1] : null;
    }

    private static void CollectByName(DependencyObject root, string name, List<FrameworkElement> results)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) results.Add(fe);
            CollectByName(child, name, results);
        }
    }

    /// <summary>
    /// Returns the first descendant of <typeparamref name="T"/> whose
    /// <see cref="FrameworkElement.Name"/> equals <paramref name="name"/>,
    /// found by depth-first visual-tree traversal, or <c>null</c>.
    /// </summary>
    public static T? FindChildByName<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild && typedChild.Name == name) return typedChild;
            var result = FindChildByName<T>(child, name);
            if (result is not null) return result;
        }
        return null;
    }
}
