using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

internal static class ProviderThrottleTranscriptBlockFactory
{
    internal static TranscriptHostNoticeEntry Append(
        TranscriptThreadState thread,
        string text,
        DateTimeOffset occurredAt,
        int sequence)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var entry = new TranscriptHostNoticeEntry(sequence, text, occurredAt);
        var container = Create(entry);

        if (thread.CurrentTurn is { } turn)
        {
            turn.HostNotices.Add(entry);
            turn.NarrativeSection.Blocks.Add(container);
        }
        else
        {
            thread.Document.Blocks.Add(container);
        }

        return entry;
    }

    internal static BlockUIContainer Create(TranscriptHostNoticeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var textBlock = new TextBlock
        {
            Text = $"⚠ {entry.Text}",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TaskFailureText");

        var border = new Border
        {
            Child = textBlock,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6)
        };
        border.SetResourceReference(Border.BackgroundProperty, "SystemErrorBackground");

        return TranscriptCardLayout.CreateContainer(
            border,
            entry,
            new Thickness(0, 4, 0, 6));
    }
}
