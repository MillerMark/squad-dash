namespace SquadDash;

internal enum DirectQuickReplyCompletionAction {
    None,
    RegisterSilentWatchdog,
    EnqueueCoordinatorFollowUp
}

internal static class DirectQuickReplyCompletionPolicy {
    internal static DirectQuickReplyCompletionAction Decide(
        bool needsDirectQuickReplyFollowUp,
        bool isPromptRunning,
        bool reportPromoted,
        bool hostPayloadHandled = false) {
        if (hostPayloadHandled)
            return DirectQuickReplyCompletionAction.None;

        if (needsDirectQuickReplyFollowUp)
            return isPromptRunning
                ? DirectQuickReplyCompletionAction.RegisterSilentWatchdog
                : DirectQuickReplyCompletionAction.EnqueueCoordinatorFollowUp;

        if (reportPromoted && isPromptRunning)
            return DirectQuickReplyCompletionAction.RegisterSilentWatchdog;

        return DirectQuickReplyCompletionAction.None;
    }
}
