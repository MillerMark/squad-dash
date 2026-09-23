using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

internal static class ToolEditLinkPresenter {
    internal static bool TryRender(
        TextBlock message, ToolTranscriptDescriptor descriptor, string? output, string? argsJson,
        Func<string, string, Task> openPatch) {
        if (ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output, argsJson) is not { } summary)
            return false;

        var fileRun = new Run(summary.DisplayName);
        if (summary.IsDeletedFile)
            fileRun.TextDecorations = TextDecorations.Strikethrough;

        if (ToolTranscriptFormatter.TryExtractEditUnifiedDiff(descriptor, output, argsJson) is { } patch) {
            var fileLink = new Hyperlink(fileRun) {
                Cursor = Cursors.Hand,
                ToolTip = ToolTipHelper.MakeThemedToolTip("View this captured edit")
            };
            fileLink.SetResourceReference(TextElement.ForegroundProperty, "TaskLinkText");
            fileLink.Click += async (_, e) => {
                e.Handled = true;
                await openPatch(summary.DisplayName, patch);
            };
            message.Inlines.Add(fileLink);
            if (ToolTranscriptFormatter.IsCapturedContentSnapshot(patch)) {
                message.Inlines.Add(new Run(" (captured content)"));
                return true;
            }
        }
        else {
            fileRun.SetResourceReference(TextElement.ForegroundProperty,
                summary.IsDeletedFile ? "DiffDeletedFileText" : "TableCellText");
            message.Inlines.Add(fileRun);
        }
        message.Inlines.Add(new Run(" "));
        var addedRun = new Run($"+{summary.AddedLineCount}") { FontWeight = FontWeights.SemiBold };
        addedRun.SetResourceReference(TextElement.ForegroundProperty, "DiffAddedSummary");
        message.Inlines.Add(addedRun);
        message.Inlines.Add(new Run(" "));
        var removedRun = new Run($"-{summary.RemovedLineCount}") { FontWeight = FontWeights.SemiBold };
        removedRun.SetResourceReference(TextElement.ForegroundProperty, "DiffRemovedSummary");
        message.Inlines.Add(removedRun);
        if (summary.IsNewFile)
            message.Inlines.Add(new Run(" ➕"));
        return true;
    }
}
