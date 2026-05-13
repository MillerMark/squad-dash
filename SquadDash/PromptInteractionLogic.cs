using System;
using System.Collections.Generic;

namespace SquadDash;

internal enum PromptHintFeature {
    EnterSend,
    ShiftEnterNewline,
    CtrlArrowHistory,
    PushToTalk,
    CtrlEnterPrioritize
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
    PrioritizeInQueue,
    NavigateHistoryPrevious,
    NavigateHistoryNext,
    IntelliSenseUp,
    IntelliSenseDown,
    IntelliSenseAccept,
    IntelliSenseDismiss,
    AddQueueSlot
}

internal sealed record PromptHistoryNavigationResult(
    bool Changed,
    string Text,
    int? HistoryIndex,
    string? HistoryDraft);

internal sealed record InteractiveControlState(
    bool AgentItemsEnabled,
    bool OutputEnabled,
    bool CopyEnabled,
    bool PromptEnabled,
    bool RunEnabled,
    bool AbortEnabled,
    bool RunDoctorEnabled,
    bool InstallSquadEnabled);

internal static class PromptInputBehavior {
    public static PromptInputAction ResolveAction(
        PromptInputKey key,
        bool ctrlPressed,
        bool shiftPressed,
        bool runButtonEnabled,
        bool isMultiLinePrompt) {
        if (key == PromptInputKey.Enter && ctrlPressed && !shiftPressed)
            return PromptInputAction.PrioritizeInQueue;

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
        IReadOnlyList<string> history,
        int? historyIndex,
        string? historyDraft,
        string currentText,
        int direction) {
        if (history.Count == 0)
            return new PromptHistoryNavigationResult(false, currentText, historyIndex, historyDraft);

        var effectiveDraft = historyDraft;
        var effectiveIndex = historyIndex;

        if (effectiveIndex is null) {
            effectiveDraft = currentText;
            effectiveIndex = history.Count;
        }

        var nextIndex = Math.Clamp(
            effectiveIndex.Value + direction,
            0,
            history.Count);

        if (nextIndex == effectiveIndex.Value)
            return new PromptHistoryNavigationResult(false, currentText, historyIndex, historyDraft);

        if (nextIndex == history.Count) {
            return new PromptHistoryNavigationResult(
                true,
                effectiveDraft ?? string.Empty,
                null,
                effectiveDraft);
        }

        return new PromptHistoryNavigationResult(
            true,
            history[nextIndex],
            nextIndex,
            effectiveDraft);
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
        "/deactivate",
        "/doctor",
        "/dropTasks",
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
    /// <c>/hire</c> belongs here because the Hire UI opens immediately; if the user
    /// completes a hire while the coordinator is busy, <c>PromptExecutionController</c>
    /// calls its <c>enqueuePrompt</c> callback to add the resulting hire prompt to the
    /// back of the queue automatically.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ImmediateLocalCommands = new(StringComparer.OrdinalIgnoreCase) {
        "/agents",
        "/approval",
        "/doctor",
        "/dropTasks",
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