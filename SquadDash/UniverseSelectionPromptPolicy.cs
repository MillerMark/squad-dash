using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal static class UniverseSelectionPromptPolicy {
    internal static bool ShouldPrompt(
        IReadOnlyList<SquadTeamMember> members,
        WorkspaceConversationState conversationState) {
        if (SquadTeamRosterLoader.HasNonUtilityMembers(members))
            return false;

        return !conversationState.Turns.Any(IsRealConversationTurn) &&
               !conversationState.GetThreads().Any(thread => thread.Turns.Count > 0);
    }

    private static bool IsRealConversationTurn(TranscriptTurnRecord turn) =>
        !turn.IsSessionBoundary && !turn.IsTourInjected && !turn.IsNewProjectOnboarding;
}
