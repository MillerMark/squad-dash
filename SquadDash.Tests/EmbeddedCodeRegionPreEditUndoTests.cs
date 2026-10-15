using System;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using ICSharpCode.AvalonEdit;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
internal sealed class EmbeddedCodeRegionPreEditUndoTests
{
    private const string MixedDocument = "Hello\n```python\nx=1\n```\nWorld";

    private sealed class LiveHistory : IDisposable
    {
        public RichTextBox Host { get; } = new() { IsUndoEnabled = false };
        public PromptUndoHistory History { get; }
        public bool Suppress { get; set; }

        public LiveHistory()
        {
            EmbeddedCodeRegionBlock.ClearActiveRegion();
            Host.SetPlainText(MixedDocument);
            EmbeddedCodeRegionManager.ConvertAllFences(Host);
            Host.SetCaretOffset(0);
            History = new PromptUndoHistory(Host);
            History.CaptureBaseline();
            Host.SelectionChanged += (_, _) =>
            {
                if (!Suppress)
                    History.RefreshSelection();
            };
            Host.TextChanged += (_, _) =>
            {
                if (!Suppress)
                    History.PushSnapshot();
            };
            EmbeddedCodeRegionBlock.AnyRegionContentEditing += BeforeRegionEdit;
            EmbeddedCodeRegionBlock.AnyRegionContentEdited += AfterRegionEdit;
        }

        private void BeforeRegionEdit(object? sender, EventArgs e)
        {
            if (!Suppress && sender is EmbeddedCodeRegionBlock region)
                History.RefreshSelection(region);
        }

        private void AfterRegionEdit(object? sender, EventArgs e)
        {
            if (!Suppress && sender is EmbeddedCodeRegionBlock region)
                History.PushSnapshot(region);
        }

        public EmbeddedCodeRegionBlock Region => Host.Document.Blocks.OfType<BlockUIContainer>()
            .Select(c => c.Child).OfType<EmbeddedCodeRegionBlock>().Single();

        public void Dispose()
        {
            EmbeddedCodeRegionBlock.AnyRegionContentEditing -= BeforeRegionEdit;
            EmbeddedCodeRegionBlock.AnyRegionContentEdited -= AfterRegionEdit;
            EmbeddedCodeRegionBlock.ClearActiveRegion();
        }
    }

    [TestCase(1, 4)]
    [TestCase(4, 1)]
    [TestCase(3, 3)]
    public void FirstHostEdit_UndoRestoresLivePreEditSelection_AndRedoRestoresReplacement(int anchor, int caret)
    {
        using var live = new LiveHistory();
        live.Host.Selection.Select(live.Host.GetTextPointerAt(anchor), live.Host.GetTextPointerAt(caret));
        Assert.That(live.History.Count, Is.EqualTo(1), "selection movement must not append an undo entry");

        live.Host.Selection.Text = "Q";
        var edited = PromptUndoManager.CaptureSnapshot(live.Host);
        Assert.That(live.History.Count, Is.EqualTo(2));

        Assert.That(live.History.Undo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host),
            Is.EqualTo(new PromptSnapshot(MixedDocument, caret, anchor)));
        Assert.That(live.Host.Selection.Text, Is.EqualTo(anchor == caret ? "" : "ell"));
        Assert.That(live.History.Count, Is.EqualTo(2), "restore callbacks must not record edits");

