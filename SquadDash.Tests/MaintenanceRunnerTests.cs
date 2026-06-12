using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Behavioral specs for <see cref="MaintenanceRunner"/>.
/// Tests will compile once Arjun Sen's Phase 1 implementation lands.
/// </summary>
[TestFixture]
internal sealed class MaintenanceRunnerTests {

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private string _stateDir = null!;
    private string _workspaceDir = null!;

    [SetUp]
    public void SetUp() {
        var root = TestContext.CurrentContext.WorkDirectory;
        _stateDir    = Path.Combine(root, $"maint_runner_state_{Guid.NewGuid():N}");
        _workspaceDir = Path.Combine(root, $"maint_runner_ws_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stateDir);
        Directory.CreateDirectory(_workspaceDir);
    }

    [TearDown]
    public void TearDown() {
        foreach (var dir in new[] { _stateDir, _workspaceDir }) {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    // ── Builder helpers ───────────────────────────────────────────────────────

    private static MaintenanceMdConfig MakeConfig(
        IReadOnlyList<MaintenanceTask>? tasks = null,
        int maxTasksPerSession = 10,
        string safety = "branch") =>
        new(
            IdleTimeout: 15,
            MaxTasksPerSession: maxTasksPerSession,
            Safety: safety,
            Tasks: tasks ?? []);

    private static MaintenanceTask MakeTask(
        string id,
        bool enabled = true,
        string frequency = "always",
        string safety = "branch",
        string title = "",
        string instructions = "Do work.") =>
        new(id, enabled, frequency, safety, title.Length > 0 ? title : id, instructions);

    private MaintenanceRunner MakeRunner(
        Func<string, CancellationToken, Task<int>>? executePromptAsync = null,
        MaintenanceStateStore? stateStore = null,
        Action<string>? onTaskStarted = null,
        Action<string, string, int, DateTimeOffset, TimeSpan>? onTaskCompleted = null,
        Action<MaintenanceReport>? onCompleted = null,
        Func<string, CancellationToken, Task<string?>>? getCommitShaAsync = null,
        Func<DateTimeOffset, bool>? wasInboxSavedSince = null) {
        return new MaintenanceRunner(
            executePromptAsync: executePromptAsync ?? ((_, _) => Task.FromResult(-1)),
            stateStore:         stateStore ?? new MaintenanceStateStore(_stateDir),
            onTaskStarted:      onTaskStarted  ?? (_ => { }),
            onTaskCompleted:    onTaskCompleted ?? ((_, _, _, _, _) => { }),
            onCompleted:        onCompleted    ?? (_ => { }),
            getCommitShaAsync:  getCommitShaAsync,
            wasInboxSavedSince: wasInboxSavedSince);
    }

    // ── Skips disabled tasks ──────────────────────────────────────────────────

    [Test]
    public async Task StartAsync_SkipsDisabledTasks() {
        var executed = new List<string>();
        var config = MakeConfig([
            MakeTask("disabled-task", enabled: false),
            MakeTask("enabled-task",  enabled: true),
        ]);

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { executed.Add(prompt); return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(executed, Has.Count.EqualTo(1), "Only the enabled task should be executed");
    }

    [Test]
    public async Task StartAsync_DoesNotResolveCommitSha_WhenNoEnabledPerCommitTasks() {
        var commitShaCalls = 0;
        var config = MakeConfig([
            MakeTask("disabled-per-commit", enabled: false, frequency: "per-commit"),
            MakeTask("daily-task", enabled: true, frequency: "daily"),
        ]);

        var runner = MakeRunner(
            getCommitShaAsync: (_, _) => {
                commitShaCalls++;
                return Task.FromResult<string?>("abc123");
            });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(commitShaCalls, Is.Zero,
            "Commit SHA lookup should not run unless an enabled per-commit task needs it");
    }

    [Test]
    public async Task StartAsync_ResolvesCommitSha_ForEnabledPerCommitTasks() {
        var commitShaCalls = 0;
        var config = MakeConfig([
            MakeTask("per-commit-task", enabled: true, frequency: "per-commit"),
        ]);

        var runner = MakeRunner(
            getCommitShaAsync: (_, _) => {
                commitShaCalls++;
                return Task.FromResult<string?>("abc123");
            });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(commitShaCalls, Is.EqualTo(1),
            "Enabled per-commit tasks need the current HEAD SHA for eligibility");
    }

    [Test]
    public async Task StartAsync_ResolvesCommitSha_ForEnabledAfterCommitsTasks() {
        var commitShaCalls = 0;
        var config = MakeConfig([
            MakeTask("after-commits-task", enabled: true, frequency: "after-commits"),
        ]);

        var runner = MakeRunner(
            getCommitShaAsync: (_, _) => {
                commitShaCalls++;
                return Task.FromResult<string?>("abc123");
            });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(commitShaCalls, Is.EqualTo(1),
            "Enabled after-commits tasks need the current HEAD SHA for eligibility");
    }

    // ── Skips ineligible tasks ────────────────────────────────────────────────

    [Test]
    public async Task StartAsync_SkipsIneligibleTasks() {
        const string taskId = "ran-today";
        var stateStore = new MaintenanceStateStore(_stateDir);
        stateStore.RecordRun(taskId, commitSha: null);

        var executed = new List<string>();
        var config = MakeConfig([
            MakeTask(taskId, frequency: "daily"),
            MakeTask("always-task", frequency: "always"),
        ]);

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { executed.Add(prompt); return Task.FromResult(-1); },
            stateStore: stateStore);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(executed, Has.Count.EqualTo(1), "The daily task run today must be skipped");
    }

    // ── Runs eligible tasks in order ─────────────────────────────────────────

    [Test]
    public async Task StartAsync_RunsEligibleTasksInOrder() {
        var order = new List<string>();
        var config = MakeConfig([
            MakeTask("task-a", instructions: "Instructions A"),
            MakeTask("task-b", instructions: "Instructions B"),
            MakeTask("task-c", instructions: "Instructions C"),
        ]);

        var runner = MakeRunner(
            onTaskStarted: title => order.Add(title));

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(order, Is.EqualTo(new[] { "task-a", "task-b", "task-c" }),
            "Tasks must be started in the order they appear in the config");
    }

    // ── Respects MaxTasksPerSession ───────────────────────────────────────────

    [Test]
    public async Task StartAsync_RespectsMaxTasksPerSessionCap() {
        var started = new List<string>();
        var config = MakeConfig(
            tasks: [
                MakeTask("t1"), MakeTask("t2"), MakeTask("t3"),
                MakeTask("t4"), MakeTask("t5"), MakeTask("t6"),
            ],
            maxTasksPerSession: 3);

        var runner = MakeRunner(onTaskStarted: id => started.Add(id));

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(started, Has.Count.EqualTo(3),
            "Runner must stop after MaxTasksPerSession tasks regardless of how many are eligible");
    }

    // ── RecordRun called after each success ───────────────────────────────────

    [Test]
    public async Task StartAsync_CallsRecordRunAfterEachSuccessfulTask() {
        var stateStore = new MaintenanceStateStore(_stateDir);
        var config = MakeConfig([
            MakeTask("rec-task-1", frequency: "daily"),
            MakeTask("rec-task-2", frequency: "daily"),
        ]);

        var runner = MakeRunner(stateStore: stateStore);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        // After running both daily tasks they must now be ineligible.
        Assert.That(await stateStore.IsEligibleAsync("rec-task-1", "daily", null), Is.False,
            "rec-task-1 must be marked as run");
        Assert.That(await stateStore.IsEligibleAsync("rec-task-2", "daily", null), Is.False,
            "rec-task-2 must be marked as run");
    }

    // ── Cancellation token interrupt ──────────────────────────────────────────

    [Test]
    public async Task StartAsync_CancellationToken_StopsAfterCurrentTask() {
        var startedIds = new List<string>();
        using var cts = new CancellationTokenSource();

        // Cancel after the first task begins.
        var firstTaskTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var config = MakeConfig([
            MakeTask("cancel-task-1"),
            MakeTask("cancel-task-2"),
        ]);

        var runner = MakeRunner(
            executePromptAsync: async (_, ct) => {
                firstTaskTcs.TrySetResult();
                await Task.Delay(50, ct);
                return -1;
            },
            onTaskStarted: id => {
                startedIds.Add(id);
                if (id == "cancel-task-1")
                    cts.Cancel();
            });

        await runner.StartAsync(config, _workspaceDir, cts.Token).ConfigureAwait(false);

        Assert.That(startedIds, Does.Not.Contain("cancel-task-2"),
            "Runner must not start the next task after cancellation");
    }

    // ── onTaskStarted callback ────────────────────────────────────────────────

    [Test]
    public async Task StartAsync_OnTaskStarted_FiredWithTaskTitle() {
        var titles = new List<string>();
        var config = MakeConfig([
            MakeTask("greet-task", title: "Say Hello"),
        ]);

        var runner = MakeRunner(onTaskStarted: title => titles.Add(title));

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(titles, Has.Count.EqualTo(1));
        Assert.That(titles[0], Is.EqualTo("Say Hello"),
            "onTaskStarted must receive the task title");
    }

    // ── onTaskCompleted callback ──────────────────────────────────────────────

    [Test]
    public async Task StartAsync_OnTaskCompleted_FiredWithTaskId() {
        var completedIds = new List<string>();
        var config = MakeConfig([
            MakeTask("complete-me"),
        ]);

        var runner = MakeRunner(onTaskCompleted: (id, _, _, _, _) => completedIds.Add(id));

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(completedIds, Has.Count.EqualTo(1));
        Assert.That(completedIds[0], Is.EqualTo("complete-me"),
            "onTaskCompleted must receive the task id");
    }

    // ── onCompleted (MaintenanceReport) ──────────────────────────────────────

    [Test]
    public async Task StartAsync_OnCompleted_FiredWithRunAndSkippedTaskLists() {
        MaintenanceReport? report = null;

        var stateStore = new MaintenanceStateStore(_stateDir);
        stateStore.RecordRun("skipped-daily", commitSha: null);

        var config = MakeConfig([
            MakeTask("run-task",     frequency: "always"),
            MakeTask("skipped-daily", frequency: "daily"),
        ]);

        var runner = MakeRunner(
            stateStore: stateStore,
            onCompleted: r => report = r);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(report, Is.Not.Null, "onCompleted must be invoked");
        Assert.Multiple(() => {
            Assert.That(report!.RanTaskIds,     Does.Contain("run-task"));
            Assert.That(report.SkippedTaskIds,  Does.Contain("skipped-daily"));
        });
    }

    // ── Safety prompt injection — report-only ─────────────────────────────────

    [Test]
    public async Task StartAsync_ReportOnlyTask_PromptContainsReportOnlyPrefix() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("analysis", safety: "report-only", instructions: "Analyse the diff.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("report").IgnoreCase.Or.Contains("read-only").IgnoreCase,
            "report-only safety must inject a read-only / reporting prefix into the prompt");
        Assert.That(capturedPrompt, Does.Not.Contain("direct commit").IgnoreCase,
            "report-only prompt must not instruct the agent to commit directly");
    }

    [Test]
    public async Task StartAsync_ReportOnlyTask_PromptWarnsInboxBlockMustNotBeFenced() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("analysis", safety: "report-only", instructions: "Analyse the diff.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("do not wrap it in markdown code fences").IgnoreCase);
    }

    // ── Safety prompt injection — branch ─────────────────────────────────────

    [Test]
    public async Task StartAsync_BranchTask_PromptContainsBranchInstruction() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("refactor", safety: "branch", instructions: "Refactor the module.")],
            safety: "branch");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("branch").IgnoreCase,
            "branch safety must inject a branch-creation instruction into the prompt");
    }

    // ── Safety prompt injection — direct ──────────────────────────────────────

    [Test]
    public async Task StartAsync_DirectTask_PromptContainsDirectCommitInstruction() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("cleanup", safety: "direct", instructions: "Clean up logs.")],
            safety: "direct");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("commit directly").IgnoreCase,
            "direct safety must inject a 'commit directly' instruction into the prompt");
    }

    // ── Safety floor — global report-only overrides per-task branch ───────────

    [Test]
    public async Task StartAsync_SafetyFloor_GlobalReportOnlyOverridesPerTaskBranch() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("analysis", safety: "branch", instructions: "Analyse the diff.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("report").IgnoreCase,
            "global report-only must override per-task branch and inject report prefix");
        Assert.That(capturedPrompt, Does.Not.Contain("Create branch").IgnoreCase,
            "report-only floor must suppress the branch-creation instruction");
    }

    // ── Safety floor — global report-only overrides per-task direct ───────────

    [Test]
    public async Task StartAsync_SafetyFloor_GlobalReportOnlyOverridesPerTaskDirect() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "direct", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("report").IgnoreCase,
            "global report-only must override per-task direct and inject report prefix");
    }

    // ── Safety floor — global branch overrides per-task direct ───────────────

    [Test]
    public async Task StartAsync_SafetyFloor_GlobalBranchOverridesPerTaskDirect() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("refactor", safety: "direct", instructions: "Refactor module.")],
            safety: "branch");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("branch").IgnoreCase,
            "global branch must override per-task direct and inject branch instruction");
    }

    // ── Branch name includes date and task slug ───────────────────────────────

    [Test]
    public async Task StartAsync_BranchTask_PromptContainsBranchNameWithDateAndSlug() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("my-task", safety: "branch", instructions: "Do the work.")],
            safety: "branch");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        var expectedDate = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        Assert.That(capturedPrompt, Does.Contain("maintenance/").IgnoreCase,
            "branch prompt must contain the 'maintenance/' prefix");
        Assert.That(capturedPrompt, Does.Contain("my-task"),
            "branch prompt must contain the task slug");
        Assert.That(capturedPrompt, Does.Contain(expectedDate),
            "branch prompt must contain today's date in yyyyMMdd format");
    }

    // ── Safety override note ──────────────────────────────────────────────────

    [Test]
    public async Task StartAsync_SafetyOverride_NoteRecordedInResult_WhenFloorApplies() {
        MaintenanceReport? report = null;
        var config = MakeConfig(
            tasks: [MakeTask("override-task", safety: "direct", instructions: "Do work.")],
            safety: "branch");

        var runner = MakeRunner(onCompleted: r => report = r);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.TaskResults[0].SafetyOverrideNote, Is.Not.Null.And.Not.Empty,
            "SafetyOverrideNote must be set when the global floor overrides the task's declared safety");
        Assert.That(report.TaskResults[0].SafetyOverrideNote, Does.Contain("branch"),
            "SafetyOverrideNote must mention the effective safety level");
    }

    [Test]
    public async Task StartAsync_SafetyOverride_NoNote_WhenNoFloorApplied() {
        MaintenanceReport? report = null;
        var config = MakeConfig(
            tasks: [MakeTask("no-override-task", safety: "branch", instructions: "Do work.")],
            safety: "branch");

        var runner = MakeRunner(onCompleted: r => report = r);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.TaskResults[0].SafetyOverrideNote, Is.Null.Or.Empty,
            "SafetyOverrideNote must be null when the task's declared safety matches the effective safety");
    }

    [Test]
    public async Task StartAsync_SafetyOverride_NoteIncluded_WhenGlobalReportOnlyOverridesDirect() {
        MaintenanceReport? report = null;
        var config = MakeConfig(
            tasks: [MakeTask("report-only-override", safety: "direct", instructions: "Do work.")],
            safety: "report-only");

        var runner = MakeRunner(onCompleted: r => report = r);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.TaskResults[0].SafetyOverrideNote, Is.Not.Null.And.Not.Empty,
            "SafetyOverrideNote must be set when report-only global floor overrides direct task safety");
    }

    // ── forceTaskIds — "Run Now" single-task behaviour ───────────────────────

    [Test]
    public async Task StartAsync_ForceTaskIds_RunsOnlyForcedTask_SkipsOtherEligibleTasks() {
        var started = new List<string>();
        var config = MakeConfig([
            MakeTask("task-a", frequency: "always"),
            MakeTask("task-b", frequency: "always"),
            MakeTask("task-c", frequency: "always"),
        ]);

        var runner = MakeRunner(onTaskStarted: id => started.Add(id));

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None,
            forceTaskIds: new HashSet<string> { "task-b" });

        Assert.That(started, Is.EqualTo(new[] { "task-b" }),
            "Only the forced task must run; other eligible tasks must be skipped");
    }

    [Test]
    public async Task StartAsync_ForceTaskIds_RunsForcedTask_EvenWhenIneligible() {
        const string taskId = "ran-today";
        var stateStore = new MaintenanceStateStore(_stateDir);
        stateStore.RecordRun(taskId, commitSha: null);

        var started = new List<string>();
        var config = MakeConfig([
            MakeTask(taskId, frequency: "daily"),
        ]);

        var runner = MakeRunner(
            onTaskStarted: id => started.Add(id),
            stateStore: stateStore);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None,
            forceTaskIds: new HashSet<string> { taskId });

        Assert.That(started, Does.Contain(taskId),
            "A forced task must run even if it is not yet eligible");
    }

    [Test]
    public async Task StartAsync_ForceTaskIds_SkippedTasksInReport_ContainNonForcedTasks() {
        MaintenanceReport? report = null;
        var config = MakeConfig([
            MakeTask("forced-task",  frequency: "always"),
            MakeTask("skipped-task", frequency: "always"),
        ]);

        var runner = MakeRunner(onCompleted: r => report = r);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None,
            forceTaskIds: new HashSet<string> { "forced-task" });

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.RanTaskIds,     Does.Contain("forced-task"));
        Assert.That(report.SkippedTaskIds,  Does.Contain("skipped-task"),
            "Non-forced tasks must appear in SkippedTaskIds even when normally eligible");
    }

    [Test]
    public async Task StartAsync_ForceTaskIds_Empty_RunsAllEligibleTasksNormally() {
        var started = new List<string>();
        var config = MakeConfig([
            MakeTask("task-x", frequency: "always"),
            MakeTask("task-y", frequency: "always"),
        ]);

        var runner = MakeRunner(onTaskStarted: id => started.Add(id));

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None,
            forceTaskIds: new HashSet<string>());

        Assert.That(started, Is.EqualTo(new[] { "task-x", "task-y" }),
            "An empty forceTaskIds set must behave identically to null (all eligible tasks run)");
    }

    [Test]
    public async Task StartAsync_ForceTaskIds_PerCommitTask_DoesNotFetchCommitSha_WhenForcedTaskIsNotPerCommit() {
        var commitShaCalls = 0;
        var config = MakeConfig([
            MakeTask("per-commit-task", enabled: true, frequency: "per-commit"),
            MakeTask("daily-task",      enabled: true, frequency: "daily"),
        ]);

        var runner = MakeRunner(
            getCommitShaAsync: (_, _) => {
                commitShaCalls++;
                return Task.FromResult<string?>("abc123");
            });

        // Force only the daily task — the per-commit task is not in scope, so no SHA needed.
        await runner.StartAsync(config, _workspaceDir, CancellationToken.None,
            forceTaskIds: new HashSet<string> { "daily-task" });

        Assert.That(commitShaCalls, Is.Zero,
            "Commit SHA must not be fetched when the forced task does not require it");
    }

    // ── IsRunning ────────────────────────────────────────────────────────────

    [Test]
    public async Task IsRunning_TrueWhileRunning_FalseAfterComplete() {
        bool? isRunningDuringTask = null;
        var config = MakeConfig([MakeTask("running-probe")]);

        MaintenanceRunner? runner = null;
        runner = MakeRunner(
            executePromptAsync: (_, _) => {
                isRunningDuringTask = runner!.IsRunning;
                return Task.FromResult(-1);
            });

        Assert.That(runner.IsRunning, Is.False, "IsRunning must be false before StartAsync");

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(isRunningDuringTask, Is.True,  "IsRunning must be true while a task executes");
        Assert.That(runner.IsRunning,    Is.False,  "IsRunning must be false after completion");
    }

    // ── SubstituteOptions — conditional directives ────────────────────────────

    [Test]
    public void SubstituteOptions_IfBlock_IncludesMatchingBranch() {
        var opts = new[] {
            new MaintenanceOption("if_found", "fix", "radio", null, null, null),
        };
        var instructions = """
            Preamble.
            {{#if if_found == "fix"}}
            Apply the fix.
            {{/if}}
            {{#if if_found == "report"}}
            Write a report.
            {{/if}}
            Postamble.
            """;
        var result = MaintenanceRunner.SubstituteOptions(instructions, opts);
        Assert.That(result, Does.Contain("Apply the fix."));
        Assert.That(result, Does.Not.Contain("Write a report."));
        Assert.That(result, Does.Contain("Preamble."));
        Assert.That(result, Does.Contain("Postamble."));
    }

    [Test]
    public void SubstituteOptions_IfBlock_ExcludesNonMatchingBranch() {
        var opts = new[] {
            new MaintenanceOption("if_found", "report", "radio", null, null, null),
        };
        var instructions = """
            {{#if if_found == "fix"}}
            Apply the fix.
            {{/if}}
            {{#if if_found == "report"}}
            Write a report.
            {{/if}}
            """;
        var result = MaintenanceRunner.SubstituteOptions(instructions, opts);
        Assert.That(result, Does.Not.Contain("Apply the fix."));
        Assert.That(result, Does.Contain("Write a report."));
    }

    [Test]
    public void SubstituteOptions_UnlessBlock_ExcludesMatchingValue() {
        var opts = new[] {
            new MaintenanceOption("if_found", "fix", "radio", null, null, null),
        };
        var instructions = """
            {{#unless if_found == "fix"}}
            This should be hidden.
            {{/unless}}
            Always shown.
            """;
        var result = MaintenanceRunner.SubstituteOptions(instructions, opts);
        Assert.That(result, Does.Not.Contain("This should be hidden."));
        Assert.That(result, Does.Contain("Always shown."));
    }

    [Test]
    public void SubstituteOptions_PlainPlaceholder_StillSubstituted() {
        var opts = new[] {
            new MaintenanceOption("if_found", "branch", "radio", null, null, null),
        };
        var instructions = "Mode is {{if_found}}.";
        var result = MaintenanceRunner.SubstituteOptions(instructions, opts);
        Assert.That(result, Is.EqualTo("Mode is branch."));
    }

    [Test]
    public void SubstituteOptions_NullOptions_ReturnsInstructionsUnchanged() {
        const string instructions = "Do the thing.";
        var result = MaintenanceRunner.SubstituteOptions(instructions, null);
        Assert.That(result, Is.EqualTo(instructions));
    }

    [Test]
    public void SubstituteOptions_ThreeBranchOptions_OnlySelectedBranchAppears() {
        var opts = new[] {
            new MaintenanceOption("if_found", "branch", "radio", null, null, null),
        };
        var instructions = """
            Found issues.
            {{#if if_found == "fix"}}
            Fix inline.
            {{/if}}
            {{#if if_found == "branch"}}
            Create a maintenance branch.
            {{/if}}
            {{#if if_found == "report"}}
            Send report to Inbox.
            {{/if}}
            Done.
            """;
        var result = MaintenanceRunner.SubstituteOptions(instructions, opts);
        Assert.That(result, Does.Not.Contain("Fix inline."));
        Assert.That(result, Does.Contain("Create a maintenance branch."));
        Assert.That(result, Does.Not.Contain("Send report to Inbox."));
    }

    // ── Fix 1: stronger report-only prompt ───────────────────────────────────

    [Test]
    public async Task StartAsync_ReportOnlyTask_PromptStartsWithInboxPreamble() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.StartWith("⚠️ INBOX REQUIREMENT"),
            "The INBOX requirement block must appear at the very top of the prompt");
    }

    [Test]
    public async Task StartAsync_ReportOnlyTask_PromptEndsWithMandatoryChecklist() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("FINAL CHECKLIST").IgnoreCase,
            "A mandatory checklist must be appended at the end of report-only prompts");
        Assert.That(capturedPrompt, Does.EndWith("DO NOT END WITH ANYTHING ELSE."),
            "The very last text must be the mandatory closing reminder");
    }

    [Test]
    public async Task StartAsync_ReportOnlyTask_PromptContainsInboxTemplateExample() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Contain("\"from\": \"argus-weld\""),
            "The prompt must include a filled-in template skeleton so the model sees the expected shape");
        Assert.That(capturedPrompt, Does.Contain("INBOX_MESSAGE_JSON:"),
            "The prompt must show the exact INBOX_MESSAGE_JSON keyword in the template");
    }

    [Test]
    public async Task StartAsync_BranchTask_PromptDoesNotContainInboxPreambleOrChecklist() {
        var capturedPrompt = string.Empty;
        var config = MakeConfig(
            tasks: [MakeTask("refactor", safety: "branch", instructions: "Refactor module.")],
            safety: "branch");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { capturedPrompt = prompt; return Task.FromResult(-1); });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(capturedPrompt, Does.Not.Contain("INBOX REQUIREMENT"),
            "Non-report-only tasks must not include the INBOX preamble");
        Assert.That(capturedPrompt, Does.Not.Contain("FINAL CHECKLIST").IgnoreCase,
            "Non-report-only tasks must not include the mandatory checklist");
    }

    // ── Fix 2: post-completion inbox fallback ─────────────────────────────────

    [Test]
    public async Task StartAsync_ReportOnlyTask_SendsRecoveryPrompt_WhenInboxNotSaved() {
        var prompts = new List<string>();
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { prompts.Add(prompt); return Task.FromResult(-1); },
            wasInboxSavedSince: _ => false);   // simulate: no inbox message was saved

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(prompts, Has.Count.EqualTo(2),
            "Runner must send a recovery prompt when no inbox message was saved after a report-only task");
        Assert.That(prompts[1], Does.Contain("INBOX_MESSAGE_JSON").IgnoreCase,
            "The recovery prompt must ask the model to emit an INBOX_MESSAGE_JSON block");
        Assert.That(prompts[1], Does.Contain("no").IgnoreCase.Or.Contain("not").IgnoreCase,
            "The recovery prompt must explain why recovery is needed");
    }

    [Test]
    public async Task StartAsync_ReportOnlyTask_NoRecoveryPrompt_WhenInboxWasSaved() {
        var prompts = new List<string>();
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { prompts.Add(prompt); return Task.FromResult(-1); },
            wasInboxSavedSince: _ => true);    // simulate: inbox message was saved successfully

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(prompts, Has.Count.EqualTo(1),
            "Runner must NOT send a recovery prompt when the inbox message was already saved");
    }

    [Test]
    public async Task StartAsync_ReportOnlyTask_NoRecoveryPrompt_WhenCallbackNotProvided() {
        var prompts = new List<string>();
        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { prompts.Add(prompt); return Task.FromResult(-1); },
            wasInboxSavedSince: null);

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(prompts, Has.Count.EqualTo(1),
            "Runner must not attempt recovery when wasInboxSavedSince was not provided");
    }

    [Test]
    public async Task StartAsync_BranchTask_NoRecoveryPrompt_EvenWhenCallbackReturnsFalse() {
        var prompts = new List<string>();
        var config = MakeConfig(
            tasks: [MakeTask("refactor", safety: "branch", instructions: "Refactor module.")],
            safety: "branch");

        var runner = MakeRunner(
            executePromptAsync: (prompt, _) => { prompts.Add(prompt); return Task.FromResult(-1); },
            wasInboxSavedSince: _ => false);   // callback returns false but task is not report-only

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(prompts, Has.Count.EqualTo(1),
            "Recovery prompt must only fire for report-only tasks");
    }

    [Test]
    public async Task StartAsync_RecoveryPrompt_PassesCorrectTaskStartTimestampToCallback() {
        DateTimeOffset? callbackTimestamp = null;
        var beforeStart = DateTimeOffset.UtcNow;

        var config = MakeConfig(
            tasks: [MakeTask("scan", safety: "report-only", instructions: "Scan for issues.")],
            safety: "report-only");

        var runner = MakeRunner(
            executePromptAsync: (_, _) => Task.FromResult(-1),
            wasInboxSavedSince: ts => { callbackTimestamp = ts; return true; });

        await runner.StartAsync(config, _workspaceDir, CancellationToken.None);

        Assert.That(callbackTimestamp, Is.Not.Null,
            "wasInboxSavedSince must be called with the task start timestamp");
        Assert.That(callbackTimestamp!.Value, Is.GreaterThanOrEqualTo(beforeStart),
            "Timestamp passed to wasInboxSavedSince must be at or after the task started");
    }

    // ── StripPreambleForDisplay ───────────────────────────────────────────────

    // Read the exact private constants via reflection so the tests never drift from
    // production values — any change to the constants is automatically reflected here.
    private static string RealInboxPreamble =>
        (string)typeof(MaintenanceRunner)
            .GetField("ReportOnlyInboxPreamble",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    private static string RealInboxReminder =>
        (string)typeof(MaintenanceRunner)
            .GetField("MaintenanceInboxReminder",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    [Test]
    public void StripPreambleForDisplay_PlainText_ReturnedUnchanged() {
        const string prompt = "Scan all TODO comments and report findings.";
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Is.EqualTo(prompt));
    }

    [Test]
    public void StripPreambleForDisplay_EmptyString_ReturnsEmpty() {
        var result = MaintenanceRunner.StripPreambleForDisplay(string.Empty);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void StripPreambleForDisplay_WhitespaceOnly_ReturnsEmpty() {
        var result = MaintenanceRunner.StripPreambleForDisplay("   \n\n   ");
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void StripPreambleForDisplay_InboxPreamblePrefix_IsStripped() {
        var prompt = RealInboxPreamble + "Do not modify source files. Scan for issues.";
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Does.Not.Contain("INBOX REQUIREMENT"),
            "The inbox preamble header must not appear in the stripped output");
        Assert.That(result, Does.Contain("Scan for issues."),
            "The actual task instructions must be preserved after stripping");
    }

    [Test]
    public void StripPreambleForDisplay_InboxPreamblePrefix_LeavesNoLeadingWhitespace() {
        var prompt = RealInboxPreamble + "  Do not modify source files. Scan for issues.";
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Does.Not.StartWith(" "),
            "The result must be trimmed — no leading whitespace after preamble removal");
    }

    [Test]
    public void StripPreambleForDisplay_InboxReminderBlock_IsStripped() {
        var prompt = "Check for security issues.\n\n" + RealInboxReminder;
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Does.Not.Contain("<maintenance_inbox_reminder>"),
            "The maintenance inbox reminder tag must not appear in the stripped output");
        Assert.That(result, Does.Contain("Check for security issues."),
            "The task instructions before the reminder must be preserved");
    }

    [Test]
    public void StripPreambleForDisplay_InboxReminderBlock_EverythingAfterItIsAlsoStripped() {
        var prompt = "Find all dead code.\n\n" + RealInboxReminder + "\n\nSome trailing content that must vanish.";
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Does.Not.Contain("trailing content"),
            "Everything after the maintenance inbox reminder must be removed");
    }

    [Test]
    public void StripPreambleForDisplay_BothPreambleAndReminder_BothStripped() {
        var prompt = RealInboxPreamble + "Do not modify source files. Audit dependencies.\n\n" + RealInboxReminder;
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Does.Not.Contain("INBOX REQUIREMENT"),
            "Inbox preamble must be stripped when both elements are present");
        Assert.That(result, Does.Not.Contain("<maintenance_inbox_reminder>"),
            "Inbox reminder must be stripped when both elements are present");
        Assert.That(result, Does.Contain("Audit dependencies."),
            "The task instructions sandwiched between preamble and reminder must be preserved");
    }

    [Test]
    public void StripPreambleForDisplay_PreambleAppearsInMiddle_NotStripped() {
        // The method only strips the preamble when it is a prefix — mid-string occurrence must survive.
        const string preambleFragment = "⚠️ INBOX REQUIREMENT — READ THIS FIRST ⚠️\n";
        var prompt = "Some instructions. " + preambleFragment + " More instructions.";
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Does.Contain("INBOX REQUIREMENT"),
            "A preamble-like string that is not a prefix must not be stripped");
        Assert.That(result, Does.Contain("Some instructions."),
            "Text before a mid-string preamble look-alike must be preserved");
    }

    [Test]
    public void StripPreambleForDisplay_PreambleAloneWithNoTaskInstructions_ReturnsEmpty() {
        var result = MaintenanceRunner.StripPreambleForDisplay(RealInboxPreamble);
        Assert.That(result, Is.EqualTo(string.Empty),
            "Stripping a prompt that is only the preamble must yield an empty string");
    }

    [Test]
    public void StripPreambleForDisplay_ResultIsAlwaysTrimmed() {
        var prompt = RealInboxPreamble + "   Audit the codebase.   ";
        var result = MaintenanceRunner.StripPreambleForDisplay(prompt);
        Assert.That(result, Is.EqualTo("Audit the codebase."),
            "The result must be trimmed of leading and trailing whitespace");
    }
}
