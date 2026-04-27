using System;
using System.IO;
using System.Data;
using System.Windows;
using System.Configuration;
using System.Threading.Tasks;
using System.Windows.Threading;
using SquadDash.Screenshots;

namespace SquadDash {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            base.OnStartup(e);

            var startupArguments = StartupFolderParser.ParseArguments(e.Args);
            var workspacePaths = string.IsNullOrWhiteSpace(startupArguments.ApplicationRoot)
                ? WorkspacePathsProvider.Discover()
                : new WorkspacePathsProvider(startupArguments.ApplicationRoot);
            SquadDashTrace.Write(
                "Startup",
                $"App.OnStartup appRoot={startupArguments.ApplicationRoot ?? "(auto)"} workspace={startupArguments.StartupFolder ?? "(none)"}");
            SquadDashRuntimeStamp.WriteStartupStamp(workspacePaths);
            var startupFolder = startupArguments.StartupFolder;

            // Resolve screenshot refresh options from raw parsed args.
            var refreshOptions = startupArguments.RefreshScreenshots
                ? new ScreenshotRefreshOptions(
                    startupArguments.RefreshScreenshotName is null
                        ? ScreenshotRefreshMode.All
                        : ScreenshotRefreshMode.Named,
                    startupArguments.RefreshScreenshotName)
                : ScreenshotRefreshOptions.None;

            // Diagnostic: write early confirmation to the refresh log so we can verify
            // this process reached OnStartup with the correct refresh mode.
            if (refreshOptions.Mode != ScreenshotRefreshMode.None)
            {
                var diagLine = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] [startup] Refresh mode detected: {refreshOptions.Mode} target={refreshOptions.TargetName ?? "(all)"} args=[{string.Join(", ", e.Args)}]";
                try { File.AppendAllText(Path.Combine(workspacePaths.ScreenshotsDirectory, "refresh-log.txt"), diagLine + Environment.NewLine); }
                catch { /* best-effort */ }
            }

            // In screenshot-refresh modethis process is headless and ephemeral — it must
            // be allowed to run alongside an existing interactive instance for the same
            // workspace, so skip the single-instance ownership check entirely.
            WorkspaceOwnershipLease? startupWorkspaceLease = null;
            if (!WorkspaceStartupRoutingPolicy.ShouldBypassSingleInstanceRouting(refreshOptions) &&
                TryHandleStartupWorkspaceRouting(startupFolder, workspacePaths, out startupWorkspaceLease))
                return;

            var window = new MainWindow(startupFolder, startupWorkspaceLease, workspacePaths, refreshOptions);
            MainWindow = window;
            window.Show();

            if (!string.IsNullOrWhiteSpace(startupFolder) && !Directory.Exists(startupFolder)) {
                MessageBox.Show(
                    $"Startup folder not found:\n{startupFolder}",
                    "Invalid Startup Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
            SquadDashTrace.Write("Unhandled", $"Dispatcher exception: {e.Exception}");

            TryEmergencySave();

            if (MainWindow is MainWindow window)
                window.ReportUnhandledUiException("Dispatcher", e.Exception);

            if (ShouldSuppressDuringShutdown(e.Exception)) {
                e.Handled = true;
                return;
            }

            // Keep the UI alive for recoverable dispatcher-thread failures. The
            // failing callback is still logged and surfaced in MainWindow.
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            SquadDashTrace.Write("Unhandled", $"AppDomain exception: {e.ExceptionObject}");
            TryEmergencySave();

            if (MainWindow is MainWindow window && e.ExceptionObject is Exception exception)
                window.ReportUnhandledUiException("AppDomain", exception);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
            SquadDashTrace.Write("Unhandled", $"TaskScheduler exception: {e.Exception}");

            // Swallow benign ObjectDisposedException from CancellationTokenSources that were
            // disposed while a background task still held a reference (common during doc reloads).
            var inner = e.Exception.InnerException ?? e.Exception;
            if (inner is ObjectDisposedException) {
                e.SetObserved();
                return;
            }

            // This handler fires on the finalizer thread — marshal to the UI thread before
            // touching any WPF objects.
            if (MainWindow is MainWindow window) {
                var ex = e.Exception;
                window.Dispatcher.BeginInvoke(() => window.ReportUnhandledUiException("TaskScheduler", ex));
            }

            e.SetObserved();
        }

        private bool TryHandleStartupWorkspaceRouting(
            string? startupFolder,
            IWorkspacePaths workspacePaths,
            out WorkspaceOwnershipLease? startupWorkspaceLease) {
            startupWorkspaceLease = null;

            if (!string.IsNullOrWhiteSpace(startupFolder) && !Directory.Exists(startupFolder))
                return false;

            var settingsSnapshot = new ApplicationSettingsStore().Load();
            var candidateWorkspace = StartupWorkspaceResolver.Resolve(
                startupFolder,
                settingsSnapshot.LastOpenedFolder,
                workspacePaths.ApplicationRoot);
            if (string.IsNullOrWhiteSpace(candidateWorkspace))
                return false;

            var decision = new WorkspaceOpenCoordinator().ReserveOrActivate(
                workspacePaths.ApplicationRoot,
                candidateWorkspace,
                Environment.ProcessId,
                ProcessIdentity.GetCurrentProcessStartedAtUtcTicks());

            switch (decision.Disposition) {
                case WorkspaceOpenDisposition.OpenHere:
                    startupWorkspaceLease = decision.Lease;
                    return false;

                case WorkspaceOpenDisposition.ActivatedExisting:
                    SquadDashTrace.Write(
                        "Startup",
                        $"Activated an existing SquadDash instance for workspace={candidateWorkspace} during startup routing.");
                    Shutdown();
                    return true;

                case WorkspaceOpenDisposition.Blocked:
                    MessageBox.Show(
                        $"That workspace is already open in another SquadDash window:{Environment.NewLine}{candidateWorkspace}",
                        "Workspace Already Open",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Shutdown();
                    return true;

                default:
                    return false;
            }
        }

        private void TryEmergencySave() {
            try {
                if (MainWindow is MainWindow w)
                    w.Dispatcher.Invoke(w.EmergencySave);
            }
            catch (Exception ex) {
                SquadDashTrace.Write("Unhandled", $"TryEmergencySave failed: {ex.Message}");
            }
        }

        private static bool ShouldSuppressDuringShutdown(Exception exception) {
            var dispatcher = Current?.MainWindow?.Dispatcher;
            var isShuttingDown = dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
            if (!isShuttingDown)
                return false;

            return exception is InvalidOperationException ||
                   exception is ObjectDisposedException ||
                   exception is OperationCanceledException;
        }

    }

}
