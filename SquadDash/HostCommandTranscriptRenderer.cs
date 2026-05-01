using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal static class HostCommandTranscriptRenderer {
    /// <summary>
    /// Creates and returns a Block (BlockUIContainer wrapping an Expander) representing a
    /// host command invocation for insertion into the transcript FlowDocument.
    /// </summary>
    internal static Block RenderEntry(HostCommandTranscriptEntry entry) {
        var result = entry.Result;

        // ── Header (collapsed view) ──────────────────────────────────────────

        var iconBlock = new TextBlock {
            Text = "⚙",
            Width = 24,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBlock.SetResourceReference(TextBlock.ForegroundProperty,
            result.Success ? "SystemInfoText" : "ToolFailureIcon");

        var commandBlock = new TextBlock {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        commandBlock.SetResourceReference(TextBlock.ForegroundProperty,
            result.Success ? "ToolBodyText" : "ToolFailureText");
        commandBlock.Text = BuildCommandLabel(entry.Invocation);

        var arrowBlock = new TextBlock {
            Text = " → ",
            VerticalAlignment = VerticalAlignment.Center
        };
        arrowBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var statusBlock = new TextBlock {
            Text = result.Success ? "✓" : "✗",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBlock.SetResourceReference(TextBlock.ForegroundProperty,
            result.Success ? "ToolSuccessIcon" : "ToolFailureIcon");

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(iconBlock);
        headerPanel.Children.Add(commandBlock);
        headerPanel.Children.Add(arrowBlock);
        headerPanel.Children.Add(statusBlock);

        // ── Expander body (expanded view) ────────────────────────────────────

        var contentPanel = new StackPanel {
            Margin = new Thickness(28, 4, 0, 2)
        };

        if (result.HasOutput) {
            var outputBox = new TextBox {
                Text = result.Output!.Trim(),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                MinHeight = 40,
                MaxHeight = 200,
                Margin = new Thickness(0, 0, 0, 4)
            };
            outputBox.SetResourceReference(TextBox.BackgroundProperty, "CodeSurface");
            outputBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
            outputBox.SetResourceReference(TextBox.ForegroundProperty, "CodeText");
            contentPanel.Children.Add(outputBox);
        }

        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage)) {
            var errorBlock = new TextBlock {
                Text = result.ErrorMessage.Trim(),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };
            errorBlock.SetResourceReference(TextBlock.ForegroundProperty, "ToolFailureText");
            contentPanel.Children.Add(errorBlock);
        }

        if (contentPanel.Children.Count == 0) {
            var emptyHint = new TextBlock {
                Text = result.Success ? "Command completed with no output." : "Command failed with no details.",
                Margin = new Thickness(0, 2, 0, 2)
            };
            emptyHint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            contentPanel.Children.Add(emptyHint);
        }

        // ── Expander ─────────────────────────────────────────────────────────

        var expander = new Expander {
            Header = headerPanel,
            Content = contentPanel,
            IsExpanded = !result.Success,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 2, 0, 2)
        };

        if (Application.Current?.TryFindResource("TranscriptExpanderStyle") is Style expanderStyle)
            expander.Style = expanderStyle;

        return new BlockUIContainer(expander) { Margin = new Thickness(0, 1, 0, 1) };
    }

    /// <summary>
    /// Renders all host command entries for a completed turn, appending them to
    /// the NarrativeSection of the turn. Called from FinalizeCurrentTurnResponse.
    /// </summary>
    internal static void RenderAllEntries(TranscriptTurnView turn) {
        foreach (var entry in turn.HostCommandEntries)
            turn.NarrativeSection.Blocks.Add(RenderEntry(entry));
    }

    private static string BuildCommandLabel(HostCommandInvocation invocation) {
        if (invocation.Parameters is null || invocation.Parameters.Count == 0)
            return invocation.Command;

        var paramList = string.Join(", ",
            invocation.Parameters.Select(kv => $"{kv.Key}: {kv.Value}"));
        return $"{invocation.Command}({paramList})";
    }
}
