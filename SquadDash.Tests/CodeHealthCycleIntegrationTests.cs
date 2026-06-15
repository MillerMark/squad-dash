using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// End-to-end integration test for the full maintenance pipeline:
/// idle threshold → runner picks eligible task → executePromptAsync called →
/// MaintenanceTaskResult recorded → report written → banner-triggered event fires.
/// </summary>
[TestFixture]
internal sealed class MaintenanceCycleIntegrationTests {

    private TestWorkspace _workspace = null!;
    private string _workspaceDir = null!;
    private string _stateDir     = null!;

    [SetUp]
    public void SetUp() {
        _workspace    = new TestWorkspace();
        _workspaceDir = _workspace.GetPath("workspace");
        _stateDir     = _workspace.GetPath("state");
        Directory.CreateDirectory(_workspaceDir);
        Directory.CreateDirectory(_stateDir);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public async Task FullMaintenanceCycle_ForceIdleThreshold_WritesReport_RaisesEvent_UpdatesStateStore() {
        // ── Arrange ───────────────────────────────────────────────────────────
        const string taskId           = "lint-check";
        const string taskTitle        = "Run Linter";
        const string taskInstructions = "Run the linter on all source files.";

        var config = new MaintenanceMdConfig(
            IdleTimeout:        15,
            MaxTasksPerSession: 5,
            Safety:             "report-only",
            Tasks: [
                new MaintenanceTask(
                    Id:           taskId,
                    Enabled:      true,
                    Frequency:    "always",
                    Safety:       "report-only",
                    Title:        taskTitle,
                    Instructions: taskInstructions),
            ]);

        var stateStore   = new MaintenanceStateStore(_stateDir);
        var reportWriter = new MaintenanceReportWriter(_workspaceDir);

        // Stub: captures the prompt text passed to executePromptAsync
        string? capturedPrompt = null;
        Task<int> ExecutePromptStub(string prompt, CancellationToken ct) {
            capturedPrompt = prompt;
            return Task.FromResult(-1);
        }

        // onCompleted mirrors what MainWindow does: write report then surface the banner event
        MaintenanceReport? bannerReport = null;
        void OnCompleted(MaintenanceReport report) {
            reportWriter.WriteReport(report);
            bannerReport = report;
        }

        var runner = new MaintenanceRunner(
            executePromptAsync: ExecutePromptStub,
            stateStore:         stateStore,
            onTaskStarted:      _ => { },
            onTaskCompleted:    (_, _, _, _, _) => { },
            onCompleted:        OnCompleted);

        // Fake IdleDetectionService: ForceIdle() fires IdleThresholdReached synchronously
        // when no prompt/loop/runner is active, simulating the user being idle.
        Task? runnerTask = null;
        var idleService = new IdleDetectionService();
        idleService.IdleThresholdReached += () => {
            runnerTask = runner.StartAsync(config, _workspaceDir, CancellationToken.None);
        };

        idleService.Start(thresholdMinutes: 0.001);
        idleService.ForceIdle();   // fires IdleThresholdReached immediately

        // ── Act ───────────────────────────────────────────────────────────────
        Assert.That(runnerTask, Is.Not.Null, "IdleThresholdReached must have triggered StartAsync");
        await runnerTask!;

        // ── Assert 1: report file written ─────────────────────────────────────
        var reportPaths = reportWriter.GetReportPaths();
        Assert.That(reportPaths, Has.Count.EqualTo(1),
            "Exactly one report file must be written after the cycle");
        Assert.That(File.Exists(reportPaths[0]), Is.True,
            "Report file must exist on disk");

        // ── Assert 2: banner-triggered event fired ────────────────────────────
        Assert.That(bannerReport, Is.Not.Null,
            "onCompleted (banner-triggered event) must have been raised");
        Assert.That(bannerReport!.RanTaskIds, Does.Contain(taskId),
            "Report must list the task as ran");
        Assert.That(bannerReport.TaskResults, Has.Count.EqualTo(1),
            "Report must contain exactly one task result");
        Assert.That(bannerReport.TaskResults[0].Outcome, Is.EqualTo(MaintenanceTaskOutcome.Completed),
            "Task outcome must be Completed");

        // ── Assert 3: state store updated ─────────────────────────────────────
        var lastRunAt = stateStore.GetLastRunAt(taskId);
        Assert.That(lastRunAt, Is.Not.Null,
            "State store must record a run timestamp for the executed task");
        Assert.That(lastRunAt!.Value.Date, Is.EqualTo(DateTime.UtcNow.Date),
            "Recorded run timestamp must be today (UTC)");

        // ── Assert 4: correct prompt delivered ────────────────────────────────
        Assert.That(capturedPrompt, Is.Not.Null,
            "executePromptAsync stub must have been called");
        Assert.That(capturedPrompt, Does.Contain(taskInstructions),
            "Prompt passed to executePromptAsync must contain the task instructions");
    }
}
