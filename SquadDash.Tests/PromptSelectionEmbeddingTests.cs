using System.Threading;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
internal sealed class PromptSelectionEmbeddingTests
{
    [SetUp]
    public void SetUp() => EmbeddedCodeRegionBlock.ClearActiveRegion();

    [TearDown]
    public void TearDown() => EmbeddedCodeRegionBlock.ClearActiveRegion();

    [TestCase('"', "word", "\"word\"", "\"word\"")]
    [TestCase('"', " word  ", " \"word\"  ", "\"word\"")]
    [TestCase('`', "word", "`word`", "`word`")]
    [TestCase('`', " word  ", " `word`  ", " `word`  ")]
    [TestCase('\'', "word", "`word`", "`word`")]
    [TestCase('\'', " word  ", " `word`  ", " `word`  ")]
    [TestCase('{', "user_name42", "{user_name42}", "{user_name42}")]
    [TestCase('}', "user_name42", "{user_name42}", "{user_name42}")]
    [TestCase('{', "user_name42 ", "{user_name42} ", "{user_name42}")]
    [TestCase('}', "user_name42 ", "{user_name42} ", "{user_name42}")]
    [TestCase('{', "42", "{42}", "{42}")]
    [TestCase('}', "_", "{_}", "{_}")]
    [TestCase('{', "\u03b1_2", "{\u03b1_2}", "{\u03b1_2}")]
    public void Embed_UndoVisitsPressedKeyThenOriginal_AndRedoRestoresSelection(
        char key, string selected, string replacement, string finalSelection)
    {
        foreach (var backward in new[] { false, true })
        {
            var (box, history) = CreateEditor("before " + selected + " after", 7, selected.Length, backward);
            var original = PromptUndoManager.CaptureSnapshot(box);

            Assert.That(Embed(box, history, key), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(box.GetPlainText(), Is.EqualTo("before " + replacement + " after"));
                Assert.That(box.GetSelectedText(), Is.EqualTo(finalSelection));
                Assert.That(history.Count, Is.EqualTo(3));
            });
            var embedded = PromptUndoManager.CaptureSnapshot(box);

            Assert.That(history.Undo(), Is.True);
            Assert.That(box.GetPlainText(), Is.EqualTo("before " + key + " after"));
            Assert.That(history.Undo(), Is.True);
            Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
            Assert.That(history.CanUndo, Is.False);

            Assert.That(history.Redo(), Is.True);
            Assert.That(box.GetPlainText(), Is.EqualTo("before " + key + " after"));
            Assert.That(history.Redo(), Is.True);
            Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(embedded));
            Assert.That(history.CanRedo, Is.False);
        }
    }

    [TestCase('`')]
    [TestCase('\'')]
    public void MultilineCode_PreservesFenceWrappingAndIntermediateUndo(char key)
    {
        const string selected = "one\ntwo";
        var (box, history) = CreateEditor(selected, 0, selected.Length);
        var original = PromptUndoManager.CaptureSnapshot(box);

        Assert.That(Embed(box, history, key), Is.True);
        Assert.That(box.GetPlainText(), Is.EqualTo("```\none\ntwo\n```"));
        Assert.That(box.GetSelectedText(), Is.EqualTo("```\none\ntwo\n```"));
        Assert.That(history.Count, Is.EqualTo(3));
        Assert.That(history.Undo(), Is.True);
        Assert.That(box.GetPlainText(), Is.EqualTo(key.ToString()));
        Assert.That(history.Undo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
    }

    [TestCase("two words")]
    [TestCase("name-value")]
    [TestCase("name.value")]
    [TestCase("name!")]
    [TestCase("{name}")]
    [TestCase(" name")]
    [TestCase("name  ")]
    [TestCase("name\t")]
    [TestCase("name\nvalue")]
    [TestCase("name\rvalue")]
    [TestCase(" ")]
    [TestCase("  ")]
    [TestCase("name\u00a0")]
    public void InvalidBraceSelection_LeavesNormalReplacementAndSingleUndo(string selected)
    {
        foreach (var key in new[] { '{', '}' })
        {
            var (box, history) = CreateEditor("before " + selected + " after", 7, selected.Length);
            var original = PromptUndoManager.CaptureSnapshot(box);

            Assert.That(Embed(box, history, key), Is.False);
            Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
            Assert.That(history.Count, Is.EqualTo(1));

            box.ReplaceSelection(key.ToString());
            Assert.That(box.GetPlainText(), Is.EqualTo("before " + key + " after"));
            Assert.That(history.Undo(), Is.True);
            Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
            Assert.That(history.CanUndo, Is.False);
        }
    }

    [TestCase('"')]
    [TestCase('`')]
    [TestCase('\'')]
    [TestCase('{')]
    [TestCase('}')]
    public void NoSelection_LeavesNormalTypingUnchanged(char key)
    {
        var (box, history) = CreateEditor("before after", 7, 0);
        var original = PromptUndoManager.CaptureSnapshot(box);

        Assert.That(Embed(box, history, key), Is.False);
        Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
        Assert.That(history.Count, Is.EqualTo(1));
        box.ReplaceSelection(key.ToString());
        Assert.That(box.GetPlainText(), Is.EqualTo("before " + key + "after"));
        Assert.That(history.Undo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
    }

    [Test]
    public void MultilineQuote_LeavesNormalReplacementUnchanged()
    {
        const string selected = "one\ntwo";
        var (box, history) = CreateEditor(selected, 0, selected.Length);
        var original = PromptUndoManager.CaptureSnapshot(box);

        Assert.That(Embed(box, history, '"'), Is.False);
        Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(original));
        Assert.That(history.Count, Is.EqualTo(1));
    }

    [TestCase('"')]
    [TestCase('`')]
    [TestCase('\'')]
    [TestCase('{')]
    [TestCase('}')]
    public void SubsequentTyping_DoesNotCoalesceAwayTheWrappedState(char key)
    {
        var (box, history) = CreateEditor("word", 0, 4);
        Assert.That(Embed(box, history, key), Is.True);
        var wrapped = PromptUndoManager.CaptureSnapshot(box);

        box.ReplaceSelection("x");
        Assert.That(history.Undo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(box), Is.EqualTo(wrapped));
        Assert.That(history.Undo(), Is.True);
        Assert.That(box.GetPlainText(), Is.EqualTo(key.ToString()));
        Assert.That(history.Undo(), Is.True);
        Assert.That(box.GetPlainText(), Is.EqualTo("word"));
    }

    private static bool Embed(RichTextBox box, PromptUndoHistory history, char key) => key switch
    {
        '"' => MarkdownEditorCommands.ApplyInlineQuote(box, history),
        '`' or '\'' => MarkdownEditorCommands.ApplyInlineCodeOrFence(box, history, key),
        '{' or '}' => MarkdownEditorCommands.ApplyInlineBraces(box, history, key),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static (RichTextBox Box, PromptUndoHistory History) CreateEditor(
        string text, int start, int length, bool backward = false)
    {
        var box = new RichTextBox { IsUndoEnabled = false };
        box.SetPlainText(text);
        var history = new PromptUndoHistory(box);
        history.CaptureBaseline();
        box.TextChanged += (_, _) => history.PushSnapshot();
        box.SelectionChanged += (_, _) => history.RefreshSelection();
        box.Selection.Select(
            box.GetTextPointerAt(backward ? start + length : start),
            box.GetTextPointerAt(backward ? start : start + length));
        return (box, history);
    }
}
