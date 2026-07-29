using System;

namespace SquadDash;

/// <summary>
/// Enforces the ordering contract for plan progress: durable persistence must succeed before any
/// observer is notified, while an observer failure must not invalidate an already-saved transition.
/// </summary>
internal static class PlanProgressPublisher
{
    internal static bool TryPublish(
        Plan plan,
        Action<Plan> persist,
        Action<Plan> notify,
        out string? persistenceError,
        out string? notificationError)
    {
        persistenceError = null;
        notificationError = null;
        try
        {
            persist(plan);
        }
        catch (Exception ex)
        {
            persistenceError = ex.Message;
            return false;
        }

        try { notify(plan); }
        catch (Exception ex) { notificationError = ex.Message; }
        return true;
    }
}
