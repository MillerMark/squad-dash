using System;
using System.Collections.Generic;
using System.Windows.Documents;

namespace SquadDash;

internal enum PromptHintFeature {
    EnterSend,
    ShiftEnterNewline,
    CtrlArrowHistory,
    PushToTalk,
    CtrlEnterSteer,
    CtrlShiftEnterPrioritize,
    CtrlQAddQueue,
    CtrlTabNavigation
}

internal enum PromptInputKey {
    Other,
    Enter,
    Up,
    Down,
    Tab,
    Escape,
    Q
}

internal enum PromptInputAction {
    None,
    SubmitPrompt,
    SteerPrompt,
    PrioritizeInQueue,
    NavigateHistoryPrevious,
    NavigateHistoryNext,
    IntelliSenseUp,
    IntelliSenseDown,
    IntelliSenseAccept,
    IntelliSenseDismiss,
    AddQueueSlot
}

internal sealed record PromptHistoryEntry(
    string Text,
    IReadOnlyList<FollowUpAttachment> Attachments,
    bool IsDictated = false);

internal sealed record PromptHistoryNavigationResult(
    bool Changed,
    string Text,
    IReadOnlyList<FollowUpAttachment>? Attachments,
    int? HistoryIndex,
    string? HistoryDraft,
    IReadOnlyList<FollowUpAttachment>? HistoryDraftAttachments,
    bool IsDictated = false);

internal sealed record InteractiveControlState(
    bool AgentItemsEnabled,
    bool OutputEnabled,
    bool CopyEnabled,
    bool PromptEnabled,
    bool RunEnabled,
    bool AbortEnabled,
    bool RunDoctorEnabled,
    bool InstallSquadEnabled);

/// <summary>
/// Result of the transcript prompt-navigation decision math. Indices are into the
/// same prompt list that was passed to <see cref="PromptScrollNavMath.Compute"/>.
/// </summary>
internal readonly record struct PromptScrollNavState(int NearestAboveIdx, int NearestBelowIdx) {
    public bool CanGoUp => NearestAboveIdx >= 0;
    public bool CanGoDown => NearestBelowIdx >= 0;
}

/// <summary>
/// Pure decision math for the transcript prompt-navigation (↑/↓) buttons.
/// Separated from the live WPF <c>ScrollViewer</c> geometry in <c>MainWindow</c> so the
/// above/below selection can be unit-tested with plain numbers.
/// </summary>
internal static class PromptScrollNavMath {
    public const double DefaultDeadZone = 50.0;

    // Smallest offset delta we still treat as "real" downward movement. Guards against
    // floating-point noise when the clamped target offset essentially equals the
    // current viewport top.
    private const double MovementEpsilon = 0.5;

    /// <summary>
    /// Computes the nearest prompt above and below the current viewport top.
    /// </summary>
    /// <param name="promptAbsYs">
    ///   Absolute document-space Y of each prompt paragraph's top. Entries that are
    ///   <see cref="double.NaN"/> (geometry not currently measurable) are skipped.
    /// </param>
    /// <param name="viewportTop">Current vertical scroll offset.</param>
    /// <param name="scrollableMax">Maximum scroll offset (ExtentHeight − ViewportHeight).</param>
    /// <param name="deadZone">Half-height band around the viewport top treated as "here".</param>
    public static PromptScrollNavState Compute(
        IReadOnlyList<double> promptAbsYs,
        double viewportTop,
        double scrollableMax,
        double deadZone = DefaultDeadZone) {
        int nearestAboveIdx = -1;
        int nearestBelowIdx = -1;
        double bestAboveY = double.MinValue;
        double bestBelowY = double.MaxValue;

        for (int i = 0; i < promptAbsYs.Count; i++) {
            double absY = promptAbsYs[i];
            if (double.IsNaN(absY)) continue;

            if (absY < viewportTop - deadZone) {
                // Above the viewport top — candidate for ↑ (go back). Prefer the one
                // closest to the viewport top (largest absolute Y).
                if (absY > bestAboveY) {
                    bestAboveY = absY;
                    nearestAboveIdx = i;
                }
            } else if (absY > viewportTop + deadZone) {
                // Below the viewport top — candidate for ↓ (go forward), but only when
                // scrolling toward it actually moves the document down. The offset that
                // brings the prompt to the viewport top equals its absolute Y clamped to
                // the scrollable range; if that clamped offset is still greater than the
                // current viewport top the move is meaningful. This intentionally keeps
                // ↓ enabled for prompts in the final viewport-height (clamped to the
                // document bottom) — scrolling to the bottom is legitimate downward
                // navigation — while disabling it only when already pinned at the bottom.
                double targetOffset = Math.Clamp(absY, 0.0, scrollableMax);
                if (targetOffset > viewportTop + MovementEpsilon && absY < bestBelowY) {
                    bestBelowY = absY;
                    nearestBelowIdx = i;
                }
            }
        }

        return new PromptScrollNavState(nearestAboveIdx, nearestBelowIdx);
    }
}

