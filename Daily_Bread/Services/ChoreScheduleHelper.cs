using Daily_Bread.Data.Models;

namespace Daily_Bread.Services;

/// <summary>
/// Static helper class for chore scheduling logic shared across services.
/// </summary>
public static class ChoreScheduleHelper
{
    /// <summary>
    /// Checks if a chore is scheduled to be active on a specific date. 
    /// </summary>
    /// <param name="chore">The chore definition to check.</param>
    /// <param name="date">The date to check against.</param>
    /// <returns>True if the chore is scheduled for the given date.</returns>
    public static bool IsChoreScheduledForDate(ChoreDefinition chore, DateOnly date)
    {
        // Inactive chores are never scheduled
        if (!chore.IsActive)
            return false;

        // Check date range constraints
        if (chore.StartDate.HasValue && date < chore.StartDate.Value)
            return false;
        if (chore.EndDate.HasValue && date > chore.EndDate.Value)
            return false;

        // Check day of week
        var dayFlag = GetDayOfWeekFlag(date.DayOfWeek);
        return (chore.ActiveDays & dayFlag) != 0;
    }

    /// <summary>
    /// Converts System.DayOfWeek to the DaysOfWeek flags enum.
    /// </summary>
    /// <param name="dayOfWeek">The day of week to convert.</param>
    /// <returns>The corresponding DaysOfWeek flag.</returns>
    public static DaysOfWeek GetDayOfWeekFlag(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Sunday => DaysOfWeek.Sunday,
        DayOfWeek.Monday => DaysOfWeek.Monday,
        DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
        DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
        DayOfWeek.Thursday => DaysOfWeek.Thursday,
        DayOfWeek.Friday => DaysOfWeek.Friday,
        DayOfWeek.Saturday => DaysOfWeek.Saturday,
        _ => DaysOfWeek.None
    };

    /// <summary>
    /// Gets the start of the week (Sunday) for a given date.
    /// </summary>
    /// <param name="date">Any date in the week.</param>
    /// <returns>The Sunday of that week.</returns>
    public static DateOnly GetWeekStartDate(DateOnly date)
    {
        int daysFromSunday = (int)date.DayOfWeek;
        return date.AddDays(-daysFromSunday);
    }

    /// <summary>
    /// Gets the end of the week (Saturday) for a given date.
    /// </summary>
    /// <param name="date">Any date in the week.</param>
    /// <returns>The Saturday of that week.</returns>
    public static DateOnly GetWeekEndDate(DateOnly date)
    {
        int daysToSaturday = 6 - (int)date.DayOfWeek;
        return date.AddDays(daysToSaturday);
    }
}
