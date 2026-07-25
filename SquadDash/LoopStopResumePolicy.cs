namespace SquadDash;

internal readonly record struct LoopStopResumeDecision(
    bool PreserveExecution,
    bool ResumeAfterQueue,
    bool PreserveForRestart);

/// <summary>
/// Separates an explicit user/host stop from the temporary stops used to drain the
/// queue or restart the process. An explicit stop always wins, including when a
/// rebuild had already requested a restart.
/// </summary>
internal static class LoopStopResumePolicy
{
    internal static LoopStopResumeDecision Resolve(
        bool explicitStop,
        bool restartPending,
        bool interruptedByQueue,
        bool queueHasReadyItems,
        bool loopAlreadyQueued)
    {
        if (explicitStop)
            return new(false, false, false);

        var resumeAfterQueue =
            interruptedByQueue || queueHasReadyItems || loopAlreadyQueued;
        var preserveForRestart = restartPending;
        return new(
            resumeAfterQueue || preserveForRestart,
            resumeAfterQueue,
            preserveForRestart);
    }
}
