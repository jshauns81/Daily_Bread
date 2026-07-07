using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// The single source of truth for importance-share screen-time pricing (MECHANICS_AMENDMENT.md §A).
/// Given a child and any date in a week, it computes each pool's penalty budget (minutes) and the
/// per-instance minute price of every scheduled chore, so reconciliation, the at-risk card, and the
/// UI's "live price" all agree on one set of numbers.
/// </summary>
public interface IScreenTimePricingService
{
    /// <summary>
    /// Computes the week's pool budgets and per-chore instance prices for a child. Honors per-date
    /// schedule overrides (Add/Remove/Move) for SpecificDays chores; WeeklyFrequency chores contribute
    /// their <see cref="ChoreDefinition.WeeklyTargetCount"/> instances to the weekday pool (locked
    /// decision — flexible reps have no day). Only chores with Importance &gt; 0 assigned to the child
    /// participate.
    /// </summary>
    Task<WeekPricing> GetWeekPricingAsync(int childProfileId, DateOnly anyDateInWeek);
}

/// <summary>
/// The computed pricing for one child-week: each pool's penalty budget in minutes plus the
/// per-instance minute price of every priced chore.
/// </summary>
/// <param name="WeekdayBudgetMinutes">Weekday pool penalty budget = round(hours × at-risk% × 60).</param>
/// <param name="WeekendBudgetMinutes">Weekend pool penalty budget = round(hours × at-risk% × 60).</param>
/// <param name="ChorePrices">Per-chore price keyed by ChoreDefinition Id.</param>
public sealed record WeekPricing(
    int WeekdayBudgetMinutes,
    int WeekendBudgetMinutes,
    IReadOnlyDictionary<int, ChorePrice> ChorePrices);

/// <summary>
/// The price of a single chore within a week: which pool it draws from, how many instances are
/// scheduled, and the minutes lost per missed instance.
/// </summary>
/// <param name="Pool">The pool this chore is priced against (all its instances counted here).</param>
/// <param name="ScheduledInstances">Number of scheduled instances this week (days, or the weekly target).</param>
/// <param name="PerInstanceMinutes">Minutes lost when one instance is missed.</param>
public sealed record ChorePrice(
    ScreenTimePool Pool,
    int ScheduledInstances,
    int PerInstanceMinutes);

/// <summary>
/// Pure importance-share math (MECHANICS_AMENDMENT.md §A). Kept static and dependency-free so
/// reconciliation, the at-risk card, and the UI can all reuse the exact same calculation.
/// </summary>
public static class ScreenTimePricing
{
    /// <summary>
    /// The minutes lost for one missed instance of a chore:
    /// <c>(importance ÷ poolImportanceSum) × poolBudgetMinutes</c>, rounded to the nearest minute.
    /// Because <paramref name="poolImportanceSum"/> is summed over every instance in the pool, missing
    /// every instance costs exactly the pool budget (never more), up to rounding. Guards a zero/empty
    /// pool (no priced instances) by returning 0.
    /// </summary>
    public static int PriceInstance(int importance, int poolImportanceSum, int poolBudgetMinutes)
    {
        if (poolImportanceSum <= 0 || importance <= 0 || poolBudgetMinutes <= 0)
        {
            return 0;
        }

        return (int)Math.Round(
            (double)importance / poolImportanceSum * poolBudgetMinutes,
            MidpointRounding.AwayFromZero);
    }
}

