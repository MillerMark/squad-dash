using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class TranscriptTitleSpinnerTests
{
    private readonly List<ActivitySpinner> _spinners = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var spinner in _spinners)
            spinner.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        _spinners.Clear();
    }

    [TestCase(SpinnerActivityKind.Thinking)]
    [TestCase(SpinnerActivityKind.Reading)]
    [TestCase(SpinnerActivityKind.Writing)]
    public void SelectingActiveCard_InheritsCurrentActivityWithoutAnotherPulse(SpinnerActivityKind kind)
    {
        var card = Card();
        var source = Spinner(card);
        card.FireActivityPulse(kind);
        Tick(source, 12);

        var title = Spinner(card, 18);

        Assert.Multiple(() =>
        {
            Assert.That(title.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(State<SpinnerActivityKind>(title, "_currentKind"), Is.EqualTo(kind));
            Assert.That(State<double>(title, "_angularVelocity"),
                Is.EqualTo(State<double>(source, "_angularVelocity")));
            Assert.That(State<double>(title, "_spinnerOpacity"),
                Is.EqualTo(State<double>(source, "_spinnerOpacity")));
            Assert.That(State<double>(title, "_writeDotRadius"),
                Is.EqualTo(State<double>(source, "_writeDotRadius") * 1.5).Within(0.0001));
        });
        card.FireActivityPulse(SpinnerActivityKind.Writing);
        Assert.That(State<SpinnerActivityKind>(title, "_currentKind"), Is.EqualTo(SpinnerActivityKind.Writing));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectingDifferentCardOrNoCard_DropsPreviousActivityAndSubscriptions(bool clearCard)
    {
        var previous = Card();
        var next = Card();
        var title = Spinner(previous);
        previous.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(title, 12);

        title.DataContext = clearCard ? null : next;
        previous.FireActivityPulse(SpinnerActivityKind.Writing);
        previous.NotifyTurnEnded();

        Assert.Multiple(() =>
        {
            Assert.That(title.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(State<double>(title, "_animatedWidth"), Is.Zero);
            Assert.That(State<double>(title, "_writeDotRadius"), Is.Zero);
            Assert.That(State<double>(title, "_velocityTarget"), Is.Zero);
        });
        if (!clearCard)
        {
            next.FireActivityPulse(SpinnerActivityKind.Reading);
            Assert.That(title.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(State<SpinnerActivityKind>(title, "_currentKind"), Is.EqualTo(SpinnerActivityKind.Reading));
        }
    }

    [TestCase(false, 3.0)]
    [TestCase(true, 0.2)]
    public void SelectingFadingCard_PreservesFadeProgressAndDoesNotRestartCompletedTurn(bool paused, double duration)
    {
        var card = Card();
        var source = Spinner(card);
        card.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(source, 12);
        if (paused)
            card.NotifyActivityPaused();
        else
            card.NotifyTurnEnded();
        Tick(source, 1);

        var title = Spinner(card);
        Assert.Multiple(() =>
        {
            Assert.That(State<bool>(title, "_forcedFadeOutActive"), Is.True);
            Assert.That(State<double>(title, "_forcedFadeOutDuration"), Is.EqualTo(duration));
            Assert.That(State<double>(title, "_forcedFadeOutElapsed"),
                Is.EqualTo(State<double>(source, "_forcedFadeOutElapsed")));
        });
        Tick(source, 200);
        Tick(title, 200);
        var completedTitle = Spinner(card);
        Assert.Multiple(() =>
        {
            Assert.That(source.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(title.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(completedTitle.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void IdleCoasting_UsesExistingFrictionAndCollapsesWithoutTurnEnd()
    {
        var card = Card();
        var source = Spinner(card);
        card.FireActivityPulse(SpinnerActivityKind.Thinking);
        Tick(source, 40);
        var title = Spinner(card);
        Assert.That(State<double>(title, "_velocityAccelPhase"), Is.Zero);
        Tick(title, 1600);
        Assert.That(title.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void Reload_ReattachesToCurrentCardAndTheme()
    {
        var card = Card();
        var source = Spinner(card);
        var title = Spinner(card);
        title.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        card.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(source, 12);
        title.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        var originalTheme = AgentStatusCard.IsDarkTheme;
        try
        {
            AgentStatusCard.SetTheme(!originalTheme);
            Assert.That(title.AccentColor, Is.EqualTo(((SolidColorBrush)card.EffectiveSpinnerAccentBrush).Color));
            card.AccentColorHex = "#8030C0";
            Assert.That(title.AccentColor, Is.EqualTo(((SolidColorBrush)card.EffectiveSpinnerAccentBrush).Color));
            card.FireActivityPulse(SpinnerActivityKind.Reading);
            Assert.That(State<SpinnerActivityKind>(title, "_currentKind"), Is.EqualTo(SpinnerActivityKind.Reading));
            card.NotifyActivityPaused();
            Assert.That(State<double>(title, "_forcedFadeOutDuration"), Is.EqualTo(0.2));
        }
        finally
        {
            AgentStatusCard.SetTheme(originalTheme);
        }
    }

    [Test]
    public void StandaloneSpinner_StillSupportsManualAndContinuousActivity()
    {
        var spinner = Spinner();
        spinner.Pulse(SpinnerActivityKind.Writing);
        Tick(spinner, 12);
        spinner.DataContext = new object();
        Assert.That(State<SpinnerActivityKind>(spinner, "_currentKind"), Is.EqualTo(SpinnerActivityKind.Writing));
        spinner.SetContinuousActive(true);
        Tick(spinner, 1600);
        Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Visible));
        spinner.SetContinuousActive(false);
        Tick(spinner, 1600);
        Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void SettledSpinner_RendersWithoutInvalidatingLayout()
    {
        var spinner = Spinner();
        spinner.SetContinuousActive(true);
        Tick(spinner, 200);
        var size = new Size(100, 40);
        var invalidMeasureFrames = 0;
        var invalidArrangeFrames = 0;
        for (var i = 0; i < 120; i++)
        {
            spinner.Measure(size);
            spinner.Arrange(new Rect(size));
            Tick(spinner, 1);
            if (!spinner.IsMeasureValid) invalidMeasureFrames++;
            if (!spinner.IsArrangeValid) invalidArrangeFrames++;
        }

        TestContext.Out.WriteLine($"Settled spinner / 120 ticks: measureInvalid={invalidMeasureFrames}, arrangeInvalid={invalidArrangeFrames}");
        Assert.Multiple(() =>
        {
            Assert.That(invalidMeasureFrames, Is.Zero);
            Assert.That(invalidArrangeFrames, Is.Zero);
        });
    }

    [Test]
    public void ResizingWithMultipleSpinners_DoesNotQueueExtraLayoutForFrameDrawing()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 12; i++)
        {
            var spinner = Spinner();
            var title = new TextBlock { Text = $"Agent {i}", FontSize = 16, FontWeight = FontWeights.SemiBold };
            TranscriptTitleSpinnerLayout.SetTitle(spinner, title);
            spinner.SetContinuousActive(true);
            Tick(spinner, 200);
            panel.Children.Add(spinner);
        }
        var extraLayoutRequests = 0;
        for (var frame = 0; frame < 120; frame++)
        {
            var size = new Size(500 + frame, 40);
            panel.Measure(size);
            panel.Arrange(new Rect(size));
            foreach (var spinner in _spinners)
            {
                Tick(spinner, 1);
                if (!spinner.IsMeasureValid || !spinner.IsArrangeValid)
                    extraLayoutRequests++;
            }
        }
        TestContext.Out.WriteLine($"12 spinners / 120 resize frames: extraLayoutRequests={extraLayoutRequests}");
        Assert.That(extraLayoutRequests, Is.Zero);
    }

    [Test]
    public void RenderOnlyFrames_UpdateChildDrawingAndClearItAfterFade()
    {
        var spinner = Spinner();
        spinner.Pulse(SpinnerActivityKind.Writing);
        Tick(spinner, 12);
        Assert.That(VisualTreeHelper.GetChildrenCount(spinner), Is.EqualTo(1));
        var visual = (DrawingVisual)VisualTreeHelper.GetChild(spinner, 0);
        var before = visual.Drawing;
        Tick(spinner, 1);
        Assert.Multiple(() =>
        {
            Assert.That(visual.Drawing, Is.Not.SameAs(before));
            Assert.That(visual.ContentBounds.IsEmpty, Is.False);
            Assert.That(State<double>(spinner, "_writeDotRadius"), Is.GreaterThan(0.5));
        });
        spinner.BeginForcedFadeOut();
        Tick(spinner, 200);
        Assert.Multiple(() =>
        {
            Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(visual.ContentBounds.IsEmpty, Is.True);
        });
    }

    [Test]
    public void FontSizeChange_StillRemeasuresAndSettlesToExactExpandedWidth()
    {
        var spinner = Spinner();
        spinner.SetContinuousActive(true);
        Tick(spinner, 200);
        spinner.Measure(new Size(100, 40));
        spinner.FontSize = 20;
        Assert.That(spinner.IsMeasureValid, Is.False);
        Tick(spinner, 200);
        spinner.Measure(new Size(100, 40));
        Assert.That(spinner.DesiredSize.Width,
            Is.EqualTo(ActivitySpinner.GetExpandedLayoutWidth(20)).Within(0.0001));
    }

    [Test]
    public void DockedTitle_FollowsAnimatedWidthAndReturnsToLeftWithoutResidualGap()
    {
        var card = Card();
        var spinner = Spinner(card);
        var title = new TextBlock { Text = "Coordinator", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left };
        TranscriptTitleSpinnerLayout.SetTitle(spinner, title);
        var panel = new DockPanel { LastChildFill = false };
        var navigation = new Border { Width = 56, Height = 22 };
        DockPanel.SetDock(navigation, Dock.Right);
        panel.Children.Add(navigation);
        panel.Children.Add(spinner);
        panel.Children.Add(title);
        card.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(spinner, 80);
        var initialX = LayoutTitleX(panel, title);
        Assert.That(initialX, Is.EqualTo(TranscriptTitleSpinnerLayout.GetExpandedWidth(title, 16)).Within(0.001));
        title.FontSize = 20;
        Assert.That(spinner.FontSize, Is.EqualTo(12));
        Tick(spinner, 40);
        var previousX = LayoutTitleX(panel, title);
        card.NotifyTurnEnded();
        var intermediatePositions = new HashSet<double>();
        for (var i = 0; i < 200; i++)
        {
            Tick(spinner, 1);
            var x = LayoutTitleX(panel, title);
            Assert.That(x, Is.LessThanOrEqualTo(previousX + 0.01));
            if (x > 0.5 && x < initialX)
                intermediatePositions.Add(Math.Round(x, 2));
            previousX = x;
        }
        Assert.Multiple(() =>
        {
            Assert.That(intermediatePositions.Count, Is.GreaterThan(10));
            Assert.That(previousX, Is.Zero);
            Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void MainTitle_ReusesAutoWidthSpinnerBeforeLeftAlignedTitleWithDisplayedCardBindings()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Parse(ReadSource("MainWindow.xaml"));
        var panel = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "TranscriptTitleDockPanel");
        var children = panel.Elements().ToList();
        var spinner = children.Single(element => (string?)element.Attribute(x + "Name") == "TranscriptTitleSpinner");
        var title = children.Single(element => (string?)element.Attribute(x + "Name") == "TranscriptTitleTextBlock");
        Assert.Multiple(() =>
        {
            Assert.That(spinner.Name.LocalName, Is.EqualTo(nameof(ActivitySpinner)));
            Assert.That(children.IndexOf(spinner) + 1, Is.EqualTo(children.IndexOf(title)));
            Assert.That((string?)spinner.Attribute("DockPanel.Dock"), Is.EqualTo("Left"));
            Assert.That((string?)spinner.Attribute(XName.Get("TranscriptTitleSpinnerLayout.Title", "clr-namespace:SquadDash")),
                Is.EqualTo("{Binding ElementName=TranscriptTitleTextBlock}"));
            Assert.That((string?)spinner.Attribute("VerticalAlignment"), Is.EqualTo("Center"));
            Assert.That((string?)spinner.Attribute("IsVisibleChanged"), Is.EqualTo("TranscriptTitleSpinner_IsVisibleChanged"));
            Assert.That(spinner.Attribute("Width"), Is.Null);
            Assert.That(spinner.Attribute("MinWidth"), Is.Null);
            Assert.That(spinner.Attribute("Margin"), Is.Null);
            Assert.That((string?)title.Attribute("HorizontalAlignment"), Is.EqualTo("Left"));
            Assert.That((string?)panel.Attribute("LastChildFill"), Is.EqualTo("False"));
        });
        var source = ReadSource("MainWindow.xaml.cs");
        Assert.That(source, Does.Contain("TranscriptTitleSpinner.DataContext = FindAgentCardForThread(thread);")
            .And.Contain("TranscriptTitleSpinner.DataContext = _leadAgent;"));
        Assert.That(ReadSource("MainWindow.ResizePerformance.cs"),
            Does.Contain("TranscriptTitleSpinnerLayout.GetExpandedWidth(TranscriptTitleTextBlock, fontSize)"));
    }

    [Test]
    public void SecondaryTitles_ConstructOwnSpinnerAndRebindWhenPanelAgentChanges()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        Assert.That(source, Does.Contain("var titleSpinner = new ActivitySpinner { VerticalAlignment = VerticalAlignment.Center };")
            .And.Contain("TranscriptTitleSpinnerLayout.SetTitle(titleSpinner, titleBlock);")
            .And.Contain("titleSpinner.DataContext = agent;")
            .And.Contain("titleRow.Children.Add(titleSpinner);")
            .And.Contain("titleRow.Children.Add(titleBlock);")
            .And.Contain("TitleSpinner = titleSpinner,")
            .And.Contain("entry.TitleSpinner.DataContext = currentCard;")
            .And.Contain("titleSpinner.IsVisibleChanged += (_, _) => QueueSecondaryTranscriptTitleFit(entry);")
            .And.Contain("if (e.WidthChanged) QueueSecondaryTranscriptTitleFit(entry);")
            .And.Contain("TranscriptTitleSpinnerLayout.GetExpandedWidth(entry.TitleBlock, fontSize)"));
        Assert.That(source.IndexOf("titleRow.Children.Add(titleSpinner);", StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("titleRow.Children.Add(titleBlock);", StringComparison.Ordinal)));
        Assert.That(ReadSource("MainWindow.ResizePerformance.cs"),
            Does.Contain("new List<SecondaryTranscriptEntry>(_pendingSecondaryTitleFits)")
                .And.Contain("ApplyCachedSecondaryTranscriptTitleFit(entry);")
                .And.Contain("_pendingSecondaryTitleFits.Clear();"));
    }

    [TestCase("Segoe UI", 16)]
    [TestCase("Consolas", 22)]
    public void TitleSizing_UsesSixtyPercentAndActualSpaceAdvanceAndTracksTypography(string family, double fontSize)
    {
        var title = new TextBlock
        {
            Text = "Agent",
            FontFamily = new FontFamily(family),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var spinner = Spinner();
        spinner.VerticalAlignment = VerticalAlignment.Center;
        TranscriptTitleSpinnerLayout.SetTitle(spinner, title);
        spinner.SetContinuousActive(true);
        Tick(spinner, 200);
        var panel = new DockPanel { LastChildFill = false };
        panel.Children.Add(spinner);
        panel.Children.Add(title);

        void AssertMetrics()
        {
            var space = new FormattedText(" ", title.Language.GetSpecificCulture(), title.FlowDirection,
                new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch),
                title.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(title).PixelsPerDip).WidthIncludingTrailingWhitespace;
            var x = LayoutTitleX(panel, title);
            Assert.Multiple(() =>
            {
                Assert.That(spinner.FontSize, Is.EqualTo(title.FontSize * 0.6).Within(0.0001));
                Assert.That(spinner.Margin, Is.EqualTo(new Thickness(0, 0, space, 0)));
                Assert.That(space, Is.GreaterThan(0));
                Assert.That(x, Is.EqualTo(ActivitySpinner.GetExpandedLayoutWidth(title.FontSize * 0.6) + space).Within(0.001));
                Assert.That(spinner.TranslatePoint(new Point(0, spinner.ActualHeight / 2), panel).Y,
                    Is.EqualTo(title.TranslatePoint(new Point(0, title.ActualHeight / 2), panel).Y).Within(0.001));
            });
        }

        AssertMetrics();
        title.FontSize += 5;
        title.FontFamily = new FontFamily("Times New Roman");
        title.FontWeight = FontWeights.Bold;
        title.FontStyle = FontStyles.Italic;
        Tick(spinner, 200);
        AssertMetrics();
        Assert.That(Spinner().FontSize, Is.EqualTo(12), "Unconfigured agent-card/plan spinners keep their default size.");
    }

    [Test]
    public void MultiplePanelTitles_KeepTheirOwnActivityWritingDotAndCompletionFade()
    {
        var cards = new[] { Card(), Card(), Card() };
        var titles = cards.Select(card =>
        {
            var spinner = Spinner();
            TranscriptTitleSpinnerLayout.SetTitle(spinner, new TextBlock { FontSize = 16 });
            spinner.DataContext = card;
            return spinner;
        }).ToArray();
        var kinds = new[] { SpinnerActivityKind.Thinking, SpinnerActivityKind.Reading, SpinnerActivityKind.Writing };
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].FireActivityPulse(kinds[i]);
            Tick(titles[i], 12);
        }
        Assert.Multiple(() =>
        {
            for (var i = 0; i < titles.Length; i++)
            {
                Assert.That(titles[i].Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(State<SpinnerActivityKind>(titles[i], "_currentKind"), Is.EqualTo(kinds[i]));
                Assert.That(State<double>(titles[i], "_writeDotRadius"), i == 2 ? Is.GreaterThan(0.5) : Is.Zero);
            }
        });
        cards[2].NotifyTurnEnded();
        cards[1].NotifyActivityPaused();
        Assert.Multiple(() =>
        {
            Assert.That(State<bool>(titles[0], "_forcedFadeOutActive"), Is.False);
            Assert.That(State<double>(titles[1], "_forcedFadeOutDuration"), Is.EqualTo(0.2));
            Assert.That(State<double>(titles[2], "_forcedFadeOutDuration"), Is.EqualTo(3));
        });
        Tick(titles[1], 200);
        Tick(titles[2], 200);
        Assert.That(titles[1].Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(titles[2].Visibility, Is.EqualTo(Visibility.Collapsed));
        Tick(titles[0], 1600);
        Assert.That(titles[0].Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    private ActivitySpinner Spinner(AgentStatusCard? card = null, double fontSize = 12)
    {
        var spinner = new ActivitySpinner { FontSize = fontSize, DataContext = card };
        _spinners.Add(spinner);
        return spinner;
    }

    private static AgentStatusCard Card() => new("Agent", "A", "Developer", "Ready", "", "", "#4682B4", "agent");

    private static T State<T>(ActivitySpinner spinner, string name) =>
        (T)typeof(ActivitySpinner).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(spinner)!;

    private static void Tick(ActivitySpinner spinner, int frames)
    {
        var lastTick = typeof(ActivitySpinner).GetField("_lastTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tick = typeof(ActivitySpinner).GetMethod("OnPhysicsTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < frames; i++)
        {
            lastTick.SetValue(spinner, DateTime.UtcNow.AddSeconds(-0.05));
            tick.Invoke(spinner, [null, EventArgs.Empty]);
        }
    }

    private static double LayoutTitleX(DockPanel panel, TextBlock title)
    {
        panel.Measure(new Size(500, 40));
        panel.Arrange(new Rect(0, 0, 500, 40));
        panel.UpdateLayout();
        return title.TranslatePoint(new Point(), panel).X;
    }

    private static string ReadSource(string fileName)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SquadDash", fileName);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        throw new FileNotFoundException($"Could not locate {fileName}.");
    }
}
