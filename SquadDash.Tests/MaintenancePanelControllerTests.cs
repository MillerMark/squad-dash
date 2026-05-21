using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Integration tests for <see cref="MaintenancePanelController.Refresh"/>.
/// All tests run on a dedicated STA thread via <see cref="WpfTestContext"/> because
/// WPF controls require an STA apartment.
/// </summary>
[TestFixture]
internal sealed class MaintenancePanelControllerTests {

    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (MaintenancePanelController controller,
                    StackPanel listPanel,
                    TextBlock  statusLabel,
                    Button     runNowButton)
        CreateController() {
        var listPanel              = new StackPanel();
        var statusLabel            = new TextBlock();
        var runNowButton           = new Button();
        var enabledOnIdleCheckBox  = new CheckBox();
        var controller   = new MaintenancePanelController(
            listPanel,
            statusLabel,
            runNowButton,
            enabledOnIdleCheckBox,
            getWorkspacePath:  () => null,
            runNow:            () => { },
            toggleTaskEnabled: (_, _) => { });
        return (controller, listPanel, statusLabel, runNowButton);
    }

    private static MaintenanceMdConfig MakeConfig(IReadOnlyList<MaintenanceTask>? tasks = null) =>
        new(IdleTimeout: 15, MaxTasksPerSession: 5, Safety: "branch", Tasks: tasks ?? []);

    private static MaintenanceTask MakeTask(
        string id, string title, bool enabled = true, string frequency = "daily", string safety = "branch") =>
        new(id, enabled, frequency, safety, title, "Do work.");

    /// <summary>
    /// Searches the logical tree of <paramref name="panel"/> for all CheckBox instances.
    /// Each task row is: Border → Grid → [CheckBox (col 0), StackPanel (col 1)].
    /// </summary>
    private static IEnumerable<CheckBox> FindCheckBoxes(StackPanel panel) {
        foreach (UIElement child in panel.Children) {
            if (child is Border border && border.Child is Grid grid)
                foreach (UIElement item in grid.Children)
                    if (item is CheckBox cb)
                        yield return cb;
        }
    }

    /// <summary>
    /// Recursively walks the logical tree rooted at <paramref name="parent"/> and
    /// collects all <see cref="TextBlock"/> instances.
    /// </summary>
    private static List<TextBlock> CollectTextBlocks(DependencyObject parent) {
        var result = new List<TextBlock>();
        CollectTextBlocksCore(parent, result);
        return result;
    }

