using System.Windows.Threading;

namespace SquadDash.GuidedTours;

/// <summary>
/// Coalesces callout-dismiss signals and runs tour teardown only after WPF has
/// finished routing the input event that produced them.
/// </summary>
internal sealed class GuidedTourDismissalDeferral
{
    private readonly Dispatcher _dispatcher;
    private bool _pending;

    public GuidedTourDismissalDeferral(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    internal bool IsPending => _pending;

    public void Request(Func<bool> isTourActive, Action stopTour)
    {
        ArgumentNullException.ThrowIfNull(isTourActive);
        ArgumentNullException.ThrowIfNull(stopTour);

        if (_pending || !isTourActive())
            return;

        _pending = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _pending = false;
            if (isTourActive())
                stopTour();
        });
    }
}
