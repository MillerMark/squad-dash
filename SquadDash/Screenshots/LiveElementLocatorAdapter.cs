using System;
using System.Windows;

namespace SquadDash.Screenshots;

/// <summary>
/// Routes <see cref="ILiveElementLocator"/> calls to MainWindow visual-tree helpers via
/// injected delegates. MainWindow holds one instance and passes it wherever an
/// <see cref="ILiveElementLocator"/> is required.
/// </summary>
internal sealed class LiveElementLocatorAdapter : ILiveElementLocator
{
    private readonly Func<string, FrameworkElement?>       _findByName;
    private readonly Func<FrameworkElement, Rect>          _getBoundsRelativeToWindow;
    private readonly Func<FrameworkElement, bool>          _isVisible;

    internal LiveElementLocatorAdapter(
        Func<string, FrameworkElement?>      findByName,
        Func<FrameworkElement, Rect>         getBoundsRelativeToWindow,
        Func<FrameworkElement, bool>         isVisible)
    {
        _findByName                = findByName                ?? throw new ArgumentNullException(nameof(findByName));
        _getBoundsRelativeToWindow = getBoundsRelativeToWindow ?? throw new ArgumentNullException(nameof(getBoundsRelativeToWindow));
        _isVisible                 = isVisible                 ?? throw new ArgumentNullException(nameof(isVisible));
    }

    // ── ILiveElementLocator ───────────────────────────────────────────────
    FrameworkElement? ILiveElementLocator.FindByName(string name)                    => _findByName(name);
    Rect              ILiveElementLocator.GetBoundsRelativeToWindow(FrameworkElement element) => _getBoundsRelativeToWindow(element);
    bool              ILiveElementLocator.IsVisible(FrameworkElement element)        => _isVisible(element);
}
