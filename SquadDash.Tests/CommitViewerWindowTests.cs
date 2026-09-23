using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CommitViewerWindowTests
{
    [Test]
    public void EveryLaunch_ConnectsNavigationToTheExistingEnvironmentZoomHandler()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "MainWindow.xaml.cs")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "MainWindow.xaml.cs"));
        var launches = Regex.Matches(source,
            @"var viewer = await CommitViewerWindow\.Create(?:Range|FromPatch)?Async\([\s\S]*?\);(?<wiring>[\s\S]*?)viewer\.Show\(\);");

        Assert.That(launches, Is.Not.Empty);
        foreach (Match launch in launches)
            Assert.That(launch.Groups["wiring"].Value,
                Does.Contain("viewer.ConfigureEnvironmentFontZoom(Window_PreviewMouseWheel);"));
    }

    [TestCase("--- a/file.cs\n+++ b/file.cs\n@@ -1 +1 @@\n---old\n+++new", 1, 1)]
    [TestCase("--- /dev/null\n+++ b/file.cs\n@@ -0,0 +1,2 @@\n+first\n+second", 2, 0)]
    [TestCase("--- a/file.cs\n+++ /dev/null\n@@ -1,2 +0,0 @@\n-first\n-second", 0, 2)]
    [TestCase("", 0, 0)]
    [TestCase(" \r\n ", 0, 0)]
    public void CreateFromPatchAsync_RendersSuppliedContentWithoutRepositoryOrCommits(
        string patch, int added, int removed) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? window = null;
            try
            {
                var creation = CommitViewerWindow.CreateFromPatchAsync("file.cs", patch);
                Assert.That(creation.IsCompletedSuccessfully, Is.True, "No asynchronous git loading is needed.");
                window = creation.GetAwaiter().GetResult();
                var files = ViewerField<ListBox>(window, "_fileList");
                var commits = ViewerField<ListBox>(window, "_commitList");
                var diff = ViewerField<StackPanel>(window, "_diffPanel");
                Assert.Multiple(() =>
                {
                    Assert.That(ViewerField<string?>(window, "_repositoryPath"), Is.Null);
                    Assert.That(ViewerField<string>(window, "_commitSha"), Is.Empty);
                    Assert.That(ViewerField<IReadOnlyList<CommitViewerEntry>>(window, "_commits"), Is.Empty);
                    Assert.That(commits.Items.Count, Is.Zero);
                    Assert.That(commits.Parent, Is.Null);
                    Assert.That(files.Items.Count, Is.EqualTo(1));
                    Assert.That(files.SelectedIndex, Is.Zero);
                    Assert.That(ViewerField<TextBlock>(window, "_heading").Text, Is.EqualTo("file.cs"));
                    Assert.That(window.Title, Is.EqualTo("Changes - file.cs"));
                    var row = (Grid)((ListBoxItem)files.Items[0]).Content;
                    Assert.That(((TextBlock)row.Children[0]).Text, Is.EqualTo("file.cs"));
                    var summary = (TextBlock)row.Children[1];
                    Assert.That(new TextRange(summary.ContentStart, summary.ContentEnd).Text,
                        Is.EqualTo($"+{added}  -{removed}"));
                });

                void AssertDiff()
                {
                    var lines = diff.Children.OfType<TextBlock>().ToArray();
                    if (string.IsNullOrWhiteSpace(patch))
                    {
                        Assert.That(lines.Single().Text, Is.EqualTo("No text diff is available for this file."));
                        return;
                    }
                    Assert.That(lines.Select(line => new TextRange(line.ContentStart, line.ContentEnd).Text),
                        Is.EqualTo(patch.Split('\n')));
                    Assert.That(lines.Count(line => Equals(line.Tag, DiffLineKind.Added)), Is.EqualTo(added));
                    Assert.That(lines.Count(line => Equals(line.Tag, DiffLineKind.Removed)), Is.EqualTo(removed));
                    foreach (var line in lines.Where(line => Equals(line.Tag, DiffLineKind.Added) ||
                                                            Equals(line.Tag, DiffLineKind.Removed)))
                        Assert.That(line.FontSize, Is.EqualTo(ViewerField<double>(window, "_codeFontSize")));
                }

                AssertDiff();
                files.SelectedIndex = -1;
                files.SelectedIndex = 0;
                AssertDiff();
                var ex = Assert.Throws<TargetInvocationException>(() =>
                    InvokeViewer(window, "RunGitAsync", (object)new[] { "--version" }));
                Assert.That(ex!.InnerException, Is.TypeOf<InvalidOperationException>(),
                    "Patch mode must reject git before starting any process.");
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    [TestCase(0.75, false)]
    [TestCase(1.0, false)]
    [TestCase(1.6, false)]
    [TestCase(0.75, true)]
    [TestCase(1.0, true)]
    [TestCase(1.6, true)]
    public void Navigation_UsesEnvironmentScaleAndRoutesZoomWithoutChangingDiff(double initialScale, bool binary) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var keys = new[] { "FontSizeBody", "FontSizeNormal", "FontSizeSmall", "FontSizeSubtitle" };
            var saved = keys.Where(app.Resources.Contains).ToDictionary(key => key, key => app.Resources[key]);
            foreach (var key in saved.Keys)
                app.Resources.Remove(key);

            static ResourceDictionary FontResources(double scale) => new()
            {
                ["FontSizeBody"] = Math.Round(13 * scale, 1),
                ["FontSizeNormal"] = Math.Round(13 * scale, 1),
                ["FontSizeSmall"] = Math.Round(12 * scale, 1),
                ["FontSizeSubtitle"] = Math.Round(18 * scale, 1)
            };

            var fonts = FontResources(initialScale);
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? window = null;
            try
            {
                window = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12"), new("abcdef34") }, null, null],
                    culture: null)!;
                var fileType = typeof(CommitViewerWindow).GetNestedType("CommitFile", BindingFlags.NonPublic)!;
                var file = Activator.CreateInstance(fileType, "SquadDash/CommitViewerWindow.cs", 12, 3, binary)!;
                var item = (ListBoxItem)InvokeViewer(window, "CreateFileItem", file)!;
                var fileList = ViewerField<ListBox>(window, "_fileList");
                fileList.Items.Add(item);
                var row = (Grid)item.Content;
                var path = (TextBlock)row.Children[0];
                var summary = (TextBlock)row.Children[1];
                var commitList = ViewerField<ListBox>(window, "_commitList");
                var commitLabel = new TextBlock { Text = "Commit subject" };
                commitList.Items.Add(new ListBoxItem { Content = commitLabel });
                InvokeViewer(window, "RenderDiff", "diff --git a/file b/file\n@@ -1 +1 @@\n-old\n+new");
                var diffPanel = ViewerField<StackPanel>(window, "_diffPanel");
                var diffScroll = (ScrollViewer)diffPanel.Parent;
                var diffSizes = diffPanel.Children.OfType<TextBlock>().Select(line => line.FontSize).ToArray();

                void AssertScale(double scale)
                {
                    window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    Assert.Multiple(() =>
                    {
                        Assert.That(path.FontSize, Is.EqualTo(Math.Round(13 * scale, 1)));
                        Assert.That(summary.FontSize, Is.EqualTo(path.FontSize));
                        foreach (var run in summary.Inlines.OfType<Run>())
                            Assert.That(run.FontSize, Is.EqualTo(path.FontSize));
                        Assert.That(commitLabel.FontSize, Is.EqualTo(path.FontSize));
                        Assert.That(ViewerField<TextBlock>(window, "_status").FontSize, Is.EqualTo(path.FontSize));
                        Assert.That(ViewerField<TextBlock>(window, "_heading").FontSize, Is.EqualTo(Math.Round(18 * scale, 1)));
                        Assert.That(diffPanel.Children.OfType<TextBlock>().Select(line => line.FontSize), Is.EqualTo(diffSizes));
                        Assert.That(fileList.Items[0], Is.SameAs(item), "Existing rows must update without rebuilding.");
                    });
                }

                AssertScale(initialScale);
                var calls = 0;
                window.ConfigureEnvironmentFontZoom((_, e) =>
                {
                    calls++;
                    var next = FontResources(e.Delta > 0 ? 1.6 : 0.75);
                    app.Resources.MergedDictionaries[app.Resources.MergedDictionaries.IndexOf(fonts)] = next;
                    fonts = next;
                    e.Handled = true;
                });

                static MouseWheelEventArgs Wheel(int delta) => new(Mouse.PrimaryDevice, Environment.TickCount, delta)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };

                var fileWheel = Wheel(120);
                path.RaiseEvent(fileWheel);
                Assert.That(fileWheel.Handled, Is.True);
                Assert.That(calls, Is.EqualTo(1));
                AssertScale(1.6);
                commitLabel.RaiseEvent(Wheel(-120));
                Assert.That(calls, Is.EqualTo(2));
                AssertScale(0.75);

                // A zero-delta event cannot change the persisted code zoom, even if Ctrl is physically held.
                diffScroll.RaiseEvent(Wheel(0));
                Assert.That(calls, Is.EqualTo(2), "The environment handler must not receive diff-pane input.");
                AssertScale(0.75);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
                foreach (var (key, value) in saved)
                    app.Resources[key] = value;
            }
        });

    private static T ViewerField<T>(CommitViewerWindow window, string name) =>
        (T)typeof(CommitViewerWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static object? InvokeViewer(CommitViewerWindow window, string name, params object[] arguments) =>
        typeof(CommitViewerWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    [Test]
    public void FileListAutoFit_WiresHardenedSplitterWithGuardsAndWidthAssignment()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "CommitViewerWindow.cs")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "Could not find CommitViewerWindow.cs.");
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "CommitViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("CommitViewerLayout.CreateFileListSplitter((sender, e) =>"));
            Assert.That(source, Does.Match(
                @"if \(e\.ClickCount != 2\)\s*\{\s*return;\s*\}\s*" +
                @"if \(_fileList\.Items\.Count == 0\)\s*\{\s*return;\s*\}\s*e\.Handled = true;"));
            Assert.That(source, Does.Contain("var fileColumn = split.ColumnDefinitions[offset];"));
            Assert.That(source, Does.Contain(".Select(CommitViewerLayout.MeasureFileListItemWidth).ToArray()"));
            Assert.That(source, Does.Match(
                @"var fittedWidth = new GridLength\(CommitViewerLayout\.CalculateFileListWidth\(\s*" +
                @"itemWidths,\s*chromeWidth, availableWidth, fileColumn\.ActualWidth\)\);\s*" +
                @"fileColumn\.Width = fittedWidth;"));
            Assert.That(source.IndexOf("split.Children.Add(splitter)", StringComparison.Ordinal),
                Is.GreaterThan(source.IndexOf("split.Children.Add(diffScroll)", StringComparison.Ordinal)));
            Assert.That(source, Does.Not.Contain("splitter.PreviewMouseDoubleClick +="));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA)]
    public void FileListSplitter_ReceivesPreviewInputEvenWhenAlreadyHandled(bool handled)
    {
        MouseButtonEventArgs? received = null;
        var splitter = CommitViewerLayout.CreateFileListSplitter((_, e) => received = e);
        var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Handled = handled
        };

        splitter.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.SameAs(input));
            Assert.That(input.Handled, Is.EqualTo(handled), "Registration must not consume ordinary drag input.");
        });
    }

    [TestCase(0)]
    [TestCase(2)]
    [Apartment(ApartmentState.STA)]
    public void FileListSplitter_HitTargetFillsItsColumnAboveOverlappingSiblings(int offset)
    {
        var split = new Grid();
        for (var column = 0; column < offset; column++)
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CommitViewerLayout.FileListSplitterWidth) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var splitter = CommitViewerLayout.CreateFileListSplitter((_, _) => { });
        Grid.SetColumn(splitter, offset + 1);
        split.Children.Add(splitter);
        var overlappingSibling = new Border { Background = Brushes.Red };
        Grid.SetColumnSpan(overlappingSibling, split.ColumnDefinitions.Count);
        split.Children.Add(overlappingSibling);
        split.Measure(new Size(1100, 600));
        split.Arrange(new Rect(0, 0, 1100, 600));
        split.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(splitter.ActualWidth, Is.EqualTo(8));
            Assert.That(splitter.ActualHeight, Is.EqualTo(600));
            Assert.That(splitter.ResizeDirection, Is.EqualTo(GridResizeDirection.Columns));
            Assert.That(splitter.ResizeBehavior, Is.EqualTo(GridResizeBehavior.PreviousAndNext));
            foreach (var x in new[] { 0.5, 4.0, 7.5 })
            foreach (var y in new[] { 1.0, 300.0, 599.0 })
            {
                var point = splitter.TranslatePoint(new Point(x, y), split);
                var hit = VisualTreeHelper.HitTest(split, point)?.VisualHit;
                Assert.That(hit is not null && (hit == splitter || splitter.IsAncestorOf(hit)), Is.True,
                    $"Divider must receive input at ({x}, {y}) even without a LineColor resource.");
            }
        });
    }

    [Test]
    public async Task ResolveRepositoryRootAsync_FromNestedWorkspace_ReturnsTopLevelDirectory()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            "SquadDashCommitViewer",
            Guid.NewGuid().ToString("N"));
        var nestedWorkspace = Path.Combine(repositoryRoot, "Package");
        Directory.CreateDirectory(nestedWorkspace);

        try
        {
            await CommitViewerGit.RunAsync(repositoryRoot, "init", "--quiet");

            var resolved = await CommitViewerGit.ResolveRepositoryRootAsync(nestedWorkspace);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(repositoryRoot)).IgnoreCase);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [TestCase(1920, 1440)]
    [TestCase(2560, 1600)]
    [TestCase(3840, 1600)]
    [TestCase(800, 720)]
    public void CalculateWindowWidth_UsesThreeQuartersWithSensibleLimits(
        double workingWidth,
        double expected)
    {
        Assert.That(CommitViewerLayout.CalculateWindowWidth(workingWidth), Is.EqualTo(expected));
    }

    [Test]
    public void CalculateFileListWidth_FitsWidestCompleteRowPlusListChrome()
    {
        var width = CommitViewerLayout.CalculateFileListWidth([120, 501.25, 400], 27, 1100, 330);

        Assert.That(width, Is.EqualTo(529));
    }

    [TestCase(100, 1100, 330)]
    [TestCase(2000, 1100, 860)]
    [TestCase(2000, 1600, 1280)]
    [TestCase(2000, 700, 460)]
    [TestCase(2000, 500, 260)]
    [TestCase(2000, 200, 0)]
    public void CalculateFileListWidth_PreservesDiffSpaceWithinRemainingPaneWidth(
        double itemWidth, double availableWidth, double expected)
    {
        Assert.That(
            CommitViewerLayout.CalculateFileListWidth([itemWidth], 27, availableWidth, 330),
            Is.EqualTo(expected));
    }

    [Test]
    public void CalculateFileListWidth_EmptyListKeepsCurrentWidth()
    {
        Assert.That(
            CommitViewerLayout.CalculateFileListWidth([], 27, 1100, 415),
            Is.EqualTo(415));
    }

    [TestCase(12, false)]
    [TestCase(24, false)]
    [TestCase(24, true)]
    [Apartment(ApartmentState.STA)]
    public void MeasureFileListItemWidth_IncludesUntrimmedPathSummaryAndChrome(double fontSize, bool binary)
    {
        var path = new TextBlock
        {
            Text = "SquadDash/VeryLongDirectory/NestedDirectory/CommitViewerWindow.cs",
            FontSize = fontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 8, 0)
        };
        var summary = new TextBlock { FontSize = fontSize };
        if (binary)
            summary.Text = "binary";
        else
        {
            summary.Inlines.Add(new Run("+12345") { FontWeight = FontWeights.SemiBold });
            summary.Inlines.Add(new Run("  -67890") { FontWeight = FontWeights.SemiBold });
        }

        var row = new Grid { Margin = new Thickness(3, 5, 3, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(summary, 1);
        row.Children.Add(path);
        row.Children.Add(summary);
        var item = new ListBoxItem { Content = row, Padding = new Thickness(4), BorderThickness = new Thickness(1) };
        item.Measure(new Size(200, double.PositiveInfinity));

        var width = CommitViewerLayout.MeasureFileListItemWidth(item);
        var dpi = VisualTreeHelper.GetDpi(path).PixelsPerDip;
        var pathWidth = new FormattedText(path.Text, CultureInfo.CurrentCulture, path.FlowDirection,
            new Typeface(path.FontFamily, path.FontStyle, path.FontWeight, path.FontStretch),
            fontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
        var summaryWidth = new FormattedText(binary ? "binary" : "+12345  -67890",
            CultureInfo.CurrentCulture, summary.FlowDirection,
            new Typeface(summary.FontFamily, summary.FontStyle,
                binary ? summary.FontWeight : FontWeights.SemiBold, summary.FontStretch),
            fontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;

        Assert.Multiple(() =>
        {
            Assert.That(width, Is.GreaterThanOrEqualTo(pathWidth + summaryWidth + 26 - 1));
            Assert.That(width, Is.LessThan(pathWidth + summaryWidth + 30));
            Assert.That(path.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(item.IsMeasureValid, Is.False, "Normal constrained layout must run again.");
        });
    }

    [Test]
    public void UncertainCommitToolTip_IsConciseAndDoesNotRepeatCommitDetails()
    {
        var text = CommitViewerLayout.BuildUncertainCommitToolTip(
            "The commit predates the captured task baseline.");

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(
                "SquadDash included this commit as evidence, but could not confirm that it belongs to this step."));
            Assert.That(text, Does.Not.Contain("Why it is uncertain"));
            Assert.That(text, Does.Not.Contain("predates"));
        });
    }

    [TestCase(PlanRecoveryCommitRelation.Unknown, "Unclear.", true)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates the baseline.", false)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates baseline.", false)]
    [TestCase(PlanRecoveryCommitRelation.Task, "Confirmed inside the captured range.", false)]
    [TestCase(PlanRecoveryCommitRelation.Unrelated, "Confirmed recovery infrastructure.", true)]
    public void AttributionUncertainty_MarksCommitsNotConfirmedAsPartOfTheStep(
        string relation,
        string explanation,
        bool expected)
    {
        Assert.That(
            CommitViewerLayout.IsCommitAttributionUncertain(relation, explanation),
            Is.EqualTo(expected));
    }

    [Test]
    public void TaskCommitBeforeRecoveryBaseline_UsesPlainLanguageHint()
    {
        var hint = CommitViewerLayout.BuildCommitToolTip(
            false,
            "This is the primary implementation commit; it predates the baseline.");

        Assert.That(hint, Is.EqualTo(
            "This commit appears to implement this step, but an interruption prevented SquadDash " +
            "from automatically confirming the step as complete."));
    }
}
