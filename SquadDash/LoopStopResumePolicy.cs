namespace SquadDash;

internal readonly record struct LoopStopResumeDecision(
    bool PreserveExecution,
    bool ResumeAfterQueue,
    bool PreserveForRestart);

/// <summary>
/// Separates a terminal user/host stop from the temporary stops used to drain the
/// queue or restart the process. A suppressed resume always wins, including when a
/// rebuild had already requested a restart. Terminal plan outcomes use the same
/// suppression as an explicit Stop click.
/// </summary>
internal static class LoopStopResumePolicy
{
    internal static LoopStopResumeDecision Resolve(
        bool resumeSuppressed,
        bool restartPending,
        bool interruptedByQueue,
        bool queueHasReadyItems,
        bool loopAlreadyQueued)
    {
        if (resumeSuppressed)
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
