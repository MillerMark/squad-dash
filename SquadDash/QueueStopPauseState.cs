namespace SquadDash;

/// <summary>
/// Tracks whether the current queue pause was introduced by Stop so that the
/// pause can be released after the user's corrective response without
/// releasing a pause the user explicitly requested.
/// </summary>
internal sealed class QueueStopPauseState
{
    internal bool IsAwaitingResponse { get; private set; }

    internal void RecordStopPause(bool queueWasAlreadyPaused)
    {
        if (!queueWasAlreadyPaused)
            IsAwaitingResponse = true;
    }

    internal void RecordQueueResumed() => IsAwaitingResponse = false;

    internal bool ConsumeResponseResume(bool queueIsPausedOrPending)
    {
        var shouldResume = IsAwaitingResponse && queueIsPausedOrPending;
        IsAwaitingResponse = false;
        return shouldResume;
    }
}