/// <summary>
/// Pure insertion-index math for registering a steering prompt into the ordered
/// <c>PromptParagraphs</c> list. Steering entries are rendered inside a turn's narrative
/// <c>Section</c> (not as a top-level document block), so they must be inserted at the
/// position that keeps the list sorted by document order rather than blindly appended —
/// otherwise Shift+click "latest", index-based fallbacks, and the batch-reorder on reload
/// would see the list out of visual order.
/// </summary>
internal static class PromptRegistrationMath {
    /// <summary>
    /// Returns the index at which to insert an entry whose document-order position is
    /// <paramref name="newPosition"/> so that <paramref name="existingPositions"/> (assumed
    /// ascending in document order) stays sorted. Among entries at an equal position the new
    /// entry is placed last, matching "a steering entry occurs after the prompt that started
    /// its turn".
    /// </summary>
    public static int ResolveInsertionIndex(IReadOnlyList<int> existingPositions, int newPosition) {
        int index = existingPositions.Count;
        while (index > 0 && existingPositions[index - 1] > newPosition)
            index--;
        return index;
    }

    /// <summary>
    /// Resolves the <see cref="ResolveInsertionIndex(IReadOnlyList{int}, int)"/> insertion
    /// point for <paramref name="newParagraph"/> relative to <paramref name="existingParagraphs"/>
    /// using live document-symbol offsets. Works for paragraphs nested inside a
    /// <see cref="Section"/> as well as top-level blocks, because
    /// <see cref="TextPointer.GetOffsetToPosition"/> is monotonic in document order.
    /// </summary>
    public static int ResolveInsertionIndex(
        TextPointer documentStart,
        Paragraph newParagraph,
        IReadOnlyList<Paragraph> existingParagraphs) {
        ArgumentNullException.ThrowIfNull(documentStart);
        ArgumentNullException.ThrowIfNull(newParagraph);
        ArgumentNullException.ThrowIfNull(existingParagraphs);

        var newOffset = documentStart.GetOffsetToPosition(newParagraph.ContentStart);
        var offsets = new int[existingParagraphs.Count];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = documentStart.GetOffsetToPosition(existingParagraphs[i].ContentStart);
        return ResolveInsertionIndex(offsets, newOffset);
    }
}

internal static class PromptInputBehavior {
    public static PromptInputAction ResolveAction(
        PromptInputKey key,
        bool ctrlPressed,
        bool shiftPressed,
        bool runButtonEnabled,
        bool isMultiLinePrompt) {
        if (key == PromptInputKey.Enter && ctrlPressed && shiftPressed)
            return PromptInputAction.PrioritizeInQueue;

        if (key == PromptInputKey.Enter && ctrlPressed)
            return PromptInputAction.SteerPrompt;

        if (key == PromptInputKey.Enter && !ctrlPressed && !shiftPressed && runButtonEnabled)
            return PromptInputAction.SubmitPrompt;

        if (ctrlPressed && key == PromptInputKey.Up)
            return PromptInputAction.NavigateHistoryPrevious;

        if (ctrlPressed && key == PromptInputKey.Down)
            return PromptInputAction.NavigateHistoryNext;

        if (ctrlPressed && key == PromptInputKey.Q)
            return PromptInputAction.AddQueueSlot;

        return PromptInputAction.None;
    }

