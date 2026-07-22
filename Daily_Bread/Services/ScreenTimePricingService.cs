using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// The single source of truth for screen-time pricing (MECHANICS_AMENDMENT_II.md). Every scheduled
/// chore has a bounded, stable per-occurrence minute price of Importance × 6 (0–60). Prices no longer
/// depend on how many other chores exist (schedule normalization is removed); the per-pool cap is the
/// aggregate ceiling, applied at reconciliation. Reconciliation, the at-risk card, and the meter all
/// read these same numbers.
/// </summary>
public interface IScreenTimePricingService
{
    /// <summary>
    /// Computes the week's pool budgets and per-occurrence minute prices for a child. Honors per-date
    /// schedule overrides (Add/Remove/Move) for SpecificDays chores; WeeklyFrequency chores contribute
    /// their <see cref="ChoreDefinition.WeeklyTargetCount"/> instances to the weekday pool. Only chores
    /// with Importance &gt; 0 assigned to the child participate.
    /// </summary>
    Task<WeekPricing> GetWeekPricingAsync(int childProfileId, DateOnly anyDateInWeek);
}

/// <summary>
/// The computed pricing for one child-week: each pool's penalty budget in minutes (the aggregate cap)
/// plus the per-occurrence minute price of every priced chore.
/// </summary>
public sealed record WeekPricing(
    int WeekdayBudgetMinutes,
    int WeekendBudgetMinutes,
    IReadOnlyDictionary<int, ChorePrice> ChorePrices);

/// <summary>
/// The price of a single chore within a week: which pool it draws from, how many instances are
/// scheduled, and the minutes lost per missed occurrence (Importance × 6, capped at 60).
/// </summary>
public sealed record ChorePrice(
    ScreenTimePool Pool,
    int ScheduledInstances,
    int PerInstanceMinutes);

/// <summary>
/// Pure per-occurrence pricing (MECHANICS_AMENDMENT_II.md rule 1). Kept static and dependency-free so
/// reconciliation, the at-risk card, and the UI all reuse the exact same calculation.
/// </summary>
public static class ScreenTimePricing
{
    /// <summary>The default minutes each Importance point puts at risk for one missed occurrence.</summary>
    public const int DefaultMinutesPerImportancePoint = 6;

    /// <summary>Importance is bounded to this, so the per-occurrence ceiling is MaxImportance × rate.</summary>
    public const int MaxImportance = 10;

    /// <summary>
    /// The minutes lost for one missed occurrence: <c>min(Importance, 10) × minutesPerPoint</c>.
    /// Importance 0 (or blank) means no screen-time impact. The rate is tunable per child
    /// (ChildProfile.MinutesPerImportancePoint); the 10-point cap is the natural ceiling.
    /// </summary>
    public static int PriceOccurrence(int importance, int minutesPerPoint)
    {
        if (importance <= 0 || minutesPerPoint <= 0)
        {
            return 0;
        }

        return Math.Min(importance, MaxImportance) * minutesPerPoint;
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
        // (locked decision — flexible reps have no day). Day-based overrides do not apply.
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

        // Per-occurrence price is Importance × 6 (capped), independent of any other chore.
        var chorePrices = new Dictionary<int, ChorePrice>();
        foreach (var (choreId, tally) in instances)
        {
            var perInstance = ScreenTimePricing.PriceOccurrence(tally.Importance, childProfile.MinutesPerImportancePoint);
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
    /// majority (weekday on a tie), with all its instances counted there.
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
