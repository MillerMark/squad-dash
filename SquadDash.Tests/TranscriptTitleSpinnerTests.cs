using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

    [Test]
    public void SelectingPausedCard_PreservesFadeProgress()
    {
        var card = Card();
        var source = Spinner(card);
        card.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(source, 12);
        card.NotifyActivityPaused();
        Tick(source, 1);

        var title = Spinner(card);
        Assert.Multiple(() =>
        {
            Assert.That(State<bool>(title, "_forcedFadeOutActive"), Is.True);
            Assert.That(State<double>(title, "_forcedFadeOutDuration"), Is.EqualTo(0.2));
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
    public void TurnEnded_FadesSpinnerAndTitleSpacingOverFourTenthsOfASecond()
    {
        var card = Card();
        var spinner = Spinner(card);
        card.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(spinner, 12);

        card.NotifyTurnEnded();

        Assert.Multiple(() =>
        {
            Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(State<bool>(spinner, "_forcedFadeOutActive"), Is.True);
            Assert.That(State<double>(spinner, "_forcedFadeOutDuration"), Is.EqualTo(0.4));
        });
        Tick(spinner, 4);
        Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(State<double>(spinner, "_animatedWidth"), Is.LessThan(ActivitySpinner.GetExpandedLayoutWidth(spinner.FontSize)));

        Tick(spinner, 5);
        Assert.Multiple(() =>
        {
            Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(State<DispatcherTimer>(spinner, "_physicsTimer").IsEnabled, Is.False);
            Assert.That(State<double>(spinner, "_animatedWidth"), Is.Zero);
            Assert.That(State<double>(spinner, "_angularVelocity"), Is.Zero);
            Assert.That(State<double>(spinner, "_writeDotRadius"), Is.Zero);
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
    public void DockedTitle_SlidesLeftDuringTurnEndedFade()
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
        Assert.That(spinner.FontSize, Is.EqualTo(16));
        Tick(spinner, 40);
        var previousX = LayoutTitleX(panel, title);
        card.NotifyTurnEnded();
        var intermediatePositions = new List<double>();
        for (var i = 0; i < 4; i++)
        {
            Tick(spinner, 1);
            var x = LayoutTitleX(panel, title);
            Assert.That(x, Is.LessThan(previousX));
            intermediatePositions.Add(x);
            previousX = x;
        }
        Tick(spinner, 5);
        var finalX = LayoutTitleX(panel, title);
        Assert.Multiple(() =>
        {
            Assert.That(intermediatePositions, Has.All.GreaterThan(0));
            Assert.That(finalX, Is.Zero);
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
        Assert.That(source, Does.Contain("TranscriptTitleSpinner.DataContext = thread.SpinnerActivity;")
            .And.Contain("TranscriptTitleSpinner.DataContext = _leadAgent;"));
        Assert.That(ReadSource("MainWindow.ResizePerformance.cs"),
            Does.Contain("TranscriptTitleSpinnerLayout.GetExpandedWidth(TranscriptTitleTextBlock, fontSize)"));
    }

    [Test]
    public void SecondaryTitles_ConstructOwnSpinnerAndRebindWhenPanelAgentChanges()
    {
        var source = ReadSource("MainWindow.xaml.cs");
        Assert.That(source, Does.Contain("TranscriptTitleSpinnerLayout.CreateSecondaryTitleRow(titleBlock, thread.SpinnerActivity, out var titleSpinner)")
            .And.Contain("headerTextStack.Children.Add(titleRow);")
            .And.Contain("TitleSpinner = titleSpinner,")
            .And.Contain("entry.Thread.SpinnerActivity.AccentSource = currentCard;")
            .And.Contain("titleSpinner.IsVisibleChanged += (_, _) => QueueSecondaryTranscriptTitleFit(entry);")
            .And.Contain("if (e.WidthChanged) QueueSecondaryTranscriptTitleFit(entry);")
            .And.Contain("TranscriptTitleSpinnerLayout.GetExpandedWidth(entry.TitleBlock, fontSize)"));
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
                Assert.That(spinner.FontSize, Is.EqualTo(title.FontSize * 0.8).Within(0.0001));
                Assert.That(spinner.Margin, Is.EqualTo(new Thickness(0, 0, space, 0)));
                Assert.That(space, Is.GreaterThan(0));
                Assert.That(x, Is.EqualTo(ActivitySpinner.GetExpandedLayoutWidth(title.FontSize * 0.8) + space).Within(0.001));
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
            Assert.That(State<double>(titles[2], "_forcedFadeOutDuration"), Is.EqualTo(0.4));
            Assert.That(titles[2].Visibility, Is.EqualTo(Visibility.Visible));
        });
        Tick(titles[1], 200);
        Tick(titles[2], 200);
        Assert.That(titles[1].Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(titles[2].Visibility, Is.EqualTo(Visibility.Collapsed));
        Tick(titles[0], 1600);
        Assert.That(titles[0].Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void SameRosterAgent_DistinctTranscriptSources_DoNotShareSpinnerActivity()
    {
        var sharedAgent = Card();
        var completedTranscript = new TranscriptSpinnerActivitySource { AccentSource = sharedAgent };
        var activeTranscript = new TranscriptSpinnerActivitySource { AccentSource = sharedAgent };
        var completedSpinner = Spinner();
        var activeSpinner = Spinner();
        completedSpinner.DataContext = completedTranscript;
        activeSpinner.DataContext = activeTranscript;

        completedTranscript.FireActivityPulse(SpinnerActivityKind.Thinking);
        activeTranscript.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(completedSpinner, 12);
        Tick(activeSpinner, 12);
        completedTranscript.NotifyTurnEnded();
        Tick(completedSpinner, 9);

        activeTranscript.FireActivityPulse(SpinnerActivityKind.Writing);
        Tick(activeSpinner, 2);

        Assert.Multiple(() =>
        {
            Assert.That(completedSpinner.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(State<DispatcherTimer>(completedSpinner, "_physicsTimer").IsEnabled, Is.False);
            Assert.That(activeSpinner.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(State<SpinnerActivityKind>(activeSpinner, "_currentKind"), Is.EqualTo(SpinnerActivityKind.Writing));
        });
    }

    [Test]
    public void OpeningActiveTranscriptAfterEarlierPulse_RestoresOnlyThatTranscriptActivity()
    {
        var sharedAgent = Card();
        var activeTranscript = new TranscriptSpinnerActivitySource { AccentSource = sharedAgent };
        var completedTranscript = new TranscriptSpinnerActivitySource { AccentSource = sharedAgent };
        activeTranscript.FireActivityPulse(SpinnerActivityKind.Reading);
        completedTranscript.FireActivityPulse(SpinnerActivityKind.Thinking);
        completedTranscript.NotifyTurnEnded();

        var activeSpinner = Spinner();
        var completedSpinner = Spinner();
        activeSpinner.DataContext = activeTranscript;
        completedSpinner.DataContext = completedTranscript;

        Assert.Multiple(() =>
        {
            Assert.That(activeSpinner.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(State<SpinnerActivityKind>(activeSpinner, "_currentKind"), Is.EqualTo(SpinnerActivityKind.Reading));
            Assert.That(completedSpinner.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(SpinnerActivityKind.Thinking)]
    [TestCase(SpinnerActivityKind.Reading)]
    [TestCase(SpinnerActivityKind.Writing)]
    public void FirstPulse_AtRealFrameCadence_DoesNotFadeDuringAcceleration(SpinnerActivityKind kind)
    {
        var card = Card();
        var title = new TextBlock { Text = "Secondary", FontSize = 16 };
        var row = TranscriptTitleSpinnerLayout.CreateSecondaryTitleRow(title, card, out var spinner);
        _spinners.Add(spinner);
        card.FireActivityPulse(kind);
        Tick(spinner, 1, 0.001);
        Assert.That(State<DispatcherTimer>(spinner, "_physicsTimer").IsEnabled, Is.True);
        Tick(spinner, 60, 1.0 / 60);
        row.Measure(new Size(300, 40));
        row.Arrange(new Rect(0, 0, 300, 40));
        Assert.Multiple(() =>
        {
            Assert.That(spinner.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(State<double>(spinner, "_currentScale"), Is.GreaterThan(0.9));
            Assert.That(State<double>(spinner, "_spinnerOpacity"), Is.GreaterThan(0.7));
            Assert.That(((DrawingVisual)VisualTreeHelper.GetChild(spinner, 0)).ContentBounds.IsEmpty, Is.False);
        });
    }

    [TestCase(96)]
    [TestCase(144)]
    [TestCase(192)]
    public void LoadedProductionTitles_RenderIndependentActivitySpaceAndTitleOnlyOpacity(double rasterDpi)
    {
        var cards = new[] { Card(), Card(), Card() };
        var rows = new DockPanel[3];
        var titles = new TextBlock[3];
        var spinners = new ActivitySpinner[3];
        rows[0] = LoadPrimaryTitleRow();
        spinners[0] = (ActivitySpinner)rows[0].FindName("TranscriptTitleSpinner");
        titles[0] = (TextBlock)rows[0].FindName("TranscriptTitleTextBlock");
        spinners[0].DataContext = cards[0];
        _spinners.Add(spinners[0]);
        titles[0].Text = "Coordinator";
        for (var i = 1; i < rows.Length; i++)
        {
            titles[i] = new TextBlock
            {
                Text = $"Secondary agent {i}", FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Like the real secondary panel, this resource cannot resolve until parented.
            titles[i].SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSubtitle");
            rows[i] = TranscriptTitleSpinnerLayout.CreateSecondaryTitleRow(titles[i], cards[i], out spinners[i]);
            _spinners.Add(spinners[i]);
        }
        var content = new StackPanel();
        foreach (var row in rows)
        {
            row.Height = 40;
            content.Children.Add(row);
        }
        var window = new Window
        {
            Content = content, Width = 560, Height = 200,
            ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000
        };
        window.Resources["FontSizeSubtitle"] = 16.0;
        window.Resources["SubtleText"] = Brushes.Black;
        try
        {
            window.Show();
            DrainDispatcher();
            var kinds = new[] { SpinnerActivityKind.Thinking, SpinnerActivityKind.Reading, SpinnerActivityKind.Writing };
            for (var i = 0; i < cards.Length; i++)
                cards[i].FireActivityPulse(kinds[i]);

            // Let the actual Loaded subscriptions and dispatcher timers produce the first frames.
            PumpUntil(() => spinners.All(spinner => State<double>(spinner, "_currentScale") > 0.1
                && State<double>(spinner, "_spinnerOpacity") > 0.1));
            for (var i = 0; i < spinners.Length; i++)
            {
                Assert.That(State<ISpinnerActivitySource>(spinners[i], "_subscribedSource"), Is.SameAs(cards[i]));
                Assert.That(State<DispatcherTimer>(spinners[i], "_physicsTimer").IsEnabled, Is.True);
                Assert.That(State<SpinnerActivityKind>(spinners[i], "_currentKind"), Is.EqualTo(kinds[i]));
                Tick(spinners[i], 80);
            }
            window.UpdateLayout();
            SaveRenderedTitles(content, rasterDpi);

            void AssertRenderedRows()
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    var spinner = spinners[i];
                    var title = titles[i];
                    var space = new FormattedText(" ", title.Language.GetSpecificCulture(), title.FlowDirection,
                        new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch),
                        title.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(title).PixelsPerDip).WidthIncludingTrailingWhitespace;
                    var right = spinner.TranslatePoint(new Point(spinner.ActualWidth, 0), rows[i]).X;
                    var titleX = title.TranslatePoint(new Point(), rows[i]).X;
                    var rowY = rows[i].TranslatePoint(new Point(), content).Y;
                    var bitmap = new CroppedBitmap(Render(content, rasterDpi), new Int32Rect(
                        0, (int)Math.Round(rowY * rasterDpi / 96),
                        (int)Math.Ceiling(rows[i].ActualWidth * rasterDpi / 96),
                        (int)Math.Ceiling(rows[i].ActualHeight * rasterDpi / 96)));
                    var pixels = Pixels(bitmap);
                    var scale = rasterDpi / 96;
                    var peakAlpha = 0;
                    var redPixels = 0;
                    for (var y = 0; y < bitmap.PixelHeight; y++)
                    for (var x = 0; x < (int)Math.Floor(right * scale); x++)
                    {
                        var offset = (y * bitmap.PixelWidth + x) * 4;
                        peakAlpha = Math.Max(peakAlpha, pixels[offset + 3]);
                        if (pixels[offset + 2] > pixels[offset + 1] + 40
                            && pixels[offset + 2] > pixels[offset] + 40 && pixels[offset + 3] > 50)
                            redPixels++;
                    }
                    Assert.Multiple(() =>
                    {
                        Assert.That(spinner.IsVisible, Is.True);
                        Assert.That(spinner.ActualHeight, Is.GreaterThan(4));
                        Assert.That(space, Is.GreaterThan(2), "A real title-font space must not collapse to zero.");
                        Assert.That(titleX - right, Is.EqualTo(space).Within(0.001));
                        Assert.That(spinner.Opacity, Is.EqualTo(0.85));
                        Assert.That(peakAlpha, Is.InRange(80, 218), "The actual spinner pixels must render at title-only opacity.");
                        if (i == 2)
                            Assert.That(peakAlpha, Is.InRange(214, 218), "The writing-dot border must peak at 85% alpha.");
                        Assert.That(redPixels, i == 2 ? Is.GreaterThan(0) : Is.Zero);
                    });
                    for (var y = 0; y < bitmap.PixelHeight; y++)
                    for (var x = (int)Math.Ceiling(right * scale); x < (int)Math.Floor(titleX * scale); x++)
                        Assert.That(pixels[(y * bitmap.PixelWidth + x) * 4 + 3], Is.Zero, "The measured space must be empty in the raster.");
                    TestContext.Out.WriteLine($"Title {i}: font={title.FontSize}, space={space:0.###}, renderedGap={titleX - right:0.###}, peakAlpha={peakAlpha}, redPixels={redPixels}");
                }
            }

            AssertRenderedRows();
            window.Resources["FontSizeSubtitle"] = 22.0;
            titles[0].FontSize = 22;
            DrainDispatcher();
            foreach (var spinner in spinners) Tick(spinner, 80);
            window.UpdateLayout();
            Assert.That(titles[1].FontSize, Is.EqualTo(22), "Late resource changes must reach the measured margin.");
            AssertRenderedRows();

            var nonTitle = Spinner();
            nonTitle.Pulse(SpinnerActivityKind.Writing);
            Tick(nonTitle, 80);
            var nonTitleHost = new Border { Width = 40, Height = 40, Child = nonTitle };
            nonTitleHost.Measure(new Size(40, 40));
            nonTitleHost.Arrange(new Rect(0, 0, 40, 40));
            var nonTitlePixels = Pixels(Render(nonTitleHost, rasterDpi));
            Assert.That(nonTitle.Opacity, Is.EqualTo(1), "Agent-card and plan spinners must remain unchanged.");
            Assert.That(nonTitlePixels.Where((_, index) => index % 4 == 3).Max(), Is.EqualTo(255));

            var replacement = Card();
            spinners[1].DataContext = replacement; // The production secondary-entry remap.
            cards[1].FireActivityPulse(SpinnerActivityKind.Writing);
            Assert.That(spinners[1].Visibility, Is.EqualTo(Visibility.Collapsed));
            replacement.FireActivityPulse(SpinnerActivityKind.Reading);
            Tick(spinners[1], 80);
            Assert.That(State<ISpinnerActivitySource>(spinners[1], "_subscribedSource"), Is.SameAs(replacement));
            Assert.That(spinners[1].Visibility, Is.EqualTo(Visibility.Visible));

            replacement.NotifyActivityPaused();
            cards[2].NotifyTurnEnded();
            Tick(spinners[1], 200);
            Tick(spinners[2], 200);
            Assert.That(spinners[0].Visibility, Is.EqualTo(Visibility.Visible), "Secondary completion must not end coordinator activity.");
            Tick(spinners[0], 1600);
            window.UpdateLayout();
            for (var i = 0; i < spinners.Length; i++)
            {
                Assert.That(spinners[i].Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(State<double>(spinners[i], "_spinnerOpacity"), Is.Zero);
                Assert.That(((DrawingVisual)VisualTreeHelper.GetChild(spinners[i], 0)).ContentBounds.IsEmpty, Is.True);
                Assert.That(titles[i].TranslatePoint(new Point(), rows[i]).X, Is.Zero, "No residual gap after fading.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static DockPanel LoadPrimaryTitleRow()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:SquadDash";
        var document = XDocument.Parse(ReadSource("MainWindow.xaml"));
        var row = new XElement(document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "TranscriptTitleDockPanel"));
        row.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        row.Attribute("SizeChanged")!.Remove();
        row.Elements().Where(element => (string?)element.Attribute(x + "Name") is not
            ("TranscriptTitleSpinner" or "TranscriptTitleTextBlock")).Remove();
        row.Descendants().Attributes("IsVisibleChanged").Remove();
        // Load the production XAML against the linked production types in the test assembly.
        XNamespace testLocal = $"clr-namespace:SquadDash;assembly={typeof(ActivitySpinner).Assembly.GetName().Name}";
        row.SetAttributeValue("xmlns", row.Name.NamespaceName);
        row.SetAttributeValue(XNamespace.Xmlns + "local", testLocal.NamespaceName);
        foreach (var element in row.Descendants())
        {
            if (element.Name.Namespace == local) element.Name = testLocal + element.Name.LocalName;
            foreach (var attribute in element.Attributes().Where(attribute => attribute.Name.Namespace == local).ToArray())
            {
                element.SetAttributeValue(testLocal + attribute.Name.LocalName, attribute.Value);
                attribute.Remove();
            }
        }
        return (DockPanel)XamlReader.Parse(row.ToString());
    }

    private static RenderTargetBitmap Render(FrameworkElement element, double dpi)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi / 96),
            (int)Math.Ceiling(element.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static void SaveRenderedTitles(FrameworkElement element, double dpi)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, $"transcript-title-spinners-{dpi:0}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(element, dpi)));
        using (var file = File.Create(path)) encoder.Save(file);
        TestContext.AddTestAttachment(path);
    }

    private static void DrainDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            DrainDispatcher();
            Thread.Sleep(10);
        }
        Assert.That(condition(), Is.True, "Loaded spinner timers did not produce visible activity.");
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

    private static void Tick(ActivitySpinner spinner, int frames, double secondsPerFrame = 0.05)
    {
        var lastTick = typeof(ActivitySpinner).GetField("_lastTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tick = typeof(ActivitySpinner).GetMethod("OnPhysicsTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < frames; i++)
        {
            lastTick.SetValue(spinner, DateTime.UtcNow.AddSeconds(-secondsPerFrame));
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
