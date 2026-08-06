namespace SquadDash;

/// <summary>
/// Builds static loop-state fixtures for guided-tour demonstrations.
/// The loop state is display-only: it shows a running loop in the UI without
/// executing real prompts or starting <see cref="LoopController"/>.
/// </summary>
internal static class SimulationLoopFixtureBuilder
{
    /// <summary>
    /// Builds a simulated loop state showing an active loop at iteration 3.
    /// </summary>
    internal static SimulationLoopState BuildActiveLoopState() => new(
        Iteration: 3,
        StatusText: "● Running · Round 3",
        IsRunning: true,
        IsWaiting: false);

    /// <summary>
    /// Builds a simulated loop state showing a loop waiting between iterations.
    /// </summary>
    internal static SimulationLoopState BuildWaitingLoopState() => new(
        Iteration: 2,
        StatusText: "⏳ Waiting · next in 45s",
        IsRunning: true,
        IsWaiting: true);
}

/// <summary>
/// Represents a static snapshot of loop panel state for simulation purposes.
/// Does not drive actual prompt execution.
/// </summary>
internal sealed record SimulationLoopState(
    int Iteration,
    string StatusText,
    bool IsRunning,
    bool IsWaiting);
