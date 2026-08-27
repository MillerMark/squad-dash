using System;
using System.Text;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Durable payload for a plan start that must wait behind work already owned by the
/// coordinator. The payload lives in the normal prompt queue so restart persistence,
/// ordering, and cancellation use the same path as other queued work.
/// </summary>
internal sealed record QueuedPlanStart(string PlanId, string DisplayName)
{
    private const string Prefix = "SQUADDASH_QUEUED_PLAN_START_V1:";

    internal string Encode() => Prefix + Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));

    internal static bool TryDecode(string? value, out QueuedPlanStart? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(value[Prefix.Length..]));
            var parsed = JsonSerializer.Deserialize<QueuedPlanStart>(json);
            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.PlanId) ||
                string.IsNullOrWhiteSpace(parsed.DisplayName))
                return false;

            request = parsed with
            {
                PlanId = parsed.PlanId.Trim(),
                DisplayName = parsed.DisplayName.Trim(),
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal static class PlanStartQueuePolicy
{
    internal static bool ShouldQueue(
        bool coordinatorTurnActive,
        bool loopActive,
        bool queueDrainActive,
        bool backgroundWorkActive,
        bool directFollowUpPending,
        bool queueDispatchHeld,
        int queuedItemCount) =>
        coordinatorTurnActive ||
        loopActive ||
        queueDrainActive ||
        backgroundWorkActive ||
        directFollowUpPending ||
        queueDispatchHeld ||
        queuedItemCount > 0;
}
