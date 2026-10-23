using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash.Tests;

/// <summary>
/// Runs an action on a dedicated STA thread with a pumping WPF Dispatcher.
/// Required for any test that creates or manipulates WPF objects (RichTextBox,
/// TextPointer, adorners, etc.).
/// </summary>
internal static class WpfTestContext
{
    /// <summary>Runs <paramref name="action"/> on a fresh STA thread with a live Dispatcher.</summary>
    internal static void Run(Action action)
    {
        Exception? caught = null;
        using var completed = new ManualResetEventSlim();
        var keepDispatcherAlive = false;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;

            // Queue the action so it runs once the pump is running.
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    // Application.Current is process-wide and permanently belongs to the
                    // dispatcher on which it was created. Shutting that dispatcher down
                    // leaves later WPF tests with a singleton whose owner thread is dead;
                    // Dispatcher.Invoke then waits forever. Keep that one dispatcher pumping
                    // for the lifetime of the test host and shut down all other per-call ones.
                    keepDispatcherAlive = ReferenceEquals(Application.Current?.Dispatcher, dispatcher);
                    if (!keepDispatcherAlive)
                        dispatcher.InvokeShutdown();
                    completed.Set();
                }
            });

            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        completed.Wait();
        if (!keepDispatcherAlive)
            thread.Join();
        if (caught != null)
            ExceptionDispatchInfo.Capture(caught).Throw();
    }

    /// <summary>Runs an async action on a fresh STA thread with a live Dispatcher.</summary>
    internal static void Run(Func<Task> asyncAction)
    {
        Run(() => asyncAction().GetAwaiter().GetResult());
    }
}
