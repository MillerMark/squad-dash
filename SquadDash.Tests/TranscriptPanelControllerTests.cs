using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptPanelControllerTests
{
    // ── no-op delegate defaults ───────────────────────────────────────────

    private static readonly Func<string, TranscriptTurnView>   NoOpBeginTurn        = _ => null!;
    private static readonly Action                              NoOpAction           = () => { };
    private static readonly Action<string, Brush?>             NoOpAppendLine       = (_, _) => { };
    private static readonly Action<TranscriptThreadState>      NoOpThreadAction     = _ => { };
    private static readonly Func<TranscriptThreadState>        NoOpGetThread        = () => null!;
    private static readonly Func<TranscriptResponseEntry?>     NoOpGetEntry         = () => null;
    private static readonly Action<TranscriptResponseEntry>    NoOpRenderEntry      = _ => { };
    private static readonly Func<IEnumerable<ToolTranscriptEntry>> NoOpGetTools     = () => [];
    private static readonly Action<ToolTranscriptEntry>        NoOpRenderTool       = _ => { };

    private static TranscriptPanelController BuildNoOp(
        Func<string, TranscriptTurnView>?       beginTranscriptTurn         = null,
        Action?                                  finalizeCurrentTurnResponse = null,
        Action<string, Brush?>?                 appendLine                  = null,
        Action<TranscriptThreadState>?          selectTranscriptThread      = null,
        Func<TranscriptThreadState>?            getCoordinatorThread        = null,
        Func<TranscriptResponseEntry?>?         getLastQuickReplyEntry      = null,
        Action?                                  clearLastQuickReplyEntry    = null,
        Action<TranscriptResponseEntry>?        renderResponseEntry         = null,
        Action<TranscriptThreadState>?          ensureThreadFooterAtEnd     = null,
        Action?                                  scrollToEndIfAtBottom       = null,
        Func<IEnumerable<ToolTranscriptEntry>>? getToolEntries              = null,
        Action<ToolTranscriptEntry>?            renderToolEntry             = null,
        Action?                                  updateToolSpinnerState      = null)
    {
        return new TranscriptPanelController(
            beginTranscriptTurn:         beginTranscriptTurn         ?? NoOpBeginTurn,
            finalizeCurrentTurnResponse: finalizeCurrentTurnResponse ?? NoOpAction,
            appendLine:                  appendLine                  ?? NoOpAppendLine,
            selectTranscriptThread:      selectTranscriptThread      ?? NoOpThreadAction,
            getCoordinatorThread:        getCoordinatorThread        ?? NoOpGetThread,
            getLastQuickReplyEntry:      getLastQuickReplyEntry      ?? NoOpGetEntry,
            clearLastQuickReplyEntry:    clearLastQuickReplyEntry    ?? NoOpAction,
            renderResponseEntry:         renderResponseEntry         ?? NoOpRenderEntry,
            ensureThreadFooterAtEnd:     ensureThreadFooterAtEnd     ?? NoOpThreadAction,
            scrollToEndIfAtBottom:       scrollToEndIfAtBottom       ?? NoOpAction,
            getToolEntries:              getToolEntries              ?? NoOpGetTools,
            renderToolEntry:             renderToolEntry             ?? NoOpRenderTool,
            updateToolSpinnerState:      updateToolSpinnerState      ?? NoOpAction);
    }

    /// <summary>
    /// Calls the constructor with all valid no-ops except the one slot that receives
    /// <paramref name="nullSlot"/>. Used to verify ArgumentNullException null guards.
    /// </summary>
    private static void AssertNullGuard(int nullSlot)
    {
        Assert.Throws<ArgumentNullException>(() => new TranscriptPanelController(
            beginTranscriptTurn:         nullSlot == 0  ? null! : NoOpBeginTurn,
            finalizeCurrentTurnResponse: nullSlot == 1  ? null! : NoOpAction,
            appendLine:                  nullSlot == 2  ? null! : NoOpAppendLine,
            selectTranscriptThread:      nullSlot == 3  ? null! : NoOpThreadAction,
            getCoordinatorThread:        nullSlot == 4  ? null! : NoOpGetThread,
            getLastQuickReplyEntry:      nullSlot == 5  ? null! : NoOpGetEntry,
            clearLastQuickReplyEntry:    nullSlot == 6  ? null! : NoOpAction,
            renderResponseEntry:         nullSlot == 7  ? null! : NoOpRenderEntry,
            ensureThreadFooterAtEnd:     nullSlot == 8  ? null! : NoOpThreadAction,
            scrollToEndIfAtBottom:       nullSlot == 9  ? null! : NoOpAction,
            getToolEntries:              nullSlot == 10 ? null! : NoOpGetTools,
            renderToolEntry:             nullSlot == 11 ? null! : NoOpRenderTool,
            updateToolSpinnerState:      nullSlot == 12 ? null! : NoOpAction));
    }

    // ── constructor null guards ────────────────────────────────────────────

    [Test] public void Constructor_NullBeginTranscriptTurn_Throws()        => AssertNullGuard(0);
    [Test] public void Constructor_NullFinalizeCurrentTurnResponse_Throws() => AssertNullGuard(1);
    [Test] public void Constructor_NullAppendLine_Throws()                 => AssertNullGuard(2);
    [Test] public void Constructor_NullSelectTranscriptThread_Throws()     => AssertNullGuard(3);
    [Test] public void Constructor_NullGetCoordinatorThread_Throws()       => AssertNullGuard(4);
    [Test] public void Constructor_NullGetLastQuickReplyEntry_Throws()     => AssertNullGuard(5);
    [Test] public void Constructor_NullClearLastQuickReplyEntry_Throws()   => AssertNullGuard(6);
    [Test] public void Constructor_NullRenderResponseEntry_Throws()        => AssertNullGuard(7);
    [Test] public void Constructor_NullEnsureThreadFooterAtEnd_Throws()    => AssertNullGuard(8);
    [Test] public void Constructor_NullScrollToEndIfAtBottom_Throws()      => AssertNullGuard(9);
    [Test] public void Constructor_NullGetToolEntries_Throws()             => AssertNullGuard(10);
    [Test] public void Constructor_NullRenderToolEntry_Throws()            => AssertNullGuard(11);
    [Test] public void Constructor_NullUpdateToolSpinnerState_Throws()     => AssertNullGuard(12);

    // ── delegate routing ──────────────────────────────────────────────────

    [Test]
    public void FinalizeCurrentTurnResponse_InvokesDelegateExactlyOnce()
    {
        int callCount = 0;
        ITranscriptRenderSink sink = BuildNoOp(finalizeCurrentTurnResponse: () => callCount++);

        sink.FinalizeCurrentTurnResponse();

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void AppendLine_ForwardsTextAndBrushToDelegate()
    {
        string? capturedText  = null;
        Brush?  capturedBrush = null;
        ITranscriptRenderSink sink = BuildNoOp(appendLine: (t, b) => { capturedText = t; capturedBrush = b; });

        var brush = Brushes.Red;
        sink.AppendLine("hello", brush);

        Assert.Multiple(() =>
        {
            Assert.That(capturedText,  Is.EqualTo("hello"));
            Assert.That(capturedBrush, Is.SameAs(brush));
        });
    }

    [Test]
    public void AppendLine_NullBrushIsForwardedToDelegate()
    {
        Brush? capturedBrush = Brushes.Blue; // start non-null
        ITranscriptRenderSink sink = BuildNoOp(appendLine: (_, b) => capturedBrush = b);

        sink.AppendLine("text", null);

        Assert.That(capturedBrush, Is.Null);
    }

    [Test]
    public void ScrollToEndIfAtBottom_InvokesDelegateExactlyOnce()
    {
        int callCount = 0;
        ITranscriptRenderSink sink = BuildNoOp(scrollToEndIfAtBottom: () => callCount++);

        sink.ScrollToEndIfAtBottom();

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void UpdateToolSpinnerState_InvokesDelegateExactlyOnce()
    {
        int callCount = 0;
        ITranscriptRenderSink sink = BuildNoOp(updateToolSpinnerState: () => callCount++);

        sink.UpdateToolSpinnerState();

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void ClearLastQuickReplyEntry_InvokesDelegateExactlyOnce()
    {
        int callCount = 0;
        ITranscriptRenderSink sink = BuildNoOp(clearLastQuickReplyEntry: () => callCount++);

        sink.ClearLastQuickReplyEntry();

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void LastQuickReplyEntry_ReturnsDelegateResult()
    {
        ITranscriptRenderSink sink = BuildNoOp(getLastQuickReplyEntry: () => null);

        Assert.That(sink.LastQuickReplyEntry, Is.Null);
    }

    [Test]
    public void GetToolEntries_ReturnsDelegateResult()
    {
        var entries = new List<ToolTranscriptEntry>();
        ITranscriptRenderSink sink = BuildNoOp(getToolEntries: () => entries);

        Assert.That(sink.GetToolEntries(), Is.SameAs(entries));
    }

    [Test]
    public void GetToolEntries_CalledMultipleTimes_DelegateFreshEachTime()
    {
        int callCount = 0;
        ITranscriptRenderSink sink = BuildNoOp(getToolEntries: () => { callCount++; return []; });

        sink.GetToolEntries();
        sink.GetToolEntries();

        Assert.That(callCount, Is.EqualTo(2));
    }
}

