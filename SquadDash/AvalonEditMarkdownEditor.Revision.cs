using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace SquadDash;

internal sealed partial class AvalonEditMarkdownEditor
{
    private readonly List<AvalonEditRevisionSelection> _revisions = [];
    private readonly List<DocRevisePopup> _revisionPopups = [];

    public Func<string, string, string, string, CancellationToken, Task<string>>? ReviseWithAiCallback { get; set; }
    public string DocumentPath { get; set; } = string.Empty;
    public string? CleanupPrompt { get; set; }
    public Action<TextBox>? StartPttCallback { get; set; }
    public Action? StopPttCallback { get; set; }
    public event Action? RevisionCompleted;

    private bool CanReviseSelection => !_isDisposed && ReviseWithAiCallback is not null
        && Editor.SelectionLength > 0 && CanMutateSelection();

    private bool CanMutateSelection() => CanMutateRange(Editor.SelectionStart, Editor.SelectionLength);

    private bool CanMutateRange(int start, int length) => !Editor.IsReadOnly &&
        !_revisions.Any(revision => revision.IsPending && revision.TryGetRange(out var lockedStart, out var lockedLength)
            && (length == 0 ? start > lockedStart && start < lockedStart + lockedLength
                : start < lockedStart + lockedLength && start + length > lockedStart));

    private void TriggerReviseWithAi()
    {
        if (!CanReviseSelection || Window.GetWindow(this) is not { } owner)
            return;
        var selection = new AvalonEditRevisionSelection(Editor.Document, Editor.SelectionStart, Editor.SelectionLength);
        var popup = new DocRevisePopup(
            NormalizeLf(selection.OriginalText), GetText(), DocumentPath, ReviseWithAiCallback!,
            revised => Dispatcher.Invoke(() => ApplyRevision(selection, revised, owner)),
            onSubmitting: center => {
                selection.IsPending = true;
                _revisions.Add(selection);
                Editor.TextArea.TextView.Redraw();
                FocusEditor();
                RevisionWorkingOverlay.ShowAt(center, owner);
            },
            onRevisionComplete: () => Dispatcher.Invoke(() => CompleteRevision(selection)),
            startPtt: StartPttCallback, stopPtt: StopPttCallback);
        popup.Owner = owner;
        _revisionPopups.Add(popup);
        popup.Closed += (_, _) => _revisionPopups.Remove(popup);
        PositionRevisionPopup(popup);
        popup.Show();
    }

    private void TriggerDirectReviseWithAi()
    {
        if (!CanReviseSelection || string.IsNullOrWhiteSpace(CleanupPrompt) || Window.GetWindow(this) is not { } owner)
            return;
        var selection = new AvalonEditRevisionSelection(Editor.Document, Editor.SelectionStart, Editor.SelectionLength);
        // The shared executor's public contract is RichTextBox-specific. Use a detached snapshot
        // solely as its transport; the live editor, anchors and undo stack stay in AvalonEdit.
        var bridge = new RichTextBox();
        var fullText = GetText();
        var start = NormalizeLf(Editor.Document.GetText(0, Editor.SelectionStart)).Length;
        var length = NormalizeLf(selection.OriginalText).Length;
        bridge.SetPlainText(fullText);
        bridge.SelectRange(start, length);
        selection.IsPending = true;
        _revisions.Add(selection);
        Editor.TextArea.TextView.Redraw();
        var callback = ReviseWithAiCallback!;
        string? revisedText = null;
        MarkdownRevisionExecutor.DirectRevise(bridge, DocumentPath, CleanupPrompt,
            async (instructions, selected, document, cwd, token) => {
                revisedText = await callback(instructions, selected, document, cwd, token);
                return revisedText;
            }, owner,
            onCompleted: () => {
                try
                {
                    if (!string.IsNullOrWhiteSpace(revisedText))
                        ApplyRevision(selection, revisedText, owner);
                }
                finally { CompleteRevision(selection); }
            });
    }

    private void ApplyRevision(AvalonEditRevisionSelection selection, string revised, Window owner)
    {
        if (_isDisposed)
            return;
        selection.IsPending = false;
        if (!selection.TryReplace(revised))
        {
            var result = new RevisionResultWindow(revised) { Owner = owner };
            WindowPlacementHelper.CenterOnOwnerAndEnsureOnScreen(result, owner);
            result.Show();
        }
    }

    private void CompleteRevision(AvalonEditRevisionSelection selection)
    {
        _revisions.Remove(selection);
        if (_isDisposed)
            return;
        Editor.TextArea.TextView.Redraw();
        RevisionCompleted?.Invoke();
    }

    private void PositionRevisionPopup(Window popup)
    {
        var physical = GetCaretScreenPoint(Editor);
        var bounds = NativeMethods.GetWorkAreaForPhysicalPoint((int)physical.X, (int)physical.Y);
        var point = DpiHelper.PhysicalToLogical(Editor, physical);
        var topLeft = DpiHelper.PhysicalToLogical(Editor, new Point(bounds.Left, bounds.Top));
        var bottomRight = DpiHelper.PhysicalToLogical(Editor, new Point(bounds.Right, bounds.Bottom));
        popup.Left = Math.Max(topLeft.X + 10, Math.Min(point.X - 10, bottomRight.X - popup.Width - 10));
        popup.Top = Math.Max(topLeft.Y + 10, point.Y + 241 > bottomRight.Y ? point.Y - 241 : point.Y + 6);
    }

    private void DisposeRevisions()
    {
        foreach (var popup in _revisionPopups.ToArray())
            popup.Close();
        _revisionPopups.Clear();
        _revisions.Clear();
        RevisionCompleted = null;
        ReviseWithAiCallback = null;
        StartPttCallback = null;
        StopPttCallback = null;
    }

    private sealed class RevisionReadOnlyProvider(AvalonEditMarkdownEditor owner) : IReadOnlySectionProvider
    {
        public bool CanInsert(int offset) => owner.CanMutateRange(offset, 0);
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
        {
            if (owner.CanMutateRange(segment.Offset, segment.Length))
                yield return segment;
        }
    }
}
