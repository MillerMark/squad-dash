namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>Manages content in the inline Maintenance panel.</summary>
internal sealed class MaintenancePanelController {

    private readonly StackPanel           _listPanel;
    private readonly TextBlock            _statusLabel;
    private readonly CompactPickerButton  _enabledOnIdlePicker;
    private readonly Func<string?>        _getWorkspacePath;
    private readonly Action<string, bool> _toggleTaskEnabled;
    private readonly Action               _reloadPanel;
    private readonly Action<string>       _openInMarkdownEditor;
    private readonly Action               _showInboxPanel;
    private readonly Action<string>       _runTask;
    private readonly Action               _simulateIdle;
    private readonly Action<RichTextBox, string>?            _onReviseWithAi;
    private readonly Action<RichTextBox, string, string>?    _onDirectRevise;

    private readonly MaintenancePanelViewModel _viewModel = new();
    internal MaintenancePanelViewModel ViewModel => _viewModel;
    private DispatcherTimer?       _countdownTimer;
    private DispatcherTimer?       _transientStatusTimer;

    // ── Construction ─────────────────────────────────────────────────────────

    internal MaintenancePanelController(
        StackPanel           listPanel,
        TextBlock            statusLabel,
        ContentControl       enabledOnIdleHost,
        Func<string?>        getWorkspacePath,
        Action<string, bool> toggleTaskEnabled,
        Action               reloadPanel,
        Action<string>       openInMarkdownEditor,
        Action               showInboxPanel,
        Action<string>       runTask,
        Action               simulateIdle,
        Action<RichTextBox, string>?            onReviseWithAi = null,
        Action<RichTextBox, string, string>?    onDirectRevise = null) {

        _listPanel              = listPanel;
        _statusLabel            = statusLabel;
        _getWorkspacePath       = getWorkspacePath;
        _toggleTaskEnabled      = toggleTaskEnabled;
        _reloadPanel            = reloadPanel;
        _openInMarkdownEditor   = openInMarkdownEditor;
        _showInboxPanel         = showInboxPanel;
        _runTask                = runTask;
        _simulateIdle           = simulateIdle;
        _onReviseWithAi         = onReviseWithAi;
        _onDirectRevise         = onDirectRevise;

        _enabledOnIdlePicker = new CompactPickerButton(
            headerText:     "Maintenance Tasks:",
            options:        [("Run on idle", "on-idle"), ("Manual runs only", "manual")],
            selectedValue:  "manual",
            onValueChanged: v => SetEnabledOnIdle(v == "on-idle"),
            getButtonLabel: v => v == "on-idle" ? "✔ Run on idle" : "(run manually)");
        _enabledOnIdlePicker.Control.SetResourceReference(Button.FontSizeProperty, "FontSizeSmall");
        _enabledOnIdlePicker.Control.Margin = new Thickness(0);
        var pickerMenu = new ContextMenu();
        pickerMenu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        var simulateIdleItem = new MenuItem { Header = "Simulate Idle" };
        simulateIdleItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        simulateIdleItem.Click += (_, _) => _simulateIdle();
        pickerMenu.Items.Add(simulateIdleItem);
        _enabledOnIdlePicker.Control.ContextMenu = pickerMenu;
        _enabledOnIdlePicker.Control.ContextMenuOpening += (_, e) => {
            if (!SquadDashEnvironment.IsDeveloperMode || _enabledOnIdlePicker.SelectedValue != "on-idle")
                e.Handled = true;
        };
        enabledOnIdleHost.Content = _enabledOnIdlePicker.Control;

        WireListPanelContextMenu();
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void WireListPanelContextMenu() {
        var newTaskItem = new MenuItem { Header = "New Task" };
        newTaskItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        newTaskItem.Click += (_, _) => {
            var workspacePath = _getWorkspacePath();
            if (workspacePath is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    "MaintenancePanelController: workspace path is null; cannot create task");
                return;
            }
            var mdPath = Path.Combine(workspacePath, ".squad", "maintenance.md");
            if (!File.Exists(mdPath)) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: maintenance file not found at {mdPath}");
                return;
            }
            var newId   = Guid.NewGuid().ToString("N")[..8];
            var newTask = new MaintenanceTask(
                Id:            newId,
                Enabled:       false,
                Frequency:     "weekly",
                Safety:        "branch",
                Title:         "New Task",
                Instructions:  "Describe what the agent should do here.\n\nAdd as many details as needed.",
                SourceFilePath: mdPath);
            try {
                MaintenanceMdParser.AppendTask(mdPath, newTask);
            }
            catch (Exception ex) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: failed to create task: {ex.Message}");
                return;
            }
            var ownerWindow = Window.GetWindow(_listPanel);
            if (ownerWindow is null) return;
            var editor = new MaintenanceTaskEditorWindow(
                ownerWindow,
                newTask,
                () => new ApplicationSettingsStore().Load(),
                _reloadPanel,
                onReviseWithAi: _onReviseWithAi,
                onDirectRevise: _onDirectRevise);
            editor.Closed += (_, _) => FloatingWindowPositionStore.Shared.Save("MaintenanceTaskEditor", editor);
            FloatingWindowPositionStore.Shared.TryRestore("MaintenanceTaskEditor", editor);
            editor.Show();
        };

        var editItem = new MenuItem { Header = "Edit Maintenance File…" };
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
                _openInMarkdownEditor(mdPath);
            } catch (Exception ex) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: failed to open maintenance file: {ex.Message}");
            }
        };

        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        menu.Items.Add(newTaskItem);
        menu.Items.Add(editItem);

        _listPanel.ContextMenu = menu;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    internal void Refresh(MaintenanceMdConfig? config, MaintenanceStateStore? stateStore) {
        _viewModel.Config     = config;
        _viewModel.StateStore = stateStore;

        _enabledOnIdlePicker.SelectedValue = (config?.EnabledOnIdle ?? false) ? "on-idle" : "manual";

        RebuildList();
    }

    internal void SetFilter(string text) {
        _viewModel.FilterText = text.Trim();
        ApplyFilter();
    }

    private void ApplyFilter() {
        foreach (UIElement child in _listPanel.Children) {
            if (child is FrameworkElement fe && fe.Tag is MaintenanceTask task) {
                bool matches = string.IsNullOrEmpty(_viewModel.FilterText)
                    || PanelFilterHelper.Matches(task.Title, _viewModel.FilterText)
                    || PanelFilterHelper.Matches(task.Instructions ?? string.Empty, _viewModel.FilterText);
                fe.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Call when the runner starts a task. Updates the header to "Running now — [title]…"
    /// </summary>
    internal void OnRunnerStarted(string taskTitle) {
        _viewModel.RunnerActive     = true;
        _viewModel.RunningTaskTitle = taskTitle;
        StopCountdown();
        SyncStatusLabel();
    }

    /// <summary>
    /// Call when the runner finishes. Restarts the countdown.
    /// </summary>
    internal void OnRunnerCompleted() {
        _viewModel.RunnerActive     = false;
        _viewModel.RunningTaskTitle = null;
        SyncStatusLabel();
    }

    /// <summary>
    /// Sets the next expected idle/maintenance time for the countdown display.
    /// Pass <see cref="DateTimeOffset.MaxValue"/> to hide the countdown.
    /// </summary>
    internal void SetNextMaintenanceAt(DateTimeOffset next) {
        _viewModel.NextMaintenanceAt = next;
        if (!_viewModel.RunnerActive)
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
            var newEnabled = MaintenanceMdParser.ToggleTaskEnabled(mdPath, taskId);

            if (newEnabled is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenancePanelController: task '{taskId}' not found in {mdPath}");
                return;
            }

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

        if (_viewModel.Config is null || _viewModel.Config.Tasks is null || _viewModel.Config.Tasks.Count == 0) {
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
            var sortedTasks = _viewModel.Config.Tasks
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var task in sortedTasks)
                _listPanel.Children.Add(BuildTaskRow(task));
        }

        SyncStatusLabel();
        AppendReportsSection();
        ApplyFilter();
    }

    private void AppendReportsSection() {
        var separator = new Separator { Margin = new Thickness(0, 8, 0, 4) };
        separator.SetResourceReference(Separator.BackgroundProperty, "SubtleBorder");
        _listPanel.Children.Add(separator);

        var inboxBtn = new Button {
            Content                    = "Inbox",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness            = new Thickness(0),
            Padding                    = new Thickness(4, 3, 4, 3),
            Margin                     = new Thickness(0, 0, 0, 4),
            Cursor                     = Cursors.Hand,
        };
        inboxBtn.SetResourceReference(Button.StyleProperty,    "FlatButtonStyle");
        inboxBtn.SetResourceReference(Button.FontSizeProperty, "FontSizeSmall");
        inboxBtn.SetResourceReference(Button.ForegroundProperty, "SubtleText");
        inboxBtn.Click += (_, _) => _showInboxPanel();
        _listPanel.Children.Add(inboxBtn);

        var reportsExpander = new Expander {
            Header     = "Recent Reports",
            IsExpanded = false,
            Margin     = new Thickness(0, 4, 0, 0),
            Content    = BuildReportsContent(),
        };
        reportsExpander.SetResourceReference(Expander.StyleProperty, "ThemedExpanderStyle");
        _listPanel.Children.Add(reportsExpander);
    }

    private StackPanel BuildReportsContent() {
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var workspacePath = _getWorkspacePath();
        var reportsDir = workspacePath is null
            ? null
            : Path.Combine(workspacePath, ".squad", "maintenance-reports");

        List<string> reportFiles = [];
        if (reportsDir is not null && Directory.Exists(reportsDir)) {
            reportFiles = Directory.GetFiles(reportsDir, "*.md")
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        if (reportFiles.Count == 0) {
            var noReports = new TextBlock {
                Text   = "No reports yet.",
                Margin = new Thickness(4, 2, 4, 2),
            };
            noReports.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            noReports.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            content.Children.Add(noReports);
            return content;
        }

        var grouped = reportFiles
            .GroupBy(p => {
                var n    = Path.GetFileNameWithoutExtension(p);
                var dash = n.IndexOf('-');
                return dash > 0 ? n[..dash] : n;  // YYYYMMDD key
            })
            .OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        foreach (var dayGroup in grouped) {
            int totalTasks = dayGroup.Sum(f => {
                var (_, cnt) = ParseReportSummary(f);
                return cnt;
            });
            var representative = dayGroup.OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).First();

            var dateKey  = dayGroup.Key;
            var relLabel = FormatRelativeDay(dateKey);
            var taskWord = totalTasks == 1 ? "task" : "tasks";
            var label    = $"{relLabel} — {totalTasks} {taskWord}";
            var btn = new Button {
                Content                    = label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness            = new Thickness(0),
                Padding                    = new Thickness(4, 2, 4, 2),
                Cursor                     = Cursors.Hand,
                Tag                        = representative,
            };
            btn.SetResourceReference(Button.StyleProperty,      "FlatButtonStyle");
            btn.SetResourceReference(Button.FontSizeProperty,   "FontSizeSmall");
            btn.SetResourceReference(Button.ForegroundProperty, "SubtleText");
            var capturedPath = representative;
            btn.Click += (_, _) => _openInMarkdownEditor(capturedPath);
            content.Children.Add(btn);
        }

        return content;
    }

    private static string FormatRelativeDay(string dateKey) {
        if (dateKey.Length != 8 ||
            !DateTime.TryParseExact(dateKey, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return dateKey;

        var today    = DateTime.Today;
        var daysDiff = (today - date.Date).Days;

        if (daysDiff == 0) return "Today";
        if (daysDiff == 1) return "Yesterday";
        if (daysDiff < 7)  return date.ToString("dddd");
        if (daysDiff < 14) return $"Last {date:dddd}";
        return date.ToString("MMM d, yyyy");
    }

    private static (string date, int taskCount) ParseReportSummary(string filePath) {
        var name     = Path.GetFileNameWithoutExtension(filePath);
        var dashIdx  = name.IndexOf('-');
        var datePart = dashIdx > 0 ? name[..dashIdx] : name;
        var date     = datePart.Length == 8
            ? $"{datePart[..4]}-{datePart[4..6]}-{datePart[6..8]}"
            : datePart;

        int taskCount = 0;
        try {
            bool inTasksSection = false;
            foreach (var line in File.ReadLines(filePath)) {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("## ")) {
                    inTasksSection = string.Equals(trimmed, "## Tasks Run", StringComparison.Ordinal);
                    continue;
                }
                if (inTasksSection && trimmed.StartsWith("- "))
                    taskCount++;
            }
        }
        catch { /* ignore read errors */ }

        return (date, taskCount);
    }

    private Border BuildTaskRow(MaintenanceTask task) {
        var row = new Border {
            Padding    = new Thickness(0, 2, 0, 2),
            Background = Brushes.Transparent,
            Tag        = task,
        };

        if (!string.IsNullOrWhiteSpace(task.Instructions))
            MarkdownHoverPopup.Attach(
                row,
                buildHeader: () => {
                    var header = new TextBlock {
                        Text       = task.Title,
                        FontWeight = FontWeights.SemiBold,
                        Margin     = new Thickness(0, 0, 0, 6),
                    };
                    header.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
                    header.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                    return header;
                },
                getMarkdown: () => task.Instructions,
                maxWidth:    800,
                placementCallback: (popupSize, targetSize, _) => {
                    var screenPos = row.PointToScreen(new Point(0, 0));
                    if (screenPos.X - popupSize.Width < 0)
                        return new[] { new CustomPopupPlacement(new Point(targetSize.Width, 0), PopupPrimaryAxis.None) };
                    return new[] { new CustomPopupPlacement(new Point(-popupSize.Width, 0), PopupPrimaryAxis.None) };
                });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Child = grid;

        // ── Checkbox ─────────────────────────────────────────────────────────
        var check = new CheckBox {
            IsChecked         = task.Enabled,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 0, 6, 0),
        };
        check.SetResourceReference(CheckBox.FontSizeProperty, "FontSizeBody");
        check.SetResourceReference(CheckBox.ForegroundProperty, "LabelText");
        void ApplyCheckboxScale() {
            double scale = check.FontSize / 13.0;
            check.LayoutTransform = new ScaleTransform(scale, scale);
        }
        check.Loaded += (_, _) => ApplyCheckboxScale();
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(CheckBox.FontSizeProperty, typeof(CheckBox))
            .AddValueChanged(check, (_, _) => ApplyCheckboxScale());
        Grid.SetColumn(check, 0);
        check.Checked   += (_, _) => ToggleTaskEnabled(task.Id);
        check.Unchecked += (_, _) => ToggleTaskEnabled(task.Id);
        grid.Children.Add(check);

        // ── Right column: DockPanel with optional gear button + content ───────
        var rightColumn = new DockPanel { LastChildFill = true };
        Grid.SetColumn(rightColumn, 1);
        grid.Children.Add(rightColumn);

        var rightPanel = new StackPanel { Margin = new Thickness(0) };

        // Title
        var titleBlock = new TextBlock {
            Text         = task.Title,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, -3, 0, 2),
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        rightPanel.Children.Add(titleBlock);

        // Chips: frequency picker + safety — only visible when task is enabled
        var chipRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0),
            Visibility = task.Enabled ? Visibility.Visible : Visibility.Collapsed };
        var taskIdForFreq   = task.Id;
        
        // Build day options for weekly submenu
        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var currentFreq = task.Frequency;
        var currentDay = currentFreq.StartsWith("weekly-") ? currentFreq.Substring(7) : "";
        
        var frequencyOptions = new (string DisplayName, string Value, (string DisplayName, string Value)[]?)[] {
            ("Always",         "always",         null),
            ("Daily",          "daily",          null),
            ("Weekly",         "weekly",         days.Select(d => (d, $"weekly-{d}")).ToArray()),
            ("Monthly",        "monthly",        null),
            ("After Commits",  "after-commits",  null),
        };

        CompactPickerButton? frequencyPicker = null;
        frequencyPicker = new CompactPickerButton(
            headerText:     "Run Frequency:",
            optionsWithSubmenus: frequencyOptions,
            selectedValue:  task.Frequency,
            onValueChanged: newFreq => ChangeTaskFrequency(taskIdForFreq, newFreq),
            getButtonLabel: freq => GetFrequencyDisplayText(freq));
        chipRow.Children.Add(frequencyPicker.Control);
        var optsSummary = BuildOptionsSummary(task.Options);
        if (!string.IsNullOrEmpty(optsSummary)) {
            var summaryBlock = new TextBlock {
                Text              = $" \u2014 {optsSummary}",
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 0, 2),
            };
            summaryBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            chipRow.Children.Add(summaryBlock);
        }
        rightPanel.Children.Add(chipRow);

        // Last-run status
        var lastRun = _viewModel.StateStore?.GetLastRunAt(task.Id);
        if (lastRun.HasValue) {
            var relTime = StatusTimingPresentation.FormatRelativeTimestamp(
                new DateTimeOffset(lastRun.Value, TimeSpan.Zero));
            var lastRunBlock = new TextBlock {
                Text   = $"Last run: {relTime}",
                Margin = new Thickness(5, -1, 0, 0),
            };
            lastRunBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            lastRunBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            rightPanel.Children.Add(lastRunBlock);
        }

        // Gear button + popup (if task has options)
        if (task.Options is { Count: > 0 }) {
            var popupOptionsPanel = new StackPanel { Margin = new Thickness(0) };
            var popupTitleBlock = new TextBlock {
                Text         = task.Title,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 6),
            };
            popupTitleBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            popupTitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            popupOptionsPanel.Children.Add(popupTitleBlock);
            foreach (var opt in task.Options) {
                if (!string.Equals(opt.Type, "checkbox", StringComparison.OrdinalIgnoreCase) &&
                    opt.Label is { Length: > 0 }) {
                    var labelBlock = new TextBlock {
                        Text         = opt.Label,
                        TextWrapping = TextWrapping.Wrap,
                        Margin       = new Thickness(0, 2, 0, 1),
                    };
                    labelBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
                    labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
                    if (!string.IsNullOrEmpty(opt.Tooltip))
                        labelBlock.ToolTip = MakeThemedToolTip(opt.Tooltip);
                    popupOptionsPanel.Children.Add(labelBlock);
                }
                if (opt.Choices is { Count: > 0 }) {
                    foreach (var choice in opt.Choices) {
                        var rb = new RadioButton {
                            Content   = choice.Value,
                            GroupName = $"task-{task.Id}-{opt.Key}",
                            IsChecked = string.Equals(choice.Value, opt.RawValue, StringComparison.OrdinalIgnoreCase),
                            Margin    = new Thickness(8, 1, 0, 2),
                        };
                        rb.SetResourceReference(RadioButton.FontSizeProperty,   "FontSizeBody");
                        rb.SetResourceReference(RadioButton.ForegroundProperty, "ImportantText");
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
                        popupOptionsPanel.Children.Add(rb);
                    }
                }
                else if (string.Equals(opt.Type, "checkbox", StringComparison.OrdinalIgnoreCase)) {
                    var isChecked = IsStringTrueValue(opt.RawValue);
                    var cb = new CheckBox {
                        Content   = opt.Label ?? opt.Key,
                        IsChecked = isChecked,
                        Margin    = new Thickness(0, 1, 0, 1),
                    };
                    cb.SetResourceReference(CheckBox.FontSizeProperty,   "FontSizeBody");
                    cb.SetResourceReference(CheckBox.ForegroundProperty, "ImportantText");
                    if (!string.IsNullOrEmpty(opt.Tooltip))
                        cb.ToolTip = MakeThemedToolTip(opt.Tooltip);
                    var capturedPath   = GetMaintenanceMdPath();
                    var capturedTaskId = task.Id;
                    var capturedOptKey = opt.Key;
                    cb.Checked += (_, _) => {
                        if (capturedPath is not null)
                            MaintenanceMdParser.UpdateOptionValue(capturedPath, capturedTaskId, capturedOptKey, "true");
                    };
                    cb.Unchecked += (_, _) => {
                        if (capturedPath is not null)
                            MaintenanceMdParser.UpdateOptionValue(capturedPath, capturedTaskId, capturedOptKey, "false");
                    };
                    popupOptionsPanel.Children.Add(cb);
                }
            }

            var popupBorder = new Border {
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(10, 4, 10, 10),
                MinWidth        = 180,
                Child           = popupOptionsPanel,
            };
            popupBorder.SetResourceReference(Border.BackgroundProperty,  "InputSurface");
            popupBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

            var gearButton = new Button {
                Content           = "⚙",
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(4, 0, 0, 0),
                Cursor            = Cursors.Hand,
                ToolTip           = "Settings",
            };
            gearButton.SetResourceReference(Button.StyleProperty,      "FlatButtonStyle");
            gearButton.SetResourceReference(Button.FontSizeProperty,   "FontSizeSmall");
            gearButton.SetResourceReference(Button.ForegroundProperty, "SubtleText");

            var popup = new Popup {
                StaysOpen               = false,
                Placement               = PlacementMode.Custom,
                PlacementTarget         = gearButton,
                AllowsTransparency      = false,
                PopupAnimation          = PopupAnimation.None,
                CustomPopupPlacementCallback = (popupSize, targetSize, _) => {
                    // Prefer right of the gear button; fall back to left if it would clip off-screen.
                    var screen = SystemParameters.PrimaryScreenWidth;
                    var target = gearButton.PointToScreen(new Point(0, 0));
                    var rightX = targetSize.Width;                  // popup left = right edge of target
                    var leftX  = -popupSize.Width;                  // popup right = left edge of target
                    var fitsRight = (target.X + targetSize.Width + popupSize.Width) <= screen;
                    var x = fitsRight ? rightX : leftX;
                    return [new CustomPopupPlacement(new Point(x, 0), PopupPrimaryAxis.Vertical)];
                },
                Child              = popupBorder,
            };

            gearButton.Click += (_, _) => {
                if (popup.IsOpen) {
                    popup.IsOpen = false;
                    return;
                }
                popup.IsOpen = true;
            };

            DockPanel.SetDock(gearButton, Dock.Right);
            rightColumn.Children.Add(gearButton);
        }

        rightColumn.Children.Add(rightPanel);

        // Per-task context menu — "Run Now" and "Edit Task"
        var taskMenu    = new ContextMenu();
        taskMenu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        var runNowItem = new MenuItem { Header = "Run Now" };
        runNowItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        var capturedTaskIdForRun = task.Id;
        runNowItem.Click += (_, _) => _runTask(capturedTaskIdForRun);
        taskMenu.Items.Add(runNowItem);
        var sep = new Separator();
        sep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        taskMenu.Items.Add(sep);
        var editTaskItem = new MenuItem { Header = "Edit Task..." };
        editTaskItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        editTaskItem.Click += (_, _) => {
            var ownerWindow = Window.GetWindow(_listPanel);
            if (ownerWindow is null) return;
            var capturedTask = task;
            var editor = new MaintenanceTaskEditorWindow(
                ownerWindow,
                capturedTask,
                () => new ApplicationSettingsStore().Load(),
                _reloadPanel,
                onReviseWithAi: _onReviseWithAi,
                onDirectRevise: _onDirectRevise);
            editor.Closed += (_, _) => FloatingWindowPositionStore.Shared.Save("MaintenanceTaskEditor", editor);
            FloatingWindowPositionStore.Shared.TryRestore("MaintenanceTaskEditor", editor);
            editor.Show();
        };
        taskMenu.Items.Add(editTaskItem);
        row.ContextMenu = taskMenu;

        return row;
    }

    private static string FrequencyTooltip(string frequency) {
        var freqLower = frequency.ToLowerInvariant();
        
        if (freqLower.StartsWith("weekly-")) {
            var dayPart = frequency.Substring(7);
            return $"Runs at most once per calendar week on {dayPart}s.";
        }
        
        return freqLower switch {
            "daily"          => "Runs at most once per calendar day.",
            "weekly"         => "Runs at most once per calendar week (Monday–Sunday UTC). Legacy format, treating as Monday.",
            "monthly"        => "Runs at most once per calendar month.",
            "after-commits"  => "Runs once per new commit since the last run.",
            "per-commit"     => "Runs once per new commit since the last run.",
            "always"         => "Runs every idle cycle with no cooldown.",
            _                => $"Frequency: {frequency}",
        };
    }

    private static string GetFrequencyDisplayText(string frequency) {
        var freqLower = frequency.ToLowerInvariant();
        
        if (freqLower.StartsWith("weekly-")) {
            var dayPart = frequency.Substring(7);
            return $"every {dayPart}";
        }
        
        return freqLower switch {
            "daily"    => "Daily",
            "weekly"   => "Weekly",
            "monthly"  => "Monthly",
            "always"   => "Always",
            "after-commits" => "After Commits",
            _          => frequency,
        };
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

    private static bool IsStringTrueValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1",    StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a concise display string from a list of maintenance options, showing only
    /// values that are "active": checked checkboxes (by label/key), selected radio values,
    /// and non-empty free-text values.  Returns an empty string when there is nothing to show.
    /// </summary>
    internal static string BuildOptionsSummary(IReadOnlyList<MaintenanceOption>? options) {
        if (options is null or { Count: 0 })
            return string.Empty;

        var parts = new List<string>();
        foreach (var opt in options) {
            if (string.Equals(opt.Type, "checkbox", StringComparison.OrdinalIgnoreCase)) {
                if (IsStringTrueValue(opt.RawValue))
                    parts.Add(opt.Label ?? opt.Key);
            }
            else if (opt.Choices is { Count: > 0 }) {
                if (!string.IsNullOrEmpty(opt.RawValue))
                    parts.Add(opt.RawValue);
            }
            else {
                if (!string.IsNullOrEmpty(opt.RawValue))
                    parts.Add(opt.RawValue);
            }
        }
        return string.Join(", ", parts);
    }

    // ── Status header ─────────────────────────────────────────────────────────

    internal void ShowTransientStatus(string message) {
        _transientStatusTimer?.Stop();

        _statusLabel.Text       = message;
        _statusLabel.Visibility = Visibility.Visible;

        _transientStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _transientStatusTimer.Tick += (_, _) => {
            _transientStatusTimer?.Stop();
            _transientStatusTimer = null;
            SyncStatusLabel();
        };
        _transientStatusTimer.Start();
    }

    private void SyncStatusLabel() {
        if (_viewModel.RunnerActive) {
            var title = _viewModel.RunningTaskTitle ?? "task";
            _statusLabel.Text = $"● Running — {title}…";
            _statusLabel.Visibility = Visibility.Visible;
            return;
        }

        if (_viewModel.Config is null) {
            _statusLabel.Visibility = Visibility.Collapsed;
            return;
        }

        // If no next time set, hide the countdown
        if (_viewModel.NextMaintenanceAt == DateTimeOffset.MaxValue) {
            _statusLabel.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateCountdownLabel();
    }

    private void UpdateCountdownLabel() {
        var remaining = _viewModel.NextMaintenanceAt - DateTimeOffset.Now;
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

    // ── Width measurement ─────────────────────────────────────────────────────

    private double MeasureTextWidth(string text, FontWeight weight)
    {
        var fontSize = _listPanel.TryFindResource("FontSizeBody") is double fs ? fs : 13.0;
        var typeface = new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, weight, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(_listPanel).PixelsPerDip;
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        return ft.Width;
    }

    public double? GetMaximumUsefulWidth(int maxRows = 50)
    {
        double maxRowWidth = 0;
        int count = 0;

        if (_listPanel.IsLoaded)
        {
            foreach (var child in _listPanel.Children)
            {
                if (count >= maxRows) break;
                if (child is not Border { Tag: MaintenanceTask task }) continue;
                var textWidth = MeasureTextWidth(task.Title, FontWeights.Normal);
                const double perRowChrome = 19; // checkbox col (~13) + checkbox right margin (6)
                maxRowWidth = Math.Max(maxRowWidth, textWidth + perRowChrome);
                count++;
            }
        }

        const double panelChrome = 43; // padding(24) + border(2) + scrollbar(17)
        if (maxRowWidth <= 0)
            return 260 + panelChrome;

        return maxRowWidth + panelChrome;
    }

    // ── Countdown timer ───────────────────────────────────────────────────────

    private void StartCountdown() {
        if (_viewModel.NextMaintenanceAt == DateTimeOffset.MaxValue) return;
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
