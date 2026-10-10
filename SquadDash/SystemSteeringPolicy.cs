namespace SquadDash;

internal enum SystemSteeringDecision
{
    None,
    Gentle,
    Strong,
}

/// <summary>Pure deterministic schedule for no-write system steering.</summary>
internal static class SystemSteeringPolicy
{
    internal const int GentleNoticeMinute = 15;
    internal const int FirstStrongNoticeMinute = 20;
    internal const int StrongNoticeIntervalMinutes = 5;

    internal static SystemSteeringDecision Decide(
        int minutesSinceWrite,
        bool firstNoticeSent,
        int nextStrongNoticeMinute,
        int currentToolCount,
        int toolCountAtLastNotice)
    {
        if (!firstNoticeSent)
            return minutesSinceWrite >= GentleNoticeMinute
                ? SystemSteeringDecision.Gentle
                : SystemSteeringDecision.None;

        return minutesSinceWrite >= nextStrongNoticeMinute &&
               currentToolCount > toolCountAtLastNotice
            ? SystemSteeringDecision.Strong
            : SystemSteeringDecision.None;
    }

    internal static int NextStrongMinute(int currentThreshold, int minutesSinceWrite) =>
        System.Math.Max(
            currentThreshold + StrongNoticeIntervalMinutes,
            ((minutesSinceWrite / StrongNoticeIntervalMinutes) + 1) * StrongNoticeIntervalMinutes);
}
