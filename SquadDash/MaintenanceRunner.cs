using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>Orchestrates maintenance task execution against the configured task list.</summary>
internal sealed class MaintenanceRunner {

    private readonly Func<string, CancellationToken, Task<int>> _executePromptAsync;
    private readonly MaintenanceStateStore                       _stateStore;
    private readonly Action<string>                              _onTaskStarted;
    private readonly Action<string, string, int>                 _onTaskCompleted;
    private readonly Action<MaintenanceReport>                   _onCompleted;
    private readonly Func<string, CancellationToken, Task<string?>> _getCommitShaAsync;

    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public MaintenanceRunner(
        Func<string, CancellationToken, Task<int>> executePromptAsync,
        MaintenanceStateStore                       stateStore,
        Action<string>                              onTaskStarted,
        Action<string, string, int>                 onTaskCompleted,
        Action<MaintenanceReport>                   onCompleted,
        Func<string, CancellationToken, Task<string?>>? getCommitShaAsync = null) {

        _executePromptAsync = executePromptAsync;
        _stateStore         = stateStore;
        _onTaskStarted      = onTaskStarted;
        _onTaskCompleted    = onTaskCompleted;
        _onCompleted        = onCompleted;
        _getCommitShaAsync  = getCommitShaAsync ?? TryGetCommitShaAsync;
    }

    /// <summary>
    /// Runs eligible maintenance tasks in order. Awaitable — completes when all tasks have run.
    /// </summary>
    public async Task StartAsync(
        MaintenanceMdConfig  config,
        string               workspacePath,
        CancellationToken    ct) {

        SquadInstallerService.EnsureMaintenanceStateInGitIgnore(workspacePath);
        _isRunning = true;
        var startedAt = DateTimeOffset.UtcNow;
        var ranIds     = new List<string>();
        var skippedIds = new List<string>();
        var results    = new List<MaintenanceTaskResult>();

        try {
            var tasks = config.Tasks ?? [];

            // Try to obtain the HEAD commit SHA only when an enabled per-commit task needs it.
            string? commitSha = NeedsCommitSha(tasks)
                ? await _getCommitShaAsync(workspacePath, ct).ConfigureAwait(false)
                : null;

            int runCount = 0;

            foreach (var task in tasks) {
                if (ct.IsCancellationRequested)
                    break;

                if (!task.Enabled)
                    continue;

                if (!_stateStore.IsEligible(task.Id, task.Frequency, commitSha)) {
                    skippedIds.Add(task.Id);
                    continue;
                }

                if (runCount >= config.MaxTasksPerSession)
                    break;

                _onTaskStarted(task.Title);
                runCount++;

                // Runtime safety floor check — log a warning if the global floor overrides the task's declared safety.
                var effectiveSafety = ApplySafetyFloor(config.Safety, task.Safety);
                string? safetyOverrideNote = null;
                if (!string.Equals(effectiveSafety, task.Safety, StringComparison.OrdinalIgnoreCase)) {
                    safetyOverrideNote = $"Safety downgraded from '{task.Safety}' to '{effectiveSafety}' by global floor.";
                    SquadDashTrace.Write(TraceCategory.General,
                        $"MaintenanceRunner: task '{task.Id}' safety override — declared '{task.Safety}', effective '{effectiveSafety}' (global floor '{config.Safety}').");
                }

                var taskStart = Stopwatch.GetTimestamp();
                try {
                    var prompt = BuildPrompt(task, config.Safety, startedAt);
                    var anchorIndex = await _executePromptAsync(prompt, ct).ConfigureAwait(false);

                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    _stateStore.RecordRun(task.Id, commitSha);
                    ranIds.Add(task.Id);
                    results.Add(new MaintenanceTaskResult(
                        Id:                 task.Id,
                        Title:              task.Title,
                        Outcome:            MaintenanceTaskOutcome.Completed,
                        Duration:           elapsed,
                        SafetyOverrideNote: safetyOverrideNote));
                    _onTaskCompleted(task.Id, task.Title, anchorIndex);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    ranIds.Add(task.Id);
                    results.Add(new MaintenanceTaskResult(
                        Id:                 task.Id,
                        Title:              task.Title,
                        Outcome:            MaintenanceTaskOutcome.Error,
                        Duration:           elapsed,
                        ErrorMessage:       ex.Message,
                        SafetyOverrideNote: safetyOverrideNote));
                    _onTaskCompleted(task.Id, task.Title, -1);
                }
            }

            var report = new MaintenanceReport {
                RanTaskIds     = ranIds,
                SkippedTaskIds = skippedIds,
                TaskResults    = results,
                StartedAt      = startedAt,
                CompletedAt    = DateTimeOffset.UtcNow,
            };

            _onCompleted(report);
        }
        finally {
            _isRunning = false;
        }
    }

