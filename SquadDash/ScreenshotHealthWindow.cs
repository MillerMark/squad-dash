using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using SquadDash.Screenshots;

namespace SquadDash;

/// <summary>
/// Floating panel that runs the screenshot structural health check and displays
/// per-definition results — pass/warning/error/not-captured — with issue details.
///
/// <para>Open via <c>open_panel health</c> or the host command; close with the Close button.
/// Follows the singleton floating-window pattern used by <see cref="TasksStatusWindow"/>
/// and <see cref="TraceWindow"/>.</para>
/// </summary>
internal sealed class ScreenshotHealthWindow : Window
{
    private readonly ScreenshotHealthChecker _checker;
    private readonly Button                  _runButton;
    private readonly TextBlock               _summaryBlock;
    private readonly StackPanel              _resultsPanel;
    private readonly ScrollViewer            _scrollViewer;
    private CancellationTokenSource?         _cts;

    public ScreenshotHealthWindow(ScreenshotHealthChecker checker)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));

        Title              = "Screenshot Health";
        Width              = 580;
        Height             = 500;
        MinWidth           = 420;
        MinHeight          = 320;
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        ResizeMode         = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar      = false;
        ShowActivated      = false;
        Topmost            = false;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(4),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        // ── Outer shell ─────────────────────────────────────────────────────────

        var outerBorder = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4) };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // hint
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // summary
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });     // results
        outerBorder.Child = root;
        Content           = outerBorder;

        // ── Header ──────────────────────────────────────────────────────────────

        var header = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var closeButton = new Button
        {
            Content             = "Close",
            MinWidth            = 76,
            Height              = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);

        _runButton = new Button
        {
            Content             = "Run Check",
            MinWidth            = 90,
            Height              = 30,
            Margin              = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _runButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(_runButton, true);
        _runButton.Click += OnRunCheckClicked;
        DockPanel.SetDock(_runButton, Dock.Right);
        header.Children.Add(_runButton);

        var titleBlock = new TextBlock
        {
            Text                = "Screenshot Health",
            FontSize            = 18,
            FontWeight          = FontWeights.SemiBold,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        header.Children.Add(titleBlock);

        // ── Hint ────────────────────────────────────────────────────────────────

        var hintBlock = new TextBlock
        {
            Text         = "Structural health check for screenshot definitions. Click Run Check to scan all definitions.",
            Margin       = new Thickness(0, 8, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(hintBlock, 1);
        root.Children.Add(hintBlock);

        // ── Summary bar ─────────────────────────────────────────────────────────

        _summaryBlock = new TextBlock
        {
            Text         = "No results yet — press Run Check to begin.",
            FontSize     = 13,
            Margin       = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        _summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        Grid.SetRow(_summaryBlock, 2);
        root.Children.Add(_summaryBlock);

        // ── Results area ────────────────────────────────────────────────────────

        var contentBorder = new Border
        {
            Padding         = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(12),
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 3);
        root.Children.Add(contentBorder);

        _resultsPanel = new StackPanel();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _resultsPanel,
        };
        _scrollViewer.SetResourceReference(ScrollViewer.BackgroundProperty, "CardSurface");
        // Remove the white dead-corner square where the two scrollbars meet.
        _scrollViewer.Loaded += (_, _) =>
        {
            if (_scrollViewer.TryFindResource("CardSurface") is Brush cardBrush)
                _scrollViewer.Resources[SystemColors.ControlBrushKey] = cardBrush;
        };
        contentBorder.Child = _scrollViewer;

        // Show a placeholder row so the content area is not blank on first open.
        AddPlaceholderRow("Press  Run Check  to scan screenshot definitions.");
    }

    // ── Button handler ──────────────────────────────────────────────────────────

    private async void OnRunCheckClicked(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _runButton.IsEnabled = false;
        _runButton.Content   = "Checking…";
        _summaryBlock.Text   = "Running structural checks…";
        _summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _resultsPanel.Children.Clear();

        try
        {
            var report = await _checker.CheckAllAsync(_cts.Token).ConfigureAwait(true);
            DisplayReport(report);
        }
        catch (OperationCanceledException)
        {
            _summaryBlock.Text = "Check cancelled.";
            _summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            AddPlaceholderRow("Check was cancelled.");
        }
        catch (Exception ex)
        {
            _summaryBlock.Text = $"Check failed: {ex.Message}";
            _summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "TaskPriorityHigh");
            AddPlaceholderRow($"Error: {ex.Message}");
        }
        finally
        {
            _runButton.IsEnabled = true;
            _runButton.Content   = "Run Check";
        }
    }

    // ── Report rendering ────────────────────────────────────────────────────────

    private void DisplayReport(ScreenshotHealthReport report)
    {
        _resultsPanel.Children.Clear();

        // Summary line
        if (report.Results.Count == 0)
        {
            _summaryBlock.Text = "No screenshot definitions are registered.";
            _summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            AddPlaceholderRow("No definitions found.");
            return;
        }

        var summaryParts = new System.Collections.Generic.List<string>();
        if (report.PassCount        > 0) summaryParts.Add($"✅ {report.PassCount} pass");
        if (report.WarningCount     > 0) summaryParts.Add($"⚠️ {report.WarningCount} warning{(report.WarningCount == 1 ? "" : "s")}");
        if (report.ErrorCount       > 0) summaryParts.Add($"❌ {report.ErrorCount} error{(report.ErrorCount == 1 ? "" : "s")}");
        if (report.NotCapturedCount > 0) summaryParts.Add($"⬜ {report.NotCapturedCount} not captured");

        _summaryBlock.Text = summaryParts.Count > 0
            ? string.Join("   ", summaryParts) +
              $"   — {report.Results.Count} definition{(report.Results.Count == 1 ? "" : "s")} at {report.GeneratedAt.ToLocalTime():HH:mm:ss}"
            : $"✅ All {report.Results.Count} definitions passed at {report.GeneratedAt.ToLocalTime():HH:mm:ss}";

        string summaryColorKey = report.ErrorCount > 0   ? "TaskPriorityHigh"
                               : report.WarningCount > 0 ? "TaskPriorityMid"
                                                         : "BodyText";
        _summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, summaryColorKey);

        // Result rows — errors first, then warnings, then not-captured, then pass
        var ordered = report.Results
            .OrderBy(r => r.Status switch
            {
                ScreenshotHealthStatus.Error       => 0,
                ScreenshotHealthStatus.Warning     => 1,
                ScreenshotHealthStatus.NotCaptured => 2,
                ScreenshotHealthStatus.Stale       => 3,
                _                                  => 4,
            })
            .ThenBy(r => r.DefinitionName)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var result = ordered[i];
            _resultsPanel.Children.Add(BuildResultRow(result));

            // Thin separator between rows (omit after last)
            if (i < ordered.Count - 1)
            {
                var sep = new Border
                {
                    Height  = 1,
                    Margin  = new Thickness(0, 3, 0, 3),
                    Opacity = 0.25,
                };
                sep.SetResourceReference(Border.BackgroundProperty, "LineColor");
                _resultsPanel.Children.Add(sep);
            }
        }

        _scrollViewer.ScrollToTop();
    }

    private static FrameworkElement BuildResultRow(ScreenshotHealthResult result)
    {
        var (statusIcon, colorKey) = result.Status switch
        {
            ScreenshotHealthStatus.Pass        => ("✅", "BodyText"),
            ScreenshotHealthStatus.Warning     => ("⚠️", "TaskPriorityMid"),
            ScreenshotHealthStatus.Error       => ("❌", "TaskPriorityHigh"),
            ScreenshotHealthStatus.NotCaptured => ("⬜", "SubtleText"),
            ScreenshotHealthStatus.Stale       => ("🕐", "TaskPriorityMid"),
            _                                  => ("❓", "SubtleText"),
        };

        var row = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        // ── Definition name line ──
        var nameBlock = new TextBlock
        {
            Text         = $"{statusIcon}  {result.DefinitionName}",
            FontSize     = 13,
            FontFamily   = new FontFamily("Consolas"),
            FontWeight   = result.Issues.Count > 0 ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.NoWrap,
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        row.Children.Add(nameBlock);

        // ── Issue lines ──
        foreach (var issue in result.Issues)
        {
            var (issueIcon, issueSeverityKey) = issue.Severity switch
            {
                ScreenshotIssueSeverity.Error   => ("❌", "TaskPriorityHigh"),
                ScreenshotIssueSeverity.Warning => ("⚠️", "TaskPriorityMid"),
                ScreenshotIssueSeverity.Info    => ("ℹ️", "SubtleText"),
                _                               => ("·",  "SubtleText"),
            };

            var issueLine = new TextBlock
            {
                Text         = $"   {issueIcon}  [{issue.Code}]  {issue.Message}",
                FontSize     = 12,
                FontFamily   = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(18, 1, 0, 1),
            };
            issueLine.SetResourceReference(TextBlock.ForegroundProperty, issueSeverityKey);
            row.Children.Add(issueLine);
        }

        return row;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void AddPlaceholderRow(string message)
    {
        var block = new TextBlock
        {
            Text         = message,
            FontSize     = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(4, 4, 4, 4),
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _resultsPanel.Children.Add(block);
    }
}
