using System.Windows;

namespace SquadDash.Screenshots;

/// <summary>
/// Abstracts live WPF visual-tree queries so <see cref="ScreenshotHealthChecker"/>
/// stays decoupled from <c>MainWindow</c> and the WPF element hierarchy.
/// </summary>
public interface ILiveElementLocator
{
    /// <summary>Finds a named element anywhere in the window's logical tree.</summary>
    FrameworkElement? FindByName(string name);

    /// <summary>
    /// Returns the bounding box of <paramref name="element"/> in window-relative
    /// logical pixels.  Returns <see cref="Rect.Empty"/> if the element has no layout.
    /// </summary>
    Rect GetBoundsRelativeToWindow(FrameworkElement element);

    /// <summary>True if the element is Visible and has been laid out (ActualWidth &gt; 0).</summary>
    bool IsVisible(FrameworkElement element);
}
