using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

// Content used across tests: "First paragraph\nMiddle paragraph\nThird paragraph"
//
// Plain-text offsets (each '\n' counts as 1 character, matching GetTextPointerAt):
//   Para 1 "First paragraph"  → [0, 14]  (15 chars)
//   '\n' separator             → offset 15
//   Para 2 "Middle paragraph" → [16, 31] (16 chars)
//   '\n' separator             → offset 32
//   Para 3 "Third paragraph"  → [33, 47] (15 chars)

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class EditorRevisionLockTests
{
    private const string ThreeParagraphs = "First paragraph\nMiddle paragraph\nThird paragraph";
    private const int Para2Start = 16; // first char of "Middle paragraph"
    private const int Para2End   = 32; // ContentEnd of "Middle paragraph" run (after 'h')

    // Local replica of the private production method, enabling white-box coverage of its logic.
    private static bool IsCaretInLockedRange(RichTextBox tb, MarkdownDocumentTabState state)
    {
        var selStart = tb.Selection.Start;
        var selEnd   = tb.Selection.End;
        foreach (var range in state.LockedRanges)
        {
            if (selStart.CompareTo(range.End) <= 0 && selEnd.CompareTo(range.Start) >= 0)
                return true;
        }
        return false;
    }

    /// Returns a fresh MarkdownDocumentTabState loaded with ThreeParagraphs.
    /// Must be called from within a WpfTestContext.Run block.
    private static MarkdownDocumentTabState SetupState()
    {
        var state = MarkdownDocumentTabState.Load("test-tab", "nonexistent_test_path.md");
        state.EditorTextBox.SetPlainText(ThreeParagraphs);
        return state;
    }

    // ── Lock lifecycle ───────────────────────────────────────────────────────

    [Test]
    public void AddRevisionLock_SetsHasLockedRanges_AndOneEntry()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;

            Assert.That(state.HasLockedRanges, Is.False, "no locks initially");

            var lockStart = rtb.GetTextPointerAt(Para2Start);
            var lockEnd   = rtb.GetTextPointerAt(Para2End);
            state.AddRevisionLock(lockStart, lockEnd);

            Assert.That(state.HasLockedRanges, Is.True);
            Assert.That(state.LockedRanges.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void RemoveRevisionLock_ClearsHasLockedRanges_AndEmptyList()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;

            var revLock = state.AddRevisionLock(
                rtb.GetTextPointerAt(Para2Start),
                rtb.GetTextPointerAt(Para2End));

            state.RemoveRevisionLock(revLock);

            Assert.That(state.HasLockedRanges, Is.False);
            Assert.That(state.LockedRanges, Is.Empty);
        });
    }

    [Test]
    public void MultipleRevisionLocks_CanCoexist_AndAreRemovedIndependently()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;

            var lock1 = state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));
            var lock2 = state.AddRevisionLock(rtb.GetTextPointerAt(0),          rtb.GetTextPointerAt(5));

            Assert.That(state.HasLockedRanges, Is.True);
            Assert.That(state.LockedRanges.Count, Is.EqualTo(2));

            state.RemoveRevisionLock(lock1);
            Assert.That(state.LockedRanges.Count, Is.EqualTo(1));

            state.RemoveRevisionLock(lock2);
            Assert.That(state.HasLockedRanges, Is.False);
            Assert.That(state.LockedRanges, Is.Empty);
        });
    }

    // ── IsCaretInLockedRange: middle paragraph locked ────────────────────────

    [Test]
    public void IsCaretInLockedRange_ReturnsTrue_WhenCaretInsideLockedParagraph()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            rtb.SelectRange(20, 0); // caret at offset 20 — inside "Middle paragraph" ('e' of "Middle")

            Assert.That(IsCaretInLockedRange(rtb, state), Is.True);
        });
    }

    [Test]
    public void IsCaretInLockedRange_ReturnsFalse_WhenCaretInFirstParagraph()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            rtb.SelectRange(5, 0); // caret at offset 5 — inside "First paragraph"

            Assert.That(IsCaretInLockedRange(rtb, state), Is.False);
        });
    }

    [Test]
    public void IsCaretInLockedRange_ReturnsFalse_WhenCaretInThirdParagraph()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            rtb.SelectRange(35, 0); // caret at offset 35 — inside "Third paragraph"

            Assert.That(IsCaretInLockedRange(rtb, state), Is.False);
        });
    }

    [Test]
    public void IsCaretInLockedRange_ReturnsTrue_WhenSelectionOverlapsLockFromFirstParagraph()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            // Selection from offset 10 (in para 1) to 28 (inside para 2) — partial overlap
            rtb.SelectRange(10, 18);

            Assert.That(IsCaretInLockedRange(rtb, state), Is.True);
        });
    }

    [Test]
    public void IsCaretInLockedRange_ReturnsFalse_WhenSelectionEntirelyInFirstParagraph()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            rtb.SelectRange(0, 10); // selection entirely within "First paragraph"

            Assert.That(IsCaretInLockedRange(rtb, state), Is.False);
        });
    }

    // ── Non-locked paragraphs while lock is active ───────────────────────────

    [Test]
    public void CaretInParagraph1_IsNotInLockedRange_WhileMiddleIsLocked()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            rtb.SelectRange(3, 0); // caret well inside para 1
            Assert.That(IsCaretInLockedRange(rtb, state), Is.False,
                "Para 1 edits should be permitted while para 2 is locked");
        });
    }

    [Test]
    public void CaretInParagraph3_IsNotInLockedRange_WhileMiddleIsLocked()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;
            state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            rtb.SelectRange(40, 0); // caret well inside para 3
            Assert.That(IsCaretInLockedRange(rtb, state), Is.False,
                "Para 3 edits should be permitted while para 2 is locked");
        });
    }

    [Test]
    public void LockPointers_TrackMiddleParagraph_AfterEditToParagraph1()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;

            var revLock = state.AddRevisionLock(
                rtb.GetTextPointerAt(Para2Start),
                rtb.GetTextPointerAt(Para2End));

            // Replace "First" (5 chars) with "BeginningLong" (13 chars) in para 1
            rtb.SelectRange(0, 5);
            rtb.ReplaceSelection("BeginningLong");

            // The lock's TextPointers are live; they should still span "Middle paragraph"
            var lockedText = new TextRange(revLock.Start, revLock.End).Text.Replace("\r\n", "\n");
            Assert.That(lockedText, Is.EqualTo("Middle paragraph"),
                "WPF TextPointers track with document edits; the lock must follow the moved content");

            // HasLockedRanges must not change as a result of document edits
            Assert.That(state.HasLockedRanges, Is.True);
        });
    }

    // ── Lock removal and editability ─────────────────────────────────────────

    [Test]
    public void AfterRemoveRevisionLock_IsCaretInLockedRange_ReturnsFalse_ForFormerlyLockedRegion()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;

            var revLock = state.AddRevisionLock(
                rtb.GetTextPointerAt(Para2Start),
                rtb.GetTextPointerAt(Para2End));

            state.RemoveRevisionLock(revLock);

            rtb.SelectRange(20, 0); // caret still in the formerly-locked region
            Assert.That(IsCaretInLockedRange(rtb, state), Is.False,
                "removed lock must no longer block caret in that region");
        });
    }

    [Test]
    public void AfterRemoveRevisionLock_HasLockedRanges_IsFalse()
    {
        WpfTestContext.Run(() =>
        {
            var state   = SetupState();
            var rtb     = state.EditorTextBox;
            var revLock = state.AddRevisionLock(rtb.GetTextPointerAt(Para2Start), rtb.GetTextPointerAt(Para2End));

            state.RemoveRevisionLock(revLock);

            Assert.That(state.HasLockedRanges, Is.False);
        });
    }

    // ── Simulated AI replacement of the locked paragraph ────────────────────

    [Test]
    public void SimulatedAiReplacement_UpdatesTextInLockedRegion_AndClearsLock()
    {
        WpfTestContext.Run(() =>
        {
            var state = SetupState();
            var rtb   = state.EditorTextBox;

            var lockStart = rtb.GetTextPointerAt(Para2Start);
            var lockEnd   = rtb.GetTextPointerAt(Para2End);
            var revLock   = state.AddRevisionLock(lockStart, lockEnd);

            // Simulate: AI returned — remove lock, then overwrite the formerly-locked paragraph
            state.RemoveRevisionLock(revLock);
            Assert.That(state.HasLockedRanges, Is.False, "lock cleared before applying AI output");

            new TextRange(lockStart, lockEnd).Text = "AI revised text";

            var fullText = rtb.GetPlainText();
            Assert.That(fullText, Does.Contain("AI revised text"),
                "AI output must appear in the document after replacement");
            Assert.That(fullText, Does.Not.Contain("Middle paragraph"),
                "original locked text must be gone after AI replacement");
        });
    }
}
