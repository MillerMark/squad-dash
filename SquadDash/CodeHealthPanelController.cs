namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>Manages content in the inline Maintenance panel.</summary>
internal sealed class CodeHealthPanelController {

    private readonly StackPanel           _listPanel;
    private readonly TextBlock            _statusLabel;
    private readonly CompactPickerButton  _enabledOnIdlePicker;
    private readonly Func<string?>        _getWorkspacePath;
    private readonly Action<string, bool> _toggleTaskEnabled;
    private readonly Action               _reloadPanel;
    private readonly Action<string>       _openInMarkdownEditor;
    private readonly Action<string>       _openReportViewer;
    private readonly Action               _showInboxPanel;
    private readonly Action<string>       _runTask;
    private readonly Action               _simulateIdle;
    private readonly Action<RichTextBox, string>?            _onReviseWithAi;
    private readonly Action<RichTextBox, string, string>?    _onDirectRevise;

    private readonly CodeHealthPanelViewModel _viewModel = new();
    internal CodeHealthPanelViewModel ViewModel => _viewModel;
    private DispatcherTimer?       _countdownTimer;
    private DispatcherTimer?       _transientStatusTimer;

    // ── Construction ─────────────────────────────────────────────────────────

    internal CodeHealthPanelController(
        StackPanel           listPanel,
        TextBlock            statusLabel,
        ContentControl       enabledOnIdleHost,
        Func<string?>        getWorkspacePath,
        Action<string, bool> toggleTaskEnabled,
        Action               reloadPanel,
        Action<string>       openInMarkdownEditor,
        Action<string>       openReportViewer,
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
        _openReportViewer       = openReportViewer;
        _showInboxPanel         = showInboxPanel;
        _runTask                = runTask;
        _simulateIdle           = simulateIdle;
        _onReviseWithAi         = onReviseWithAi;
        _onDirectRevise         = onDirectRevise;

        _enabledOnIdlePicker = new CompactPickerButton(
            headerText:     "Code Health Tasks:",
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
                    "CodeHealthPanelController: workspace path is null; cannot create task");
                return;
            }
            var mdPath = Path.Combine(workspacePath, ".squad", "code-health.md");
            if (!File.Exists(mdPath)) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"CodeHealthPanelController: CodeHealth file not found at {mdPath}");
                return;
            }
            var newId   = Guid.NewGuid().ToString("N")[..8];
            var newTask = new CodeHealthTask(
                Id:            newId,
                Enabled:       false,
                Frequency:     "weekly",
                Safety:        "branch",
                Title:         "New Task",
                Instructions:  "Describe what the agent should do here.\n\nAdd as many details as needed.",
                SourceFilePath: mdPath);
            try {
                CodeHealthMdParser.AppendTask(mdPath, newTask);
            }
            catch (Exception ex) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"CodeHealthPanelController: failed to create task: {ex.Message}");
                return;
            }
            var ownerWindow = Window.GetWindow(_listPanel);
            if (ownerWindow is null) return;
            var editor = new CodeHealthTaskEditorWindow(
                ownerWindow,
                newTask,
                () => new ApplicationSettingsStore().Load(),
                _reloadPanel,
                _reloadPanel,
                onReviseWithAi: _onReviseWithAi,
                onDirectRevise: _onDirectRevise,
                stateStore: _viewModel.StateStore,
                workspacePath: _getWorkspacePath?.Invoke());
            editor.Closed += (_, _) => FloatingWindowPositionStore.Shared.Save("CodeHealthTaskEditor", editor);
            FloatingWindowPositionStore.Shared.TryRestore("CodeHealthTaskEditor", editor);
            editor.Show();
        };

        var editItem = new MenuItem { Header = "Edit Code Health File…" };
        editItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        editItem.Click += (_, _) => {
            var workspacePath = _getWorkspacePath();
            if (workspacePath is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    "CodeHealthPanelController: workspace path is null; cannot open code health file");
                return;
            }
            var mdPath = Path.Combine(workspacePath, ".squad", "code-health.md");
            if (!File.Exists(mdPath)) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"CodeHealthPanelController: CodeHealth file not found at {mdPath}");
                return;
            }
            try {
                _openInMarkdownEditor(mdPath);
            } catch (Exception ex) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"CodeHealthPanelController: failed to open code health file: {ex.Message}");
            }
        };

        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        menu.Items.Add(newTaskItem);
        menu.Items.Add(editItem);

        _listPanel.ContextMenu = menu;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    internal void Refresh(CodeHealthMdConfig? config, CodeHealthStateStore? stateStore) {
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
            if (child is FrameworkElement fe && fe.Tag is CodeHealthTask task) {
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
        return workspacePath is null ? null : Path.Combine(workspacePath, ".squad", "code-health.md");
    }

    private void SetEnabledOnIdle(bool value) {
        var mdPath = GetMaintenanceMdPath();
        if (mdPath is null) return;
        CodeHealthMdParser.UpdateEnabledOnIdle(mdPath, value);
    }

    /// <summary>
    /// Reads <c>.squad/code-health.md</c>, locates the <paramref name="taskId"/> entry,
    /// flips its <c>enabled:</c> value, writes the file back preserving all other content,
    /// then invokes the host's reload callback so the panel refreshes.
    /// </summary>
    internal void ToggleTaskEnabled(string taskId) {
        var workspacePath = _getWorkspacePath();
        if (workspacePath is null) return;

        var mdPath = Path.Combine(workspacePath, ".squad", "code-health.md");
        if (!File.Exists(mdPath)) return;

        try {
            var newEnabled = CodeHealthMdParser.ToggleTaskEnabled(mdPath, taskId);

            if (newEnabled is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"CodeHealthPanelController: task '{taskId}' not found in {mdPath}");
                return;
            }

            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthPanelController: task '{taskId}' toggled → enabled={newEnabled.Value}");

            _toggleTaskEnabled(taskId, newEnabled.Value);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthPanelController: failed to toggle task '{taskId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the <c>frequency:</c> field for <paramref name="taskId"/> in code-health.md
    /// and reloads the panel so the change is reflected immediately.
    /// </summary>
    private void ChangeTaskFrequency(string taskId, string newFrequency) {
        var mdPath = GetMaintenanceMdPath();
        if (mdPath is null) return;
        
        CodeHealthMdParser.UpdateFrequency(mdPath, taskId, newFrequency);
        _reloadPanel();
    }

    // ── List construction ─────────────────────────────────────────────────────

    private void RebuildList() {
        _listPanel.Children.Clear();

        if (_viewModel.Config is null || _viewModel.Config.Tasks is null || _viewModel.Config.Tasks.Count == 0) {
            var empty = new TextBlock {
                Text         = "No code health tasks configured.",
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
            : Path.Combine(workspacePath, ".squad", "code-health-reports");

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
            btn.Click += (_, _) => _openReportViewer(capturedPath);
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

    private Border BuildTaskRow(CodeHealthTask task) {
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
                getMarkdown: () => ResolveAndMarkTemplateVariables(
                    task.Instructions, task.Id, _viewModel.StateStore, _getWorkspacePath()),
                maxWidth:    800,
                placementCallback: (popupSize, targetSize, _) => {
                    var screenPos = row.PointToScreen(new Point(0, 0));
                    if (screenPos.X - popupSize.Width < 0)
                        return new[] { new CustomPopupPlacement(new Point(targetSize.Width, 0), PopupPrimaryAxis.None) };
                    return new[] { new CustomPopupPlacement(new Point(-popupSize.Width, 0), PopupPrimaryAxis.None) };
                },
                postProcessDocument: doc => {
                    var highlightBrush = row.TryFindResource("TemplateVariableHighlight") as Brush
                                      ?? new SolidColorBrush(Color.FromRgb(0xD6, 0xE8, 0xFF));
                    HighlightSentinelRuns(doc, highlightBrush);
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

        // Title with override indicator
        var workspacePath = _getWorkspacePath();
        var isOverridden = workspacePath is not null && CodeHealthMdParser.IsTaskOverridden(task.Id, workspacePath);
        var titleText = isOverridden ? $"{task.Title} *" : task.Title;
        var titleBlock = new TextBlock {
            Text         = titleText,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, -3, 0, 2),
            FontWeight   = isOverridden ? FontWeights.Bold : FontWeights.Normal,
            FontStyle    = isOverridden ? FontStyles.Italic : FontStyles.Normal,
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        if (isOverridden)
            titleBlock.ToolTip = MakeThemedToolTip("This task has been customized. Click 'Revert to Default Implementation' to restore the system version.");
        rightPanel.Children.Add(titleBlock);

        // Chips: frequency picker + safety — only visible when task is enabled
        var chipRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0),
            Visibility = task.Enabled ? Visibility.Visible : Visibility.Collapsed };
        var taskIdForFreq   = task.Id;
        
        // Build day options for weekly submenu
        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var currentFreq = task.Frequency;
        
        // Normalise legacy per-commit / after-commits → every-1-commits so the submenu check state works
        var normalizedFreq = (string.Equals(currentFreq, "per-commit",    StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(currentFreq, "after-commits", StringComparison.OrdinalIgnoreCase))
            ? "every-1-commits"
            : currentFreq;

        // Build "After Commits" submenu entries
        var commitThresholds = new[] { 1, 2, 3, 4, 5, 10, 15, 20, 25, 50, 100 };
        SubitemEntry[] commitSubitems = new[] { SubitemEntry.Header("After Commits:"), SubitemEntry.Separator }
            .Concat(commitThresholds.Select(n => SubitemEntry.Item($"{n} or more", $"every-{n}-commits")))
            .ToArray();

        var frequencyOptions = new (string DisplayName, string Value, SubitemEntry[]?)[] {
            ("Always",         "always",         null),
            ("Daily",          "daily",          null),
            ("Weekly",         "weekly",         days.Select(d => SubitemEntry.Item(d, $"weekly-{d}")).ToArray()),
            ("Monthly",        "monthly",        null),
            ("After Commits",  "after-commits",  commitSubitems),
        };

        CompactPickerButton? frequencyPicker = null;
        frequencyPicker = new CompactPickerButton(
            headerText:     "Run Frequency:",
            optionsWithSubmenus: frequencyOptions,
            selectedValue:  normalizedFreq,
            onValueChanged: newFreq => ChangeTaskFrequency(taskIdForFreq, newFreq),
            getButtonLabel: freq => GetFrequencyDisplayText(freq));
        chipRow.Children.Add(frequencyPicker.Control);
        
        // Safety level picker
        var effectiveSafety = GetEffectiveSafetyLevel(task);
        var safetyPickerOptions = new (string DisplayName, string Value)[] {
            ("report (no changes)",                            "report-only"),
            ("branch (create a new branch before making changes)", "branch"),
            ("direct (make changes directly to current branch)", "direct"),
        };
        
        var safetyPicker = new CompactPickerButton(
            headerText:     "Safety level",
            options:        safetyPickerOptions,
            selectedValue:  effectiveSafety,
            onValueChanged: newSafety => ChangeTaskSafety(task.Id, newSafety),
            getButtonLabel: safety => GetSafetyDisplayText(safety));
        safetyPicker.Control.ToolTip = MakeThemedToolTip(GetSafetyDisplayTooltip(effectiveSafety));
        chipRow.Children.Add(safetyPicker.Control);
        
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
                Margin = new Thickness(5, 0, 0, 0),
            };
            lastRunBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            lastRunBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            rightPanel.Children.Add(lastRunBlock);
        }

        // Gear button + popup (if task has options OR has safety options)
        bool hasSafetyOptions = task.HasSafetyOptions;
        bool hasTaskOptions = task.Options is { Count: > 0 };
        
        if (hasTaskOptions || hasSafetyOptions) {
            var popupOptionsPanel = new StackPanel { Margin = new Thickness(0) };
            var popupTitleBlock = new TextBlock {
                Text         = task.Title,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 6),
            };
            popupTitleBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            popupTitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            popupOptionsPanel.Children.Add(popupTitleBlock);
            
            // Add safety options radio buttons if enabled
            if (hasSafetyOptions) {
                var safetyLabel = new TextBlock {
                    Text         = "Safety Level",
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 2, 0, 1),
                };
                safetyLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
                safetyLabel.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
                safetyLabel.ToolTip = MakeThemedToolTip("Control execution scope: report-only (no changes), branch (create branch), or direct (current branch)");
                popupOptionsPanel.Children.Add(safetyLabel);
                
                var currentSafety = _viewModel.StateStore?.GetSafetyOverride(task.Id) ?? task.Safety;
                
                var safetyOptions = new[] {
                    ("report-only", "Report (no code changes)"),
                    ("branch",      "Branch (create before changes)"),
                    ("direct",      "Direct (changes on this branch)"),
                };
                
                foreach (var (safetyValue, safetyLabel2) in safetyOptions) {
                    var rb = new RadioButton {
                        Content   = safetyLabel2,
                        GroupName = $"task-{task.Id}-safety",
                        IsChecked = string.Equals(safetyValue, currentSafety, StringComparison.OrdinalIgnoreCase),
                        Margin    = new Thickness(8, 1, 0, 2),
                    };
                    rb.SetResourceReference(RadioButton.FontSizeProperty,   "FontSizeBody");
                    rb.SetResourceReference(RadioButton.ForegroundProperty, "ImportantText");
                    var capturedSafetyValue = safetyValue;
                    var capturedTaskId = task.Id;
                    var capturedStateStore = _viewModel.StateStore;
                    rb.Checked += (_, _) => {
                        capturedStateStore?.SetSafetyOverride(capturedTaskId, capturedSafetyValue);
                    };
                    popupOptionsPanel.Children.Add(rb);
                }
                
                // Add separator if there are task options
                if (hasTaskOptions) {
                    var optionsSeparator = new Separator { Margin = new Thickness(0, 4, 0, 4) };
                    optionsSeparator.SetResourceReference(Separator.BackgroundProperty, "SubtleBorder");
                    popupOptionsPanel.Children.Add(optionsSeparator);
                }
            }
            
            // Add task-specific options
            if (hasTaskOptions && task.Options is not null) {
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
                            if (!string.IsNullOrEmpty(choice.Tooltip))
                                rb.ToolTip = MakeThemedToolTip(choice.Tooltip);
                            var capturedPath   = GetMaintenanceMdPath();
                            var capturedTaskId = task.Id;
                            var capturedOptKey = opt.Key;
                            var capturedValue  = choice.Value;
                            rb.Checked += (_, _) => {
                                if (capturedPath is not null)
                                    CodeHealthMdParser.UpdateOptionValue(capturedPath, capturedTaskId, capturedOptKey, capturedValue);
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
                                CodeHealthMdParser.UpdateOptionValue(capturedPath, capturedTaskId, capturedOptKey, "true");
                        };
                        cb.Unchecked += (_, _) => {
                            if (capturedPath is not null)
                                CodeHealthMdParser.UpdateOptionValue(capturedPath, capturedTaskId, capturedOptKey, "false");
                        };
                        popupOptionsPanel.Children.Add(cb);
                    }
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

            var gearIcon = new System.Windows.Shapes.Path
            {
                Data            = Geometry.Parse("M661,446.875C542.75,446.875,446.875,542.75,446.875,661C446.875,779.3125,542.75,875.1875,661,875.1875C779.3125,875.1875,875.1875,779.3125,875.1875,661C875.1875,542.75,779.3125,446.875,661,446.875z M583.5,-0.5L738.5625,-0.5C759.9375,-0.5,777.3125,16.875,777.3125,38.3125L777.3125,209.5 843.125,229.9375C858.875,236.5625,874.1875,244.0625,889,252.375L898.9375,258.6875 1019.1875,138.5C1034.3125,123.3125,1058.8125,123.3125,1074,138.5L1183.5625,248.0625C1198.75,263.25,1198.75,287.75,1183.5625,302.875L1063.3125,423.1875 1066.875,428.125C1083.8125,457.5625,1097.625,488.9375,1107.875,521.875L1113.125,544.75 1283.75,544.75C1305.1875,544.75,1322.5,562.125,1322.5,583.5L1322.5,738.5625C1322.5,759.9375,1305.1875,777.3125,1283.75,777.3125L1113.125,777.3125 1107.875,800.1875C1097.625,833.125,1083.8125,864.5,1066.875,893.9375L1063.3125,898.875 1183.5625,1019.1875C1198.75,1034.3125,1198.75,1058.8125,1183.5625,1074L1074,1183.5625C1058.8125,1198.75,1034.3125,1198.75,1019.1875,1183.5625L898.9375,1063.375 889,1069.6875C874.1875,1078,858.875,1085.4375,843.125,1092.125L777.3125,1112.5625 777.3125,1283.75C777.3125,1305.1875,759.9375,1322.5,738.5625,1322.5L583.5,1322.5C562.125,1322.5,544.75,1305.1875,544.75,1283.75L544.75,1112.5625 478.9375,1092.125C463.1875,1085.4375,447.875,1078,433.0625,1069.6875L423.125,1063.375 302.875,1183.5625C287.75,1198.75,263.25,1198.75,248.0625,1183.5625L138.5,1074C123.3125,1058.8125,123.3125,1034.3125,138.5,1019.1875L258.75,898.875 255.125,893.9375C238.25,864.5,224.4375,833.125,214.1875,800.1875L208.9375,777.3125 38.3125,777.3125C16.875,777.3125,-0.5,759.9375,-0.5,738.5625L-0.5,583.5C-0.5,562.125,16.875,544.75,38.3125,544.75L208.9375,544.75 214.1875,521.875C224.4375,488.9375,238.25,457.5625,255.125,428.125L258.75,423.1875 138.5,302.875C123.3125,287.75,123.3125,263.25,138.5,248.0625L248.0625,138.5C263.25,123.3125,287.75,123.3125,302.875,138.5L423.125,258.6875 433.0625,252.375C447.875,244.0625,463.1875,236.5625,478.9375,229.9375L544.75,209.5 544.75,38.3125C544.75,16.875,562.125,-0.5,583.5,-0.5z"),
                Stretch         = Stretch.Uniform,
                IsHitTestVisible = false,
            };
            gearIcon.SetResourceReference(System.Windows.Shapes.Path.FillProperty,   "SubtleText");
            gearIcon.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeSmall");
            gearIcon.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeSmall");

            var gearButton = new Button {
                Content           = gearIcon,
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(4, 0, 0, 0),
                Cursor            = Cursors.Hand,
                ToolTip           = MakeThemedToolTip($"{task.Title} Settings"),
            };
            gearButton.SetResourceReference(Button.StyleProperty, "FlatButtonStyle");

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

        // Per-task context menu — "Run Now", "Edit Task", and optionally "Revert to Default Implementation"
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
            var editor = new CodeHealthTaskEditorWindow(
                ownerWindow,
                capturedTask,
                () => new ApplicationSettingsStore().Load(),
                _reloadPanel,
                _reloadPanel,
                onReviseWithAi: _onReviseWithAi,
                onDirectRevise: _onDirectRevise,
                stateStore: _viewModel.StateStore,
                workspacePath: _getWorkspacePath?.Invoke());
            editor.Closed += (_, _) => FloatingWindowPositionStore.Shared.Save("CodeHealthTaskEditor", editor);
            FloatingWindowPositionStore.Shared.TryRestore("CodeHealthTaskEditor", editor);
            editor.Show();
        };
        taskMenu.Items.Add(editTaskItem);
        
        // Add "Revert to Default Implementation" and/or "Move changes to main code health file" if task is overridden
        bool taskIsOverridden = workspacePath is not null && CodeHealthMdParser.ShouldShowRevertOption(task.Id, workspacePath);
        bool showPromote      = SquadDashEnvironment.IsDeveloperMode && taskIsOverridden;

        if (taskIsOverridden) {
            var revertSeparator = new Separator();
            revertSeparator.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
            taskMenu.Items.Add(revertSeparator);

            var revertItem = new MenuItem { Header = "Revert to Default Implementation" };
            revertItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            var capturedTaskId = task.Id;
            var capturedWorkspacePath = workspacePath!;
            revertItem.Click += (_, _) => {
                try {
                    CodeHealthMdParser.RevertTaskToDefault(capturedTaskId, capturedWorkspacePath);
                    _reloadPanel();
                }
                catch (Exception ex) {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"CodeHealthPanelController: failed to revert task '{capturedTaskId}': {ex.Message}");
                }
            };
            taskMenu.Items.Add(revertItem);

            if (showPromote) {
                var promoteItem = new MenuItem { Header = "Move changes to main code health file" };
                promoteItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                var capturedTaskIdForPromote = task.Id;
                var capturedWorkspaceForPromote = workspacePath!;
                promoteItem.Click += (_, _) => {
                    try {
                        CodeHealthMdParser.PromoteOverrideToSystemFile(capturedTaskIdForPromote, capturedWorkspaceForPromote);
                        _reloadPanel();
                    }
                    catch (Exception ex) {
                        SquadDashTrace.Write(TraceCategory.General,
                            $"CodeHealthPanelController: failed to promote task '{capturedTaskIdForPromote}': {ex.Message}");
                    }
                };
                taskMenu.Items.Add(promoteItem);
            }
        }
        
        row.ContextMenu = taskMenu;

        return row;
    }

    private static string FrequencyTooltip(string frequency) {
        var freqLower = frequency.ToLowerInvariant();
        
        if (freqLower.StartsWith("weekly-")) {
            var dayPart = frequency.Substring(7);
            return $"Runs at most once per calendar week on {dayPart}s.";
        }

        if (freqLower.StartsWith("every-") && freqLower.EndsWith("-commits")) {
            var nStr = freqLower["every-".Length..^"-commits".Length];
            if (int.TryParse(nStr, out var n))
                return n == 1
                    ? "Runs once per new commit since the last run."
                    : $"Runs after every {n} or more new commits since the last run.";
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

        if (freqLower.StartsWith("every-") && freqLower.EndsWith("-commits")) {
            var nStr = freqLower["every-".Length..^"-commits".Length];
            return int.TryParse(nStr, out var n) ? $"After {n}+ Commits" : "After Commits";
        }
        
        return freqLower switch {
            "daily"         => "Daily",
            "weekly"        => "Weekly",
            "monthly"       => "Monthly",
            "always"        => "Always",
            "after-commits" => "After Commits",
            "per-commit"    => "After 1+ Commits",
            _               => frequency,
        };
    }

    private static ToolTip MakeThemedToolTip(string text) => ToolTipHelper.MakeThemedToolTip(text);

    private static bool IsStringTrueValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1",    StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a concise display string from a list of maintenance options, showing only
    /// values that are "active": checked checkboxes (by label/key), selected radio values,
    /// and non-empty free-text values.  Returns an empty string when there is nothing to show.
    /// </summary>
    internal static string BuildOptionsSummary(IReadOnlyList<CodeHealthOption>? options) {
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

    // ── Template variable resolution ─────────────────────────────────────────

    /// <summary>The sentinel character that brackets a resolved variable value in pre-processed instructions.</summary>
    internal const char HighlightSentinel = '\x01';

    private static readonly Regex TemplateVarRegex =
        new(@"\{\{([A-Za-z_][A-Za-z0-9_]*)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Replaces known <c>{{identifier}}</c> tokens in <paramref name="instructions"/> with their
    /// current resolved values, wrapping each substitution in <see cref="HighlightSentinel"/>
    /// characters so callers can highlight those spans in the rendered FlowDocument.
    /// Conditional tokens such as <c>{{#if …}}</c> and <c>{{/if}}</c> are left untouched.
    /// </summary>
    internal static string ResolveAndMarkTemplateVariables(
        string?                instructions,
        string                 taskId,
        CodeHealthStateStore? stateStore,
        string?                workspacePath) {
        if (string.IsNullOrEmpty(instructions))
            return instructions ?? string.Empty;

        return TemplateVarRegex.Replace(instructions, m => {
            var varName  = m.Groups[1].Value;
            string? resolved = varName switch {
                "last_reviewed_sha" =>
                    stateStore?.GetLastCommitSha(taskId) is { Length: > 0 } sha ? sha : "(none)",
                "new_commit_count" =>
                    workspacePath is { Length: > 0 }
                        // TODO: migrate ResolveAndMarkTemplateVariables to async so this bridge can be removed.
                        ? stateStore?.GetCommitCountSinceAsync(taskId, workspacePath).GetAwaiter().GetResult().ToString() ?? "(unknown)"
                        : "(pending)",
                _ => null,
            };
            return resolved is not null
                ? $"{HighlightSentinel}{resolved}{HighlightSentinel}"
                : m.Value;
        });
    }

    /// <summary>
    /// Walks all <see cref="Run"/> elements in <paramref name="doc"/> and splits any that
    /// contain <see cref="HighlightSentinel"/> characters into plain and highlighted runs.
    /// </summary>
    internal static void HighlightSentinelRuns(FlowDocument doc, Brush highlightBrush) =>
        ProcessBlocks(doc.Blocks, highlightBrush);

    private static void ProcessBlocks(BlockCollection blocks, Brush highlightBrush) {
        foreach (var block in blocks.ToList()) {
            switch (block) {
                case Paragraph para:
                    ProcessInlineCollection(para.Inlines, highlightBrush);
                    break;
                case System.Windows.Documents.List list:
                    foreach (ListItem item in list.ListItems)
                        ProcessBlocks(item.Blocks, highlightBrush);
                    break;
                case Section section:
                    ProcessBlocks(section.Blocks, highlightBrush);
                    break;
            }
        }
    }

    private static void ProcessInlineCollection(InlineCollection inlines, Brush highlightBrush) {
        foreach (var span in inlines.OfType<Span>().ToList())
            ProcessInlineCollection(span.Inlines, highlightBrush);
        foreach (var run in inlines.OfType<Run>()
                                   .Where(r => r.Text.Contains(HighlightSentinel))
                                   .ToList())
            SplitAndHighlightRun(run, inlines, highlightBrush);
    }

    private static void SplitAndHighlightRun(Run original, InlineCollection parent, Brush highlightBrush) {
        var parts = original.Text.Split(HighlightSentinel);
        var newInlines = new List<Inline>();
        for (int i = 0; i < parts.Length; i++) {
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            CopyRunFormatting(original, run);
            if (i % 2 == 1)  // odd index = between sentinel markers = resolved value
                run.Background = highlightBrush;
            newInlines.Add(run);
        }
        if (newInlines.Count == 0) {
            parent.Remove(original);
            return;
        }
        foreach (var inline in newInlines)
            parent.InsertBefore(original, inline);
        parent.Remove(original);
    }

    private static void CopyRunFormatting(Run source, Run target) {
        var fw = source.ReadLocalValue(TextElement.FontWeightProperty);
        if (fw != DependencyProperty.UnsetValue)
            target.FontWeight = (FontWeight)fw;
        var fs = source.ReadLocalValue(TextElement.FontStyleProperty);
        if (fs != DependencyProperty.UnsetValue)
            target.FontStyle = (FontStyle)fs;
        var ff = source.ReadLocalValue(TextElement.FontFamilyProperty);
        if (ff != DependencyProperty.UnsetValue && ff is FontFamily fontFamily)
            target.FontFamily = fontFamily;
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
            _statusLabel.Text       = "Code Health window — idle…";
            _statusLabel.Visibility = Visibility.Visible;
            return;
        }

        var text = remaining.TotalHours >= 1
            ? $"Next code health in: {(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
            : remaining.TotalMinutes >= 1
                ? $"Next code health in: {(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s"
                : $"Next code health in: {(int)remaining.TotalSeconds}s";

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
                if (child is not Border { Tag: CodeHealthTask task }) continue;
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

    public double GetMaximumUsefulHeight()
    {
        const double titleRow      = 40;
        const double statusRow     = 28;
        const double taskRowHeight = 36;
        const double cap           = 480;
        const double floor         = 120;

        int count = 0;
        foreach (var child in _listPanel.Children)
            if (child is Border { Tag: CodeHealthTask }) count++;

        double h = titleRow + statusRow + count * taskRowHeight + 24;
        return Math.Clamp(h, floor, cap);
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

    // ── Safety level helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Gets the effective safety level for a task, considering:
    /// 1. User override from settings (highest priority)
    /// 2. safety_default from code-health.md
    /// 3. safety from code-health.md (lowest priority)
    /// </summary>
    private string GetEffectiveSafetyLevel(CodeHealthTask task) {
        var override_ = _viewModel.StateStore?.GetSafetyOverride(task.Id);
        if (!string.IsNullOrEmpty(override_))
            return override_;
        
        if (!string.IsNullOrEmpty(task.SafetyDefault))
            return task.SafetyDefault;
        
        return task.Safety;
    }

    private static string GetSafetyDisplayText(string safety) =>
        safety.ToLowerInvariant() switch {
            "report-only" => "report",
            "branch"      => "branch",
            "direct"      => "direct",
            _             => safety,
        };

    private static string GetSafetyDisplayTooltip(string safety) =>
        safety.ToLowerInvariant() switch {
            "report-only" => "This task is directed to make no changes to the code. Results are reported only.",
            "branch"      => "This task will create a new branch before making changes.",
            "direct"      => "This task makes changes directly to the current branch.",
            _             => "Unknown safety level",
        };

    /// <summary>
    /// Updates the <c>safety:</c> field for <paramref name="taskId"/> in code-health.md
    /// and reloads the panel so the change is reflected immediately.
    /// </summary>
    private void ChangeTaskSafety(string taskId, string newSafety) {
        var mdPath = GetMaintenanceMdPath();
        if (mdPath is null) return;
        
        // Store in state store instead of updating the file
        _viewModel.StateStore?.SetSafetyOverride(taskId, newSafety);
        _reloadPanel();
    }
}


