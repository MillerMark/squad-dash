using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>A live, theme-aware view over deterministic transcript work metrics.</summary>
internal sealed class WorkMetricsWindow : ChromedWindow
{
    private const string All = "All";
    private readonly Func<IReadOnlyList<WorkMetricsRunSnapshot>> _getRuns;
    private readonly StackPanel _phaseFilter;
    private readonly StackPanel _agentFilter;
    private readonly StackPanel _runFilter;
    private readonly StackPanel _statusFilter;
    private readonly Grid _summary;
    private readonly TextBlock[] _summaryValues;
    private readonly TextBlock _emptySummary;
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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var filters = new Grid();
        _phaseFilter = AddFilter(filters, "Phase");
        _agentFilter = AddFilter(filters, "Agent");
        _runFilter = AddFilter(filters, "Run");
        _statusFilter = AddFilter(filters, "Status");
        var filterScroll = new ScrollViewer
        {
            Content = filters,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
        };
        filterScroll.SetResourceReference(Control.StyleProperty, "RosterScrollViewerStyle");
        filterScroll.SetBinding(MaxHeightProperty, new Binding(nameof(ActualHeight))
        {
            Source = root,
            Converter = new FractionConverter(),
            ConverterParameter = "0.25",
        });
        root.Children.Add(filterScroll);

        var summaryHost = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        _summary = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        _summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        (string Label, string ToolTip)[] labels =
        {
            ("Runs", "The number of agent runs (transcript turns) included in these metrics."),
            ("Agents", "The number of distinct agent names in these runs, ignoring letter case."),
            ("Child runs", "Runs with a recorded parent reference, indicating sub-agent or delegated work."),
            ("Tool calls", "Total recorded tool calls across these runs, including failed and in-progress calls."),
            ("Reads", "Tool calls classified by name as reading files, content, or agent and session information."),
            ("Searches", "Tool calls classified by name as searches, such as grep, glob, or web search."),
            ("Writes", "Tool calls classified by name as file creation or editing, regardless of success."),
            ("Read/search %", "The percentage of all recorded tool calls classified as reads or searches."),
            ("Captured files", "Sum of each run's distinct files with captured edits from successful, completed tool calls; the same file can count again in another run."),
        };
        _summaryValues = new TextBlock[labels.Length];
        for (var row = 0; row < labels.Length; row++)
        {
            _summary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = BuildSummaryText(labels[row].Label);
            label.ToolTip = ToolTipHelper.MakeThemedToolTip(labels[row].ToolTip);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            var value = BuildSummaryText(string.Empty);
            value.HorizontalAlignment = HorizontalAlignment.Left;
            value.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            _summary.Children.Add(label);
            _summary.Children.Add(value);
            _summaryValues[row] = value;
        }
        _emptySummary = BuildSummaryText("No work metrics match these filters.");
        _emptySummary.Visibility = Visibility.Collapsed;
        summaryHost.Children.Add(_summary);
        summaryHost.Children.Add(_emptySummary);
        Grid.SetRow(summaryHost, 1);
        root.Children.Add(summaryHost);

        var description = new TextBlock
        {
            Text = "Counts come directly from stored tool events. Unknown tool names remain Other; command text is never inferred.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        description.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        Grid.SetRow(description, 3);
        root.Children.Add(description);

        _runRows = new StackPanel { Orientation = Orientation.Vertical };
        var scroll = new ScrollViewer
        {
            Content = _runRows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.SetResourceReference(Control.StyleProperty, "RosterScrollViewerStyle");
        Grid.SetRow(scroll, 2);
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

    private static StackPanel AddFilter(Grid parent, string label)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        caption.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        var options = new StackPanel { Name = label + "Filter" };
        group.Children.Add(caption);
        group.Children.Add(options);
        Grid.SetColumn(group, parent.ColumnDefinitions.Count);
        parent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 130 });
        parent.Children.Add(group);
        return options;
    }

    private static TextBlock BuildSummaryText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        block.SetResourceReference(TextBlock.ForegroundProperty, "ActivePanelTitle");
        block.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        return block;
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
            ReplaceItems(_statusFilter, new[] { "Live", "Completed" }, sort: false);
        }
        finally
        {
            _updatingFilters = false;
        }
    }

    private void ReplaceItems(StackPanel group, IEnumerable<string> values, bool sort = true)
    {
        var selected = SelectedValue(group);
        var options = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var items = new[] { All }.Concat(sort
            ? options.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            : options).ToArray();
        // Keep the existing controls (and keyboard focus) through unchanged timer ticks.
        if (group.Tag is string[] previousItems && previousItems.SequenceEqual(items))
            return;

        selected = items.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? All;
        group.Children.Clear();
        group.Tag = items;
        if (items.Length == 2)
        {
            var text = new TextBlock
            {
                Text = items[1],
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 2),
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            group.Children.Add(text);
            return;
        }

        foreach (var item in items)
        {
            var radio = new RadioButton
            {
                Content = new TextBlock { Text = item },
                Tag = item,
                GroupName = group.Name,
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = item == selected,
            };
            radio.SetResourceReference(Control.StyleProperty, "ThemedRadioButtonStyle");
            radio.SetResourceReference(Control.ForegroundProperty, "LabelText");
            radio.Checked += (_, _) => { if (!_updatingFilters) Refresh(); };
            group.Children.Add(radio);
        }
    }

    private static string SelectedValue(StackPanel group) =>
        group.Children.OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true)?.Tag as string ?? All;

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
        _summary.Visibility = runs.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _emptySummary.Visibility = runs.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var summaryValues = new[]
        {
            $"{runs.Length:N0}", $"{agentCount:N0}", $"{childRunCount:N0}", $"{calls:N0}",
            $"{reads:N0}", $"{searches:N0}", $"{writes:N0}", $"{readSearchPct:0.#}%", $"{files:N0}",
        };
        for (var row = 0; row < summaryValues.Length; row++)
            _summaryValues[row].Text = summaryValues[row];

        _runRows.Children.Clear();
        foreach (var run in runs.OrderByDescending(run => run.IsLive).ThenBy(run => run.StartedAt))
            _runRows.Children.Add(BuildRunCard(run, now));
    }

    private IEnumerable<WorkMetricsRunSnapshot> ApplyFilters(IEnumerable<WorkMetricsRunSnapshot> runs)
    {
        var phase = SelectedValue(_phaseFilter);
        var agent = SelectedValue(_agentFilter);
        var runLabel = SelectedValue(_runFilter);
        var status = SelectedValue(_statusFilter);
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
