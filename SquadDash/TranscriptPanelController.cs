using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Routes <see cref="ITranscriptRenderSink"/> calls from external callers
/// (e.g. <see cref="PromptExecutionController"/>) to the MainWindow transcript
/// rendering methods via injected delegates. MainWindow holds one instance and
/// passes it wherever an <see cref="ITranscriptRenderSink"/> is required.
/// </summary>
internal sealed class TranscriptPanelController : ITranscriptRenderSink
{
    private readonly Func<string, TranscriptTurnView> _beginTranscriptTurn;
    private readonly Action _finalizeCurrentTurnResponse;
    private readonly Action<string, Brush?> _appendLine;
    private readonly Action<TranscriptThreadState> _selectTranscriptThread;
    private readonly Func<TranscriptThreadState> _getCoordinatorThread;
    private readonly Func<TranscriptResponseEntry?> _getLastQuickReplyEntry;
    private readonly Action _clearLastQuickReplyEntry;
    private readonly Action<TranscriptResponseEntry> _renderResponseEntry;
    private readonly Action<TranscriptThreadState> _ensureThreadFooterAtEnd;
    private readonly Action _scrollToEndIfAtBottom;
    private readonly Func<IEnumerable<ToolTranscriptEntry>> _getToolEntries;
    private readonly Action<ToolTranscriptEntry> _renderToolEntry;
    private readonly Action _updateToolSpinnerState;

    internal TranscriptPanelController(
        Func<string, TranscriptTurnView> beginTranscriptTurn,
        Action finalizeCurrentTurnResponse,
        Action<string, Brush?> appendLine,
        Action<TranscriptThreadState> selectTranscriptThread,
        Func<TranscriptThreadState> getCoordinatorThread,
        Func<TranscriptResponseEntry?> getLastQuickReplyEntry,
        Action clearLastQuickReplyEntry,
        Action<TranscriptResponseEntry> renderResponseEntry,
        Action<TranscriptThreadState> ensureThreadFooterAtEnd,
        Action scrollToEndIfAtBottom,
        Func<IEnumerable<ToolTranscriptEntry>> getToolEntries,
        Action<ToolTranscriptEntry> renderToolEntry,
        Action updateToolSpinnerState)
    {
        ArgumentNullException.ThrowIfNull(beginTranscriptTurn);
        ArgumentNullException.ThrowIfNull(finalizeCurrentTurnResponse);
        ArgumentNullException.ThrowIfNull(appendLine);
        ArgumentNullException.ThrowIfNull(selectTranscriptThread);
        ArgumentNullException.ThrowIfNull(getCoordinatorThread);
        ArgumentNullException.ThrowIfNull(getLastQuickReplyEntry);
        ArgumentNullException.ThrowIfNull(clearLastQuickReplyEntry);
        ArgumentNullException.ThrowIfNull(renderResponseEntry);
        ArgumentNullException.ThrowIfNull(ensureThreadFooterAtEnd);
        ArgumentNullException.ThrowIfNull(scrollToEndIfAtBottom);
        ArgumentNullException.ThrowIfNull(getToolEntries);
        ArgumentNullException.ThrowIfNull(renderToolEntry);
        ArgumentNullException.ThrowIfNull(updateToolSpinnerState);

        _beginTranscriptTurn         = beginTranscriptTurn;
        _finalizeCurrentTurnResponse = finalizeCurrentTurnResponse;
        _appendLine                  = appendLine;
        _selectTranscriptThread      = selectTranscriptThread;
        _getCoordinatorThread        = getCoordinatorThread;
        _getLastQuickReplyEntry      = getLastQuickReplyEntry;
        _clearLastQuickReplyEntry    = clearLastQuickReplyEntry;
        _renderResponseEntry         = renderResponseEntry;
        _ensureThreadFooterAtEnd     = ensureThreadFooterAtEnd;
        _scrollToEndIfAtBottom       = scrollToEndIfAtBottom;
        _getToolEntries              = getToolEntries;
        _renderToolEntry             = renderToolEntry;
        _updateToolSpinnerState      = updateToolSpinnerState;
    }

    // ── ITranscriptRenderSink ─────────────────────────────────────────────
    TranscriptTurnView ITranscriptRenderSink.BeginTranscriptTurn(string prompt)        => _beginTranscriptTurn(prompt);
    void ITranscriptRenderSink.FinalizeCurrentTurnResponse()                           => _finalizeCurrentTurnResponse();
    void ITranscriptRenderSink.AppendLine(string text, Brush? brush)                  => _appendLine(text, brush);
    void ITranscriptRenderSink.SelectTranscriptThread(TranscriptThreadState thread)    => _selectTranscriptThread(thread);
    TranscriptThreadState ITranscriptRenderSink.CoordinatorThread                     => _getCoordinatorThread();
    TranscriptResponseEntry? ITranscriptRenderSink.LastQuickReplyEntry                => _getLastQuickReplyEntry();
    void ITranscriptRenderSink.ClearLastQuickReplyEntry()                              => _clearLastQuickReplyEntry();
    void ITranscriptRenderSink.RenderResponseEntry(TranscriptResponseEntry entry)     => _renderResponseEntry(entry);
    void ITranscriptRenderSink.EnsureThreadFooterAtEnd(TranscriptThreadState thread)  => _ensureThreadFooterAtEnd(thread);
    void ITranscriptRenderSink.ScrollToEndIfAtBottom()                                => _scrollToEndIfAtBottom();
    IEnumerable<ToolTranscriptEntry> ITranscriptRenderSink.GetToolEntries()           => _getToolEntries();
    void ITranscriptRenderSink.RenderToolEntry(ToolTranscriptEntry entry)             => _renderToolEntry(entry);
    void ITranscriptRenderSink.UpdateToolSpinnerState()                               => _updateToolSpinnerState();
}
