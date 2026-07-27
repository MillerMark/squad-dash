namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>Manages content in the inline Plans panel.</summary>
internal sealed class PlansPanelController
{
    private readonly StackPanel  _activePanel;
    private readonly StackPanel  _completedPanel;
    private readonly UIElement   _completedSection;
    private readonly Action<Plan> _openPlan;
    private readonly Action<bool>? _syncBorderVisibility;
    private readonly Action<bool>? _setMenuChecked;
    private readonly Action?       _persistVisibility;

    private readonly PlansPanelViewModel _viewModel = new();
    internal PlansPanelViewModel ViewModel => _viewModel;

    private bool _panelVisible;
    internal bool PanelVisible => _panelVisible;

    // Cached plan list for targeted live updates — avoids a full store reload on each event.
    private List<Plan> _currentPlans = [];

    // ── Construction ─────────────────────────────────────────────────────────

    internal PlansPanelController(
        StackPanel   activePanel,
        StackPanel   completedPanel,
        UIElement    completedSection,
        Action<Plan> openPlan,
        bool         initialShowCompleted  = false,
        Action<bool>? syncBorderVisibility = null,
        Action<bool>? setMenuChecked       = null,
        Action?       persistVisibility    = null)
    {
        _activePanel          = activePanel;
        _completedPanel       = completedPanel;
        _completedSection     = completedSection;
        _openPlan             = openPlan;
        _syncBorderVisibility = syncBorderVisibility;
        _setMenuChecked       = setMenuChecked;
        _persistVisibility    = persistVisibility;

        _viewModel.ShowCompleted = initialShowCompleted;
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

    internal void SetShowCompleted(bool show)
    {
        _viewModel.ShowCompleted = show;
        // Toggle completed section visibility without a full rebuild.
        var anyCompleted = _currentPlans.Any(p => PlanLifecycleStatus.IsTerminal(p.LifecycleStatus));
        _completedSection.Visibility =
            show && anyCompleted ? Visibility.Visible : Visibility.Collapsed;
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

    // ── Row building ──────────────────────────────────────────────────────────

    private void RebuildPanels()
    {
        _activePanel.Children.Clear();
        _completedPanel.Children.Clear();

        var active    = _currentPlans.Where(p => !PlanLifecycleStatus.IsTerminal(p.LifecycleStatus)).ToList();
        var completed = _currentPlans.Where(p => PlanLifecycleStatus.IsTerminal(p.LifecycleStatus)).ToList();

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

        _completedSection.Visibility =
            _viewModel.ShowCompleted && completed.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private Border BuildRow(Plan plan)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding    = new Thickness(4, 5, 4, 5),
            Cursor     = Cursors.Hand,
        };

        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var rowStack = new StackPanel { Orientation = Orientation.Vertical };

        // ── Title row: status icon + title ────────────────────────────────
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        var iconBlock = new TextBlock
        {
            Text              = StatusIcon(plan.LifecycleStatus),
            Margin            = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        iconBlock.SetResourceReference(TextBlock.ForegroundProperty, StatusForegroundKey(plan.LifecycleStatus));

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
                Text   = $"{plan.Progress.CompletedCount}/{plan.Progress.TotalCount}",
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            countBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            countBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

            progressRow.Children.Add(bar);
            progressRow.Children.Add(countBlock);
            rowStack.Children.Add(progressRow);
        }

        row.Child = rowStack;

        // ── Tooltip with branch and summary ──────────────────────────────
        var tipLines = new List<string> { plan.LifecycleStatus };
        if (!string.IsNullOrWhiteSpace(plan.Branch))
            tipLines.Add($"Branch: {plan.Branch}");
        if (!string.IsNullOrWhiteSpace(plan.Summary))
            tipLines.Add(plan.Summary.Length > 120 ? plan.Summary[..120] + "…" : plan.Summary);
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
        row.ContextMenu = menu;

        return row;
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
        PlanLifecycleStatus.Executing        => "▶",
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