    private static void CollectTextBlocksCore(DependencyObject parent, List<TextBlock> result) {
        if (parent is TextBlock tb)
            result.Add(tb);

        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dep)
                CollectTextBlocksCore(dep, result);
    }

    // ── Status header — idle ──────────────────────────────────────────────────

    [Test]
    public void Refresh_HeaderText_WhenIdle() {
        WpfTestContext.Run(() => {
            var (controller, _, statusLabel, _) = CreateController();
            var config = MakeConfig([MakeTask("t1", "Task One")]);

            controller.Refresh(config, stateStore: null);

            // With _nextMaintenanceAt == DateTimeOffset.MaxValue (the default) and no runner
            // active, the status label must be hidden.
            Assert.That(statusLabel.Visibility, Is.EqualTo(Visibility.Collapsed),
                "Status label must be collapsed when the runner is idle and no countdown is scheduled");
        });
    }

    // ── Status header — running ───────────────────────────────────────────────

    [Test]
    public void Refresh_HeaderText_WhenRunning() {
        WpfTestContext.Run(() => {
            var (controller, _, statusLabel, _) = CreateController();
            var config = MakeConfig([MakeTask("t1", "Build Checks")]);

            // Simulate runner starting before the next Refresh call
            controller.OnRunnerStarted("Build Checks");
            controller.Refresh(config, stateStore: null);

            Assert.That(statusLabel.Visibility, Is.EqualTo(Visibility.Visible),
                "Status label must be visible while the runner is active");
            Assert.That(statusLabel.Text, Does.Contain("Running"),
                "Status text must mention 'Running' while a task is executing");
            Assert.That(statusLabel.Text, Does.Contain("Build Checks"),
                "Status text must include the currently running task title");
        });
    }

    [Test]
    public void OnRunnerCompleted_ResetsHeaderToIdle() {
        WpfTestContext.Run(() => {
            var (controller, _, statusLabel, _) = CreateController();
            var config = MakeConfig([MakeTask("t1", "Lint")]);
            controller.Refresh(config, stateStore: null);

            controller.OnRunnerStarted("Lint");
            Assert.That(statusLabel.Text, Does.Contain("Running"),
                "Pre-condition: label must say Running");

            controller.OnRunnerCompleted();
            // After completion with MaxValue next time, label collapses again
            Assert.That(statusLabel.Visibility, Is.EqualTo(Visibility.Collapsed),
                "Status label must collapse again after the runner completes");
        });
    }

    // ── Checkbox state reflects config ────────────────────────────────────────

    [Test]
    public void Refresh_CheckboxState_ReflectsConfig() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([
                MakeTask("task-a", "Enabled Task",  enabled: true),
                MakeTask("task-b", "Disabled Task", enabled: false),
            ]);

            controller.Refresh(config, stateStore: null);

            var checkBoxes = FindCheckBoxes(listPanel).ToList();
            Assert.That(checkBoxes, Has.Count.EqualTo(2),
                "Each configured task must produce exactly one checkbox");
            Assert.That(checkBoxes[0].IsChecked, Is.True,
                "Checkbox for an enabled task must be checked");
            Assert.That(checkBoxes[1].IsChecked, Is.False,
                "Checkbox for a disabled task must be unchecked");
        });
    }

    [Test]
    public void Refresh_EmptyConfig_ShowsNoCheckboxes() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig(tasks: []);  // no tasks

            controller.Refresh(config, stateStore: null);

            var checkBoxes = FindCheckBoxes(listPanel).ToList();
            Assert.That(checkBoxes, Is.Empty,
                "No checkboxes must appear when the config has no tasks");
        });
    }

    // ── Last-run info from state store ────────────────────────────────────────

    [Test]
    public void Refresh_LastRunInfo_ShownWhenAvailable() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([MakeTask("tracked-task", "Tracked Task")]);

            var store = new MaintenanceStateStore(_workspace.RootPath);
            store.RecordRun("tracked-task", commitSha: null);

            controller.Refresh(config, store);

            var lastRunBlocks = CollectTextBlocks(listPanel)
                .Where(tb => tb.Text.Contains("Last run:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.That(lastRunBlocks, Is.Not.Empty,
                "A 'Last run:' TextBlock must appear when the state store has a recorded run");
        });
    }

    [Test]
    public void Refresh_LastRunInfo_AbsentWhenNoStateStore() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([MakeTask("any-task", "Any Task")]);

            // Pass null state store — no last-run info can be resolved
            controller.Refresh(config, stateStore: null);

            var lastRunBlocks = CollectTextBlocks(listPanel)
                .Where(tb => tb.Text.Contains("Last run:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.That(lastRunBlocks, Is.Empty,
                "No 'Last run:' text should appear when no state store is provided");
        });
    }

    // ── First run — state file absent ─────────────────────────────────────────

    [Test]
    public void Refresh_FirstRun_WhenStateFileAbsent() {
        WpfTestContext.Run(() => {
            // Point to an empty sub-directory that contains no maintenance-state.json
            var emptyDir = Path.Combine(_workspace.RootPath, "empty");
            Directory.CreateDirectory(emptyDir);

            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([MakeTask("new-task", "Brand New Task")]);

            var store = new MaintenanceStateStore(emptyDir);
            store.Reload();  // no file exists — loads empty state gracefully

            Assert.DoesNotThrow(() => controller.Refresh(config, store),
                "Refresh must not throw when no maintenance-state.json exists yet");

            // The task has never run — no "Last run:" label must appear
            var lastRunBlocks = CollectTextBlocks(listPanel)
                .Where(tb => tb.Text.Contains("Last run:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.That(lastRunBlocks, Is.Empty,
                "No 'Last run:' info must appear for a task that has never run (first run scenario)");

            // At least one checkbox must be present (the task row was built)
            var checkBoxes = FindCheckBoxes(listPanel).ToList();
            Assert.That(checkBoxes, Has.Count.EqualTo(1),
                "The task row must still be rendered on first run");
        });
    }

    // ── Run Now button ────────────────────────────────────────────────────────

    [Test]
    public void RunNowButton_DisabledWhileRunning_ReEnabledOnComplete() {
        WpfTestContext.Run(() => {
            var (controller, _, _, runNowButton) = CreateController();

            Assert.That(runNowButton.IsEnabled, Is.True,
                "Run Now button must be enabled initially");

            controller.OnRunnerStarted("Some Task");
            Assert.That(runNowButton.IsEnabled, Is.False,
                "Run Now button must be disabled while a task is running");

            controller.OnRunnerCompleted();
            Assert.That(runNowButton.IsEnabled, Is.True,
                "Run Now button must be re-enabled after runner completes");
        });
    }

    // ── Recent Reports section ────────────────────────────────────────────────

    private static Expander? FindReportsExpander(StackPanel panel) {
        foreach (UIElement child in panel.Children)
            if (child is Expander exp && exp.Header is string h && h.Contains("Recent Reports"))
                return exp;
        return null;
    }

    private static List<Button> CollectButtons(DependencyObject parent) {
        var result = new List<Button>();
        CollectButtonsCore(parent, result);
        return result;
    }

    private static void CollectButtonsCore(DependencyObject parent, List<Button> result) {
        if (parent is Button btn)
            result.Add(btn);
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dep)
                CollectButtonsCore(dep, result);
    }

    private static (MaintenancePanelController controller,
                    StackPanel listPanel,
                    TextBlock  statusLabel,
                    Button     runNowButton)
        CreateControllerWithWorkspace(string? workspacePath) {
        var listPanel             = new StackPanel();
        var statusLabel           = new TextBlock();
        var runNowButton          = new Button();
        var enabledOnIdleCheckBox = new CheckBox();
        var controller   = new MaintenancePanelController(
            listPanel,
            statusLabel,
            runNowButton,
            enabledOnIdleCheckBox,
            getWorkspacePath:  () => workspacePath,
            runNow:            () => { },
            toggleTaskEnabled: (_, _) => { });
        return (controller, listPanel, statusLabel, runNowButton);
    }

    [Test]
    public void ReportsSection_ShowsNoReportsYet_WhenNullWorkspacePath() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController(); // getWorkspacePath returns null
            var config = MakeConfig([MakeTask("t1", "Task One")]);

            controller.Refresh(config, stateStore: null);

            var expander = FindReportsExpander(listPanel);
            Assert.That(expander, Is.Not.Null, "Recent Reports expander must be present");

            expander!.IsExpanded = true;
            var textBlocks = CollectTextBlocks((DependencyObject)expander.Content);
            Assert.That(textBlocks.Any(tb => tb.Text.Contains("No reports yet", StringComparison.OrdinalIgnoreCase)),
                Is.True, "Should show 'No reports yet' when workspace path is null");
        });
    }

    [Test]
    public void ReportsSection_ShowsNoReportsYet_WhenDirectoryEmpty() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_empty");
            var reportsDir = Path.Combine(workspacePath, ".squad", "maintenance-reports");
            Directory.CreateDirectory(reportsDir);

            var (controller, listPanel, _, _) = CreateControllerWithWorkspace(workspacePath);
            var config = MakeConfig([MakeTask("t1", "Task One")]);

            controller.Refresh(config, stateStore: null);

            var expander = FindReportsExpander(listPanel);
            Assert.That(expander, Is.Not.Null, "Recent Reports expander must be present");

            expander!.IsExpanded = true;
            var textBlocks = CollectTextBlocks((DependencyObject)expander.Content);
            Assert.That(textBlocks.Any(tb => tb.Text.Contains("No reports yet", StringComparison.OrdinalIgnoreCase)),
                Is.True, "Should show 'No reports yet' when reports directory is empty");
        });
    }

    [Test]
    public void ReportsSection_ShowsReportItem_WhenReportExists() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_with_report");
            var reportsDir = Path.Combine(workspacePath, ".squad", "maintenance-reports");
            Directory.CreateDirectory(reportsDir);

            var reportContent = """
                # Maintenance Report — 2026-05-20 14:30

                **Session duration:** 45s

                ## Tasks Run

                - ✅ Code cleanup (cleanup) — 30s
                - ✅ Lint check (lint) — 15s

                ## Summary

                All tasks completed.
                """;
            File.WriteAllText(Path.Combine(reportsDir, "20260520-143000.md"), reportContent);

            var (controller, listPanel, _, _) = CreateControllerWithWorkspace(workspacePath);
            var config = MakeConfig([MakeTask("t1", "Task One")]);

            controller.Refresh(config, stateStore: null);

            var expander = FindReportsExpander(listPanel);
            Assert.That(expander, Is.Not.Null, "Recent Reports expander must be present");

            expander!.IsExpanded = true;
            var buttons = CollectButtons((DependencyObject)expander.Content);
            Assert.That(buttons, Is.Not.Empty, "At least one report button must be present");

            var content = buttons[0].Content?.ToString() ?? "";
            Assert.That(content, Does.Contain("2026-05-20"), "Button must show the report date");
            Assert.That(content, Does.Contain("2 tasks"), "Button must show task count");
        });
    }

    [Test]
    public void ReportsSection_ShowsOneTask_Singular() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_one_task");
            var reportsDir = Path.Combine(workspacePath, ".squad", "maintenance-reports");
            Directory.CreateDirectory(reportsDir);

            var reportContent = """
                # Maintenance Report — 2026-05-21 10:00

                **Session duration:** 20s

                ## Tasks Run

                - ✅ Code cleanup (cleanup) — 20s

                ## Summary

                Done.
                """;
            File.WriteAllText(Path.Combine(reportsDir, "20260521-100000.md"), reportContent);

            var (controller, listPanel, _, _) = CreateControllerWithWorkspace(workspacePath);
            controller.Refresh(MakeConfig([MakeTask("t1", "Task One")]), stateStore: null);

            var expander = FindReportsExpander(listPanel);
            expander!.IsExpanded = true;
            var buttons = CollectButtons((DependencyObject)expander.Content);
            Assert.That(buttons, Is.Not.Empty);

            var content = buttons[0].Content?.ToString() ?? "";
            Assert.That(content, Does.Contain("1 task"), "Singular 'task' must be used for count of 1");
            Assert.That(content, Does.Not.Contain("1 tasks"), "Must not use plural for count of 1");
        });
    }

    [Test]
    public void ReportsSection_CollapsedByDefault() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            controller.Refresh(MakeConfig([MakeTask("t1", "Task One")]), stateStore: null);

            var expander = FindReportsExpander(listPanel);
            Assert.That(expander, Is.Not.Null, "Recent Reports expander must be present");
            Assert.That(expander!.IsExpanded, Is.False, "Expander must be collapsed by default");
        });
    }

    // ── Safety warning chip ───────────────────────────────────────────────────

    /// <summary>
    /// Recursively walks the logical tree rooted at <paramref name="parent"/> and
    /// collects all <see cref="Border"/> instances.
    /// </summary>
    private static List<Border> CollectBorders(DependencyObject parent) {
        var result = new List<Border>();
        CollectBordersCore(parent, result);
        return result;
    }

    private static void CollectBordersCore(DependencyObject parent, List<Border> result) {
        if (parent is Border b)
            result.Add(b);
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dep)
                CollectBordersCore(dep, result);
    }

    [Test]
    public void Refresh_DirectSafetyTask_ShowsWarningChip() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([MakeTask("t1", "Direct Task", safety: "direct")]);

            controller.Refresh(config, stateStore: null);

            var textBlocks = CollectTextBlocks(listPanel);
            Assert.That(
                textBlocks.Any(tb =>
                    tb.Text.Contains("⚠", StringComparison.Ordinal) ||
                    tb.Text.Contains("direct commits", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "A warning chip with '⚠ direct commits' must appear for a task with safety: direct");
        });
    }

    [Test]
    public void Refresh_BranchSafetyTask_NoWarningChip() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([MakeTask("t1", "Branch Task", safety: "branch")]);

            controller.Refresh(config, stateStore: null);

            var textBlocks = CollectTextBlocks(listPanel);
            Assert.That(
                textBlocks.All(tb =>
                    !tb.Text.Contains("⚠", StringComparison.Ordinal) &&
                    !tb.Text.Contains("direct commits", StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "No warning chip must appear for a task with safety: branch");
        });
    }

    [Test]
    public void Refresh_ReportOnlySafetyTask_ShowsPlainSafetyChip_NotWarning() {
        WpfTestContext.Run(() => {
            var (controller, listPanel, _, _) = CreateController();
            var config = MakeConfig([MakeTask("t1", "Report Only Task", safety: "report-only")]);

            controller.Refresh(config, stateStore: null);

            var textBlocks = CollectTextBlocks(listPanel);
            Assert.That(
                textBlocks.Any(tb => string.Equals(tb.Text, "report-only", StringComparison.Ordinal)),
                Is.True,
                "A plain 'report-only' chip must appear for a task with safety: report-only");
            Assert.That(
                textBlocks.All(tb => !tb.Text.Contains("⚠", StringComparison.Ordinal)),
                Is.True,
                "No warning chip must appear for a non-direct, non-branch safety level");
        });
    }

    // ── ToggleTaskEnabled ─────────────────────────────────────────────────────

    private static string WriteMaintFile(string workspacePath, string content) {
        var squadDir = Path.Combine(workspacePath, ".squad");
        Directory.CreateDirectory(squadDir);
        var mdPath = Path.Combine(squadDir, "maintenance.md");
        File.WriteAllText(mdPath, content);
        return mdPath;
    }

    private static (MaintenancePanelController controller,
                    List<(string taskId, bool enabled)> toggleCalls)
        CreateControllerWithTracking(string workspacePath) {
        var listPanel             = new StackPanel();
        var statusLabel           = new TextBlock();
        var runNowButton          = new Button();
        var enabledOnIdleCheckBox = new CheckBox();
        var toggleCalls  = new List<(string, bool)>();
        var controller   = new MaintenancePanelController(
            listPanel,
            statusLabel,
            runNowButton,
            enabledOnIdleCheckBox,
            getWorkspacePath:  () => workspacePath,
            runNow:            () => { },
            toggleTaskEnabled: (id, en) => toggleCalls.Add((id, en)));
        return (controller, toggleCalls);
    }

    [Test]
    public void ToggleTaskEnabled_WhenEnabled_WritesDisabledToFile() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_toggle_e2d");
            var mdPath = WriteMaintFile(workspacePath,
                "---\nconfigured: true\ntasks:\n  - id: my-task\n    enabled: true\n    frequency: daily\n    title: My Task\n    instructions: Do work.\n---\n");

            var (controller, _) = CreateControllerWithTracking(workspacePath);
            controller.ToggleTaskEnabled("my-task");

            var content = File.ReadAllText(mdPath);
            Assert.That(content, Does.Contain("enabled: false"),
                "enabled flag must be flipped to false");
            Assert.That(content, Does.Not.Contain("enabled: true"),
                "enabled: true must not remain in file");
        });
    }

    [Test]
    public void ToggleTaskEnabled_WhenDisabled_WritesEnabledToFile() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_toggle_d2e");
            var mdPath = WriteMaintFile(workspacePath,
                "---\nconfigured: true\ntasks:\n  - id: my-task\n    enabled: false\n    frequency: daily\n    title: My Task\n    instructions: Do work.\n---\n");

            var (controller, _) = CreateControllerWithTracking(workspacePath);
            controller.ToggleTaskEnabled("my-task");

            var content = File.ReadAllText(mdPath);
            Assert.That(content, Does.Contain("enabled: true"),
                "enabled flag must be flipped to true");
        });
    }

    [Test]
    public void ToggleTaskEnabled_PreservesOtherFileContent() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_toggle_preserve");
            var mdPath = WriteMaintFile(workspacePath,
                "---\nconfigured: true\nidle_timeout: 20\ntasks:\n  - id: task-a\n    enabled: false\n    frequency: daily\n    title: Task A\n    instructions: Do A.\n  - id: task-b\n    enabled: true\n    frequency: per-commit\n    title: Task B\n    instructions: Do B.\n---\n");

            var (controller, _) = CreateControllerWithTracking(workspacePath);
            controller.ToggleTaskEnabled("task-a");

            var after = File.ReadAllText(mdPath);
            Assert.That(after, Does.Contain("idle_timeout: 20"),      "Global config line preserved");
            Assert.That(after, Does.Contain("id: task-b"),             "task-b still present");
            Assert.That(after, Does.Contain("frequency: per-commit"),  "task-b frequency preserved");
            Assert.That(after, Does.Contain("title: Task B"),          "task-b title preserved");
            Assert.That(after, Does.Contain("  - id: task-a"),         "task-a still present");
        });
    }

    [Test]
    public void ToggleTaskEnabled_InvokesReloadCallback() {
        WpfTestContext.Run(() => {
            var workspacePath = Path.Combine(_workspace.RootPath, "ws_toggle_cb");
            WriteMaintFile(workspacePath,
                "---\nconfigured: true\ntasks:\n  - id: my-task\n    enabled: false\n    frequency: daily\n    title: My Task\n    instructions: Do work.\n---\n");

            var (controller, toggleCalls) = CreateControllerWithTracking(workspacePath);
            controller.ToggleTaskEnabled("my-task");

            Assert.That(toggleCalls, Has.Count.EqualTo(1),
                "Reload callback must be invoked exactly once after toggle");
            Assert.That(toggleCalls[0].taskId, Is.EqualTo("my-task"),
                "Callback must receive the correct task ID");
            Assert.That(toggleCalls[0].enabled, Is.True,
                "Task was disabled; toggle must enable it and report enabled=true");
        });
    }
}
