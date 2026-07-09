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
        bool loopActiveOnExit,
        bool loopAlreadyQueued,
        bool queueHasReadyItems,
        bool startupShiftHeld,
        bool hasPendingQuickReplies)
    {
        if (!loopActiveOnExit)
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
