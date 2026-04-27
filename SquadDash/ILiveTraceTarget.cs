namespace SquadDash;

/// <summary>
/// Sink for trace entries. Set on <see cref="TranscriptScrollController.TraceTarget"/> and
/// <see cref="SquadDashTrace.TraceTarget"/> while the <see cref="TraceWindow"/> is open;
/// null (cleared) when the window closes so all trace calls become zero-overhead no-ops.
/// </summary>
internal interface ILiveTraceTarget
{
    void AddEntry(TraceCategory category, string detail);
}