        Assert.That(live.History.Redo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(edited));
    }

    [TestCase(0, 3, false)]
    [TestCase(3, 0, false)]
    [TestCase(1, 1, false)]
    [TestCase(0, 3, true)]
    [TestCase(3, 0, true)]
    public void FirstRegionEdit_UndoRestoresPreEditSelection_WithoutKeyboardFocus(int anchor, int caret, bool nativeEdit)
    {
        using var live = new LiveHistory();
        var region = live.Region;
        region.RestoreContentSelection(anchor, caret);
        var expected = PromptUndoManager.CaptureSnapshot(live.Host, region);
        Assert.That(expected.CaretOffset, Is.GreaterThan(0));

        if (nativeEdit)
        {
            var editor = GetEditor(region);
            editor.TextArea.Selection.ReplaceSelectionWithText("Q");
        }
        else
        {
            // The same path used by redirected input and voice dictation, without OS focus.
            region.InsertHostText("Q");
        }
        var edited = PromptUndoManager.CaptureSnapshot(live.Host, region);
        Assert.That(live.History.Count, Is.EqualTo(2));
        Assert.That(live.History.Undo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host, live.Region), Is.EqualTo(expected));
        Assert.That(live.Region.GetContentSelectionLocal(), Is.EqualTo((anchor, caret)));
        Assert.That(live.Host.Selection.IsEmpty, Is.True);
        Assert.That(live.History.Redo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host, live.Region), Is.EqualTo(edited));
    }

    [Test]
    public void HostTyping_WithSelectionNotifications_StillCoalesces()
    {
        using var live = new LiveHistory();
        live.Host.SetCaretOffset(3);
        live.Host.Selection.Text = "a";
        live.Host.Selection.Text = "b";
        Assert.That(live.History.Count, Is.EqualTo(2));
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(new PromptSnapshot(MixedDocument, 3, 3)));
    }

    [Test]
    public void RegionTyping_WithPreEditNotifications_StillCoalesces()
    {
        using var live = new LiveHistory();
        live.Region.RestoreContentSelection(1, 1);
        var expected = PromptUndoManager.CaptureSnapshot(live.Host, live.Region);
        live.Region.InsertHostText("a");
        live.Region.InsertHostText("b");
        Assert.That(live.History.Count, Is.EqualTo(2));
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host, live.Region), Is.EqualTo(expected));
    }

    [Test]
    public void NativeRegionTyping_StillCoalesces()
    {
        using var live = new LiveHistory();
        live.Region.RestoreContentSelection(1, 1);
        var expected = PromptUndoManager.CaptureSnapshot(live.Host, live.Region);
        var editor = GetEditor(live.Region);
        editor.TextArea.Selection.ReplaceSelectionWithText("a");
        editor.TextArea.Selection.ReplaceSelectionWithText("b");
        Assert.That(live.History.Count, Is.EqualTo(2));
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host, live.Region), Is.EqualTo(expected));
    }

    [Test]
    public void RegionClosingFence_RedoPreservesHostExitCaret()
    {
        using var live = new LiveHistory();
        live.Region.RestoreContentSelection(3, 3);
        var expected = PromptUndoManager.CaptureSnapshot(live.Host, live.Region);
        GetEditor(live.Region).TextArea.Selection.ReplaceSelectionWithText("a\n```");
        Assert.That(EmbeddedCodeRegionBlock.ActiveRegion, Is.Null);
        var edited = PromptUndoManager.CaptureSnapshot(live.Host);
        Assert.That(live.History.Undo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host, live.Region), Is.EqualTo(expected));
        Assert.That(live.History.Redo(), Is.True);
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(edited));
    }

    [TestCase(1, 4)]
    [TestCase(4, 1)]
    [TestCase(3, 3)]
    public void HostEditAfterRegionExit_CapturesNewHostSelection(int anchor, int caret)
    {
        using var live = new LiveHistory();
        live.Region.RestoreContentSelection(1, 1);
        live.Region.InsertHostText("a");
        // MainWindow's host mouse-down route clears the active region and ends the typing burst.
        live.History.CloseCoalescingWindow();
        EmbeddedCodeRegionBlock.ClearActiveRegion();
        live.Host.Selection.Select(live.Host.GetTextPointerAt(anchor), live.Host.GetTextPointerAt(caret));
        var expected = PromptUndoManager.CaptureSnapshot(live.Host);
        live.Host.Selection.Text = "Q";
        Assert.That(live.History.Count, Is.EqualTo(3));
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(expected));
    }

    [Test]
    public void ReplacementAfterTyping_StartsNewStep_AndPreservesItsSelection()
    {
        using var live = new LiveHistory();
        live.Host.SetCaretOffset(3);
        live.Host.Selection.Text = "a";
        live.Host.Selection.Select(live.Host.GetTextPointerAt(1), live.Host.GetTextPointerAt(4));
        var expected = PromptUndoManager.CaptureSnapshot(live.Host);
        live.Host.Selection.Text = "Q";
        Assert.That(live.History.Count, Is.EqualTo(3));
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(expected));
    }

    [Test]
    public void SelectionMoveAfterUndo_PreservesRedo_UntilTextChanges()
    {
        using var live = new LiveHistory();
        live.Host.Selection.Text = "Q";
        live.History.Undo();
        live.Host.Selection.Select(live.Host.GetTextPointerAt(4), live.Host.GetTextPointerAt(1));
        Assert.That(live.History.CanRedo, Is.True);
        live.Host.Selection.Text = "R";
        Assert.That(live.History.CanRedo, Is.False);
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(new PromptSnapshot(MixedDocument, 1, 4)));
    }

    [Test]
    public void SelectionRefresh_DoesNotCommitUnrecordedText()
    {
        using var live = new LiveHistory();
        live.Suppress = true;
        live.Host.Selection.Text = "changed";
        live.Host.SetCaretOffset(2);
        live.History.RefreshSelection();
        Assert.That(live.History.Count, Is.EqualTo(1));
        live.Suppress = false;
        live.History.PushSnapshot();
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(new PromptSnapshot(MixedDocument, 0, 0)));
    }

    [Test]
    public void RegionNotifications_FromAnotherPrompt_DoNotChangeHistory()
    {
        using var live = new LiveHistory();
        using var other = new LiveHistory();
        other.Region.RestoreContentSelection(0, 3);
        other.Region.InsertHostText("Q");
        Assert.That(live.History.Count, Is.EqualTo(1));
        Assert.That(other.History.Count, Is.EqualTo(2));
    }

    [Test]
    public void Clear_Rebaselines_AndNextReplacementUsesNewSelection()
    {
        using var live = new LiveHistory();
        live.Host.Selection.Text = "Q";
        live.History.Clear();
        Assert.That(live.History.CanUndo, Is.False);
        live.Host.Selection.Select(live.Host.GetTextPointerAt(1), live.Host.GetTextPointerAt(3));
        var expected = PromptUndoManager.CaptureSnapshot(live.Host);
        live.Host.Selection.Text = "R";
        live.History.Undo();
        Assert.That(PromptUndoManager.CaptureSnapshot(live.Host), Is.EqualTo(expected));
        Assert.That(live.History.CanUndo, Is.False);
    }

    private static TextEditor GetEditor(EmbeddedCodeRegionBlock region)
        => (TextEditor)((Border)((StackPanel)region.Child).Children[1]).Child;
}
