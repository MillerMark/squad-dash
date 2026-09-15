using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal sealed record ProviderFailureOccurrence(
    string AgentLabel,
    ProviderFailurePresentation Failure,
    DateTimeOffset OccurredAt,
    string OccurrenceId);

internal static class ProviderFailureTranscriptBlockFactory
{
    internal const string ForegroundResourceKey = "SystemErrorText";
    internal const string BackgroundResourceKey = "SystemErrorBackground";
    internal const string CodeBlockTag = "codeblock";

    internal static BlockUIContainer Append(
        TranscriptThreadState thread,
        ProviderFailurePresentation failure,
        string agentLabel,
        double transcriptFontSize)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var container = Create(failure, agentLabel, transcriptFontSize);
        var fingerprint = ((ProviderFailureTranscriptTag)container.Tag).Fingerprint;
        var previousMatches = thread.Document.Blocks
            .OfType<BlockUIContainer>()
            .Where(block => block.Tag is ProviderFailureTranscriptTag tag &&
                            string.Equals(tag.Fingerprint, fingerprint, StringComparison.Ordinal))
            .ToArray();
        foreach (var previous in previousMatches)
            thread.Document.Blocks.Remove(previous);
        thread.Document.Blocks.Add(container);
        return container;
    }

    internal static BlockUIContainer AppendAggregated(
        TranscriptThreadState thread,
        ProviderFailurePresentation failure,
        string agentLabel,
        double transcriptFontSize,
        string operationScope,
        DateTimeOffset occurredAt,
        string occurrenceId)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(failure);
        var normalizedLabel = string.IsNullOrWhiteSpace(agentLabel) ? "Agent" : agentLabel.Trim();
        var fingerprint = BuildAggregateFingerprint(failure, operationScope);
        var existing = thread.Document.Blocks
            .OfType<BlockUIContainer>()
            .FirstOrDefault(block => block.Tag is ProviderFailureAggregateTranscriptTag tag &&
                                     string.Equals(tag.Fingerprint, fingerprint, StringComparison.Ordinal));
        if (existing?.Tag is ProviderFailureAggregateTranscriptTag existingTag)
        {
            existingTag.Add(new ProviderFailureOccurrence(
                normalizedLabel,
                failure,
                occurredAt,
                occurrenceId));
            return existing;
        }

        var aggregateTag = new ProviderFailureAggregateTranscriptTag(fingerprint);
        var container = CreateAggregate(aggregateTag, transcriptFontSize);
        aggregateTag.Add(new ProviderFailureOccurrence(
            normalizedLabel,
            failure,
            occurredAt,
            occurrenceId));
        thread.Document.Blocks.Add(container);
        return container;
    }

    internal static BlockUIContainer Create(
        ProviderFailurePresentation failure,
        string agentLabel,
        double transcriptFontSize)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var normalizedLabel = string.IsNullOrWhiteSpace(agentLabel) ? "Agent" : agentLabel.Trim();
        var baseFontSize = transcriptFontSize;
        var titleFontSize = baseFontSize + 1d;

        var panel = new StackPanel();
        panel.Children.Add(CreateTextBlock(
            $"⛔ {normalizedLabel}: {failure.Title}",
            FontWeights.Bold,
            new Thickness(0, 0, 0, 5),
            titleFontSize));
        panel.Children.Add(CreateTextBlock(
            failure.Explanation,
            FontWeights.Normal,
            new Thickness(0, 0, 0, 5),
            baseFontSize));

        if (!string.IsNullOrWhiteSpace(failure.ContextLine))
        {
            var context = CreateTextBlock(
                failure.ContextLine,
                FontWeights.Normal,
                new Thickness(0, 0, 0, 7),
                baseFontSize);
            panel.Children.Add(context);
        }

        panel.Children.Add(CreateTextBlock(
            "Provider error",
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 3),
            baseFontSize));

        var codeBlock = new TextBox
        {
            Text = failure.RawError,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = baseFontSize,
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

        var guidance = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0),
            FontSize = baseFontSize
        };
        guidance.Inlines.Add(new Run("How to fix: ") { FontWeight = FontWeights.SemiBold });
        guidance.Inlines.Add(new Run(failure.Guidance));
        guidance.SetResourceReference(TextBlock.ForegroundProperty, ForegroundResourceKey);
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

        return TranscriptCardLayout.CreateContainer(
            border,
            new ProviderFailureTranscriptTag(
                BuildFingerprint(failure, normalizedLabel),
                failure,
                normalizedLabel),
            new Thickness(0, 5, 0, 10));
    }

    private static BlockUIContainer CreateAggregate(
        ProviderFailureAggregateTranscriptTag tag,
        double transcriptFontSize)
    {
        var title = CreateTextBlock(
            string.Empty,
            FontWeights.Bold,
            new Thickness(0, 0, 0, 4),
            transcriptFontSize + 1d);
        var range = CreateTextBlock(
            string.Empty,
            FontWeights.Normal,
            new Thickness(0, 0, 0, 7),
            transcriptFontSize);

        var previous = new Button
        {
            Content = "‹",
            MinWidth = 32,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(7, 2, 7, 2),
            IsTabStop = true
        };
        previous.SetResourceReference(FrameworkElement.StyleProperty, "ThemedButtonStyle");
        AutomationProperties.SetName(previous, "Previous provider error");
        var page = CreateTextBlock(
            string.Empty,
            FontWeights.SemiBold,
            new Thickness(0, 3, 6, 0),
            transcriptFontSize);
        var next = new Button
        {
            Content = "›",
            MinWidth = 32,
            Padding = new Thickness(7, 2, 7, 2),
            IsTabStop = true
        };
        next.SetResourceReference(FrameworkElement.StyleProperty, "ThemedButtonStyle");
        AutomationProperties.SetName(next, "Next provider error");

        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 7)
        };
        navigation.Children.Add(previous);
        navigation.Children.Add(page);
        navigation.Children.Add(next);

        var selected = CreateTextBlock(
            string.Empty,
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 5),
            transcriptFontSize);
        var explanation = CreateTextBlock(
            string.Empty,
            FontWeights.Normal,
            new Thickness(0, 0, 0, 5),
            transcriptFontSize);
        var context = CreateTextBlock(
            string.Empty,
            FontWeights.Normal,
            new Thickness(0, 0, 0, 7),
            transcriptFontSize);
        var providerErrorLabel = CreateTextBlock(
            "Provider error",
            FontWeights.SemiBold,
            new Thickness(0, 0, 0, 3),
            transcriptFontSize);
        var codeBlock = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = transcriptFontSize,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 7),
            Tag = CodeBlockTag,
            IsTabStop = true
        };
        codeBlock.SetResourceReference(Control.BackgroundProperty, BackgroundResourceKey);
        codeBlock.SetResourceReference(Control.ForegroundProperty, ForegroundResourceKey);
        codeBlock.SetResourceReference(Control.BorderBrushProperty, ForegroundResourceKey);
        var guidance = CreateTextBlock(
            string.Empty,
            FontWeights.Normal,
            new Thickness(0),
            transcriptFontSize);

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(range);
        panel.Children.Add(navigation);
        panel.Children.Add(selected);
        panel.Children.Add(explanation);
        panel.Children.Add(context);
        panel.Children.Add(providerErrorLabel);
        panel.Children.Add(codeBlock);
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

        tag.Attach(
            title,
            range,
            selected,
            explanation,
            context,
            codeBlock,
            guidance,
            page,
            previous,
            next);
        previous.Click += (_, _) => tag.Move(-1);
        next.Click += (_, _) => tag.Move(1);

        return TranscriptCardLayout.CreateContainer(border, tag, new Thickness(0, 5, 0, 10));
    }

    internal static bool IsProviderFailureContainer(Block block) =>
        block is BlockUIContainer { Tag: ProviderFailureTranscriptTag or ProviderFailureAggregateTranscriptTag };

    internal static string BuildGroupingKey(ProviderFailurePresentation failure) =>
        string.Join("|", failure.Category, failure.Title, failure.ContextLine ?? string.Empty);

    private static string BuildFingerprint(ProviderFailurePresentation failure, string agentLabel) =>
        string.Join("|", agentLabel, failure.Category, failure.Title, failure.ContextLine ?? string.Empty);

    private static string BuildAggregateFingerprint(ProviderFailurePresentation failure, string operationScope) =>
        string.Join("|", operationScope.Trim(), BuildGroupingKey(failure));

    private static TextBlock CreateTextBlock(string text, FontWeight weight, Thickness margin, double fontSize)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = weight,
            Margin = margin,
            FontSize = fontSize
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, ForegroundResourceKey);
        return textBlock;
    }

    internal sealed record ProviderFailureTranscriptTag(
        string Fingerprint,
        ProviderFailurePresentation Failure,
        string AgentLabel) : ICopyable
    {
        public string GetCopyText() => Failure.BuildCopyText(AgentLabel);
    }

    internal sealed class ProviderFailureAggregateTranscriptTag(string fingerprint) : ICopyable
    {
        private readonly List<ProviderFailureOccurrence> _occurrences = [];
        private readonly HashSet<string> _occurrenceIds = new(StringComparer.Ordinal);
        private TextBlock? _title;
        private TextBlock? _range;
        private TextBlock? _selected;
        private TextBlock? _explanation;
        private TextBlock? _context;
        private TextBox? _codeBlock;
        private TextBlock? _guidance;
        private TextBlock? _page;
        private Button? _previous;
        private Button? _next;
        private int _selectedIndex = -1;

        internal string Fingerprint { get; } = fingerprint;
        internal IReadOnlyList<ProviderFailureOccurrence> Occurrences => _occurrences;
        internal int SelectedIndex => _selectedIndex;

        internal void Attach(
            TextBlock title,
            TextBlock range,
            TextBlock selected,
            TextBlock explanation,
            TextBlock context,
            TextBox codeBlock,
            TextBlock guidance,
            TextBlock page,
            Button previous,
            Button next)
        {
            _title = title;
            _range = range;
            _selected = selected;
            _explanation = explanation;
            _context = context;
            _codeBlock = codeBlock;
            _guidance = guidance;
            _page = page;
            _previous = previous;
            _next = next;
            Refresh();
        }

        internal void Add(ProviderFailureOccurrence occurrence)
        {
            if (!_occurrenceIds.Add(occurrence.OccurrenceId))
                return;

            var followedLatest = _selectedIndex < 0 || _selectedIndex == _occurrences.Count - 1;
            _occurrences.Add(occurrence);
            if (followedLatest)
                _selectedIndex = _occurrences.Count - 1;
            Refresh();
        }

        internal void Move(int delta)
        {
            if (_occurrences.Count == 0)
                return;
            _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _occurrences.Count - 1);
            Refresh();
        }

        private void Refresh()
        {
            if (_occurrences.Count == 0 || _title is null)
                return;

            _selectedIndex = Math.Clamp(_selectedIndex, 0, _occurrences.Count - 1);
            var occurrence = _occurrences[_selectedIndex];
            var failure = occurrence.Failure;
            var allTemporary = _occurrences.All(item =>
                item.AgentLabel.StartsWith("Temporary Agent", StringComparison.OrdinalIgnoreCase));
            _title.Text = _occurrences.Count == 1
                ? $"⛔ {occurrence.AgentLabel}: {failure.Title}"
                : $"⛔ {_occurrences.Count} {(allTemporary ? "temporary-agent" : "agent")} failures: {failure.Title}";

            var firstAt = _occurrences.Min(item => item.OccurredAt);
            var lastAt = _occurrences.Max(item => item.OccurredAt);
            _range!.Text = _occurrences.Count == 1
                ? $"Occurred {firstAt:g}"
                : $"Collected {_occurrences.Count} related failures from {firstAt:g} through {lastAt:g}.";
            _page!.Text = $"Error {_selectedIndex + 1} of {_occurrences.Count}";
            _selected!.Text = $"{occurrence.AgentLabel}  •  {occurrence.OccurredAt:g}";
            _explanation!.Text = failure.Explanation;
            _context!.Text = failure.ContextLine ?? string.Empty;
            _context.Visibility = string.IsNullOrWhiteSpace(failure.ContextLine)
                ? Visibility.Collapsed
                : Visibility.Visible;
            _codeBlock!.Text = failure.RawError;
            _guidance!.Text = "How to fix: " + failure.Guidance;
            _previous!.IsEnabled = _selectedIndex > 0;
            _next!.IsEnabled = _selectedIndex < _occurrences.Count - 1;
            AutomationProperties.SetName(_previous, $"Previous provider error, showing {_selectedIndex + 1} of {_occurrences.Count}");
            AutomationProperties.SetName(_next, $"Next provider error, showing {_selectedIndex + 1} of {_occurrences.Count}");
        }

        public string GetCopyText()
        {
            if (_occurrences.Count == 0)
                return string.Empty;
            if (_occurrences.Count == 1)
                return _occurrences[0].Failure.BuildCopyText(_occurrences[0].AgentLabel);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"{_occurrences.Count} related provider failures");
            builder.AppendLine($"From {_occurrences.Min(item => item.OccurredAt):O} through {_occurrences.Max(item => item.OccurredAt):O}");
            for (var index = 0; index < _occurrences.Count; index++)
            {
                var occurrence = _occurrences[index];
                builder.AppendLine();
                builder.AppendLine($"--- Error {index + 1} of {_occurrences.Count} • {occurrence.OccurredAt:O} ---");
                builder.AppendLine(occurrence.Failure.BuildCopyText(occurrence.AgentLabel));
            }
            return builder.ToString().TrimEnd();
        }
    }
}
