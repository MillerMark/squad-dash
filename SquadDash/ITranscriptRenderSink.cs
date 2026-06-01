using System.Collections.Generic;
using System.Windows.Media;

namespace SquadDash;

internal interface ITranscriptRenderSink {
    TranscriptTurnView BeginTranscriptTurn(string prompt);
    void FinalizeCurrentTurnResponse();
    void AppendLine(string text, Brush? brush);
    void SelectTranscriptThread(TranscriptThreadState thread);
    TranscriptThreadState CoordinatorThread { get; }
    TranscriptResponseEntry? LastQuickReplyEntry { get; }
    void ClearLastQuickReplyEntry();
    void RenderResponseEntry(TranscriptResponseEntry entry);
    void EnsureThreadFooterAtEnd(TranscriptThreadState thread);
    void ScrollToEndIfAtBottom();
    IEnumerable<ToolTranscriptEntry> GetToolEntries();
    void RenderToolEntry(ToolTranscriptEntry entry);
    void UpdateToolSpinnerState();
}
