using System;

namespace SquadDash;

internal sealed record WorkHoursSettings(
    bool MondayWork    = true,
    bool TuesdayWork   = true,
    bool WednesdayWork = true,
    bool ThursdayWork  = true,
    bool FridayWork    = true,
    bool SaturdayWork  = false,
    bool SundayWork    = false,
    int  WorkDayStartHour = 9,   // 0-23
    int  WorkDayEndHour   = 17   // 0-23, exclusive (5pm = end of work day)
)
{
    public static WorkHoursSettings Default { get; } = new();

    public bool IsWorkDay(DayOfWeek dow) => dow switch {
        DayOfWeek.Monday    => MondayWork,
        DayOfWeek.Tuesday   => TuesdayWork,
        DayOfWeek.Wednesday => WednesdayWork,
        DayOfWeek.Thursday  => ThursdayWork,
        DayOfWeek.Friday    => FridayWork,
        DayOfWeek.Saturday  => SaturdayWork,
        DayOfWeek.Sunday    => SundayWork,
        _                   => false,
    };
}
