using System;

namespace SquadDash;

/// <summary>
/// Durable host-owned queue hold created when plan execution needs recovery.
/// This is intentionally separate from the user's Play/Pause preference.
/// </summary>
internal sealed record QueueRecoveryHoldState(
    string PlanId,
    string? TaskId,
    string? AttemptId,
    string Reason,
    DateTimeOffset CreatedAt)
{
    internal static QueueRecoveryHoldState? Normalize(QueueRecoveryHoldState? hold)
    {
        if (hold is null || string.IsNullOrWhiteSpace(hold.PlanId) || string.IsNullOrWhiteSpace(hold.Reason))
            return null;

        return hold with
        {
            PlanId = hold.PlanId.Trim(),
            TaskId = NormalizeOptional(hold.TaskId),
            AttemptId = NormalizeOptional(hold.AttemptId),
            Reason = hold.Reason.Trim(),
            CreatedAt = hold.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : hold.CreatedAt.ToUniversalTime(),
        };
    }

    internal bool Matches(string planId, string? taskId = null)
    {
        if (!string.Equals(PlanId, planId?.Trim(), StringComparison.Ordinal))
            return false;

        return string.IsNullOrWhiteSpace(taskId) ||
               string.IsNullOrWhiteSpace(TaskId) ||
               string.Equals(TaskId, taskId.Trim(), StringComparison.Ordinal);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
