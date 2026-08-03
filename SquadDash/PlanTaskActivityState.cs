namespace SquadDash;

/// <summary>
/// Visual activity state for a plan task, driving UI indicators across the Plans panel,
/// Loop panel, and Plan Viewer surfaces.
/// </summary>
internal enum PlanTaskActivityState
{
    /// <summary>Task is actively executing (spinner indicator).</summary>
    Executing,

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
