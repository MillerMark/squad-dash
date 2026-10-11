using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SquadDash.Tests;

// Focused UI regressions contributed under Vesper Knox's testing ownership.
[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class WorkMetricsWindowTests
{
    private static readonly string[] FilterFields = ["_phaseFilter", "_agentFilter", "_runFilter", "_statusFilter"];
    private static readonly DateTimeOffset Started = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Filters_DefaultToAll_WithFourIndependentOrderedOptionSets()
    {
        WithWindow((window, runs) =>
        {
            Assert.That(Options(window, "_phaseFilter"), Is.EqualTo(new[] { "All", "Implementation", "Validation", "Verification" }));
            Assert.That(Options(window, "_agentFilter"), Is.EqualTo(new[] { "All", "Alpha", "Beta" }));
            Assert.That(Options(window, "_runFilter"), Is.EqualTo(new[] { "All" }.Concat(
                runs.Select(RunLabel).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))));
            Assert.That(Options(window, "_statusFilter"), Is.EqualTo(new[] { "All", "Live", "Completed" }));
            Assert.That(FilterFields.Select(field => Radios(window, field)[0].GroupName).Distinct().Count(), Is.EqualTo(4));
            foreach (var field in FilterFields)
            {
                Assert.That(Selected(window, field), Is.EqualTo("All"));
                Assert.That(Radios(window, field).Count(radio => radio.IsChecked == true), Is.EqualTo(1));
                foreach (var radio in Radios(window, field))
                    Assert.That(((TextBlock)radio.Content).Text, Is.EqualTo(radio.Tag));
            }

            runs.Add(Run("blank", " ", PlanWorkItemKinds.Unscoped, false, 4));
            Refresh(window);
            Assert.That(Options(window, "_agentFilter"), Is.EqualTo(new[] { "All", "Alpha", "Beta" }));
            Assert.That(Options(window, "_phaseFilter"), Does.Contain("Unscoped"));
        });
    }

    [TestCase("_phaseFilter", "Verification", new[] { "r2" })]
    [TestCase("_agentFilter", "Alpha", new[] { "r1", "r2" })]
    [TestCase("_statusFilter", "Live", new[] { "r1", "r3" })]
    [TestCase("_statusFilter", "Completed", new[] { "r2" })]
    public void RadioSelection_UsesExistingFilterPredicatesAndRefreshesCards(string field, string option, string[] expected)
    {
        WithWindow((window, runs) =>
        {
            Select(window, field, option);
            AssertFiltered(window, runs, expected);
            Assert.That(Values(window)[0].Text, Is.EqualTo(expected.Length.ToString("N0")));
            foreach (var other in FilterFields.Where(candidate => candidate != field))
                Assert.That(Selected(window, other), Is.EqualTo("All"));
        });
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    public void SingleOption_ShowsBoldThemedText_AndLeavesMetricsUnfiltered(string theme)
    {
        WithWindow((window, runs) =>
        {
            runs.RemoveRange(1, 2);
            Refresh(window);
            var expectedLabels = new[] { "Implementation", "Alpha", RunLabel(runs[0]) };
            for (var index = 0; index < 3; index++)
            {
                var field = FilterFields[index];
                var group = Field<StackPanel>(window, field);
                Assert.That(group.Children.Count, Is.EqualTo(1));
                Assert.That(Radios(window, field), Is.Empty);
                var text = (TextBlock)group.Children[0];
                Assert.That(text.Text, Is.EqualTo(expectedLabels[index]));
                Assert.That(text.FontWeight, Is.EqualTo(FontWeights.Bold));
                Assert.That(text.Foreground, Is.SameAs(window.FindResource("LabelText")));
                Assert.That(text.FontSize, Is.EqualTo(window.FindResource("FontSizeBody")));
                Assert.That(text.Focusable, Is.False);
                Assert.That(Selected(window, field), Is.EqualTo("All"));
            }
            AssertFiltered(window, runs, "r1");
            Assert.That(Values(window)[0].Text, Is.EqualTo("1"));
            Assert.That(Values(window)[3].Text, Is.EqualTo("10"));
            Assert.That(Options(window, "_statusFilter"), Is.EqualTo(new[] { "All", "Live", "Completed" }));

            runs.Add(Run("same", "alpha", PlanWorkItemKinds.Implementation, false, 1));
            runs.Add(Run("blank", " ", PlanWorkItemKinds.Implementation, true, 2));
            Refresh(window);
            var agentGroup = Field<StackPanel>(window, "_agentFilter");
            var agentText = (TextBlock)agentGroup.Children[0];
            Assert.That(agentGroup.Children.Count, Is.EqualTo(1));
            Assert.That(agentText.Text, Is.EqualTo("Alpha"));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("All"));
            AssertFiltered(window, runs, "r1", "same", "blank");
            Assert.That(Values(window)[0].Text, Is.EqualTo("3"));
            Assert.That(Values(window)[3].Text, Is.EqualTo("30"));
            Assert.That(Values(window)[7].Text, Is.EqualTo("60%"));
            for (var tick = 0; tick < 3; tick++)
                Refresh(window);
            Assert.That(agentGroup.Children[0], Is.SameAs(agentText));

            window.Resources["FontSizeBody"] = 18.0;
            window.UpdateLayout();
            var checkedRadio = Radios(window, "_statusFilter").Single(radio => radio.IsChecked == true);
            Assert.That(agentText.FontSize, Is.EqualTo(18.0));
            Assert.That(agentText.FontSize, Is.EqualTo(((TextBlock)checkedRadio.Content).FontSize));
        }, theme: theme);
    }

    [Test]
    public void Refresh_SwitchesBetweenSingleTextAndRadios_WhenOptionSetsChange()
    {
        WithWindow((window, runs) =>
        {
            Select(window, "_agentFilter", "Alpha");
            runs.RemoveRange(1, 2);
            Refresh(window);
            var group = Field<StackPanel>(window, "_agentFilter");
            var originalText = (TextBlock)group.Children[0];
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("All"));

            runs[0] = runs[0] with { AgentName = "Delta" };
            Refresh(window);
            Assert.That(group.Children.Count, Is.EqualTo(1));
            Assert.That(group.Children[0], Is.Not.SameAs(originalText));
            Assert.That(((TextBlock)group.Children[0]).Text, Is.EqualTo("Delta"));

            runs.Add(Run("new", "Gamma", PlanWorkItemKinds.Implementation, true, 5));
            Refresh(window);
            Assert.That(group.Children.OfType<TextBlock>(), Is.Empty);
            Assert.That(Options(window, "_agentFilter"), Is.EqualTo(new[] { "All", "Delta", "Gamma" }));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("All"));
            Select(window, "_agentFilter", "Gamma");
            AssertFiltered(window, runs, "new");

            runs.RemoveAt(0);
            Refresh(window);
            Assert.That(Radios(window, "_agentFilter"), Is.Empty);
            Assert.That(((TextBlock)group.Children[0]).Text, Is.EqualTo("Gamma"));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("All"));
            AssertFiltered(window, runs, "new");

            runs.Clear();
            Refresh(window);
            Assert.That(group.Children.OfType<TextBlock>(), Is.Empty);
            Assert.That(Options(window, "_agentFilter"), Is.EqualTo(new[] { "All" }));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("All"));
            Assert.That(Field<TextBlock>(window, "_emptySummary").Visibility, Is.EqualTo(Visibility.Visible));
            runs.Add(Run("return", "Gamma", PlanWorkItemKinds.Validation, true, 6));
            Refresh(window);
            Assert.That(group.Children.Count, Is.EqualTo(1));
            Assert.That(Radios(window, "_agentFilter"), Is.Empty);
            Assert.That(((TextBlock)group.Children[0]).Text, Is.EqualTo("Gamma"));
            AssertFiltered(window, runs, "return");
            Assert.That(Field<TextBlock>(window, "_emptySummary").Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void RunAndCombinedSelections_AreIndependent_AndEmptyStateRecovers()
    {
        WithWindow((window, runs) =>
        {
            Select(window, "_runFilter", RunLabel(runs[1]));
            AssertFiltered(window, runs, "r2");
            Select(window, "_phaseFilter", "Verification");
            Select(window, "_agentFilter", "Alpha");
            Select(window, "_statusFilter", "Completed");
            AssertFiltered(window, runs, "r2");
            Assert.That(Selected(window, "_runFilter"), Is.EqualTo(RunLabel(runs[1])));
            Select(window, "_statusFilter", "Live");
            AssertFiltered(window, runs);
            Assert.That(Field<Grid>(window, "_summary").Visibility, Is.EqualTo(Visibility.Collapsed));
            var empty = Field<TextBlock>(window, "_emptySummary");
            Assert.That(empty.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(empty.Text, Is.EqualTo("No work metrics match these filters."));
            Select(window, "_statusFilter", "All");
            AssertFiltered(window, runs, "r2");
            Assert.That(empty.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Field<Grid>(window, "_summary").Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    public void Refresh_ReusesUnchangedRadiosAndFocus_AndOnlyRebuildsChangedGroups()
    {
        WithWindow((window, runs) =>
        {
            runs[1] = runs[1] with { AgentName = "Alpha" };
            Refresh(window);
            Select(window, "_phaseFilter", "Implementation");
            Select(window, "_agentFilter", "Alpha");
            Select(window, "_runFilter", RunLabel(runs[0]));
            Select(window, "_statusFilter", "Live");
            var originals = FilterFields.ToDictionary(field => field, field => Radios(window, field));
            var focused = originals["_agentFilter"].Single(radio => radio.IsChecked == true);
            FocusManager.SetFocusedElement(window, focused);
            runs.Reverse();
            for (var tick = 0; tick < 3; tick++)
                Refresh(window);
            foreach (var field in FilterFields)
                Assert.That(Radios(window, field), Is.EqualTo(originals[field]), field);
            Assert.That(FocusManager.GetFocusedElement(window), Is.SameAs(focused));

            runs.Add(Run("new", "Gamma", PlanWorkItemKinds.Implementation, true, 5));
            Refresh(window);
            Assert.That(Radios(window, "_phaseFilter"), Is.EqualTo(originals["_phaseFilter"]));
            Assert.That(Radios(window, "_statusFilter"), Is.EqualTo(originals["_statusFilter"]));
            Assert.That(Radios(window, "_agentFilter")[0], Is.Not.SameAs(originals["_agentFilter"][0]));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("Alpha"));
            Assert.That(Selected(window, "_runFilter"), Is.EqualTo(RunLabel(runs.Single(run => run.RunId == "r1"))));
            AssertFiltered(window, runs, "r1");
        });
    }

    [Test]
    public void Refresh_PreservesCaseInsensitiveSelections_AndFallsBackWhenValuesDisappear()
    {
        WithWindow((window, runs) =>
        {
            Select(window, "_phaseFilter", "Implementation");
            Select(window, "_agentFilter", "Alpha");
            Select(window, "_runFilter", RunLabel(runs[0]));
            Select(window, "_statusFilter", "Live");
            runs[0] = runs[0] with { AgentName = "ALPHA" };
            Refresh(window);
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("ALPHA"));
            Assert.That(Selected(window, "_runFilter"), Is.EqualTo(RunLabel(runs[0])));
            AssertFiltered(window, runs, "r1");

            runs.Clear();
            Refresh(window);
            foreach (var field in FilterFields.Take(3))
            {
                Assert.That(Options(window, field), Is.EqualTo(new[] { "All" }));
                Assert.That(Selected(window, field), Is.EqualTo("All"));
            }
            Assert.That(Selected(window, "_statusFilter"), Is.EqualTo("Live"), "Status options never disappear.");
            Assert.That(Field<TextBlock>(window, "_emptySummary").Visibility, Is.EqualTo(Visibility.Visible));
            runs.Add(Run("return", "Delta", PlanWorkItemKinds.Validation, true, 6));
            Refresh(window);
            AssertFiltered(window, runs, "return");
        });
    }

    [Test]
    public void ProgrammaticRepopulation_DoesNotRecursivelyRefresh()
    {
        var reads = 0;
        WithWindow((window, runs) =>
        {
            Select(window, "_agentFilter", "Alpha");
            runs.Clear();
            reads = 0;
            Refresh(window);
            Assert.That(reads, Is.EqualTo(2), "One snapshot for options and one for counts; no Checked-driven recursion.");
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("All"));
        }, () => reads++);
    }

    [Test]
    public void Timer_RefreshesLiveCountsAndOptions_PreservesSelection_AndStopsOnClose()
    {
        WithWindow((window, runs) =>
        {
            Select(window, "_agentFilter", "Alpha");
            Select(window, "_statusFilter", "Live");
            var originals = FilterFields.ToDictionary(field => field, field => Radios(window, field));
            var timer = Field<DispatcherTimer>(window, "_refreshTimer");
            Assert.That(timer.Interval, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(timer.IsEnabled, Is.True);
            runs[0] = runs[0] with { ToolCalls = 1234 };
            PumpUntil(() => Values(window)[3].Text == 1234.ToString("N0"));
            foreach (var field in FilterFields)
                Assert.That(Radios(window, field), Is.EqualTo(originals[field]));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("Alpha"));
            Assert.That(Selected(window, "_statusFilter"), Is.EqualTo("Live"));
            var card = (StackPanel)Field<StackPanel>(window, "_runRows").Children.OfType<Border>().Single().Child;
            Assert.That(card.Children.OfType<TextBlock>().Any(text => text.Text.Contains(1234.ToString("N0"))), Is.True);

            runs.Add(Run("new", "Gamma", PlanWorkItemKinds.Implementation, true, 5));
            PumpUntil(() => Options(window, "_agentFilter").Contains("Gamma"));
            Assert.That(Selected(window, "_agentFilter"), Is.EqualTo("Alpha"));
            Assert.That(Values(window)[3].Text, Is.EqualTo(1234.ToString("N0")));
            window.Close();
            Assert.That(timer.IsEnabled, Is.False);
        });
    }

    [Test]
    public void Summary_HasNineRows_FormattedWeightedTotals_AndCompactLeftAlignedValues()
    {
        WithWindow((window, runs) =>
        {
            runs.Clear();
            runs.Add(Run("large", "Alpha", PlanWorkItemKinds.Implementation, true, 0) with
            {
                ToolCalls = 1000, ReadCalls = 600, SearchCalls = 100, WriteCalls = 200, FilesChanged = 1200,
            });
            runs.Add(Run("small", "alpha", PlanWorkItemKinds.Verification, false, 1) with
            {
                ParentRunId = "large", ToolCalls = 3, ReadCalls = 1, SearchCalls = 1, WriteCalls = 1, FilesChanged = 2,
            });
            Refresh(window);
            var summary = Field<Grid>(window, "_summary");
            Assert.That(summary.RowDefinitions.Count, Is.EqualTo(9));
            Assert.That(summary.ColumnDefinitions.Count, Is.EqualTo(2));
            Assert.That(summary.ColumnDefinitions.All(column => column.Width.IsAuto), Is.True);
            Assert.That(summary.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Left));
            Assert.That(summary.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) == 0).Select(text => text.Text),
                Is.EqualTo(new[] { "Runs", "Agents", "Child runs", "Tool calls", "Reads", "Searches", "Writes", "Read/search %", "Captured files" }));
            Assert.That(Values(window).Select(text => text.Text), Is.EqualTo(new[]
            {
                "2", "1", "1", 1003.ToString("N0"), 601.ToString("N0"), 101.ToString("N0"), 201.ToString("N0"),
                (70200.0 / 1003).ToString("0.#") + "%", 1202.ToString("N0"),
            }));
            window.UpdateLayout();
            var labels = summary.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) == 0).ToArray();
            Assert.That(summary.ColumnDefinitions[0].ActualWidth,
                Is.EqualTo(labels.Max(label => label.ActualWidth)).Within(0.1));
            Assert.That(summary.ActualWidth, Is.LessThan(((Grid)summary.Parent).ActualWidth));
            for (var row = 0; row < 9; row++)
            {
                var value = Values(window)[row];
                Assert.That(Grid.GetRow(value), Is.EqualTo(row));
                Assert.That(Grid.GetColumn(value), Is.EqualTo(1));
                Assert.That(value.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Left));
                Assert.That(value.TextAlignment, Is.EqualTo(TextAlignment.Left));
                Assert.That(value.Margin, Is.EqualTo(new Thickness(12, 0, 0, 0)));
                Assert.That(value.TranslatePoint(new Point(), summary).X,
                    Is.EqualTo(summary.ColumnDefinitions[0].ActualWidth + 12).Within(0.1));
                Assert.That(labels[row].TranslatePoint(new Point(), summary).X, Is.EqualTo(0).Within(0.1));
            }
            runs.Clear();
            runs.Add(Run("zero", "Zero", PlanWorkItemKinds.Unscoped, false, 0) with
            {
                ToolCalls = 0, ReadCalls = 0, SearchCalls = 0, WriteCalls = 0, FilesChanged = 0,
            });
            Refresh(window);
            Assert.That(Values(window)[7].Text, Is.EqualTo("0%"));
        });
    }

    [Test]
    public void Summary_LabelsHaveNonemptyThemedTooltips_ValuesHaveNone()
    {
        WithWindow((window, _) =>
        {
            var summary = Field<Grid>(window, "_summary");
            var labels = summary.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) == 0).ToArray();
            Assert.That(labels.Length, Is.EqualTo(9));
            foreach (var label in labels)
            {
                Assert.That(label.ToolTip, Is.TypeOf<ToolTip>(), label.Text);
                var tooltip = (ToolTip)label.ToolTip;
                Assert.That(tooltip.Content, Is.TypeOf<TextBlock>(), label.Text);
                Assert.That(((TextBlock)tooltip.Content).Text, Is.Not.Null.And.Not.Empty, label.Text);
                tooltip.Resources = window.Resources;
                Assert.That(tooltip.Background, Is.SameAs(window.FindResource("InputSurface")), label.Text);
                Assert.That(tooltip.BorderBrush, Is.SameAs(window.FindResource("InputBorder")), label.Text);
                Assert.That(((TextBlock)tooltip.Content).Foreground, Is.SameAs(window.FindResource("ToolTipForeground")), label.Text);
            }
            var filesTooltip = (ToolTip)labels.Single(label => label.Text == "Captured files").ToolTip;
            Assert.That(((TextBlock)filesTooltip.Content).Text, Does.Contain("each run's distinct files"));
            Assert.That(((TextBlock)filesTooltip.Content).Text, Does.Contain("same file can count again"));
            Assert.That(Values(window).All(value => value.ToolTip is null), Is.True);
        });
    }

    [TestCase("Dark", 760, 620)]
    [TestCase("Light", 580, 420)]
    public void Layout_KeepsAllOptionsScrollable_CardsInStarRow_AndDescriptionAtBottom(string theme, double width, double height)
    {
        WithWindow((window, runs) =>
        {
            window.Width = width;
            window.Height = height;
            for (var i = 0; i < 50; i++)
                runs.Add(Run($"extra{i}", $"Agent {i:D2}", PlanWorkItemKinds.Implementation, true, i + 10));
            Refresh(window);
            window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(30));
            var summary = Field<Grid>(window, "_summary");
            var summaryHost = (Grid)summary.Parent;
            var root = (Grid)summaryHost.Parent;
            var filterScroll = root.Children.OfType<ScrollViewer>().Single(scroll => Grid.GetRow(scroll) == 0);
            var cardScroll = root.Children.OfType<ScrollViewer>().Single(scroll => Grid.GetRow(scroll) == 2);
            var footer = root.Children.OfType<TextBlock>().Single();
            var grid = (Grid)filterScroll.Content;
            Assert.That(grid.ColumnDefinitions.Count, Is.EqualTo(4));
            Assert.That(grid.Children.OfType<StackPanel>().Select(Grid.GetColumn), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(root.RowDefinitions.Select(row => row.Height.GridUnitType),
                Is.EqualTo(new[] { GridUnitType.Auto, GridUnitType.Auto, GridUnitType.Star, GridUnitType.Auto }));
            Assert.That(Grid.GetRow(summaryHost), Is.EqualTo(1));
            Assert.That(Grid.GetRow(footer), Is.EqualTo(3));
            Assert.That(footer.Text, Does.StartWith("Counts come directly from stored tool events."));
            Assert.That(footer.Foreground, Is.SameAs(window.FindResource("SubtleText")));
            Assert.That(Options(window, "_agentFilter").Length, Is.EqualTo(53));
            Assert.That(filterScroll.ScrollableHeight, Is.GreaterThan(0));
            Assert.That(filterScroll.ActualHeight, Is.LessThanOrEqualTo(root.ActualHeight * 0.25 + 1));
            Assert.That(cardScroll.ActualHeight, Is.GreaterThan(0));
            Assert.That(cardScroll.Content, Is.SameAs(Field<StackPanel>(window, "_runRows")));
            filterScroll.ScrollToEnd();
            window.UpdateLayout();
            var offset = filterScroll.VerticalOffset;
            Assert.That(offset, Is.GreaterThan(0));
            Refresh(window);
            window.UpdateLayout();
            Assert.That(filterScroll.VerticalOffset, Is.EqualTo(offset));
            Assert.That(Radios(window, "_agentFilter")[0].Foreground, Is.SameAs(window.FindResource("LabelText")));
            Assert.That(Radios(window, "_agentFilter")[0].Style, Is.SameAs(window.FindResource("ThemedRadioButtonStyle")));
        }, theme: theme);
    }

    private static void WithWindow(Action<WorkMetricsWindow, List<WorkMetricsRunSnapshot>> assertion,
        Action? onRead = null, string theme = "Dark")
    {
        var runs = new List<WorkMetricsRunSnapshot>
        {
            Run("r1", "Alpha", PlanWorkItemKinds.Implementation, true, 0),
            Run("r2", "alpha", PlanWorkItemKinds.Verification, false, 1),
            Run("r3", "Beta", PlanWorkItemKinds.Validation, true, 2),
        };
        var window = new WorkMetricsWindow("Work Metrics", () => { onRead?.Invoke(); return runs; })
        {
            ShowActivated = false,
        };
        try
        {
            LoadResources(window, theme);
            window.Show();
            PumpUntil(() => window.IsLoaded);
            assertion(window, runs);
        }
        finally
        {
            window.Close();
        }
    }

    private static void LoadResources(WorkMetricsWindow window, string theme)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SquadDash", "App.xaml")))
            directory = directory.Parent;
        var source = directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found.");
        using var stream = File.OpenRead(Path.Combine(source, "SquadDash", "Themes", theme + ".xaml"));
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var app = XDocument.Load(Path.Combine(source, "SquadDash", "App.xaml"));
        var styles = app.Descendants(presentation + "Style").Where(element =>
            (string?)element.Attribute(xaml + "Key") is "ThemedRadioButtonStyle" or "RosterScrollViewerStyle");
        var resources = new XElement(presentation + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", xaml), styles);
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(resources.ToString()));
        window.Resources["FontSizeBody"] = 13.0;
        window.Resources["FontSizeSmall"] = 11.0;
    }

    private static WorkMetricsRunSnapshot Run(string id, string agent, string phase, bool live, int minute) =>
        new(id, agent, null, null, phase, id, Started.AddMinutes(minute),
            live ? null : Started.AddMinutes(minute + 1), live, 10, 4, 2, 3, 0, 1, 0, 2, 5, 1);

    private static string RunLabel(WorkMetricsRunSnapshot run) => $"{run.AgentName} \u00b7 {run.StartedAt.ToLocalTime():g}";
    private static T Field<T>(WorkMetricsWindow window, string name) =>
        (T)typeof(WorkMetricsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static RadioButton[] Radios(WorkMetricsWindow window, string field) =>
        Field<StackPanel>(window, field).Children.OfType<RadioButton>().ToArray();
    private static string[] Options(WorkMetricsWindow window, string field) => Radios(window, field).Select(radio => (string)radio.Tag).ToArray();
    private static string Selected(WorkMetricsWindow window, string field) =>
        (string)typeof(WorkMetricsWindow).GetMethod("SelectedValue", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [Field<StackPanel>(window, field)])!;
    private static void Select(WorkMetricsWindow window, string field, string value) =>
        Radios(window, field).Single(radio => (string)radio.Tag == value).IsChecked = true;
    private static TextBlock[] Values(WorkMetricsWindow window) => Field<TextBlock[]>(window, "_summaryValues");
    private static void Refresh(WorkMetricsWindow window) =>
        typeof(WorkMetricsWindow).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    private static void AssertFiltered(WorkMetricsWindow window, List<WorkMetricsRunSnapshot> runs, params string[] expected)
    {
        var filtered = (IEnumerable<WorkMetricsRunSnapshot>)typeof(WorkMetricsWindow)
            .GetMethod("ApplyFilters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [runs])!;
        Assert.That(filtered.Select(run => run.RunId), Is.EquivalentTo(expected));
        var cards = Field<StackPanel>(window, "_runRows").Children.OfType<Border>().ToArray();
        Assert.That(cards.Length, Is.EqualTo(expected.Length));
        Assert.That(cards.Select(card => ((StackPanel)card.Child).Children.OfType<TextBlock>().First().Text.Split(" \u00b7 ").Last()),
            Is.EquivalentTo(expected));
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            Pump(TimeSpan.FromMilliseconds(20));
        Assert.That(condition(), Is.True, "Expected dispatcher update was not delivered.");
    }
}
