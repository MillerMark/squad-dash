namespace SquadDash;

internal enum LoopStartupResumeAction
{
    None,
    KeepQueuedLoop,
    QueueLoopBehindRestoredQueue,
    PauseForShift,
    PauseForQuickReplies,
    StartImmediately
}

internal static class LoopStartupResumePolicy
{
    internal static LoopStartupResumeAction Resolve(
        ActiveLoopExecutionState? workspaceExecution,
        bool loopAlreadyQueued,
        bool queueHasReadyItems,
        bool startupShiftHeld,
        bool hasPendingQuickReplies)
    {
        // The execution envelope lives in the workspace conversation. A process-wide
        // application setting cannot safely identify which of several workspaces owns
        // a restart, and therefore must never authorize a resume by itself.
        if (workspaceExecution is null)
            return LoopStartupResumeAction.None;

        if (loopAlreadyQueued)
            return LoopStartupResumeAction.KeepQueuedLoop;

        if (startupShiftHeld)
            return LoopStartupResumeAction.PauseForShift;

        if (hasPendingQuickReplies)
            return LoopStartupResumeAction.PauseForQuickReplies;

        if (queueHasReadyItems)
            return LoopStartupResumeAction.QueueLoopBehindRestoredQueue;

        return LoopStartupResumeAction.StartImmediately;
    }
}
