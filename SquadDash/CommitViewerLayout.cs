using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

internal static class CommitViewerLayout
{
    internal const double FileListSplitterWidth = 8;
    internal const double DefaultSingleCommitWidth = 1680;
    internal const double DefaultMultiCommitWidth = 2100;
    internal const double DefaultWindowHeight = 1140;

    internal static GridSplitter CreateFileListSplitter(MouseButtonEventHandler onPreviewLeftButtonDown)
    {
        // Keep the line thin while making the entire divider a reliable mouse target.
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(FrameworkElement.WidthProperty, 1.0);
        line.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        line.SetResourceReference(Border.BackgroundProperty, "LineColor");
        hitArea.AppendChild(line);

        var splitter = new GridSplitter
        {
            Width = FileListSplitterWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = hitArea }
        };
        Panel.SetZIndex(splitter, 1);
        // Ancestor/class handlers can mark routed input handled before instance handlers run.
        splitter.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            onPreviewLeftButtonDown, handledEventsToo: true);
        return splitter;
    }

    internal static double GetDefaultWindowWidth(int commitCount) =>
        commitCount > 1 ? DefaultMultiCommitWidth : DefaultSingleCommitWidth;

    internal static double CalculateWindowWidth(
        double workingWidth, double preferredWidth, double minimumWidth = 720) =>
        ClampWindowDimension(workingWidth, preferredWidth, minimumWidth);

    internal static double CalculateWindowHeight(
        double workingHeight, double preferredHeight, double minimumHeight = 440) =>
        ClampWindowDimension(workingHeight, preferredHeight, minimumHeight);

    internal static Rect ClampWindowBounds(
        Rect bounds,
        Rect workArea,
        double minimumWidth = 720,
        double minimumHeight = 440)
    {
        if (workArea.IsEmpty || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work area must have positive dimensions.");

        var width = CalculateWindowWidth(workArea.Width, bounds.Width, minimumWidth);
        var height = CalculateWindowHeight(workArea.Height, bounds.Height, minimumHeight);
        var left = Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }

    private static double ClampWindowDimension(double working, double preferred, double minimum)
    {
        var safeWorking = Math.Max(0, working);
        var safeMinimum = Math.Min(Math.Max(0, minimum), safeWorking);
        var safePreferred = Math.Max(0, preferred);
        return Math.Max(safeMinimum, Math.Min(safePreferred, safeWorking));
    }

    internal static double MeasureFileListItemWidth(ListBoxItem item)
    {
        // Unconstrained WPF layout includes full paths, inline summary fonts, and container chrome.
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = item.DesiredSize.Width;
        item.InvalidateMeasure();
        return width;
    }

    internal static double CalculateFileListWidth(
        IEnumerable<double> itemWidths, double chromeWidth, double availableWidth, double currentWidth)
    {
        var widest = itemWidths.DefaultIfEmpty(-1).Max();
        if (widest < 0) return currentWidth;

        var maximum = Math.Max(0, Math.Min(availableWidth * 0.8, availableWidth - 240));
        var minimum = Math.Min(330, maximum);
        return Math.Clamp(Math.Ceiling(widest + chromeWidth), minimum, maximum);
    }

    internal static string BuildUncertainCommitToolTip(string? explanation)
    {
        return "SquadDash included this commit as evidence, but could not confirm that it belongs to this step.";
    }

    internal static bool IsCommitAttributionUncertain(string relation, string? explanation)
    {
        return string.Equals(relation, PlanRecoveryCommitRelation.Unknown, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relation, PlanRecoveryCommitRelation.Unrelated, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? BuildCommitToolTip(bool isUncertain, string? explanation)
    {
        if (isUncertain) return BuildUncertainCommitToolTip(explanation);
        if (!string.IsNullOrWhiteSpace(explanation) &&
            (explanation.Contains("predates baseline", StringComparison.OrdinalIgnoreCase) ||
             explanation.Contains("predates the baseline", StringComparison.OrdinalIgnoreCase)))
        {
            return "This commit appears to implement this step, but an interruption prevented SquadDash " +
                   "from automatically confirming the step as complete.";
        }
        return explanation;
    }
}
