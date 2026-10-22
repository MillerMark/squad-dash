using System;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;

namespace SquadDash;

internal sealed class CamelCaseAvalonEditAttachment
{
    internal bool HandlePreviewKeyDown(KeyEventArgs e, TextEditor editor) =>
        HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, editor);

    internal bool HandleKey(Key key, ModifierKeys modifiers, TextEditor editor)
    {
        var control = (modifiers & ModifierKeys.Control) != 0;
        var alt = (modifiers & ModifierKeys.Alt) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        if (control == alt)
            return false;
        var arrow = key is Key.Left or Key.Right;
        var delete = control && key is Key.Back or Key.Delete;
        if (!arrow && !delete)
            return false;

        var area = editor.TextArea;
        var caret = editor.CaretOffset;
        var start = editor.SelectionStart;
        var end = start + editor.SelectionLength;
        var right = key is Key.Right or Key.Delete;
        var from = !shift && arrow && end > start ? (right ? end : start) : caret;
        var target = right
            ? CamelCaseNavigator.MoveRight(editor.Text, from)
            : CamelCaseNavigator.MoveLeft(editor.Text, from);
        if (delete)
        {
            if (editor.IsReadOnly)
                return true;
            if (end == start)
            {
                start = Math.Min(caret, target);
                end = Math.Max(caret, target);
            }
            area.Selection = Selection.Create(area, start, end);
            area.Selection.ReplaceSelectionWithText(string.Empty);
        }
        else
        {
            // AvalonEdit retains the moving caret end, including backwards selections.
            var anchor = end > start && caret == start ? end : start;
            area.Caret.Offset = target;
            area.Selection = shift
                ? Selection.Create(area, anchor, target)
                : Selection.Create(area, target, target);
            area.Caret.BringCaretToView();
        }
        return true;
    }
}
