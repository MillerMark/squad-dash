namespace SquadDash;

using System;

/// <summary>
/// Owns the prompt-queue instance and its sequence counter.
/// MainWindow holds a reference to this coordinator and delegates all queue
/// state through it, eliminating direct queue ownership from the window class.
/// </summary>
internal sealed class PromptQueueCoordinator
{
    private int _promptQueueSeq;

    /// <summary>The underlying prompt queue managed by this coordinator.</summary>
    public PromptQueue Queue { get; }

    /// <summary>
    /// Raised when a "branch-indicator" queue item is removed and the branch
    /// indicator UI should be refreshed.  The subscriber is responsible for
    /// marshalling to the UI thread.
    /// </summary>
    public event Action? BranchIndicatorUpdateRequested;

    public PromptQueueCoordinator(PromptQueue promptQueue)
    {
        Queue = promptQueue ?? throw new ArgumentNullException(nameof(promptQueue));
        Queue.ItemRemoved += OnQueueItemRemoved;
    }

    // ── Sequence-number helpers ───────────────────────────────────────────────

    /// <summary>Increments the sequence counter and returns the new value.</summary>
    public int NextSequenceNumber() => ++_promptQueueSeq;

    /// <summary>Resets the sequence counter to zero (used when restoring a saved queue).</summary>
    public void ResetSequenceNumber() => _promptQueueSeq = 0;

    // ── Internal queue event handling ─────────────────────────────────────────

    private void OnQueueItemRemoved(PromptQueueItem item)
    {
        if (item.SourceTag == "branch-indicator")
            BranchIndicatorUpdateRequested?.Invoke();
    }
}
