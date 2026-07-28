namespace SquadDash;

/// <summary>Abstraction over time and async delay for testing LoopController cadence.</summary>
internal interface ILoopClock
{
    DateTimeOffset UtcNow { get; }
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemLoopClock : ILoopClock
{
    internal static readonly SystemLoopClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