    public static PromptInputAction ResolveAction(
        PromptInputKey key,
        bool ctrlPressed,
        bool shiftPressed,
        bool runButtonEnabled,
        bool isMultiLinePrompt,
        bool isIntelliSenseOpen) {
        if (isIntelliSenseOpen) {
            // Escape always dismisses (check before other handlers)
            if (key == PromptInputKey.Escape)
                return PromptInputAction.IntelliSenseDismiss;

            // Tab always accepts
            if (key == PromptInputKey.Tab)
                return PromptInputAction.IntelliSenseAccept;

            // Up/Down navigate IntelliSense (non-Ctrl)
            if (!ctrlPressed && key == PromptInputKey.Up)
                return PromptInputAction.IntelliSenseUp;

            if (!ctrlPressed && key == PromptInputKey.Down)
                return PromptInputAction.IntelliSenseDown;

            // Enter (no modifiers) accepts
            if (key == PromptInputKey.Enter && !ctrlPressed && !shiftPressed && runButtonEnabled)
                return PromptInputAction.IntelliSenseAccept;
        }

        // Fall through to existing behavior (Tab → None when not in IntelliSense)
        return ResolveAction(key, ctrlPressed, shiftPressed, runButtonEnabled, isMultiLinePrompt);
    }
}

internal static class PromptHistoryNavigator {
    public static PromptHistoryNavigationResult Navigate(
        IReadOnlyList<PromptHistoryEntry> history,
        int? historyIndex,
        string? historyDraft,
        IReadOnlyList<FollowUpAttachment>? historyDraftAttachments,
        string currentText,
        IReadOnlyList<FollowUpAttachment> currentAttachments,
        int direction) {
        if (history.Count == 0)
            return new PromptHistoryNavigationResult(false, currentText, null, historyIndex, historyDraft, historyDraftAttachments);

        var effectiveDraft = historyDraft;
        var effectiveDraftAttachments = historyDraftAttachments;
        var effectiveIndex = historyIndex;

        if (effectiveIndex is null) {
            effectiveDraft = currentText;
            effectiveDraftAttachments = currentAttachments;
            effectiveIndex = history.Count;
        }

        var nextIndex = Math.Clamp(
            effectiveIndex.Value + direction,
            0,
            history.Count);

        if (nextIndex == effectiveIndex.Value)
            return new PromptHistoryNavigationResult(false, currentText, null, historyIndex, historyDraft, historyDraftAttachments);

        if (nextIndex == history.Count) {
            return new PromptHistoryNavigationResult(
                true,
                effectiveDraft ?? string.Empty,
                effectiveDraftAttachments,
                null,
                effectiveDraft,
                effectiveDraftAttachments);
        }

        return new PromptHistoryNavigationResult(
            true,
            history[nextIndex].Text,
            history[nextIndex].Attachments,
            nextIndex,
            effectiveDraft,
            effectiveDraftAttachments,
            IsDictated: history[nextIndex].IsDictated);
    }
}

internal static class InteractiveControlStateCalculator {
    public static InteractiveControlState Calculate(
        bool hasWorkspace,
        bool squadInstalled,
        bool isInstallingSquad,
        bool isPromptRunning,
        bool canAbortBackgroundTask,
        string? currentPromptText) {
        var interactionsEnabled = squadInstalled && !isInstallingSquad;
        var localCommandReady = LocalPromptSubmissionPolicy.CanSubmitWhilePromptRunning(currentPromptText);

        return new InteractiveControlState(
            AgentItemsEnabled: interactionsEnabled,
            OutputEnabled: interactionsEnabled,
            CopyEnabled: interactionsEnabled,
            PromptEnabled: interactionsEnabled,
            RunEnabled: interactionsEnabled && (!isPromptRunning || localCommandReady),
            AbortEnabled: interactionsEnabled && (isPromptRunning || canAbortBackgroundTask),
            RunDoctorEnabled: interactionsEnabled && !isPromptRunning,
            InstallSquadEnabled: !isInstallingSquad && hasWorkspace && !squadInstalled);
    }
}

