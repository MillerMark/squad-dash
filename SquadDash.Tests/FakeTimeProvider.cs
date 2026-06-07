namespace SquadDash;
internal sealed class FakeTimeProvider(DateTime utcNow) : ITimeProvider {
    public DateTime UtcNow => utcNow;
}
