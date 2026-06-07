using System;
using System.IO;
using System.Data;
using System.Windows;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Shell;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using SquadDash.Screenshots;

namespace SquadDash {
    public partial class App : Application {
        private bool _startupFailureReported;

        protected override void OnStartup(StartupEventArgs e) {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            base.OnStartup(e);

            var startupArguments = StartupFolderParser.ParseArguments(e.Args);
            var workspacePaths = string.IsNullOrWhiteSpace(startupArguments.ApplicationRoot)
                ? WorkspacePathsProvider.Discover()
                : new WorkspacePathsProvider(startupArguments.ApplicationRoot);
            SquadDashEnvironment.Initialize(workspacePaths.ApplicationRoot);
            SquadDashTrace.Write(
                "Startup",
                $"App.OnStartup appRoot={startupArguments.ApplicationRoot ?? "(auto)"} workspace={startupArguments.StartupFolder ?? "(none)"} newWindow={startupArguments.NoWorkspaceOnStart} restartRelaunch={startupArguments.RestartRelaunch} devMode={SquadDashEnvironment.IsDeveloperMode}");
            SquadDashRuntimeStamp.WriteStartupStamp(workspacePaths);
            var startupFolder = startupArguments.StartupFolder;

            var services = new ServiceCollection();
            services.AddSingleton<ApplicationSettingsStore>();
            services.AddSingleton<SquadTeamRosterLoader>();
            services.AddSingleton<SquadRoutingDocumentService>();
            services.AddSingleton<SquadInstallationStateService>();
            services.AddSingleton<SquadInstallerService>();
            services.AddSingleton<RunningInstanceRegistry>();
            services.AddSingleton<RestartCoordinatorStateStore>();
            services.AddSingleton<ClipboardEditorStateStore>();
            services.AddSingleton<PastedImageStore>();
            services.AddSingleton<PromptQueue>();
            services.AddSingleton<HostCommandRegistry>();
            services.AddSingleton<PostedUiActionTracker>();
            services.AddSingleton<UiActionReplayRegistry>();
            services.AddSingleton<FixtureLoaderRegistry>();
            services.AddSingleton<IPromptInstructionProvider, DefaultPromptInstructionProvider>();
            services.AddSingleton<ISquadBridgePromptBuilder, SquadBridgePromptBuilder>();
            var serviceProvider = services.BuildServiceProvider();

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
            var noWorkspaceOnStart = startupArguments.NoWorkspaceOnStart;
            if (!noWorkspaceOnStart &&
                !WorkspaceStartupRoutingPolicy.ShouldBypassSingleInstanceRouting(refreshOptions) &&
                TryHandleStartupWorkspaceRouting(startupFolder, workspacePaths, serviceProvider, startupArguments.RestartRelaunch, out startupWorkspaceLease, out noWorkspaceOnStart))
                return;

            try {
                var window = new MainWindow(startupFolder, startupWorkspaceLease, workspacePaths, refreshOptions, noWorkspaceOnStart, serviceProvider);
                startupWorkspaceLease = null;
                MainWindow = window;

                var recentFolders = serviceProvider.GetRequiredService<ApplicationSettingsStore>().Load().RecentFolders;
                RefreshJumpList(recentFolders);

                window.Show();

                if (!string.IsNullOrWhiteSpace(startupFolder) && !Directory.Exists(startupFolder)) {
                    MessageBox.Show(
                        $"Startup folder not found:\n{startupFolder}",
                        "Invalid Startup Folder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex) {
                startupWorkspaceLease?.Dispose();
                SquadDashTrace.Write("Startup", $"MainWindow startup failed; releasing workspace lease and shutting down: {ex}");
                ShowStartupFailureDialog(ex);
                Shutdown();
            }
        }

        public static void RefreshJumpList(IReadOnlyList<string> recentFolders)
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exe is null) return;

                var jumpList = new JumpList();

                jumpList.JumpItems.Add(new JumpTask
                {
                    Title = "New Window",
                    Description = "Open a new SquadDash window",
                    ApplicationPath = exe,
                    Arguments = "--new-window",
                    IconResourcePath = exe,
                });

                foreach (var folder in recentFolders)
                {
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                        continue;

                    var title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(title)) title = folder;

                    jumpList.JumpItems.Add(new JumpTask
                    {
                        Title            = title,
                        Description      = folder,
                        ApplicationPath  = exe,
                        Arguments        = $"--folder \"{folder}\"",
                        IconResourcePath = exe,
                        CustomCategory   = "Recent Workspaces",
                    });
                }

                JumpList.SetJumpList(Current, jumpList);
                jumpList.Apply();
            }
            catch { /* best-effort: JumpList is a convenience, not critical */ }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
            try {
                SquadDashTrace.Write("Unhandled", $"Dispatcher exception: {e.Exception}");

                if (MainWindow is null) {
                    ShowStartupFailureDialog(e.Exception);
                    e.Handled = true;
                    Shutdown();
                    return;
                }

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
            catch (Exception handlerEx) {
                SquadDashTrace.Write("Unhandled", $"App_DispatcherUnhandledException handler failed: {handlerEx.Message}");
                e.Handled = true;
            }
        }

