using System;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when a named <see cref="MenuItem"/> opens its submenu.
/// Parameter is the x:Name of the MenuItem in the visual tree.
/// </summary>
internal sealed class MenuOpenedAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Func<string, FrameworkElement?> _elementLocator;

    public MenuOpenedAdvanceTrigger(Func<string, FrameworkElement?> elementLocator) =>
        _elementLocator = elementLocator;

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        if (_elementLocator(parameter) is not MenuItem menuItem) return null;

        void Handler(object sender, RoutedEventArgs e) => onAdvance();
        menuItem.SubmenuOpened += Handler;
        return new Subscription(() => menuItem.SubmenuOpened -= Handler);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
