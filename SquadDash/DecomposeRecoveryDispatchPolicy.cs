namespace SquadDash;

internal static class DecomposeRecoveryDispatchPolicy
{
    internal static bool ShouldDefer(bool promptRunning, bool loopRunning) =>
        promptRunning || loopRunning;
}
