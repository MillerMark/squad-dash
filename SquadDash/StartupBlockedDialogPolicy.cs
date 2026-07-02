using System;

namespace SquadDash;

internal static class StartupBlockedDialogPolicy {
    public static bool HasPendingRestartRequest(string applicationRoot) {
        try {
            return HasPendingRestartRequest(applicationRoot, new RestartCoordinatorStateStore());
        }
        catch (Exception ex) {
            TraceCheckFailure(ex);
            return false;
        }
    }

    internal static bool HasPendingRestartRequest(
        string applicationRoot,
        RestartCoordinatorStateStore restartStateStore) {
        if (string.IsNullOrWhiteSpace(applicationRoot))
            return false;

        try {
            var request = restartStateStore.LoadRequest(applicationRoot);
            if (request is null)
                return false;

            if (restartStateStore.LoadPlan(applicationRoot, request.RequestId) is not null)
                return true;

            restartStateStore.ClearRequest(applicationRoot);
            SquadDashTrace.Write(
                "Startup",
                $"Ignoring stale pending restart request without matching plan: requestId={request.RequestId}");
            return false;
        }
        catch (Exception ex) {
            TraceCheckFailure(ex);
            return false;
        }
    }

    private static void TraceCheckFailure(Exception ex) {
        SquadDashTrace.Write(
            "Startup",
            $"Failed to check pending restart request for startup blocked-dialog policy: {ex.Message}");
    }
}
