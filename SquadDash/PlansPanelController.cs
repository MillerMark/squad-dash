namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

/// <summary>Manages content in the inline Plans panel.</summary>
internal sealed class PlansPanelController
{
    private readonly StackPanel  _activePanel;
    private readonly StackPanel  _completedPanel;
    private readonly UIElement   _completedSection;
    private readonly StackPanel  _archivedPanel;
    private readonly UIElement   _archivedSection;
    private readonly Action<Plan>  _openPlan;
    private readonly Action<Plan>? _startPlan;
    private readonly Action<Plan>? _resumePlan;
    private readonly Action<Plan>? _endPlan;
    private readonly Action<Plan>? _approveGate;
    private readonly Action<Plan>? _archivePlan;
    private readonly Action<Plan>? _pausePlan;
    private readonly Action<Plan>? _abortPlan;
    private readonly Action<Plan>? _attachFollowUp;
    private readonly Action<Plan>? _addToNewChat;
    private readonly Action<bool>? _syncBorderVisibility;
    private readonly Action<bool>? _setMenuChecked;
    private readonly Action?       _persistVisibility;
    private readonly Func<bool>?   _isPromptRunning;

    private readonly PlansPanelViewModel _viewModel = new();
    internal PlansPanelViewModel ViewModel => _viewModel;

    private bool _panelVisible;
    internal bool PanelVisible => _panelVisible;

    // Cached plan list for targeted live updates — avoids a full store reload on each event.
    private List<Plan> _currentPlans = [];

    // Braille-dot spinner indicator for the actively-executing plan.
    private TextBlock? _executingSpinnerBlock;

    // ── Construction ─────────────────────────────────────────────────────────

