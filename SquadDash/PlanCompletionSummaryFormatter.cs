using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace SquadDash;

internal static class PlanCompletionSummaryFormatter
{
    private static readonly Regex StepReferenceRegex = new(
        @"\bstep\s+(?<label>[0-9]+)\b(?!\.[0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static Paragraph CreateParagraph(
        string text,
        IReadOnlyDictionary<string, string> displayedLabelsByTaskId,
        Func<string, string, Hyperlink> createTaskLink)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(displayedLabelsByTaskId);
        ArgumentNullException.ThrowIfNull(createTaskLink);

        // Invert the actual node labels, not task IDs or a separately computed task order.
        // Duplicate labels cannot identify a unique navigation target.
        var taskIdByLabel = displayedLabelsByTaskId
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Key, StringComparer.Ordinal);

        var paragraph = new Paragraph();
        var position = 0;
        foreach (Match match in StepReferenceRegex.Matches(text))
        {
            if (!taskIdByLabel.TryGetValue(match.Groups["label"].Value, out var taskId))
                continue;

            if (match.Index > position)
                paragraph.Inlines.Add(new Run(text[position..match.Index]));
            paragraph.Inlines.Add(createTaskLink(match.Value, taskId));
            position = match.Index + match.Length;
        }

        if (position < text.Length || paragraph.Inlines.Count == 0)
            paragraph.Inlines.Add(new Run(text[position..]));
        return paragraph;
    }
}
