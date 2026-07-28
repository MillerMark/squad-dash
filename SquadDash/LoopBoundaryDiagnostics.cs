namespace SquadDash;

/// <summary>Timing record for one inter-round boundary in the loop.</summary>
internal sealed record LoopBoundaryDiagnostics(
    int Iteration,
    DateTimeOffset RoundCompletedAt,
    DateTimeOffset WaitStartedAt,
    DateTimeOffset IterationStartedAt,
    string DelaySource,
    TimeSpan ConfiguredDelay,
    TimeSpan ActualDelay,
    bool QueueDrainOccurred)
{
    internal string BuildTraceMessage() =>
        $"LoopBoundary iter={Iteration} configured={ConfiguredDelay.TotalSeconds:F1}s " +
        $"actual={ActualDelay.TotalSeconds:F1}s source={DelaySource} " +
        $"queueDrain={QueueDrainOccurred} " +
        $"roundCompleted={RoundCompletedAt:HH:mm:ss.fff} iterStart={IterationStartedAt:HH:mm:ss.fff}";
}
