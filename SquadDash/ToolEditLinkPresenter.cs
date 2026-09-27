using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

internal static class ToolEditLinkPresenter {
    internal static bool TryRender(
        TextBlock message, ToolTranscriptDescriptor descriptor, string? output, string? argsJson,
        IReadOnlyList<CapturedFileEdit>? capturedEdits,
        Func<CapturedFileEdit, Task> openPatch) {
        var edits = capturedEdits ?? ToolTranscriptFormatter.TryExtractCapturedEdits(descriptor, output, argsJson);
        if (edits.Count == 0) {
            if (ToolTranscriptFormatter.TryBuildEditDiffSummary(descriptor, output, argsJson) is not { } fallback)
                return false;
            AddEdit(message, new CapturedFileEdit(
                fallback.DisplayName,
                string.Empty,
                fallback.AddedLineCount,
                fallback.RemovedLineCount,
                fallback.IsNewFile,
                fallback.IsDeletedFile), null);
            return true;
        }

        for (var index = 0; index < edits.Count; index++) {
            if (index > 0)
                message.Inlines.Add(new Run("  ·  "));
            var edit = edits[index];
            AddEdit(message, edit, () => openPatch(edit));
        }
        return true;
    }

    private static void AddEdit(TextBlock message, CapturedFileEdit edit, Func<Task>? openPatch) {
        var fileRun = new Run(edit.DisplayName);
        if (edit.IsDeletedFile)
            fileRun.TextDecorations = TextDecorations.Strikethrough;

        if (openPatch is not null && !string.IsNullOrWhiteSpace(edit.UnifiedDiff)) {
            var fileLink = new Hyperlink(fileRun) {
                Cursor = Cursors.Hand,
                ToolTip = ToolTipHelper.MakeThemedToolTip("View this captured edit")
            };
            fileLink.SetResourceReference(TextElement.ForegroundProperty, "TaskLinkText");
            fileLink.Click += async (_, e) => {
                e.Handled = true;
                await openPatch();
            };
            message.Inlines.Add(fileLink);
        }
        else {
            fileRun.SetResourceReference(TextElement.ForegroundProperty,
                edit.IsDeletedFile ? "DiffDeletedFileText" : "TableCellText");
            message.Inlines.Add(fileRun);
        }

        if (edit.IsNewFile)
            message.Inlines.Add(new Run(" ➕"));

        message.Inlines.Add(new Run(" "));
        var addedRun = new Run($"+{edit.AddedLineCount}") { FontWeight = FontWeights.SemiBold };
        addedRun.SetResourceReference(TextElement.ForegroundProperty, "DiffAddedSummary");
        message.Inlines.Add(addedRun);
        message.Inlines.Add(new Run(" "));
        var removedRun = new Run($"-{edit.RemovedLineCount}") { FontWeight = FontWeights.SemiBold };
        removedRun.SetResourceReference(TextElement.ForegroundProperty, "DiffRemovedSummary");
        message.Inlines.Add(removedRun);

        if (ToolTranscriptFormatter.IsCapturedContentSnapshot(edit.UnifiedDiff))
            message.Inlines.Add(new Run(" (captured content)"));
    }
}
