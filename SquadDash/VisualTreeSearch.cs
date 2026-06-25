using System.Windows;
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
    /// </summary>
    public static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            var result = FindByName(child, name);
            if (result is not null) return result;
        }
        return null;
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
