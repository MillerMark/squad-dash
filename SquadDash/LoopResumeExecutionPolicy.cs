namespace SquadDash;

internal enum LoopResumeExecutionKind
{
    Refuse,
    GenericLoop,
    ExecutingPlan,
}

internal sealed record LoopResumeExecutionDecision(
    LoopResumeExecutionKind Kind,
    ActiveLoopExecutionState? Execution,
    string? GroupId,
    string? Revision);

/// <summary>
/// Resolves restart state without consulting mutable UI controls. A generic loop may resume
/// only when its exact loop path and filter were persisted. Legacy plan state remains safe
/// because its group ID still routes through the dedicated Executing Plan engine.
/// </summary>
internal static class LoopResumeExecutionPolicy
{
    internal static LoopResumeExecutionDecision Resolve(
        ActiveLoopExecutionState? persistedExecution,
        string? runtimeGroupId,
        string? legacyPersistedGroupId)
    {
        var execution = ActiveLoopExecutionState.Normalize(persistedExecution);
        var groupId = FirstNonBlank(
            runtimeGroupId,
            execution?.DecomposeGroupId,
            legacyPersistedGroupId);
        if (groupId is not null)
        {
            return new LoopResumeExecutionDecision(
                LoopResumeExecutionKind.ExecutingPlan,
                execution,
                groupId,
                execution?.DecomposeRevision);
        }

        if (execution is not null)
        {
            return new LoopResumeExecutionDecision(
                LoopResumeExecutionKind.GenericLoop,
                execution,
                null,
                null);
        }

        return new LoopResumeExecutionDecision(
            LoopResumeExecutionKind.Refuse,
            null,
            null,
            null);
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }
        return null;
    }
}
