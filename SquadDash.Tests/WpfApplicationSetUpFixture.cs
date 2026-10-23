using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NUnit.Framework;

[SetUpFixture]
internal sealed class WpfApplicationSetUpFixture
{
    private Thread? _applicationThread;
    private Dispatcher? _applicationDispatcher;

    [OneTimeSetUp]
    public void StartApplicationDispatcher()
    {
        using var ready = new ManualResetEventSlim();
        Exception? startupFailure = null;

        _applicationThread = new Thread(() =>
        {
            try
            {
                _applicationDispatcher = Dispatcher.CurrentDispatcher;
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
            catch (Exception ex)
            {
                startupFailure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (startupFailure is null)
                Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "SquadDash.Tests WPF Application"
        };
        _applicationThread.SetApartmentState(ApartmentState.STA);
        _applicationThread.Start();
        ready.Wait();

        if (startupFailure is not null)
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
    }

    [OneTimeTearDown]
    public void StopApplicationDispatcher()
    {
        _applicationDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _applicationThread?.Join(TimeSpan.FromSeconds(5));
    }
}
