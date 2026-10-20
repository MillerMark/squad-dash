using System.Threading;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class CamelCaseAvalonEditAttachmentTests
{
    [TestCase("helloWorld", 0, Key.Right)]
    [TestCase("helloWorld", 10, Key.Left)]
    [TestCase("  fooBar,baz", 0, Key.Right)]
    [TestCase("one twoWords", 12, Key.Left)]
    [TestCase("", 0, Key.Left)]
    [TestCase("", 0, Key.Right)]
    public void MovementUsesSharedMath(string text, int from, Key key)
    {
        var editor = new TextEditor { Text = text, CaretOffset = from };
        var attachment = new CamelCaseAvalonEditAttachment();
        Assert.That(attachment.HandleKey(key, ModifierKeys.Control, editor), Is.True);
        Assert.That(editor.CaretOffset, Is.EqualTo(key == Key.Right
            ? CamelCaseNavigator.MoveRight(text, from) : CamelCaseNavigator.MoveLeft(text, from)));
        Assert.That(editor.SelectionLength, Is.Zero);
    }

    [Test]
    public void ShiftSelectionPreservesAnchorAcrossDirectionChangesAndMouseReselection()
    {
        var editor = new TextEditor { Text = "oneTwoThree" };
        var attachment = new CamelCaseAvalonEditAttachment();
        var modifiers = ModifierKeys.Control | ModifierKeys.Shift;
        editor.CaretOffset = 6;
        attachment.HandleKey(Key.Left, modifiers, editor);
        Assert.That((editor.SelectionStart, editor.SelectionLength, editor.CaretOffset), Is.EqualTo((3, 3, 3)));
        attachment.HandleKey(Key.Left, modifiers, editor);
        Assert.That((editor.SelectionStart, editor.SelectionLength, editor.CaretOffset), Is.EqualTo((0, 6, 0)));
        attachment.HandleKey(Key.Right, modifiers, editor);
        Assert.That((editor.SelectionStart, editor.SelectionLength, editor.CaretOffset), Is.EqualTo((3, 3, 3)));
        editor.Select(0, 3);
        editor.CaretOffset = 3;
        attachment.HandleKey(Key.Right, modifiers, editor);
        Assert.That((editor.SelectionStart, editor.SelectionLength, editor.CaretOffset), Is.EqualTo((0, 6, 6)));
    }

    [TestCase(Key.Back, 6, "oneThree")]
    [TestCase(Key.Delete, 3, "oneThree")]
    public void WordDeletionIsOneUndoableEdit(Key key, int caret, string expected)
    {
        var editor = new TextEditor { Text = "oneTwoThree", CaretOffset = caret };
        editor.Document.UndoStack.ClearAll();
        new CamelCaseAvalonEditAttachment().HandleKey(key, ModifierKeys.Control, editor);
        Assert.That(editor.Text, Is.EqualTo(expected));
        editor.Undo();
        Assert.That(editor.Text, Is.EqualTo("oneTwoThree"));
    }

    [TestCase(Key.Back)]
    [TestCase(Key.Delete)]
    public void WordDeletionRemovesSelectionNotAnExtraWord(Key key)
    {
        var editor = new TextEditor { Text = "oneTwoThree" };
        editor.TextArea.Selection = Selection.Create(editor.TextArea, 6, 3);
        editor.CaretOffset = 3;
        new CamelCaseAvalonEditAttachment().HandleKey(key, ModifierKeys.Control, editor);
        Assert.That(editor.Text, Is.EqualTo("oneThree"));
    }

    [Test]
    public void LegacyAltGestureWorksAndUnmodifiedArrowsRemainNative()
    {
        var editor = new TextEditor { Text = "oneTwo" };
        var attachment = new CamelCaseAvalonEditAttachment();
        Assert.That(attachment.HandleKey(Key.Right, ModifierKeys.None, editor), Is.False);
        Assert.That(attachment.HandleKey(Key.Right, ModifierKeys.Alt, editor), Is.True);
        Assert.That(editor.CaretOffset, Is.EqualTo(3));
        editor.IsReadOnly = true;
        attachment.HandleKey(Key.Back, ModifierKeys.Control, editor);
        Assert.That(editor.Text, Is.EqualTo("oneTwo"));
    }
}
