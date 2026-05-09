using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SquadDash;

internal enum DiffLineKind {
    Added,
    Removed,
    Context,
    Header
}

internal sealed class DiffLine {
    public DiffLine(string text, DiffLineKind kind) {
        Text = text;
        Kind = kind;
    }

    public string Text { get; }
    public DiffLineKind Kind { get; }
}

internal sealed class DiffHoverPopup : Popup {
    internal const int MaxDisplayLines = 40;
    internal const double MaxPopupHeight = 300;
    private const double LineHeightEstimate = 16.0; // FontSize 13 + padding

    public DiffHoverPopup() {
        AllowsTransparency = true;
        StaysOpen = true;
        Placement = PlacementMode.Absolute;
        IsHitTestVisible = true;
    }

    /// <summary>
    /// Returns an estimated rendered height for a popup showing <paramref name="lineCount"/> diff lines,
    /// capped at <see cref="MaxPopupHeight"/>. Used by the caller to decide above-vs-below placement
    /// before the popup is shown.
    /// </summary>
    internal static double EstimateHeight(int lineCount) =>
        Math.Min(Math.Min(lineCount, MaxDisplayLines) * LineHeightEstimate + 20, MaxPopupHeight);

    public void ShowDiff(IEnumerable<DiffLine> diffLines) {
        var linesList = diffLines.ToList();
        var linesToDisplay = linesList.Take(MaxDisplayLines).ToList();
        var truncated = linesList.Count > MaxDisplayLines;

        var stackPanel = new StackPanel {
            Orientation = Orientation.Vertical
        };

        foreach (var line in linesToDisplay) {
            var textBlock = new TextBlock {
                Text = line.Text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Padding = new Thickness(4, 1, 4, 1),
                TextWrapping = TextWrapping.NoWrap
            };

            switch (line.Kind) {
                case DiffLineKind.Added:
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "DiffAddedText");
                    // Blue tint at ~10% opacity
                    textBlock.Background = new SolidColorBrush(Color.FromArgb(26, 107, 174, 214));
                    break;

                case DiffLineKind.Removed:
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "DiffRemovedText");
                    // Red tint at ~10% opacity
                    textBlock.Background = new SolidColorBrush(Color.FromArgb(26, 224, 112, 112));
                    break;

                case DiffLineKind.Header:
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                    textBlock.FontSize = 11;
                    textBlock.Opacity = 0.8;
                    break;

                case DiffLineKind.Context:
                    textBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
                    break;
            }

            stackPanel.Children.Add(textBlock);
        }

        if (truncated) {
            var truncatedLabel = new TextBlock {
                Text = $"… ({linesList.Count - MaxDisplayLines} more lines)",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                FontStyle = FontStyles.Italic
            };
            truncatedLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            stackPanel.Children.Add(truncatedLabel);
        }

        var scrollViewer = new ScrollViewer {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = MaxPopupHeight,
            Padding = new Thickness(8)
        };

        var border = new Border {
            Child = scrollViewer,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0)
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "LineColor");

        Child = border;
        IsOpen = true;
    }

    public static List<DiffLine> ParseDiff(string diffText) {
        var lines = new List<DiffLine>();
        if (string.IsNullOrWhiteSpace(diffText))
            return lines;

        var normalized = diffText.Replace("\r\n", "\n").Replace('\r', '\n');
        var textLines = normalized.Split('\n');

        foreach (var line in textLines) {
            // Skip entirely: headers, metadata, and no-newline markers
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal) ||
                line.StartsWith("@@", StringComparison.Ordinal) ||
                line.StartsWith("diff ", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal) ||
                line.StartsWith("new file", StringComparison.Ordinal) ||
                line.StartsWith("deleted file", StringComparison.Ordinal) ||
                line.StartsWith("\\", StringComparison.Ordinal)) {
                continue;
            }

            if (line.StartsWith('+')) {
                lines.Add(new DiffLine(line, DiffLineKind.Added));
            }
            else if (line.StartsWith('-')) {
                lines.Add(new DiffLine(line, DiffLineKind.Removed));
            }
            else {
                lines.Add(new DiffLine(line, DiffLineKind.Context));
            }
        }

        return lines;
    }
}
