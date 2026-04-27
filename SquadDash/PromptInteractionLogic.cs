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
        "/deactivate",
        "/doctor",
        "/dropTasks",
        "/help",
        "/hire",
        "/model",
        "/retire",
        "/screenshot",
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
