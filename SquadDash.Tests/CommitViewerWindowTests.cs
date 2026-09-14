using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CommitViewerWindowTests
{
    [Test]
    public void FileListAutoFit_UsesPreviewLeftButtonDownBeforeThumbHandlesTheClick()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "CommitViewerWindow.cs")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "Could not find CommitViewerWindow.cs.");
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "CommitViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("splitter.PreviewMouseLeftButtonDown += (_, e) =>"));
            Assert.That(source, Does.Contain("if (e.ClickCount != 2 || _fileList.Items.Count == 0) return;"));
            Assert.That(source, Does.Not.Contain("splitter.PreviewMouseDoubleClick +="));
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