    // ── Constants ──────────────────────────────────────────────────────────────

    private const string MaintenanceInboxReminder =
        "<maintenance_inbox_reminder>\n" +
        "You are running in maintenance mode — the user is not present. Follow these rules:\n" +
        "\n" +
        "1. Do NOT emit QUICK_REPLIES_JSON. Live quick replies require the user to be present and will block the queue.\n" +
        "\n" +
        "2. Instead, embed any decision points as deferred actions in your INBOX_MESSAGE_JSON block.\n" +
        "   Use the `actions` array so the user can make choices later when they review the message.\n" +
        "\n" +
        "3. Each action MUST have a self-contained `prompt` (except routeMode `\"done\"` which is a dismiss).\n" +
        "   Write the prompt as a complete briefing — include file paths, class names, method names, symptoms, and all\n" +
        "   context you discovered. Prefer stable identifiers (class/method names) over line numbers, which go stale.\n" +
        "   Assume the reader has NO memory of this session.\n" +
        "\n" +
        "4. For report-only tasks: send findings as an inbox message with `\"from\": \"argus-weld\"`.\n" +
        "   Subject = task title. Body = full Markdown report. Actions = any follow-up choices.\n" +
        "\n" +
        "Example actions array:\n" +
        "  \"actions\": [\n" +
        "    { \"label\": \"Fix this\", \"routeMode\": \"start_named_agent\", \"targetAgent\": \"arjun-sen\",\n" +
        "      \"prompt\": \"Arjun: during maintenance on [date] I found X in [file:line]. Please fix it. [full context]\" },\n" +
        "    { \"label\": \"Add to backlog\", \"routeMode\": \"start_coordinator\",\n" +
        "      \"prompt\": \"Add a task: [description discovered during maintenance on [date]]\" },\n" +
        "    { \"label\": \"Dismiss\", \"routeMode\": \"done\" }\n" +
        "  ]\n" +
        "</maintenance_inbox_reminder>";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildPrompt(MaintenanceTask task, string globalSafety, DateTimeOffset runDate) {
        var effectiveSafety = ApplySafetyFloor(globalSafety, task.Safety);
        var branchName      = $"maintenance/{runDate:yyyyMMdd}-{task.Id}";

        var safetyPrefix = effectiveSafety switch {
            "report-only" => "Do not modify any source files. Generate a report only.\n\n",
            "branch"      => $"Create branch `{branchName}` before making any code changes. Commit to that branch only.\n\n",
            "direct"      => "You may commit directly to the current branch.\n\n",
            _             => string.Empty,
        };

        var inboxReminder = "\n\n" + MaintenanceInboxReminder;
        return safetyPrefix + task.Instructions + inboxReminder;
    }

    private static string ApplySafetyFloor(string globalSafety, string taskSafety) {
        static int Rank(string s) => s switch {
            "report-only" => 2,
            "branch"      => 1,
            "direct"      => 0,
            _             => 0,
        };
        return Rank(globalSafety) >= Rank(taskSafety) ? globalSafety : taskSafety;
    }

    private static bool NeedsCommitSha(IEnumerable<MaintenanceTask> tasks) =>
        tasks.Any(task =>
            task.Enabled &&
            (string.Equals(task.Frequency, "after-commits", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(task.Frequency, "per-commit",    StringComparison.OrdinalIgnoreCase)));

    private static async Task<string?> TryGetCommitShaAsync(string workspacePath, CancellationToken ct) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD") {
                WorkingDirectory       = workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            try {
                var outputTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var errorTask  = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                var sha = (await outputTask.ConfigureAwait(false)).Trim();
                _ = await errorTask.ConfigureAwait(false);
                if (proc.ExitCode != 0)
                    return null;

                return string.IsNullOrEmpty(sha) ? null : sha;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                TryKillProcess(proc);
                return null;
            }
        }
        catch (OperationCanceledException) {
            return null;
        }
        catch {
            return null;
        }
    }

    private static void TryKillProcess(Process proc) {
        try {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch {
            // Best-effort cleanup only; commit SHA lookup must never fail maintenance.
        }
    }
}
