using ICSharpCode.AvalonEdit.Document;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AvalonEditRevisionSelectionTests
{
    [Test]
    public void RevisionTracksAdjacentEditsAndNormalizesReplacementWithoutLosingUndo()
    {
        var document = new TextDocument("before selected after");
        var selection = new AvalonEditRevisionSelection(document, 7, 8);
        document.Insert(7, "prefix ");
        document.Insert(22, " suffix");
        Assert.That(selection.TryReplace("new\r\ntext"), Is.True);
        Assert.That(document.Text, Is.EqualTo("before prefix new\ntext suffix after"));
        document.UndoStack.Undo();
        Assert.That(document.Text, Is.EqualTo("before prefix selected suffix after"));
        document.UndoStack.Undo();
        document.UndoStack.Undo();
        Assert.That(document.Text, Is.EqualTo("before selected after"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChangedOrDeletedSelectionCannotOverwriteOtherText(bool delete)
    {
        var document = new TextDocument("before selected after");
        var selection = new AvalonEditRevisionSelection(document, 7, 8);
        document.Replace(7, 8, delete ? "" : "changed");
        var beforeRevision = document.Text;
        Assert.That(selection.TryReplace("AI result"), Is.False);
        Assert.That(document.Text, Is.EqualTo(beforeRevision));
    }

    [Test]
    public void NoOpRevisionDoesNotAddUndoHistory()
    {
        var document = new TextDocument("original\n");
        var selection = new AvalonEditRevisionSelection(document, 0, document.TextLength);
        Assert.That(selection.TryReplace("original\r\n"), Is.True);
        Assert.That(document.UndoStack.CanUndo, Is.False);
    }

    [TestCase("new\n\n")]
    [TestCase("new\r\n\r\n")]
    public void WholeDocumentRevisionPreservesTrailingNewlines(string revised)
    {
        var document = new TextDocument("original\n");
        var selection = new AvalonEditRevisionSelection(document, 0, document.TextLength);
        Assert.That(selection.TryReplace(revised), Is.True);
        Assert.That(document.Text, Is.EqualTo("new\n\n"));
        document.UndoStack.Undo();
        Assert.That(document.Text, Is.EqualTo("original\n"));
    }
}