public sealed class ScreenTimePricingService : IScreenTimePricingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IChoreScheduleService _scheduleService;

    public ScreenTimePricingService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IFamilySettingsService familySettingsService,
        IChoreScheduleService scheduleService)
    {
        _contextFactory = contextFactory;
        _familySettingsService = familySettingsService;
        _scheduleService = scheduleService;
    }

    public async Task<WeekPricing> GetWeekPricingAsync(int childProfileId, DateOnly anyDateInWeek)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var childProfile = await context.ChildProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == childProfileId)
            ?? throw new InvalidOperationException($"Child profile {childProfileId} not found.");

        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(anyDateInWeek);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(anyDateInWeek);

        var weekdayBudget = PoolBudgetMinutes(
            childProfile.WeekdayScreenTimeHours, childProfile.WeekdayAtRiskPercent);
        var weekendBudget = PoolBudgetMinutes(
            childProfile.WeekendScreenTimeHours, childProfile.WeekendAtRiskPercent);

        // Tally each participating chore's instances per pool.
        var instances = new Dictionary<int, PoolInstanceTally>();

        // SpecificDays chores: enumerate the 7 dates so per-date overrides (Add/Remove/Move) apply.
        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            var pool = IsWeekend(date) ? ScreenTimePool.Weekend : ScreenTimePool.Weekday;
            var choresToday = await _scheduleService.GetChoresForDateAsync(date);

            foreach (var chore in choresToday)
            {
                if (chore.ScheduleType != ChoreScheduleType.SpecificDays) continue;
                if (chore.Importance <= 0) continue;
                if (chore.AssignedUserId != childProfile.UserId) continue;

                Tally(instances, chore, pool, count: 1);
            }
        }

        // WeeklyFrequency chores: WeeklyTargetCount instances, all against the weekday pool
        // (locked decision — flexible reps have no day). Loaded directly; day-based overrides
        // do not apply to flexible chores.
        var weeklyChores = await context.ChoreDefinitions
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .Where(c => c.Importance > 0)
            .Where(c => c.AssignedUserId == childProfile.UserId)
            .Where(c => c.StartDate == null || c.StartDate <= weekEnd)
            .Where(c => c.EndDate == null || c.EndDate >= weekStart)
            .ToListAsync();

        foreach (var chore in weeklyChores)
        {
            var count = Math.Max(0, chore.WeeklyTargetCount);
            if (count == 0) continue;
            Tally(instances, chore, ScreenTimePool.Weekday, count);
        }

        // Per-pool importance sums: each instance contributes its chore's Importance, so the
        // denominator is Σ over all instances (Importance × instance count) in that pool.
        var poolImportanceSums = new Dictionary<ScreenTimePool, int>
        {
            [ScreenTimePool.Weekday] = 0,
            [ScreenTimePool.Weekend] = 0
        };

        foreach (var tally in instances.Values)
        {
            poolImportanceSums[tally.Pool] += tally.Importance * tally.InstanceCount;
        }

        var chorePrices = new Dictionary<int, ChorePrice>();
        foreach (var (choreId, tally) in instances)
        {
            var budget = tally.Pool == ScreenTimePool.Weekend ? weekendBudget : weekdayBudget;
            var perInstance = ScreenTimePricing.PriceInstance(
                tally.Importance, poolImportanceSums[tally.Pool], budget);

            chorePrices[choreId] = new ChorePrice(tally.Pool, tally.InstanceCount, perInstance);
        }

        return new WeekPricing(weekdayBudget, weekendBudget, chorePrices);
    }

    private static int PoolBudgetMinutes(decimal poolHours, int atRiskPercent) =>
        (int)Math.Round(
            poolHours * (atRiskPercent / 100m) * 60m,
            MidpointRounding.AwayFromZero);

    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>
    /// Adds instances of a chore to the tally. A chore is priced against a single pool; if its
    /// instances span both weekday and weekend days, it is priced against whichever pool holds the
    /// majority of them (weekday on a tie), with all its instances counted there so the miss-everything
    /// = budget invariant still holds per pool.
    /// </summary>
    private static void Tally(
        Dictionary<int, PoolInstanceTally> instances,
        ChoreDefinition chore,
        ScreenTimePool pool,
        int count)
    {
        if (instances.TryGetValue(chore.Id, out var existing))
        {
            var weekday = existing.WeekdayCount + (pool == ScreenTimePool.Weekday ? count : 0);
            var weekend = existing.WeekendCount + (pool == ScreenTimePool.Weekend ? count : 0);
            instances[chore.Id] = existing with { WeekdayCount = weekday, WeekendCount = weekend };
        }
        else
        {
            instances[chore.Id] = new PoolInstanceTally(
                chore.Importance,
                WeekdayCount: pool == ScreenTimePool.Weekday ? count : 0,
                WeekendCount: pool == ScreenTimePool.Weekend ? count : 0);
        }
    }

    private sealed record PoolInstanceTally(int Importance, int WeekdayCount, int WeekendCount)
    {
        public int InstanceCount => WeekdayCount + WeekendCount;

        /// <summary>The pool this chore is priced against — the majority pool, weekday on a tie.</summary>
        public ScreenTimePool Pool =>
            WeekendCount > WeekdayCount ? ScreenTimePool.Weekend : ScreenTimePool.Weekday;
    }
}
