using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Renders thought narrative as italic prose while giving explicit backtick spans and
/// camelCase/PascalCase references the transcript's standard inline-code treatment.
/// </summary>
internal static class ThinkingInlineFormatter
{
    // Deliberately narrower than InlineCodeIdentifierParser: thinking text should infer code
    // only for camelCase/PascalCase names. ALL_CAPS text remains ordinary prose unless the model
    // explicitly surrounds it with backticks.
    private static readonly Regex CamelCaseIdentifierRegex = new(
        @"(?<![\p{L}\p{N}_])(?=[\p{L}\p{N}_.]*[a-z0-9][A-Z])_?[\p{L}][\p{L}\p{N}_]*(?:\.[\p{L}\p{N}_]+)*(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static void AppendTo(InlineCollection inlines, string text)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        if (string.IsNullOrEmpty(text))
            return;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0)
                inlines.Add(new LineBreak());

            AppendLine(inlines, lines[lineIndex]);
        }
    }

    private static void AppendLine(InlineCollection inlines, string line)
    {
        var position = 0;
        while (position < line.Length)
        {
            var openingTick = line.IndexOf('`', position);
            if (openingTick < 0)
            {
                AppendInferredReferences(inlines, line[position..]);
                return;
            }

            var closingTick = line.IndexOf('`', openingTick + 1);
            if (closingTick < 0)
            {
                // An unfinished streaming span stays visible as prose until its closing tick
                // arrives; inferred references before and inside it can still be recognized.
                AppendInferredReferences(inlines, line[position..]);
                return;
            }

            AppendInferredReferences(inlines, line[position..openingTick]);
            AppendCodeRun(inlines, line[(openingTick + 1)..closingTick]);
            position = closingTick + 1;
        }
    }

    private static void AppendInferredReferences(InlineCollection inlines, string text)
    {
        if (text.Length == 0)
            return;

        var position = 0;
        foreach (Match match in CamelCaseIdentifierRegex.Matches(text))
        {
            AppendProseRun(inlines, text[position..match.Index]);
            AppendCodeRun(inlines, match.Value);
            position = match.Index + match.Length;
        }

        AppendProseRun(inlines, text[position..]);
    }

    private static void AppendProseRun(InlineCollection inlines, string text)
    {
        if (text.Length == 0)
            return;

        var run = new Run(text) { FontStyle = FontStyles.Italic };
        run.SetResourceReference(TextElement.ForegroundProperty, "ThinkingText");
        inlines.Add(run);
    }

    private static void AppendCodeRun(InlineCollection inlines, string text)
    {
        if (text.Length == 0)
            return;

        var run = new Run(text)
        {
            FontFamily = new FontFamily("Consolas"),
            FontStyle = FontStyles.Normal
        };
        run.SetResourceReference(TextElement.BackgroundProperty, "CodeSurface");
        run.SetResourceReference(TextElement.ForegroundProperty, "CodeText");
        inlines.Add(run);
    }
}
