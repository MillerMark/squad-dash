namespace SquadDash;

internal sealed record LocalCommandTurnExecutionPlan<TTurn>(
    TTurn? SuspendedTurn,
    bool ShouldRestoreSuspendedTurn,
    bool ShouldRefreshLeadStatusAfterCompletion)
    where TTurn : class;

internal static class LocalCommandTurnExecutionPolicy {
    public static LocalCommandTurnExecutionPlan<TTurn> Create<TTurn>(
        bool isPromptRunning,
        TTurn? currentTurn)
        where TTurn : class {
        var shouldRestoreSuspendedTurn = isPromptRunning && currentTurn is not null;

        return new LocalCommandTurnExecutionPlan<TTurn>(
            currentTurn,
            shouldRestoreSuspendedTurn,
            ShouldRefreshLeadStatusAfterCompletion: !isPromptRunning);
    }
}