    internal PlansPanelController(
        StackPanel   activePanel,
        StackPanel   completedPanel,
        UIElement    completedSection,
        StackPanel   archivedPanel,
        UIElement    archivedSection,
        Action<Plan> openPlan,
        bool         initialShowCompleted  = false,
        bool         initialShowArchived   = false,
        Action<bool>? syncBorderVisibility = null,
        Action<bool>? setMenuChecked       = null,
        Action?       persistVisibility    = null,
        Action<Plan>? startPlan            = null,
        Action<Plan>? resumePlan           = null,
        Action<Plan>? endPlan              = null,
        Action<Plan>? approveGate          = null,
        Action<Plan>? archivePlan          = null,
        Action<Plan>? pausePlan            = null,
        Action<Plan>? abortPlan            = null,
        Action<Plan>? attachFollowUp       = null,
        Action<Plan>? addToNewChat         = null,
        Func<bool>?   isPromptRunning      = null)
    {
        _activePanel          = activePanel;
        _completedPanel       = completedPanel;
        _completedSection     = completedSection;
        _archivedPanel        = archivedPanel;
        _archivedSection      = archivedSection;
        _openPlan             = openPlan;
        _startPlan            = startPlan;
        _resumePlan           = resumePlan;
        _endPlan              = endPlan;
        _approveGate          = approveGate;
        _archivePlan          = archivePlan;
        _pausePlan            = pausePlan;
        _abortPlan            = abortPlan;
        _attachFollowUp       = attachFollowUp;
        _addToNewChat         = addToNewChat;
        _syncBorderVisibility = syncBorderVisibility;
        _setMenuChecked       = setMenuChecked;
        _persistVisibility    = persistVisibility;
        _isPromptRunning      = isPromptRunning;

        _viewModel.ShowCompleted = initialShowCompleted;
        _viewModel.ShowArchived = initialShowArchived;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    internal void Refresh(IReadOnlyList<Plan> plans)
    {
        _currentPlans = [.. plans];
        RebuildPanels();
    }

    /// <summary>
    /// Applies a live plan update without a full store reload.
    /// Replaces or inserts the updated plan in the cached list and rebuilds only the panels.
    /// Dispatcher-safe: must be called on the UI thread.
    /// </summary>
    internal void OnPlanChanged(Plan updatedPlan)
    {
        var idx = _currentPlans.FindIndex(p =>
            string.Equals(p.PlanId, updatedPlan.PlanId, StringComparison.Ordinal));
        if (idx >= 0)
            _currentPlans[idx] = updatedPlan;
        else
            _currentPlans.Add(updatedPlan);
        RebuildPanels();
    }

    /// <summary>
    /// Removes a plan by ID from the cached list and rebuilds the panels.
    /// Used by simulation cleanup to retract overlaid plans without a full store reload.
    /// </summary>
    internal void OnPlanRemoved(string planId)
    {
        var idx = _currentPlans.FindIndex(p =>
            string.Equals(p.PlanId, planId, StringComparison.Ordinal));
        if (idx >= 0)
        {
            _currentPlans.RemoveAt(idx);
            RebuildPanels();
        }
    }

    internal void SetShowCompleted(bool show)
    {
        _viewModel.ShowCompleted = show;
        // Toggle completed section visibility without a full rebuild.
        var anyCompleted = _currentPlans.Any(p =>
            PlanLifecycleStatus.IsTerminal(p.LifecycleStatus) &&
            p.LifecycleStatus != PlanLifecycleStatus.Archived);
        _completedSection.Visibility =
            show && anyCompleted ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetShowArchived(bool show)
    {
        _viewModel.ShowArchived = show;
        var anyArchived = _currentPlans.Any(p => p.LifecycleStatus == PlanLifecycleStatus.Archived);
        _archivedSection.Visibility =
            show && anyArchived ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Show(bool flash = false)
    {
        _panelVisible = true;
        _syncBorderVisibility?.Invoke(true);
        _setMenuChecked?.Invoke(true);
        _persistVisibility?.Invoke();
    }

    internal void Hide()
    {
        _panelVisible = false;
        _syncBorderVisibility?.Invoke(false);
        _setMenuChecked?.Invoke(false);
        _persistVisibility?.Invoke();
    }

    /// <summary>
    /// Returns a content-width hint for the docking snap target.
    /// Matches the minimum usable width of the panel.
    /// </summary>
    internal double GetMaximumUsefulWidth()  => 300.0;
    internal double GetMaximumUsefulHeight() => 520.0;

    /// <summary>
    /// Reveals the Plans panel, ensures the plan row exists, scrolls it into view,
    /// and applies a brief attention animation. Idempotent — repeated calls for the
    /// same plan re-highlight the existing row without duplicating it.
    /// Does not steal keyboard focus or reopen windows.
    /// </summary>
    internal void RevealCollectedPlan(Plan plan)
    {
        OnPlanChanged(plan);
        Show();
        HighlightRow(plan.PlanId);
    }

    // ── Row attention animation ───────────────────────────────────────────────

    private void HighlightRow(string planId)
    {
        var row = FindRowByPlanId(_activePanel, planId)
               ?? FindRowByPlanId(_completedPanel, planId)
               ?? FindRowByPlanId(_archivedPanel, planId);
        if (row is null)
            return;

        row.BringIntoView();

        // Respect reduced-motion preference.
        if (!SystemParameters.ClientAreaAnimation)
            return;

        try
        {
            var highlightColor = Color.FromArgb(0x30, 0xFF, 0xCC, 0x00);
            var animation = new ColorAnimation
            {
                From     = highlightColor,
                To       = Colors.Transparent,
                Duration = UiTimingConstants.PlanRowAttentionDuration,
            };
            row.Background = new SolidColorBrush(highlightColor);
            row.Background.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.UI,
                $"PlansPanelController: attention animation failed for '{planId}': {ex.Message}");
        }
    }

    private static Border? FindRowByPlanId(StackPanel panel, string planId)
    {
        foreach (UIElement child in panel.Children)
        {
            if (child is Border border &&
                border.Tag is string tag &&
                string.Equals(tag, planId, StringComparison.Ordinal))
                return border;
        }
        return null;
    }

    // ── Row building ──────────────────────────────────────────────────────────

    private void RebuildPanels()
    {
        _executingSpinnerBlock = null;
        _activePanel.Children.Clear();
        _completedPanel.Children.Clear();
        _archivedPanel.Children.Clear();

        var active = _currentPlans.Where(p => !PlanLifecycleStatus.IsTerminal(p.LifecycleStatus))
            .OrderByDescending(GetLastRunAt)
            .ThenBy(plan => plan.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var completed = _currentPlans.Where(p =>
            PlanLifecycleStatus.IsTerminal(p.LifecycleStatus) &&
            p.LifecycleStatus != PlanLifecycleStatus.Archived)
            .OrderByDescending(GetLastRunAt)
            .ThenBy(plan => plan.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var archived = _currentPlans.Where(p =>
            p.LifecycleStatus == PlanLifecycleStatus.Archived)
            .OrderByDescending(GetLastRunAt)
            .ThenBy(plan => plan.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (active.Count == 0 && completed.Count == 0)
        {
            ShowEmpty(_activePanel, "No plans");
        }
        else
        {
            foreach (var plan in active)
                _activePanel.Children.Add(BuildRow(plan));

            if (active.Count == 0)
                ShowEmpty(_activePanel, "No active plans");
        }

        foreach (var plan in completed)
            _completedPanel.Children.Add(BuildRow(plan));
        foreach (var plan in archived)
            _archivedPanel.Children.Add(BuildRow(plan));

        _completedSection.Visibility =
            _viewModel.ShowCompleted && completed.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        _archivedSection.Visibility =
            _viewModel.ShowArchived && archived.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// Returns the most recent execution touch. LastRunAt is authoritative for current plans;
    /// older plans fall back to their durable task, validation, gate, and lifecycle timestamps.
    /// Archiving is intentionally excluded because it is organization, not execution.
    /// </summary>
    internal static DateTimeOffset GetLastRunAt(Plan plan)
    {
        if (plan.Timestamps.LastRunAt is { } lastRunAt)
            return lastRunAt;

        var candidates = new List<DateTimeOffset>
        {
            plan.Timestamps.CreatedAt,
        };
        Add(plan.Timestamps.AcceptedAt);
        Add(plan.Timestamps.StartedAt);
        Add(plan.Timestamps.CompletedAt);
        Add(plan.Timestamps.InterruptedAt);
        Add(plan.Timestamps.StoppedAt);
        foreach (var task in plan.Tasks) Add(task.CompletedAt);
        foreach (var validation in plan.Validations ?? [])
        {
            Add(validation.StartedAt);
            Add(validation.CompletedAt);
        }
        foreach (var gate in plan.ApprovalGates)
        {
            Add(gate.RequestedAt);
            Add(gate.ResolvedAt);
        }
        return candidates.Max();

        void Add(DateTimeOffset? value)
        {
            if (value is { } timestamp) candidates.Add(timestamp);
        }
    }

    private Border BuildRow(Plan plan)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding    = new Thickness(4, 5, 4, 5),
            Cursor     = Cursors.Hand,
            Tag        = plan.PlanId,
        };

        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var rowStack = new StackPanel { Orientation = Orientation.Vertical };

        // ── Title row: status icon + title ────────────────────────────────
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        var iconBlock = BuildTitleStatusOrControl(plan);

        var titleBlock = new TextBlock
        {
            Text             = plan.Title,
            TextTrimming     = TextTrimming.CharacterEllipsis,
            TextWrapping     = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        titleRow.Children.Add(iconBlock);
        titleRow.Children.Add(titleBlock);
        rowStack.Children.Add(titleRow);

        // ── Progress row (only when plan has tasks) ───────────────────────
        if (plan.Progress.TotalCount > 0)
        {
            var progressRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(20, 2, 0, 0),
            };

            if (PlanTaskActivityResolver.ResolvePlanLevel(plan) == PlanTaskActivityState.Executing &&
                _isPromptRunning?.Invoke() == true)
            {
                var spinnerBlock = new TextBlock
                {
                    Text = UiTimingConstants.ToolSpinnerFrames[0],
                    Width = 14,
                    Margin = new Thickness(-20, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    ToolTip = ToolTipHelper.MakeThemedToolTip("Plan is actively executing"),
                };
                spinnerBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                spinnerBlock.SetResourceReference(TextBlock.ForegroundProperty, "PriorityMid");
                progressRow.Children.Add(spinnerBlock);
                _executingSpinnerBlock = spinnerBlock;
            }

            var bar = new ProgressBar
            {
                Height  = 3,
                Width   = 100,
                Minimum = 0,
                Maximum = plan.Progress.TotalCount,
                Value   = plan.Progress.CompletedCount,
            };
            bar.SetResourceReference(ProgressBar.ForegroundProperty, "PriorityMid");
            bar.SetResourceReference(ProgressBar.BackgroundProperty, "SubtleBorder");
            bar.SetResourceReference(ProgressBar.BorderBrushProperty, "Transparent");

            var countBlock = new TextBlock
            {
                Text   = BuildProgressCountLabel(plan),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            countBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            countBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

            progressRow.Children.Add(bar);
            progressRow.Children.Add(countBlock);
            rowStack.Children.Add(progressRow);

            if (BuildActivityLabel(plan) is { Length: > 0 } activityLabel)
            {
                var activityBlock = new TextBlock
                {
                    Text = activityLabel,
                    Margin = new Thickness(20, 1, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                activityBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                activityBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                rowStack.Children.Add(activityBlock);
            }

            // ── Validation summary row ────────────────────────────────────
            if (ValidationShieldPresenter.BuildSummaryLabel(
                    ValidationShieldPresenter.Summarize(plan)) is { } validationLabel)
            {
                var summary = ValidationShieldPresenter.Summarize(plan)!;
                var validationBlock = new TextBlock
                {
                    Text = validationLabel,
                    Margin = new Thickness(20, 1, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                validationBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                validationBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
                rowStack.Children.Add(validationBlock);
            }
        }

        row.Child = rowStack;

        // ── Tooltip with branch and summary ──────────────────────────────
        var tipLines = new List<string> { plan.LifecycleStatus };
        if (!string.IsNullOrWhiteSpace(plan.Branch))
            tipLines.Add($"Branch: {plan.Branch}");
        if (!string.IsNullOrWhiteSpace(plan.Summary))
            tipLines.Add(plan.Summary.Length > 120 ? plan.Summary[..120] + "…" : plan.Summary);
        if (plan.ApprovalGates.Any(gate => gate.Status == PlanGateStatus.AwaitingApproval))
        {
            var awaitingGate = plan.ApprovalGates
                .FirstOrDefault(g => g.Status == PlanGateStatus.AwaitingApproval);
            if (awaitingGate is not null)
                tipLines.Add($"Gate: {awaitingGate.Message}");
        }
        row.ToolTip = ToolTipHelper.MakeThemedToolTip(string.Join("\n", tipLines));

        // ── Click to open plan viewer ─────────────────────────────────────
        row.MouseLeftButtonUp += (_, _) => _openPlan(plan);

        // ── Context menu ──────────────────────────────────────────────────
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        var openItem = new MenuItem { Header = "Open Plan" };
        openItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        openItem.Click += (_, _) => _openPlan(plan);
        menu.Items.Add(openItem);

        if (_attachFollowUp is not null || _addToNewChat is not null)
        {
            menu.Items.Add(new Separator());
            if (_attachFollowUp is not null)
            {
                var attachItem = new MenuItem { Header = "Add to Chat" };
                attachItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                attachItem.Click += (_, _) => _attachFollowUp(plan);
                menu.Items.Add(attachItem);
            }
            if (_addToNewChat is not null)
            {
                var newChatItem = new MenuItem { Header = "Add to New Chat" };
                newChatItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                newChatItem.Click += (_, _) => _addToNewChat(plan);
                menu.Items.Add(newChatItem);
            }
        }

        if (plan.LifecycleStatus == PlanLifecycleStatus.Approved && _startPlan is not null)
        {
            var startItem = new MenuItem { Header = "Start Plan" };
            startItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            startItem.Click += (_, _) => _startPlan(plan);
            menu.Items.Add(startItem);
        }

        if (plan.LifecycleStatus == PlanLifecycleStatus.Interrupted)
        {
            if (PlanRecoveryResumePolicy.IsSafelyResumable(plan) && _startPlan is not null)
            {
                var resumeItem = new MenuItem
                {
                    Header = "Resume Plan",
                    ToolTip = ToolTipHelper.MakeThemedToolTip(
                        "Continue with the next runnable step. The accepted step will not be repeated."),
                };
                resumeItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                resumeItem.Click += (_, _) => _startPlan(plan);
                menu.Items.Add(resumeItem);
            }
            else if (_resumePlan is not null)
            {
                var resumeItem = new MenuItem
                {
                    Header = "Assess && Continue",
                    ToolTip = ToolTipHelper.MakeThemedToolTip(
                        "AI classifies the interrupted task, then SquadDash validates the evidence before continuing."),
                };
                resumeItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                resumeItem.Click += (_, _) => _resumePlan(plan);
                menu.Items.Add(resumeItem);
            }
            if (_endPlan is not null)
            {
                var endItem = new MenuItem { Header = "End Plan" };
                endItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                endItem.Click += (_, _) => _endPlan(plan);
                menu.Items.Add(endItem);
            }
        }

        if (plan.ApprovalGates.Any(gate => gate.Status == PlanGateStatus.AwaitingApproval) &&
            _approveGate is not null)
        {
            var approveItem = new MenuItem { Header = "Approve & Continue" };
            approveItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            approveItem.Click += (_, _) => _approveGate(plan);
            menu.Items.Add(approveItem);
        }

        if (plan.LifecycleStatus == PlanLifecycleStatus.Executing)
        {
            if (_pausePlan is not null)
            {
                var pauseItem = new MenuItem
                {
                    Header = "Pause After Current Step",
                    ToolTip = ToolTipHelper.MakeThemedToolTip(
                        "Finish and record the current step, then pause before starting more plan work."),
                };
                pauseItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                pauseItem.Click += (_, _) => _pausePlan(plan);
                menu.Items.Add(pauseItem);
            }
            if (_abortPlan is not null)
            {
                var abortItem = new MenuItem
                {
                    Header = "Abort Current Work…",
                    ToolTip = ToolTipHelper.MakeThemedToolTip(
                        "Immediately stop active agents and preserve the task for evidence-based recovery."),
                };
                abortItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                abortItem.Click += (_, _) => _abortPlan(plan);
                menu.Items.Add(abortItem);
            }
        }

        if (plan.LifecycleStatus != PlanLifecycleStatus.Archived &&
            plan.LifecycleStatus is not (PlanLifecycleStatus.Executing or PlanLifecycleStatus.AwaitingApproval) &&
            _archivePlan is not null)
        {
            var archiveItem = new MenuItem
            {
                Header = "Archive Plan",
                ToolTip = ToolTipHelper.MakeThemedToolTip(
                    "Hide this plan without deleting its task, approval, or validation history."),
            };
            archiveItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
            archiveItem.Click += (_, _) => _archivePlan(plan);
            menu.Items.Add(archiveItem);
        }

        row.ContextMenu = menu;

        return row;
    }

    private static string BuildProgressCountLabel(Plan plan) =>
        $"{plan.Progress.CompletedCount}/{plan.Progress.TotalCount} complete";

    private static string? BuildActivityLabel(Plan plan)
    {
        if (plan.ApprovalGates.Any(gate => gate.Status == PlanGateStatus.AwaitingApproval))
            return "Approval ready";
        if (PlanRecoveryResumePolicy.IsReworkPreflightPause(plan))
            return "Rework ready · workspace blocked";
        if (PlanRecoveryResumePolicy.IsAmendmentPreflightPause(plan))
            return "Amendment ready · workspace blocked";
        if (PlanRecoveryResumePolicy.IsSafelyResumable(plan))
            return "Paused after accepted step";
        if (plan.Progress.ExecutingTaskId is not { Length: > 0 } executingTaskId)
            return null;

        var taskIndex = plan.Tasks.ToList().FindIndex(task =>
            string.Equals(task.TaskId, executingTaskId, StringComparison.Ordinal));
        return taskIndex >= 0
            ? $"Step {taskIndex + 1} running"
            : "Executing";
    }

    private FrameworkElement BuildTitleStatusOrControl(Plan plan)
    {
        if (plan.LifecycleStatus == PlanLifecycleStatus.Executing && _pausePlan is not null)
        {
            var pause = new Button
            {
                Content = "Ⅱ",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = ToolTipHelper.MakeThemedToolTip("Pause plan execution after the current step is accepted."),
            };
            System.Windows.Automation.AutomationProperties.SetName(pause, "Pause after current step");
            pause.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
            pause.Click += (_, args) =>
            {
                args.Handled = true;
                _pausePlan(plan);
            };
            return pause;
        }

        if (PlanRecoveryResumePolicy.IsSafelyResumable(plan) && _startPlan is not null)
        {
            var resume = new Button
            {
                Content = "▶",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = ToolTipHelper.MakeThemedToolTip(
                    PlanRecoveryResumePolicy.IsAmendmentPreflightPause(plan)
                        ? "Resume the added amendment after committing or stashing the blocking changes; completed tasks remain accepted."
                        : PlanRecoveryResumePolicy.IsReworkPreflightPause(plan)
                            ? "Resume the already-prepared rework after committing or stashing the blocking changes."
                            : "Resume with the next runnable plan step."),
            };
            System.Windows.Automation.AutomationProperties.SetName(resume, "Resume plan");
            resume.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
            resume.Click += (_, args) =>
            {
                args.Handled = true;
                _startPlan(plan);
            };
            return resume;
        }

        if (plan.LifecycleStatus == PlanLifecycleStatus.Completed)
        {
            const double size = 14;
            const double inset = 3.25;
            var canvas = new Canvas { Width = size, Height = size };

            var square = new Rectangle
            {
                Width = size,
                Height = size,
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent,
            };
            square.SetResourceReference(Shape.StrokeProperty, "SubtleBorder");
            canvas.Children.Add(square);

            var line1 = new Line
            {
                X1 = inset, Y1 = inset,
                X2 = size - inset, Y2 = size - inset,
                StrokeThickness = 2,
            };
            line1.SetResourceReference(Shape.StrokeProperty, "ImportantText");
            canvas.Children.Add(line1);

            var line2 = new Line
            {
                X1 = size - inset, Y1 = inset,
                X2 = inset, Y2 = size - inset,
                StrokeThickness = 2,
            };
            line2.SetResourceReference(Shape.StrokeProperty, "ImportantText");
            canvas.Children.Add(line2);

            return new Border
            {
                Child = canvas,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            };
        }

        var iconBlock = new TextBlock
        {
            Text = StatusIcon(plan.LifecycleStatus),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        iconBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        iconBlock.SetResourceReference(TextBlock.ForegroundProperty, StatusForegroundKey(plan.LifecycleStatus));

        return iconBlock;
    }

    /// <summary>
    /// Called by MainWindow on each tool-spinner timer tick to advance the braille-dot frame
    /// for any executing plan indicator. No-op if no plan is currently animating.
    /// </summary>
    internal void AdvancePlanActivityFrame(int frame)
    {
        if (_executingSpinnerBlock is { } block)
            block.Text = UiTimingConstants.ToolSpinnerFrames[frame];
    }

    private static void ShowEmpty(StackPanel panel, string message)
    {
        var label = new TextBlock
        {
            Text        = message,
            Margin      = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        panel.Children.Add(label);
    }

    private static string StatusIcon(string status) => status switch
    {
        PlanLifecycleStatus.Staged           => "📋",
        PlanLifecycleStatus.Approved         => "✅",
        PlanLifecycleStatus.Executing        => "⟳",
        PlanLifecycleStatus.AwaitingApproval => "⏸",
        PlanLifecycleStatus.Interrupted      => "⚠",
        PlanLifecycleStatus.Stopped          => "⏹",
        PlanLifecycleStatus.Completed        => "✓",
        PlanLifecycleStatus.Archived         => "📁",
        PlanLifecycleStatus.Blocked          => "✖",
        _                                    => "•",
    };

    private static string StatusForegroundKey(string status) => status switch
    {
        PlanLifecycleStatus.Executing        => "PriorityMid",
        PlanLifecycleStatus.Interrupted      => "PriorityHigh",
        PlanLifecycleStatus.Blocked          => "PriorityHigh",
        PlanLifecycleStatus.Completed        => "PriorityLow",
        PlanLifecycleStatus.Archived         => "SubtleText",
        PlanLifecycleStatus.Stopped          => "SubtleText",
        _                                    => "LabelText",
    };
}
