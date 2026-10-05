using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash;

internal sealed record ButtonCaptionRun(string Text, bool Bold);

internal static class ButtonCaptionPresentation {
    // The same **...** and backslash conventions as MarkdownDocumentRenderer, without
    // its transcript/link actions. Rendering and filtering consume the very same runs.
    internal static IReadOnlyList<ButtonCaptionRun> Parse(string caption) {
        var runs = new List<ButtonCaptionRun>();
        var text = new StringBuilder();
        for (var i = 0; i < caption.Length; i++) {
            if (caption[i] == '\\' && i + 1 < caption.Length) {
                text.Append(caption[++i]);
                continue;
            }
            if (i + 1 < caption.Length && caption[i] == '*' && caption[i + 1] == '*') {
                var end = caption.IndexOf("**", i + 2, System.StringComparison.Ordinal);
                if (end >= 0) {
                    if (text.Length > 0) {
                        runs.Add(new(text.ToString(), false));
                        text.Clear();
                    }
                    runs.Add(new(caption[(i + 2)..end], true));
                    i = end + 1;
                    continue;
                }
            }
            text.Append(caption[i]);
        }
        if (text.Length > 0)
            runs.Add(new(text.ToString(), false));
        return runs;
    }

    internal static string VisibleText(string caption) =>
        string.Concat(Parse(caption).Select(run => run.Text));

    internal static bool Matches(string caption, string filter) =>
        PanelFilterHelper.Matches(VisibleText(caption), filter.Trim());

    internal static TextBlock Render(string caption) {
        var block = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        foreach (var segment in Parse(caption)) {
            var run = new Run(segment.Text);
            block.Inlines.Add(segment.Bold ? new Bold(run) : run);
        }
        return block;
    }
}
