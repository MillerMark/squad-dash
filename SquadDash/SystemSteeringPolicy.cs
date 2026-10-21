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
    internal const int RecentToolActivityGraceMinutes = 2;
    internal const int MinimumNoticeCooldownMinutes = 5;

    internal static SystemSteeringDecision Decide(
        int minutesSinceWrite,
        bool firstNoticeSent,
        int nextStrongNoticeMinute,
        int currentToolCount,
        int toolCountAtLastNotice,
        int minutesSinceLastNotice = int.MaxValue,
        bool hasRecentToolActivity = false)
    {
        if (hasRecentToolActivity)
            return SystemSteeringDecision.None;

        if (!firstNoticeSent)
            return minutesSinceWrite >= GentleNoticeMinute
                ? SystemSteeringDecision.Gentle
                : SystemSteeringDecision.None;

        if (minutesSinceLastNotice < MinimumNoticeCooldownMinutes)
            return SystemSteeringDecision.None;

        return minutesSinceWrite >= nextStrongNoticeMinute &&
               currentToolCount > toolCountAtLastNotice
            ? SystemSteeringDecision.Strong
            : SystemSteeringDecision.None;
    }

    internal static bool HasRecentToolActivity(
        DateTimeOffset now,
        DateTimeOffset? lastToolActivityAt) =>
        lastToolActivityAt is { } activityAt &&
        activityAt <= now &&
        now - activityAt < TimeSpan.FromMinutes(RecentToolActivityGraceMinutes);

    internal static DateTimeOffset ResumeEpochAfterPause(
        DateTimeOffset epochStartedAt,
        DateTimeOffset pauseStartedAt,
        DateTimeOffset resumedAt) =>
        resumedAt <= pauseStartedAt
            ? epochStartedAt
            : epochStartedAt + (resumedAt - pauseStartedAt);

    internal static int NextStrongMinute(int currentThreshold, int minutesSinceWrite) =>
        System.Math.Max(
            currentThreshold + StrongNoticeIntervalMinutes,
            ((minutesSinceWrite / StrongNoticeIntervalMinutes) + 1) * StrongNoticeIntervalMinutes);
}
