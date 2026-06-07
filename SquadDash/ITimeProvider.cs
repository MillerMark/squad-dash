namespace SquadDash;
internal interface ITimeProvider {
    DateTime UtcNow { get; }
}
internal sealed class SystemTimeProvider : ITimeProvider {
    public static readonly ITimeProvider Instance = new SystemTimeProvider();
    public DateTime UtcNow => DateTime.UtcNow;
}
