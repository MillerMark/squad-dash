using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

internal static class CommitViewerLayout
{
    internal static double CalculateWindowWidth(double workingWidth, double minimumWidth = 720) =>
        Math.Max(minimumWidth, Math.Min(1600, workingWidth * 0.75));

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
