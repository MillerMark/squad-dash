using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal static class ProviderFailureTranscriptBlockFactory
{
    internal const string ForegroundResourceKey = "SystemErrorText";
    internal const string BackgroundResourceKey = "SystemErrorBackground";
    internal const string CodeBlockTag = "codeblock";

    internal static BlockUIContainer Append(
        TranscriptThreadState thread,
        ProviderFailurePresentation failure,
        string agentLabel)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var container = Create(failure, agentLabel);
        thread.Document.Blocks.Add(container);
        return container;
    }

    internal static BlockUIContainer Create(
        ProviderFailurePresentation failure,
        string agentLabel)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var normalizedLabel = string.IsNullOrWhiteSpace(agentLabel) ? "Agent" : agentLabel.Trim();

        var panel = new StackPanel();
        panel.Children.Add(CreateTextBlock(
            $"⛔ {normalizedLabel}: {failure.Title}",
            FontWeights.Bold,
            new Thickness(0, 0, 0, 5)));
        panel.Children.Add(CreateTextBlock(
            failure.Explanation,
            FontWeights.Normal,
            new Thickness(0, 0, 0, 5)));

        if (!string.IsNullOrWhiteSpace(failure.ContextLine))
        {
            var context = CreateTextBlock(
                failure.ContextLine,
                FontWeights.Normal,
                new Thickness(0, 0, 0, 7));
            context.FontSize = 11;
            panel.Children.Add(context);
        }

        panel.Children.Add(CreateTextBlock(
            "Provider error",
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 3)));

        var codeBlock = new TextBox
        {
            Text = failure.RawError,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 7),
            Tag = CodeBlockTag,
            IsTabStop = true
        };
        codeBlock.SetResourceReference(Control.BackgroundProperty, BackgroundResourceKey);
        codeBlock.SetResourceReference(Control.ForegroundProperty, ForegroundResourceKey);
        codeBlock.SetResourceReference(Control.BorderBrushProperty, ForegroundResourceKey);
        panel.Children.Add(codeBlock);

        var guidance = CreateTextBlock(
            "How to fix: " + failure.Guidance,
            FontWeights.SemiBold,
            new Thickness(0));
        panel.Children.Add(guidance);

        var border = new Border
        {
            Child = panel,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 9, 10, 9)
        };
        border.SetResourceReference(Border.BackgroundProperty, BackgroundResourceKey);
        border.SetResourceReference(Border.BorderBrushProperty, ForegroundResourceKey);

        return new BlockUIContainer(border)
        {
            Margin = new Thickness(0, 5, 0, 10),
            Tag = new ProviderFailureCopyPayload(failure, normalizedLabel)
        };
    }

    private static TextBlock CreateTextBlock(string text, FontWeight weight, Thickness margin)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = weight,
            Margin = margin
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, ForegroundResourceKey);
        return textBlock;
    }

    private sealed record ProviderFailureCopyPayload(
        ProviderFailurePresentation Failure,
        string AgentLabel) : ICopyable
    {
        public string GetCopyText() => Failure.BuildCopyText(AgentLabel);
    }
}
