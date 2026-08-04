using System.Windows.Threading;
using SquadDash.GuidedTours;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class GuidedTourDismissalDeferralTests
{
    [Test]
    public void Request_DoesNotStopTourInsideCurrentInputCallback()
    {
        WpfTestContext.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var deferral = new GuidedTourDismissalDeferral(dispatcher);
            var stopped = false;

            deferral.Request(() => true, () => stopped = true);

            Assert.That(stopped, Is.False);
            Assert.That(deferral.IsPending, Is.True);

            dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.That(stopped, Is.True);
            Assert.That(deferral.IsPending, Is.False);
        });
    }

    [Test]
    public void Request_CoalescesPreviewAndClickDismissSignals()
    {
        WpfTestContext.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var deferral = new GuidedTourDismissalDeferral(dispatcher);
            var stopCount = 0;

            deferral.Request(() => true, () => stopCount++);
            deferral.Request(() => true, () => stopCount++);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.That(stopCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Request_DoesNotStopTourThatEndedBeforeDeferredCallback()
    {
        WpfTestContext.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var deferral = new GuidedTourDismissalDeferral(dispatcher);
            var active = true;
            var stopped = false;

            deferral.Request(() => active, () => stopped = true);
            active = false;
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.That(stopped, Is.False);
            Assert.That(deferral.IsPending, Is.False);
        });
    }
}
