using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Restores the Plan Visualizer's task-reference navigation after a description has been rendered
/// as Markdown. Markdown links and inline/fenced code are left untouched.
/// </summary>
internal static class PlanDescriptionTaskReferenceFormatter
{
    private static readonly Regex TaskReferenceRegex =
        new(@"\btask[\s-]?(\d{2,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Apply(
        FlowDocument document,
        IReadOnlyList<PlanTask> tasks,
        Func<string, string, Hyperlink> createTaskLink)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(createTaskLink);
        if (tasks.Count == 0) return;

        foreach (var block in document.Blocks.Cast<Block>().ToArray())
            Apply(block, tasks, createTaskLink);
    }

    private static void Apply(
        Block block,
        IReadOnlyList<PlanTask> tasks,
        Func<string, string, Hyperlink> createTaskLink)
    {
        switch (block)
        {
            case Paragraph paragraph:
                Apply(paragraph.Inlines, tasks, createTaskLink);
                break;
            case Section section:
                foreach (var child in section.Blocks.Cast<Block>().ToArray())
                    Apply(child, tasks, createTaskLink);
                break;
            case List list:
                foreach (var item in list.ListItems)
                foreach (var child in item.Blocks.Cast<Block>().ToArray())
                    Apply(child, tasks, createTaskLink);
                break;
        }
    }

    private static void Apply(
        InlineCollection inlines,
        IReadOnlyList<PlanTask> tasks,
        Func<string, string, Hyperlink> createTaskLink)
    {
        foreach (var inline in inlines.Cast<Inline>().ToArray())
        {
            if (inline is Hyperlink)
                continue;
            if (inline is Span span)
            {
                Apply(span.Inlines, tasks, createTaskLink);
                continue;
            }
            if (inline is not Run run || IsCode(run))
                continue;

            ReplaceRun(inlines, run, tasks, createTaskLink);
        }
    }

    private static void ReplaceRun(
        InlineCollection inlines,
        Run run,
        IReadOnlyList<PlanTask> tasks,
        Func<string, string, Hyperlink> createTaskLink)
    {
        var matches = TaskReferenceRegex.Matches(run.Text);
        if (matches.Count == 0) return;

        var replacements = new List<Inline>();
        var position = 0;
        foreach (Match match in matches)
        {
            var numberText = match.Groups[1].Value;
            var referencedTask = tasks.FirstOrDefault(task =>
                task.TaskId.EndsWith("-" + numberText, StringComparison.Ordinal) ||
                string.Equals(task.TaskId, numberText, StringComparison.Ordinal));
            if (referencedTask is null)
                continue;

            if (match.Index > position)
                replacements.Add(CloneRun(run, run.Text[position..match.Index]));

            var stepLabel = referencedTask.DisplayStepLabel
                ?? (tasks.ToList().IndexOf(referencedTask) + 1).ToString();
            var link = createTaskLink($"Step {stepLabel}", referencedTask.TaskId);
            CopyLocalTextStyle(run, link);
            replacements.Add(link);
            position = match.Index + match.Length;
        }

        if (replacements.Count == 0) return;
        if (position < run.Text.Length)
            replacements.Add(CloneRun(run, run.Text[position..]));

        foreach (var replacement in replacements)
            inlines.InsertBefore(run, replacement);
        inlines.Remove(run);
    }

    private static bool IsCode(Run run) =>
        run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
        run.ReadLocalValue(TextElement.BackgroundProperty) != DependencyProperty.UnsetValue;

    private static Run CloneRun(Run source, string text)
    {
        var clone = new Run(text);
        CopyLocalTextStyle(source, clone);
        return clone;
    }

    private static void CopyLocalTextStyle(TextElement source, TextElement target)
    {
        Copy(TextElement.FontFamilyProperty);
        Copy(TextElement.FontSizeProperty);
        Copy(TextElement.FontStretchProperty);
        Copy(TextElement.FontStyleProperty);
        Copy(TextElement.FontWeightProperty);
        Copy(TextElement.ForegroundProperty);
        Copy(TextElement.BackgroundProperty);
        Copy(Inline.TextDecorationsProperty);
        return;

        void Copy(DependencyProperty property)
        {
            var value = source.ReadLocalValue(property);
            if (value != DependencyProperty.UnsetValue)
                target.SetValue(property, value);
        }
    }
}
