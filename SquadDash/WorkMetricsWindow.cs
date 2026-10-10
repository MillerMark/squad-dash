using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>A live, theme-aware view over deterministic transcript work metrics.</summary>
internal sealed class WorkMetricsWindow : ChromedWindow
{
    private const string All = "All";
    private readonly Func<IReadOnlyList<WorkMetricsRunSnapshot>> _getRuns;
    private readonly ComboBox _phaseFilter;
    private readonly ComboBox _agentFilter;
    private readonly ComboBox _runFilter;
    private readonly ComboBox _statusFilter;
    private readonly TextBlock _summary;
    private readonly StackPanel _runRows;
    private readonly DispatcherTimer _refreshTimer;
    private bool _updatingFilters;

    internal WorkMetricsWindow(
        string title,
        Func<IReadOnlyList<WorkMetricsRunSnapshot>> getRuns)
        : base(captionHeight: CloseButtonHeight, resizeMode: ResizeMode.CanResize)
    {
        ArgumentNullException.ThrowIfNull(getRuns);
        _getRuns = getRuns;
        Title = title;
        Width = 760;
        Height = 620;
        MinWidth = 580;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(FontSizeProperty, "FontSizeBody");

        var root = new Grid { Margin = new Thickness(16, 12, 16, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var description = new TextBlock
        {
            Text = "Counts come directly from stored tool events. Unknown tool names remain Other; command text is never inferred.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        description.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        root.Children.Add(description);

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _phaseFilter = AddFilter(filters, "Phase");
        _agentFilter = AddFilter(filters, "Agent");
        _runFilter = AddFilter(filters, "Run");
        _statusFilter = AddFilter(filters, "Status");
        foreach (var filter in new[] { _phaseFilter, _agentFilter, _runFilter, _statusFilter })
            filter.SelectionChanged += (_, _) => { if (!_updatingFilters) Refresh(); };
        Grid.SetRow(filters, 1);
        root.Children.Add(filters);

        _summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _summary.SetResourceReference(TextBlock.ForegroundProperty, "ActivePanelTitle");
        _summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        Grid.SetRow(_summary, 2);
        root.Children.Add(_summary);

        _runRows = new StackPanel { Orientation = Orientation.Vertical };
        var scroll = new ScrollViewer
        {
            Content = _runRows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.SetResourceReference(Control.StyleProperty, "RosterScrollViewerStyle");
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);

        ApplyOuterBorder(titleText: title).Child = root;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => Refresh(), Dispatcher);
        Loaded += (_, _) =>
        {
            RefreshFilters();
            Refresh();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private ComboBox AddFilter(Panel parent, string label)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        caption.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        var combo = new ComboBox { MinWidth = 130, Height = 30 };
        combo.SetResourceReference(Control.StyleProperty, "ThemedComboBoxStyle");
        group.Children.Add(caption);
        group.Children.Add(combo);
        parent.Children.Add(group);
        return combo;
    }

    private void RefreshFilters()
    {
        var runs = SafeGetRuns();
        _updatingFilters = true;
        try
        {
            ReplaceItems(_phaseFilter, runs.Select(run => DisplayPhase(run.WorkItemKind)));
            ReplaceItems(_agentFilter, runs.Select(run => run.AgentName));
            ReplaceItems(_runFilter, runs.Select(BuildRunFilterLabel));
            ReplaceItems(_statusFilter, new[] { "Live", "Completed" });
        }
        finally
        {
            _updatingFilters = false;
        }
    }

    private static void ReplaceItems(ComboBox combo, IEnumerable<string> values)
    {
        var selected = combo.SelectedItem as string ?? All;
        var items = new[] { All }.Concat(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToArray();
        combo.ItemsSource = items;
        combo.SelectedItem = items.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : All;
    }

    private void Refresh()
    {
        if (!IsLoaded) return;
        RefreshFilters();
        var now = DateTimeOffset.Now;
        var runs = ApplyFilters(SafeGetRuns()).ToArray();
        var calls = runs.Sum(run => run.ToolCalls);
        var reads = runs.Sum(run => run.ReadCalls);
        var searches = runs.Sum(run => run.SearchCalls);
        var writes = runs.Sum(run => run.WriteCalls);
        var files = runs.Sum(run => run.FilesChanged);
        var agentCount = runs.Select(run => run.AgentName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var childRunCount = runs.Count(run => !string.IsNullOrWhiteSpace(run.ParentRunId));
        var readSearchPct = calls == 0 ? 0 : (reads + searches) * 100.0 / calls;
        _summary.Text = runs.Length == 0
            ? "No work metrics match these filters."
            : $"{runs.Length} run{(runs.Length == 1 ? string.Empty : "s")} · " +
              $"{agentCount} agent{(agentCount == 1 ? string.Empty : "s")} · {childRunCount} child runs · " +
              $"{calls:N0} tool calls · " +
              $"{reads:N0} reads · {searches:N0} searches · {writes:N0} writes · " +
              $"{readSearchPct:0.#}% read/search · {files:N0} captured files";

        _runRows.Children.Clear();
        foreach (var run in runs.OrderByDescending(run => run.IsLive).ThenBy(run => run.StartedAt))
            _runRows.Children.Add(BuildRunCard(run, now));
    }

    private IEnumerable<WorkMetricsRunSnapshot> ApplyFilters(IEnumerable<WorkMetricsRunSnapshot> runs)
    {
        var phase = _phaseFilter.SelectedItem as string ?? All;
        var agent = _agentFilter.SelectedItem as string ?? All;
        var runLabel = _runFilter.SelectedItem as string ?? All;
        var status = _statusFilter.SelectedItem as string ?? All;
        return runs.Where(run =>
            (phase == All || string.Equals(DisplayPhase(run.WorkItemKind), phase, StringComparison.OrdinalIgnoreCase)) &&
            (agent == All || string.Equals(run.AgentName, agent, StringComparison.OrdinalIgnoreCase)) &&
            (runLabel == All || string.Equals(BuildRunFilterLabel(run), runLabel, StringComparison.OrdinalIgnoreCase)) &&
            (status == All || (run.IsLive ? "Live" : "Completed") == status));
    }

    private Border BuildRunCard(WorkMetricsRunSnapshot run, DateTimeOffset now)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        title.Text = $"{run.AgentName} · {DisplayPhase(run.WorkItemKind)}" +
                     (string.IsNullOrWhiteSpace(run.WorkItemId) ? string.Empty : $" · {run.WorkItemId}");
        panel.Children.Add(title);

        var elapsed = StatusTimingPresentation.FormatDuration(run.ElapsedAt(now));
        panel.Children.Add(BuildLine($"{(run.IsLive ? "Live" : "Completed")} · {elapsed} · started {run.StartedAt.ToLocalTime():g}" +
                                    (string.IsNullOrWhiteSpace(run.ParentRunId) ? string.Empty : " · child agent run")));
        panel.Children.Add(BuildLine(
            $"Tools: {run.ToolCalls:N0} total · {run.ReadCalls:N0} read · {run.SearchCalls:N0} search · " +
            $"{run.WriteCalls:N0} write · {run.DelegationCalls:N0} delegation · {run.ShellCalls:N0} shell · {run.OtherCalls:N0} other"));
        panel.Children.Add(BuildLine(
            $"Read/search: {run.ReadSearchPercentage:0.#}% · Captured edits: {run.FilesChanged:N0} files, +{run.AddedLines:N0}/−{run.RemovedLines:N0} lines"));

        var card = new Border
        {
            Child = panel,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        return card;
    }

    private static TextBlock BuildLine(string text)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        line.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        line.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        return line;
    }

    private IReadOnlyList<WorkMetricsRunSnapshot> SafeGetRuns()
    {
        try { return _getRuns(); }
        catch (Exception ex)
        {
            SquadDashTrace.Write("WorkMetrics", $"Metrics refresh failed: {ex}");
            return [];
        }
    }

    private static string BuildRunFilterLabel(WorkMetricsRunSnapshot run) =>
        $"{run.AgentName} · {run.StartedAt.ToLocalTime():g}";

    private static string DisplayPhase(string kind) => kind switch
    {
        PlanWorkItemKinds.Implementation => "Implementation",
        PlanWorkItemKinds.Verification => "Verification",
        PlanWorkItemKinds.Validation => "Validation",
        _ => "Unscoped",
    };
}
