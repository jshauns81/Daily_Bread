using Daily_Bread.Data.Models;
using Daily_Bread.Services;

namespace Daily_Bread.Api;

/// <summary>
/// Pure assembly of the screen-time response from already-loaded pieces, so the
/// pool math (floor, snapshot fallback, name join) is unit-testable without a
/// controller or database. Mirrors the web ScreenTimeMeter's math exactly.
/// </summary>
public static class ScreenTimeSummary
{
    public static ScreenTimeResponse Build(
        string userId,
        DateOnly weekStart,
        DateOnly weekEnd,
        decimal weekdayPoolHours,
        decimal weekendPoolHours,
        WeekPricing pricing,
        ChildWeeklyScreenTimeBudget? snapshot,
        IReadOnlyDictionary<int, string> choreNames,
        IReadOnlyList<ScreenTimeEntryDto> recentEntries,
        decimal weeklyRoutinePayout,
        int minutesPerImportancePoint)
    {
        // Base pool: this week's frozen snapshot if reconciliation wrote one,
        // else the live profile settings.
        int weekdayBase, weekendBase, weekdayEffective, weekendEffective;
        if (snapshot != null)
        {
            weekdayBase = snapshot.WeekdayBasePoolMinutes;
            weekendBase = snapshot.WeekendBasePoolMinutes;
            weekdayEffective = snapshot.WeekdayEffectiveMinutes;
            weekendEffective = snapshot.WeekendEffectiveMinutes;
        }
        else
        {
            weekdayBase = HoursToMinutes(weekdayPoolHours);
            weekendBase = HoursToMinutes(weekendPoolHours);
            weekdayEffective = weekdayBase;
            weekendEffective = weekendBase;
        }

        // Guaranteed floor = base − the at-risk budget (the part that can never be lost).
        var weekday = new ScreenTimePoolDto(
            nameof(ScreenTimePool.Weekday),
            weekdayBase,
            weekdayEffective,
            Math.Max(0, weekdayBase - pricing.WeekdayBudgetMinutes),
            pricing.WeekdayBudgetMinutes);

        var weekend = new ScreenTimePoolDto(
            nameof(ScreenTimePool.Weekend),
            weekendBase,
            weekendEffective,
            Math.Max(0, weekendBase - pricing.WeekendBudgetMinutes),
            pricing.WeekendBudgetMinutes);

        var prices = pricing.ChorePrices
            .Where(kv => kv.Value.PerInstanceMinutes > 0)
            .Select(kv => new ScreenTimeChorePriceDto(
                kv.Key,
                choreNames.TryGetValue(kv.Key, out var name) ? name : "Chore",
                kv.Value.Pool.ToString(),
                kv.Value.ScheduledInstances,
                kv.Value.PerInstanceMinutes))
            .OrderByDescending(p => p.PerInstanceMinutes)
            .ThenBy(p => p.Name)
            .ToList();

        return new ScreenTimeResponse(
            userId, weekStart, weekEnd, weekday, weekend, prices, recentEntries,
            weeklyRoutinePayout, minutesPerImportancePoint);
    }

    public static int HoursToMinutes(decimal hours) =>
        (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
}
