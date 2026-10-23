using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class WpfTestContextTests
{
    [Test]
    public void ApplicationOwnerDispatcher_RemainsAliveAcrossWpfTestContextRuns()
    {
        Dispatcher? applicationDispatcher = null;

        WpfTestContext.Run(() =>
        {
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            applicationDispatcher = Application.Current!.Dispatcher;
        });

        Assert.That(applicationDispatcher, Is.Not.Null);
        Assert.That(applicationDispatcher!.Thread.IsAlive, Is.True,
            "Application.Current must not retain a dispatcher whose owner thread has exited.");
    }
}