        private void ShowStartupFailureDialog(Exception exception) {
            if (_startupFailureReported)
                return;

            _startupFailureReported = true;
            try {
                MessageBox.Show(
                    $"SquadDash could not finish starting.\n\n{exception.Message}\n\nSee the trace log for details.",
                    "SquadDash Startup Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch {
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            try {
                SquadDashTrace.Write("Unhandled", $"AppDomain exception: {e.ExceptionObject}");
                TryEmergencySave();

                if (MainWindow is MainWindow window && e.ExceptionObject is Exception exception)
                    window.ReportUnhandledUiException("AppDomain", exception);
            }
            catch (Exception handlerEx) {
                SquadDashTrace.Write("Unhandled", $"CurrentDomain_UnhandledException handler failed: {handlerEx.Message}");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
            try {
                SquadDashTrace.Write("Unhandled", $"TaskScheduler exception: {e.Exception}");

                // Swallow benign ObjectDisposedException from CancellationTokenSources that were
                // disposed while a background task still held a reference (common during doc reloads).
                var inner = e.Exception.InnerException ?? e.Exception;
                if (inner is ObjectDisposedException) {
                    e.SetObserved();
                    return;
                }

                // This handler fires on the finalizer thread — use Application.Current.Dispatcher
                // (not MainWindow) to avoid a cross-thread access that would itself crash.
                var ex = e.Exception;
                Application.Current?.Dispatcher?.BeginInvoke(() => {
                    if (Application.Current?.MainWindow is MainWindow window)
                        window.ReportUnhandledUiException("TaskScheduler", ex);
                });

                e.SetObserved();
            }
            catch (Exception handlerEx) {
                SquadDashTrace.Write("Unhandled", $"TaskScheduler_UnobservedTaskException handler failed: {handlerEx.Message}");
            }
        }

        private bool TryHandleStartupWorkspaceRouting(
            string? startupFolder,
            IWorkspacePaths workspacePaths,
            IServiceProvider serviceProvider,
            bool restartRelaunch,
            out WorkspaceOwnershipLease? startupWorkspaceLease,
            out bool noWorkspaceOnStart) {
            startupWorkspaceLease = null;
            noWorkspaceOnStart = false;

            if (!string.IsNullOrWhiteSpace(startupFolder) && !Directory.Exists(startupFolder))
                return false;

            var settingsSnapshot = serviceProvider.GetRequiredService<ApplicationSettingsStore>().Load();
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
                    if (string.IsNullOrWhiteSpace(startupFolder)) {
                        // No explicit workspace was requested — open a fresh no-folder window
                        // so the user can pick a different workspace from File > Open.
                        noWorkspaceOnStart = true;
                        SquadDashTrace.Write(
                            "Startup",
                            $"ActivatedExisting for {candidateWorkspace} but no explicit folder — opening new no-folder window.");
                        return false;
                    }
                    SquadDashTrace.Write(
                        "Startup",
                        $"Activated an existing SquadDash instance for workspace={candidateWorkspace} during startup routing.");
                    Shutdown();
                    return true;

                case WorkspaceOpenDisposition.Blocked:
                    if (string.IsNullOrWhiteSpace(startupFolder)) {
                        // No explicit workspace — silently open no-folder window rather than
                        // showing a confusing "already open" dialog.
                        noWorkspaceOnStart = true;
                        SquadDashTrace.Write(
                            "Startup",
                            $"Blocked for {candidateWorkspace} but no explicit folder — opening new no-folder window.");
                        return false;
                    }

                    if (StartupBlockedDialogPolicy.HasPendingRestartRequest(workspacePaths.ApplicationRoot)) {
                        SquadDashTrace.Write(
                            "Startup",
                            $"Blocked for {candidateWorkspace} while restart request is pending — exiting without modal dialog.");
                        Shutdown();
                        return true;
                    }

                    if (restartRelaunch &&
                        TryHandleRestartRelaunchBlockedStartup(
                            workspacePaths.ApplicationRoot,
                            candidateWorkspace,
                            out startupWorkspaceLease)) {
                        return startupWorkspaceLease is null;
                    }

                    SquadDashTrace.Write(
                        "Startup",
                        $"Blocked for {candidateWorkspace}; showing Workspace Already Open dialog.");
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

        private bool TryHandleRestartRelaunchBlockedStartup(
            string applicationRoot,
            string candidateWorkspace,
            out WorkspaceOwnershipLease? startupWorkspaceLease) {
            startupWorkspaceLease = null;

            var coordinator = new WorkspaceOpenCoordinator();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTime.UtcNow < deadline) {
                Thread.Sleep(200);

                var decision = coordinator.ReserveOrActivate(
                    applicationRoot,
                    candidateWorkspace,
                    Environment.ProcessId,
                    ProcessIdentity.GetCurrentProcessStartedAtUtcTicks());

                switch (decision.Disposition) {
                    case WorkspaceOpenDisposition.OpenHere:
                        startupWorkspaceLease = decision.Lease;
                        SquadDashTrace.Write(
                            "Startup",
                            $"Restart relaunch acquired workspace after startup contention workspace={candidateWorkspace}.");
                        return true;

                    case WorkspaceOpenDisposition.ActivatedExisting:
                        SquadDashTrace.Write(
                            "Startup",
                            $"Restart relaunch activated existing workspace owner after startup contention workspace={candidateWorkspace}.");
                        Shutdown();
                        return true;
                }
            }

            SquadDashTrace.Write(
                "Startup",
                $"Restart relaunch stayed blocked for {candidateWorkspace}; exiting without Workspace Already Open dialog.");
            Shutdown();
            return true;
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
