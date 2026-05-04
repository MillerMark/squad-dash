using System;
using System.Collections.Generic;

namespace SquadDash;

internal enum PromptInputKey {
    Other,
    Enter,
    Up,
    Down,
    Tab,
    Escape
}

internal enum PromptInputAction {
    None,
    SubmitPrompt,
    NavigateHistoryPrevious,
    NavigateHistoryNext,
    IntelliSenseUp,
    IntelliSenseDown,
    IntelliSenseAccept,
    IntelliSenseDismiss
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
        if (key == PromptInputKey.Enter && !ctrlPressed && !shiftPressed && runButtonEnabled)
            return PromptInputAction.SubmitPrompt;

        if (ctrlPressed && key == PromptInputKey.Up)
            return PromptInputAction.NavigateHistoryPrevious;

        if (ctrlPressed && key == PromptInputKey.Down)
            return PromptInputAction.NavigateHistoryNext;

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
        "/version"
    };

    public static bool CanSubmitWhilePromptRunning(string? prompt) =>
        !string.IsNullOrWhiteSpace(prompt) &&
        BusySafeCommands.Contains(GetSlashCommand(prompt));

    public static bool ShouldRetainPromptAfterLocalSubmit(
        string? prompt,
        bool isPromptRunning) =>
        isPromptRunning && CanSubmitWhilePromptRunning(prompt);

    private static string GetSlashCommand(string? prompt) {
        var trimmed = prompt?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var spaceIndex = trimmed.IndexOf(' ');
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
    public const string LabelSend  = "Send";
    public const string LabelQueue = "Queue";

    /// <summary>
    /// Computes the label that should appear on the Run button.
    /// </summary>
    /// <param name="coordinatorBusy">True when a prompt is running or the native loop is active.</param>
    /// <param name="queuePausedAwaitingInput">True when <c>_queuePausedNotificationFired</c> is set —
    /// i.e. the queue has been halted because the last AI turn requested human input.</param>
    /// <param name="queueCount">Number of items currently in the prompt queue.</param>
    /// <param name="activeTabId">The currently-active queued-tab ID, or null if on the live tab.</param>
    public static string Compute(
        bool coordinatorBusy,
        bool queuePausedAwaitingInput,
        int  queueCount,
        string? activeTabId) {

        // Editing a specific queued tab: "Send" only when coordinator is free.
        if (activeTabId is not null)
            return coordinatorBusy ? LabelQueue : LabelSend;

        // Queue is paused for input: bypass the "items in queue → Queue" logic so
        // the user's reply fires immediately rather than being added to the back.
        if (queuePausedAwaitingInput && queueCount > 0)
            return LabelSend;

        bool queueMode = coordinatorBusy || queueCount > 0;
        return queueMode ? LabelQueue : LabelSend;
    }
}