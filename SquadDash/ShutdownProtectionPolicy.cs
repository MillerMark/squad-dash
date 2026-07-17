namespace SquadDash;

internal static class ShutdownProtectionPolicy
{
    /// <summary>
    /// Queue state blocks shutdown only when it can lead to automatic execution.
    /// Dormant tabs are persisted and can be restored safely on the next launch.
    /// </summary>
    public static bool HasQueueWorkThatCanStart(
        bool hasExecutableQueueItem,
        bool queueManuallyPaused,
        bool rightmostQueueTabActive) =>
        hasExecutableQueueItem &&
        !queueManuallyPaused &&
        !rightmostQueueTabActive;
}
