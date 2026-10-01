namespace SquadDash;

internal static class PlanVisualizerFeatures
{
    // The plan schema already retains ParallelEligible. Turn this on when the native
    // scheduler can actually run eligible plan tasks concurrently.
    internal static bool ParallelTaskExecutionEditingEnabled => false;
}
