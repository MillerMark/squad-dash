namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>Manages content in the inline Maintenance panel.</summary>
internal sealed class MaintenancePanelController {

    private readonly StackPanel           _listPanel;
    private readonly TextBlock            _statusLabel;
    private readonly Button               _runNowButton;
    private readonly CheckBox             _enabledOnIdleCheckBox;
    private readonly Func<string?>        _getWorkspacePath;
    private readonly Action               _runNow;
    private readonly Action<string, bool> _toggleTaskEnabled;
    private readonly Action               _reloadPanel;

    private MaintenanceMdConfig?   _config;
    private MaintenanceStateStore? _stateStore;
    private bool                   _runnerActive;
    private string?                _runningTaskTitle;
    private DispatcherTimer?       _countdownTimer;
    private DateTimeOffset         _nextMaintenanceAt = DateTimeOffset.MaxValue;
    private bool                   _suppressEnabledOnIdleEvent;

    // ── Construction ─────────────────────────────────────────────────────────

    internal MaintenancePanelController(
        StackPanel           listPanel,
        TextBlock            statusLabel,
        Button               runNowButton,
        CheckBox             enabledOnIdleCheckBox,
        Func<string?>        getWorkspacePath,
        Action               runNow,
        Action<string, bool> toggleTaskEnabled,
        Action               reloadPanel) {

        _listPanel              = listPanel;
        _statusLabel            = statusLabel;
        _runNowButton           = runNowButton;
        _enabledOnIdleCheckBox  = enabledOnIdleCheckBox;
        _getWorkspacePath       = getWorkspacePath;
        _runNow                 = runNow;
        _toggleTaskEnabled      = toggleTaskEnabled;
        _reloadPanel            = reloadPanel;

        _runNowButton.Click += (_, _) => _runNow();
        _enabledOnIdleCheckBox.Checked   += (_, _) => SetEnabledOnIdle(true);
        _enabledOnIdleCheckBox.Unchecked += (_, _) => SetEnabledOnIdle(false);
        WireListPanelContextMenu();
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void WireListPanelContextMenu() {
        var editItem = new MenuItem { Header = "Edit Maintenance File" };
        editItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        editItem.Click += (_, _) => {
            var workspacePath = _getWorkspacePath();
            if (workspacePath is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    "MaintenancePanelController: workspace path is null; cannot open maintenance file");
                return;
            }
            var mdPath = Path.Combine(workspacePath, ".squad", "maintenance.md");
            if (!File.Exists(mdPath)) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: maintenance file not found at {mdPath}");
                return;
            }
            try {
                Process.Start(new ProcessStartInfo(mdPath) { UseShellExecute = true });
            } catch (Exception ex) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: failed to open maintenance file: {ex.Message}");
            }
        };

        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        menu.Items.Add(editItem);
        _listPanel.ContextMenu = menu;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    internal void Refresh(MaintenanceMdConfig? config, MaintenanceStateStore? stateStore) {
        _config     = config;
        _stateStore = stateStore;

        _suppressEnabledOnIdleEvent = true;
        _enabledOnIdleCheckBox.IsChecked = config?.EnabledOnIdle ?? false;
        _suppressEnabledOnIdleEvent = false;

        RebuildList();
    }

    /// <summary>
    /// Call when the runner starts a task. Updates the header to "Running now — [title]…"
    /// and disables the Run Now button.
    /// </summary>
    internal void OnRunnerStarted(string taskTitle) {
        _runnerActive      = true;
        _runningTaskTitle  = taskTitle;
        StopCountdown();
        SyncStatusLabel();
        _runNowButton.IsEnabled = false;
    }

    /// <summary>
    /// Call when the runner finishes. Re-enables the Run Now button and restarts the countdown.
    /// </summary>
    internal void OnRunnerCompleted() {
        _runnerActive      = false;
        _runningTaskTitle  = null;
        _runNowButton.IsEnabled = true;
        SyncStatusLabel();
    }

    /// <summary>
    /// Sets the next expected idle/maintenance time for the countdown display.
    /// Pass <see cref="DateTimeOffset.MaxValue"/> to hide the countdown.
    /// </summary>
    internal void SetNextMaintenanceAt(DateTimeOffset next) {
        _nextMaintenanceAt = next;
        if (!_runnerActive)
        {
            StopCountdown();
            StartCountdown();
        }
    }

    // ── In-place task enable/disable ──────────────────────────────────────────

    private string? GetMaintenanceMdPath() {
        var workspacePath = _getWorkspacePath();
        return workspacePath is null ? null : Path.Combine(workspacePath, ".squad", "maintenance.md");
    }

    private void SetEnabledOnIdle(bool value) {
        if (_suppressEnabledOnIdleEvent) return;
        var mdPath = GetMaintenanceMdPath();
        if (mdPath is null) return;
        MaintenanceMdParser.UpdateEnabledOnIdle(mdPath, value);
    }

    /// <summary>
    /// Reads <c>.squad/maintenance.md</c>, locates the <paramref name="taskId"/> entry,
    /// flips its <c>enabled:</c> value, writes the file back preserving all other content,
    /// then invokes the host's reload callback so the panel refreshes.
    /// </summary>
    internal void ToggleTaskEnabled(string taskId) {
        var workspacePath = _getWorkspacePath();
        if (workspacePath is null) return;

        var mdPath = Path.Combine(workspacePath, ".squad", "maintenance.md");
        if (!File.Exists(mdPath)) return;

        try {
            var rawContent = File.ReadAllText(mdPath);
            var lineEnding = rawContent.Contains("\r\n") ? "\r\n" : "\n";
            var lines = rawContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            bool? newEnabled     = null;
            bool  inFrontmatter  = false;
            bool  pastFirst      = false;
            bool  inTasksList    = false;
            bool  inTargetTask   = false;

            for (int i = 0; i < lines.Length; i++) {
                var line    = lines[i];
                var trimmed = line.TrimStart();
                int indent  = line.Length - trimmed.Length;

                // Track frontmatter boundaries (first pair of --- markers only)
                if (trimmed == "---") {
                    if (!pastFirst) { pastFirst = true; inFrontmatter = true; }
                    else            { inFrontmatter = false; }
                    continue;
                }

                if (!inFrontmatter) continue;

                // tasks: list start at indent 0
                if (indent == 0 && trimmed == "tasks:") {
                    inTasksList = true;
                    continue;
                }

                if (!inTasksList) continue;

                // Blank lines are allowed between tasks — skip without resetting state
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Any non-empty indent-0 key other than tasks: closes the tasks block
                if (indent == 0) { inTargetTask = false; inTasksList = false; continue; }

                // New task item at indent 2: "  - id: <id>"
                if (indent == 2 && trimmed.StartsWith("- ")) {
                    var rest = trimmed[2..];
                    if (rest.StartsWith("id:")) {
                        var idVal = rest["id:".Length..].Trim().Trim('"', '\'');
                        inTargetTask = string.Equals(idVal, taskId, StringComparison.Ordinal);
                    } else {
                        inTargetTask = false;
                    }
                    continue;
                }

                if (!inTargetTask) continue;

                // Task field "    enabled: true/false" at indent 4
                if (indent == 4 && trimmed.StartsWith("enabled:")) {
                    var val     = trimmed["enabled:".Length..].Trim().Trim('"', '\'');
                    bool cur    = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                    newEnabled  = !cur;
                    lines[i]    = "    enabled: " + (newEnabled.Value ? "true" : "false");
                    break;
                }
            }

            if (newEnabled is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: task '{taskId}' not found in {mdPath}");
                return;
            }

            File.WriteAllText(mdPath, string.Join(lineEnding, lines));

            SquadDashTrace.Write(TraceCategory.General,
                $"MaintenancePanelController: task '{taskId}' toggled → enabled={newEnabled.Value}");

            _toggleTaskEnabled(taskId, newEnabled.Value);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"MaintenancePanelController: failed to toggle task '{taskId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the <c>frequency:</c> field for <paramref name="taskId"/> in maintenance.md
    /// and reloads the panel so the change is reflected immediately.
    /// </summary>
    private void ChangeTaskFrequency(string taskId, string newFrequency) {
        var mdPath = GetMaintenanceMdPath();
        if (mdPath is null) return;
        MaintenanceMdParser.UpdateFrequency(mdPath, taskId, newFrequency);
        _reloadPanel();
    }

    // ── List construction ─────────────────────────────────────────────────────

    private void RebuildList() {
        _listPanel.Children.Clear();

        if (_config?.EnabledOnIdle == false) {
            var banner = new TextBlock {
                Text         = "Maintenance will not run on idle. Check \"Enable (on idle)\" to activate.",
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 8),
            };
            banner.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            banner.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            _listPanel.Children.Add(banner);
        }

        if (_config is null || _config.Tasks is null || _config.Tasks.Count == 0) {
            var empty = new TextBlock {
                Text         = "No maintenance tasks configured.",
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(4, 6, 4, 4),
                FontStyle    = FontStyles.Italic,
            };
            empty.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            _listPanel.Children.Add(empty);
        } else {
            foreach (var task in _config.Tasks)
                _listPanel.Children.Add(BuildTaskRow(task));
        }

        SyncStatusLabel();
        AppendReportsSection();
    }

    private void AppendReportsSection() {
        var workspacePath = _getWorkspacePath();
        IReadOnlyList<string> reportPaths = workspacePath is null
            ? []
            : new MaintenanceReportWriter(workspacePath).GetReportPaths();

        var separator = new Separator { Margin = new Thickness(0, 8, 0, 4) };
        separator.SetResourceReference(Separator.BackgroundProperty, "SubtleText");
        _listPanel.Children.Add(separator);

        var headerLabel = new TextBlock { Text = "Recent Reports" };
        headerLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        headerLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var expander = new Expander {
            Header     = "Recent Reports",
            IsExpanded = false,
            Margin     = new Thickness(0, 0, 0, 4),
        };
        expander.SetResourceReference(Expander.StyleProperty, "ThemedExpanderStyle");

        var contentPanel = new StackPanel { Margin = new Thickness(8, 4, 0, 4) };

        if (reportPaths.Count == 0) {
            var noReports = new TextBlock {
                Text      = "No reports yet.",
                FontStyle = FontStyles.Italic,
            };
            noReports.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            noReports.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            contentPanel.Children.Add(noReports);
        } else {
            foreach (var path in reportPaths) {
                var (dt, taskCount) = ParseReportSummary(path);
                contentPanel.Children.Add(BuildReportRow(path, dt, taskCount));
            }
        }

        expander.Content = contentPanel;
        _listPanel.Children.Add(expander);
    }

    private static (DateTimeOffset dt, int taskCount) ParseReportSummary(string path) {
        var baseName = Path.GetFileNameWithoutExtension(path);
        DateTimeOffset dt = DateTimeOffset.MinValue;
        if (baseName.Length >= 15 &&
            DateTime.TryParseExact(baseName[..15], "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            dt = new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed));

        int taskCount = 0;
        try {
            var lines = File.ReadAllLines(path);
            bool inTasksSection = false;
            foreach (var line in lines) {
                if (line.TrimEnd() == "## Tasks Run") {
                    inTasksSection = true;
                    continue;
                }
                if (inTasksSection) {
                    if (line.StartsWith("## ", StringComparison.Ordinal)) break;
                    if (line.StartsWith("- ", StringComparison.Ordinal) &&
                        !line.Contains("No tasks were run this session."))
                        taskCount++;
                }
            }
        } catch { /* ignore read errors */ }

        return (dt, taskCount);
    }

    private static Button BuildReportRow(string path, DateTimeOffset dt, int taskCount) {
        var taskWord = taskCount == 1 ? "task" : "tasks";
        var label = dt != DateTimeOffset.MinValue
            ? $"{dt.LocalDateTime:yyyy-MM-dd HH:mm}  •  {taskCount} {taskWord}"
            : $"{Path.GetFileNameWithoutExtension(path)}  •  {taskCount} {taskWord}";

        var btn = new Button {
            Content                    = label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness            = new Thickness(0),
            Padding                    = new Thickness(4, 3, 4, 3),
            Margin                     = new Thickness(0, 1, 0, 1),
            Cursor                     = Cursors.Hand,
        };
        btn.SetResourceReference(Button.StyleProperty, "FlatButtonStyle");
        btn.SetResourceReference(Button.FontSizeProperty, "FontSizeSmall");
        btn.SetResourceReference(Button.ForegroundProperty, "BodyText");
        btn.Click += (_, _) => OpenReport(path);
        return btn;
    }

    private static void OpenReport(string path) {
        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"MaintenancePanelController: failed to open report {path}: {ex.Message}");
        }
    }

    private Border BuildTaskRow(MaintenanceTask task) {
        var row = new Border {
            Padding    = new Thickness(0, 6, 0, 6),
            Background = Brushes.Transparent,
        };

        if (!string.IsNullOrWhiteSpace(task.Instructions))
            MarkdownHoverPopup.Attach(
                row,
                buildHeader: null,
                getMarkdown: () => task.Instructions,
                maxWidth:    800);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Child = grid;

        // ── Checkbox ─────────────────────────────────────────────────────────
        var check = new CheckBox {
            IsChecked         = task.Enabled,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 1, 0, 0),
        };
        check.SetResourceReference(CheckBox.FontSizeProperty, "FontSizeBody");
        check.SetResourceReference(CheckBox.ForegroundProperty, "LabelText");
        Grid.SetColumn(check, 0);
        check.Checked   += (_, _) => ToggleTaskEnabled(task.Id);
        check.Unchecked += (_, _) => ToggleTaskEnabled(task.Id);
        grid.Children.Add(check);

        // ── Right column: title + chips + options ─────────────────────────────
        var rightPanel = new StackPanel { Margin = new Thickness(0) };
        Grid.SetColumn(rightPanel, 1);
        grid.Children.Add(rightPanel);

        // Title
        var titleBlock = new TextBlock {
            Text         = task.Title,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 2),
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        rightPanel.Children.Add(titleBlock);

        // Chips: frequency picker + safety — only visible when task is enabled
        var chipRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0),
            Visibility = task.Enabled ? Visibility.Visible : Visibility.Collapsed };
        var taskIdForFreq   = task.Id;
        var frequencyPicker = new CompactPickerButton(
            headerText:     "Run Frequency",
            options: [
                ("Always",     "always"),
                ("Daily",      "daily"),
                ("Per Commit", "per-commit"),
            ],
            selectedValue:  task.Frequency,
            onValueChanged: newFreq => ChangeTaskFrequency(taskIdForFreq, newFreq));
        chipRow.Children.Add(frequencyPicker.Control);
        if (string.Equals(task.Safety, "direct", StringComparison.OrdinalIgnoreCase))
            chipRow.Children.Add(BuildWarningChip("⚠ direct commits"));
        else if (!string.Equals(task.Safety, "branch", StringComparison.OrdinalIgnoreCase))
            chipRow.Children.Add(BuildChip(task.Safety, SafetyTooltip(task.Safety)));
        rightPanel.Children.Add(chipRow);

        // Last-run status
        var lastRun = _stateStore?.GetLastRunAt(task.Id);
        if (lastRun.HasValue) {
            var relTime = StatusTimingPresentation.FormatRelativeTimestamp(
                new DateTimeOffset(lastRun.Value, TimeSpan.Zero));
            var lastRunBlock = new TextBlock {
                Text   = $"Last run: {relTime}",
                Margin = new Thickness(0, 3, 0, 0),
            };
            lastRunBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            lastRunBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            rightPanel.Children.Add(lastRunBlock);
        }

        // Radio options (if present and task is enabled)
        if (task.Enabled && task.Options is { Count: > 0 }) {
            var optionsPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var opt in task.Options) {
                if (opt.Label is { Length: > 0 }) {
                    var labelBlock = new TextBlock {
                        Text         = opt.Label,
                        TextWrapping = TextWrapping.Wrap,
                        Margin       = new Thickness(0, 2, 0, 1),
                    };
                    labelBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                    labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                    if (!string.IsNullOrEmpty(opt.Tooltip))
                        labelBlock.ToolTip = MakeThemedToolTip(opt.Tooltip);
                    optionsPanel.Children.Add(labelBlock);
                }
                if (opt.Choices is { Count: > 0 }) {
                    foreach (var choice in opt.Choices) {
                        var rb = new RadioButton {
                            Content   = choice.Value,
                            GroupName = $"task-{task.Id}-{opt.Key}",
                            IsChecked = string.Equals(choice.Value, opt.RawValue, StringComparison.OrdinalIgnoreCase),
                            Margin    = new Thickness(8, 1, 0, 1),
                        };
                        rb.SetResourceReference(RadioButton.FontSizeProperty,  "FontSizeSmall");
                        rb.SetResourceReference(RadioButton.ForegroundProperty, "BodyText");
                        rb.SetResourceReference(RadioButton.StyleProperty,      "ThemedRadioButtonStyle");
                        if (!string.IsNullOrEmpty(choice.Tooltip))
                            rb.ToolTip = MakeThemedToolTip(choice.Tooltip);
                        var capturedPath   = GetMaintenanceMdPath();
                        var capturedTaskId = task.Id;
                        var capturedOptKey = opt.Key;
                        var capturedValue  = choice.Value;
                        rb.Checked += (_, _) => {
                            if (capturedPath is not null)
                                MaintenanceMdParser.UpdateOptionValue(capturedPath, capturedTaskId, capturedOptKey, capturedValue);
                        };
                        optionsPanel.Children.Add(rb);
                    }
                }
            }
            rightPanel.Children.Add(optionsPanel);
        }

        return row;
    }

    private static string FrequencyTooltip(string frequency) => frequency.ToLowerInvariant() switch {
        "daily"      => "Runs at most once per calendar day.",
        "per-commit" => "Runs once per commit since the last time it ran.",
        "always"     => "Runs every idle cycle with no cooldown.",
        _            => $"Frequency: {frequency}",
    };

    private static string SafetyTooltip(string safety) => safety.ToLowerInvariant() switch {
        "report-only" => "Read-only — no file changes. Produces a written analysis only.",
        "branch"      => "Creates a new git branch and commits changes there. Never touches the current branch.",
        "direct"      => "Edits and commits directly on the current branch.",
        _             => $"Safety: {safety}",
    };

    private static Border BuildChip(string text, string? tooltip) {
        var label = new TextBlock { Text = text };
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXSmall");
        label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var chip = new Border {
            Child         = label,
            Padding       = new Thickness(5, 1, 5, 1),
            Margin        = new Thickness(0, 0, 4, 2),
            CornerRadius  = new CornerRadius(8),
        };
        chip.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        chip.SetResourceReference(Border.BorderBrushProperty, "InputBorder");
        chip.BorderThickness = new Thickness(1);
        if (tooltip is not null)
            chip.ToolTip = MakeThemedToolTip(tooltip);
        return chip;
    }

    private static ToolTip MakeThemedToolTip(string text) {
        var tb = new TextBlock { Text = text };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        var tip = new ToolTip {
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(6, 4, 6, 4),
            Content         = tb,
        };
        tip.SetResourceReference(ToolTip.BackgroundProperty, "InputSurface");
        tip.SetResourceReference(ToolTip.BorderBrushProperty, "InputBorder");
        return tip;
    }

    private static Border BuildWarningChip(string text) {
        var label = new TextBlock { Text = text };
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXSmall");
        label.SetResourceReference(TextBlock.ForegroundProperty, "WarningText");

        var chip = new Border {
            Child            = label,
            Padding          = new Thickness(5, 1, 5, 1),
            Margin           = new Thickness(0, 0, 4, 2),
            CornerRadius     = new CornerRadius(8),
            BorderThickness  = new Thickness(1),
        };
        chip.SetResourceReference(Border.BackgroundProperty, "WarningBackground");
        chip.SetResourceReference(Border.BorderBrushProperty, "WarningBorder");
        return chip;
    }

    // ── Status header ─────────────────────────────────────────────────────────

    private void SyncStatusLabel() {
        if (_runnerActive) {
            var title = _runningTaskTitle ?? "task";
            _statusLabel.Text = $"● Running — {title}…";
            _statusLabel.Visibility = Visibility.Visible;
            return;
        }

        if (_config is null) {
            _statusLabel.Visibility = Visibility.Collapsed;
            return;
        }

        // If no next time set, hide the countdown
        if (_nextMaintenanceAt == DateTimeOffset.MaxValue) {
            _statusLabel.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateCountdownLabel();
    }

    private void UpdateCountdownLabel() {
        var remaining = _nextMaintenanceAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) {
            _statusLabel.Text       = "Maintenance window — idle…";
            _statusLabel.Visibility = Visibility.Visible;
            return;
        }

        var text = remaining.TotalHours >= 1
            ? $"Next maintenance in: {(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
            : remaining.TotalMinutes >= 1
                ? $"Next maintenance in: {(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s"
                : $"Next maintenance in: {(int)remaining.TotalSeconds}s";

        _statusLabel.Text       = text;
        _statusLabel.Visibility = Visibility.Visible;
    }

    // ── Countdown timer ───────────────────────────────────────────────────────

    private void StartCountdown() {
        if (_nextMaintenanceAt == DateTimeOffset.MaxValue) return;
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _countdownTimer.Tick += (_, _) => UpdateCountdownLabel();
        _countdownTimer.Start();
        UpdateCountdownLabel();
    }

    private void StopCountdown() {
        _countdownTimer?.Stop();
        _countdownTimer = null;
    }
}
