namespace SquadDash;

/// <summary>
/// Visual activity state for a plan task, driving UI indicators across the Plans panel,
/// Loop panel, and Plan Viewer surfaces.
/// </summary>
internal enum PlanTaskActivityState
{
    /// <summary>Task is actively executing (spinner indicator).</summary>
    Executing,

    /// <summary>Existing repository work is being assessed before interrupted execution continues.</summary>
    Assessing,

    /// <summary>Candidate work is being independently checked before acceptance.</summary>
    Verifying,

    /// <summary>Candidate work is saved and waiting for its verification turn.</summary>
    VerificationPending,

    /// <summary>A single bounded automatic correction is in progress.</summary>
    Reworking,

    /// <summary>Task is queued or delayed, waiting to start (non-spinning indicator).</summary>
    Queued,

    /// <summary>Task is blocked by an approval gate (non-spinning "Waiting for approval").</summary>
    AwaitingApproval,

    /// <summary>Task is blocked by a failed dependency or explicit block (non-spinning).</summary>
    Blocked,

    /// <summary>Task execution was interrupted (non-spinning interrupted indicator).</summary>
    Interrupted,

    /// <summary>Task completed successfully (checkmark indicator).</summary>
    Completed,
}

internal static class PlanTaskActivityPresentation
{
    internal static PlanTaskActivityState ResolveLiveState(
        PlanTaskActivityState activityState,
        bool hasMatchingLiveRound) =>
        activityState == PlanTaskActivityState.Verifying && !hasMatchingLiveRound
            ? PlanTaskActivityState.VerificationPending
            : activityState;

    internal static bool KeepsSpinnerContinuouslyActive(
        PlanTaskActivityState activityState) =>
        activityState == PlanTaskActivityState.Assessing;

    internal static string BuildStepLabel(
        string stepLabel,
        PlanTaskActivityState activityState) => activityState switch
    {
        PlanTaskActivityState.VerificationPending => $"Step {stepLabel} - Verification pending",
        PlanTaskActivityState.Verifying => $"Step {stepLabel} - Verifying",
        PlanTaskActivityState.Assessing => $"Step {stepLabel} - Assessing",
        _ => $"Step {stepLabel}",
    };
}