internal static class SlashCommandParameterPolicy {
    /// <summary>
    /// Slash commands that require a parameter argument.
    /// When Tab-completing one of these, insert the command + a trailing space
    /// and wait for the user to type the argument — do not auto-submit.
    /// </summary>
    private static readonly HashSet<string> ParameterRequiredCommands = new(StringComparer.OrdinalIgnoreCase) {
        "/activate",
        "/deactivate",
        "/plan",
        "/retire",
    };

    public static bool RequiresParameter(string command) =>
        ParameterRequiredCommands.Contains(command.Trim());
}

internal static class LocalPromptSubmissionPolicy {
    private static readonly HashSet<string> BusySafeCommands = new(StringComparer.OrdinalIgnoreCase) {
        "/activate",
        "/agents",
        "/approval",
        "/cast",
        "/deactivate",
        "/doctor",
        "/dropTasks",
        "/health",
        "/help",
        "/hire",
        "/model",
        "/retire",
        "/status",
        "/tasks",
        "/trace",
        "/version"
    };

    /// <summary>
    /// Commands that are handled entirely as local UI operations (never touch the AI).
    /// When one of these is submitted while the coordinator is running or a native loop
    /// is active, it must be executed immediately rather than added to the prompt queue.
    /// <para>
    /// <c>/cast</c> belongs here because the cast-agent UI opens immediately; if the user
    /// completes a cast while the coordinator is busy, <c>PromptExecutionController</c>
    /// calls its <c>enqueuePrompt</c> callback to add the resulting cast prompt to the
    /// back of the queue automatically.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ImmediateLocalCommands = new(StringComparer.OrdinalIgnoreCase) {
        "/agents",
        "/approval",
        "/cast",
        "/doctor",
        "/dropTasks",
        "/health",
        "/help",
        "/hire",
        "/model",
        "/queue-sim",
        "/sessions",
        "/status",
        "/tasks",
        "/test-queue",
        "/trace",
        "/version"
    };

    public static bool CanSubmitWhilePromptRunning(string? prompt) =>
        !string.IsNullOrWhiteSpace(prompt) &&
        BusySafeCommands.Contains(GetSlashCommand(prompt));

    /// <summary>
    /// Returns <c>true</c> when <paramref name="prompt"/> is a slash command that is
    /// handled entirely as a local UI operation and must therefore execute immediately,
    /// regardless of whether the coordinator is busy or items are in the prompt queue.
    /// </summary>
    public static bool IsImmediateLocalCommand(string? prompt) =>
        !string.IsNullOrWhiteSpace(prompt) &&
        ImmediateLocalCommands.Contains(GetSlashCommand(prompt));

    public static bool ShouldRetainPromptAfterLocalSubmit(
        string? prompt,
        bool isPromptRunning) {
        if (!isPromptRunning || !CanSubmitWhilePromptRunning(prompt))
            return false;
        // Clear when the box contains only the bare slash command — the user doesn't
        // need it back after it executed.  Retain only when there is additional text
        // (e.g. "/activate AgentName") so that text isn't lost.
        var trimmed = prompt?.Trim() ?? string.Empty;
        var cmd = GetSlashCommand(trimmed);
        return trimmed.Length > cmd.Length; // has trailing arguments
    }

    private static string GetSlashCommand(string? prompt) {
        var trimmed = prompt?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var spaceIndex = trimmed.IndexOfAny(new[] { ' ', '\n', '\r' });
        return spaceIndex >= 0
            ? trimmed[..spaceIndex]
            : trimmed;
    }
}

/// <summary>
/// Pure logic for computing the Run/Send button label shown to the user.
/// Extracted from <c>MainWindow.SyncSendButton()</c> so it can be unit-tested
/// without a WPF dependency.
/// </summary>
internal static class RunButtonLabelPolicy {
    public const string LabelSend   = "Send";
    public const string LabelQueue  = "Queue";
    public const string LabelSubmit = "Submit";

    /// <summary>
    /// Computes the label that should appear on the Run button.
    /// </summary>
    /// <param name="coordinatorBusy">True when a prompt is running or the native loop is active.</param>
    /// <param name="queuePausedAwaitingInput">True when <c>_queuePausedNotificationFired</c> is set —
    /// i.e. the queue has been halted because the last AI turn requested human input.</param>
    /// <param name="queueCount">Number of items currently in the prompt queue.</param>
    /// <param name="activeTabId">The currently-active queued-tab ID, or null if on the live tab.</param>
    /// <param name="queueManuallyPaused">True when the user has manually paused the queue.
    /// While paused, any queue tab shows "Send" so it can be dispatched immediately.</param>
    /// <param name="isRightmostTab">True when the active tab is the rightmost (oldest/first-to-dispatch)
    /// queue item — the one that will actually be sent on the next dispatch cycle.</param>
    public static string Compute(
        bool coordinatorBusy,
        bool queuePausedAwaitingInput,
        int  queueCount,
        string? activeTabId,
        bool queueManuallyPaused = false,
        bool isRightmostTab = false) {

        // Editing a specific queued tab.
        if (activeTabId is not null)
        {
            // QRB awaiting answer on the rightmost tab: the item can be sent as the
            // quick-reply answer directly, even while the loop is still running.
            if (queuePausedAwaitingInput && isRightmostTab) return LabelSend;
            if (coordinatorBusy || queueManuallyPaused) return LabelQueue;
            // QRB state: any tab shows "Send" so the item can be dispatched immediately.
            if (queuePausedAwaitingInput) return LabelSend;
            // "Send" only on the rightmost tab (the one about to be dispatched).
            // Non-rightmost tabs show "Submit" to indicate they are queued behind others.
            return isRightmostTab ? LabelSend : LabelSubmit;
        }

        // Queue is paused for input: bypass the "items in queue → Queue" logic so
        // the user's reply fires immediately rather than being added to the back.
        if (queuePausedAwaitingInput && queueCount > 0)
            return LabelSend;

        // Draft tab with queue manually paused: show "Queue" — pressing the button
        // enqueues the draft; nothing will be dispatched while paused.
        if (queueManuallyPaused && !coordinatorBusy)
            return LabelQueue;

        bool queueMode = coordinatorBusy || queueCount > 0;
        return queueMode ? LabelQueue : LabelSend;
    }
}

internal enum RunButtonClickAction {
    /// <summary>Send the draft immediately (queue idle, draft tab active).</summary>
    SendPrompt,
    /// <summary>Add the draft to the back of the queue (queue running or paused, draft tab active).</summary>
    EnqueuePrompt,
    /// <summary>Dispatch the currently-selected queued tab directly (queue idle, queued tab active).</summary>
    DispatchTab,
    /// <summary>Switch back to the main draft tab and return keyboard focus to the textbox.</summary>
    FocusDraft,
}

/// <summary>
/// Pure logic for deciding what the Run/Queue button click should do given the
/// current queue state and active tab. Local slash-commands bypass this policy
/// entirely and are handled before it is consulted.
/// </summary>
internal static class RunButtonClickPolicy {
    /// <summary>
    /// Returns the action the Run button click handler should take.
    /// </summary>
    /// <param name="isPromptRunning">True when the coordinator is actively executing a prompt.</param>
    /// <param name="isNativeLoopRunning">True when a native loop is running.</param>
    /// <param name="isManuallyPaused">True when the queue is manually paused via the play/pause button.</param>
    /// <param name="activeTabId">The selected queued-tab ID, or null when the main draft tab is active.</param>
    public static RunButtonClickAction Resolve(
        bool isPromptRunning,
        bool isNativeLoopRunning,
        bool isManuallyPaused,
        string? activeTabId) {

        bool queueBusy = isPromptRunning || isNativeLoopRunning || isManuallyPaused;

        if (activeTabId is not null)
            return queueBusy ? RunButtonClickAction.FocusDraft : RunButtonClickAction.DispatchTab;

        return queueBusy ? RunButtonClickAction.EnqueuePrompt : RunButtonClickAction.SendPrompt;
    }
}
