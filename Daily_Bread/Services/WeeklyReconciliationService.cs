using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Result of weekly reconciliation for a single child under the screen-time model.
/// Money is earn-only (no penalties); the two outputs are the flat routine-pool payout and the
/// next week's screen-time reduction. See CHORE_SCREENTIME_REDESIGN.md §3–4 and
/// MECHANICS_AMENDMENT.md §A/§B/§D.
/// </summary>
public class WeeklyReconciliationResult
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }

    /// <summary>Flat routine-pool amount credited this week (WeeklyRoutinePayout × credited ÷ total).</summary>
    public decimal RoutinePayout { get; init; }

    /// <summary>Number of routine instances credited (Completed | Approved | Skipped).</summary>
    public int RoutineInstancesCredited { get; init; }

    /// <summary>Total routine instances scheduled this week (the payout denominator).</summary>
    public int RoutineInstancesTotal { get; init; }

    /// <summary>
    /// Weekday-pool screen-time minutes removed next week: the post-clamp applied loss net of
    /// redemption earn-back (§A/§D). Never exceeds the weekday penalty budget.
    /// </summary>
    public int WeekdayMinutesLost { get; init; }

    /// <summary>
    /// Weekend-pool screen-time minutes removed next week: the post-clamp applied loss net of
    /// redemption earn-back (§A/§D). Never exceeds the weekend penalty budget.
    /// </summary>
    public int WeekendMinutesLost { get; init; }

    /// <summary>Extra minutes added to each vacuum-fill routine target next week (display-only).</summary>
    public int InverseFillAddedMinutesPerRoutine { get; init; }

    /// <summary>Per-chore screen-time reductions that contributed this week.</summary>
    public List<ChoreScreenTimeReduction> ScreenTimeReductions { get; init; } = [];

    public bool HadScreenTimeLoss => WeekdayMinutesLost > 0 || WeekendMinutesLost > 0;
}

/// <summary>
/// Record of a single chore's screen-time contribution for a reconciled week.
/// </summary>
public class ChoreScreenTimeReduction
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public int MissedOccurrences { get; init; }
    public int ConsecutiveMissWeeks { get; init; }

    /// <summary>Raw per-chore minutes lost (misses × per-instance price × streak), before pool clamp.</summary>
    public int MinutesLost { get; init; }
}

/// <summary>
/// Service for running weekly reconciliation: pays the flat routine pool and computes the next
/// week's screen-time budget (importance-share pricing, compounding streak, per-pool clamp,
/// redemption earn-back). See MECHANICS_AMENDMENT.md §A/§B/§D.
/// </summary>
public interface IWeeklyReconciliationService
{
    /// <summary>
    /// Runs weekly reconciliation for all children.
    /// Should be called at the end of each week (e.g., Sunday night).
    /// </summary>
    Task<List<WeeklyReconciliationResult>> RunWeeklyReconciliationAsync(DateOnly weekEndDate);

    /// <summary>
    /// Runs weekly reconciliation for a specific child.
    /// </summary>
    Task<WeeklyReconciliationResult> ReconcileChildWeekAsync(string userId, DateOnly weekEndDate);

    /// <summary>
    /// Gets the last reconciliation date from audit records.
    /// </summary>
    Task<DateOnly?> GetLastReconciliationDateAsync();

    /// <summary>
    /// Checks if reconciliation is needed (the most recently completed week has not yet been reconciled).
    /// </summary>
    Task<bool> IsReconciliationNeededAsync();

    /// <summary>
    /// The week-end date of the most recently completed week (the one reconciliation should process) —
    /// the day before the current week's start. This is the value to pass to
    /// <see cref="RunWeeklyReconciliationAsync"/>.
    /// </summary>
    Task<DateOnly> GetWeekEndToReconcileAsync();
}

public class WeeklyReconciliationService : IWeeklyReconciliationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IScreenTimePricingService _pricingService;
    private readonly IChoreScheduleService _scheduleService;
    private readonly IDateProvider _dateProvider;
    private readonly ILogger<WeeklyReconciliationService> _logger;

    // App setting key for tracking last reconciliation
    private const string LastReconciliationKey = "LastWeeklyReconciliation";

    // Statuses that count as "the child got credit" for a routine instance (pays its slice, no ST hit).
    private static readonly ChoreStatus[] CreditedStatuses =
        [ChoreStatus.Completed, ChoreStatus.Approved, ChoreStatus.Skipped];

    public WeeklyReconciliationService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IFamilySettingsService familySettingsService,
        IScreenTimePricingService pricingService,
        IChoreScheduleService scheduleService,
        IDateProvider dateProvider,
        ILogger<WeeklyReconciliationService> logger)
    {
        _contextFactory = contextFactory;
        _familySettingsService = familySettingsService;
        _pricingService = pricingService;
        _scheduleService = scheduleService;
        _dateProvider = dateProvider;
        _logger = logger;
    }

    /// <summary>
    /// Cumulative compounding multiplier for N consecutive weeks of missing the same chore.
    /// Simplified curve (MECHANICS_AMENDMENT.md §B, locked): ×1.0 → ×1.5 → ×2.0 → ×3.0, frozen at ×3.0.
    /// The multiplier scales a chore's share; the per-pool budget (from pricing) is the aggregate clamp.
    /// </summary>
    internal static decimal CompoundingMultiplier(int consecutiveMissWeeks) => consecutiveMissWeeks switch
    {
        <= 1 => 1.0m,
        2 => 1.5m,
        3 => 2.0m,
        _ => 3.0m // 4+ weeks: frozen
    };

    private static int ToIntMinutes(decimal minutes) =>
        (int)Math.Round(minutes, MidpointRounding.AwayFromZero);

    public async Task<List<WeeklyReconciliationResult>> RunWeeklyReconciliationAsync(DateOnly weekEndDate)
    {
        _logger.LogInformation("Starting weekly reconciliation for week ending {WeekEnd}", weekEndDate);

        await using var context = await _contextFactory.CreateDbContextAsync();

        var childProfiles = await context.ChildProfiles
            .Include(p => p.User)
            .Where(p => p.IsActive)
            .ToListAsync();

        var results = new List<WeeklyReconciliationResult>();

        foreach (var child in childProfiles)
        {
            try
            {
                var result = await ReconcileChildWeekAsync(child.UserId, weekEndDate);
                results.Add(result);

                _logger.LogInformation(
                    "Reconciled {User}: routine payout ${Payout:F2} ({Credited}/{Total}), " +
                    "screen time -{Weekday}m weekday / -{Weekend}m weekend",
                    result.UserName, result.RoutinePayout, result.RoutineInstancesCredited,
                    result.RoutineInstancesTotal, result.WeekdayMinutesLost, result.WeekendMinutesLost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconcile week for user {UserId}", child.UserId);
            }
        }

        await RecordReconciliationAsync(weekEndDate);

        _logger.LogInformation(
            "Weekly reconciliation complete. {Total} children processed, {WithLoss} lost screen time",
            results.Count, results.Count(r => r.HadScreenTimeLoss));

        return results;
    }

    public async Task<WeeklyReconciliationResult> ReconcileChildWeekAsync(string userId, DateOnly weekEndDate)
    {
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(weekEndDate);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(weekEndDate);
        var nextWeekStart = weekEnd.AddDays(1);

        await using var context = await _contextFactory.CreateDbContextAsync();

        var childProfile = await context.ChildProfiles
            .Include(p => p.User)
            .Include(p => p.LedgerAccounts.Where(a => a.IsActive && a.IsDefault))
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (childProfile == null)
        {
            return new WeeklyReconciliationResult
            {
                UserId = userId,
                UserName = "Unknown",
                WeekStart = weekStart,
                WeekEnd = weekEnd
            };
        }

        // All of this child's chore logs for the week, with their definitions.
        var weekLogs = await context.ChoreLogs
            .Include(l => l.ChoreDefinition)
            .Where(l => l.ChoreDefinition.AssignedUserId == userId)
            .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
            .ToListAsync();

        // Single source of truth for per-instance minute prices and pool budgets (§A). Fetched before
        // opening the write transaction so its own reads don't nest inside it.
        var pricing = await _pricingService.GetWeekPricingAsync(childProfile.Id, weekStart);

        // Miss counting is derived from the SCHEDULE (nothing sweeps Missed-status rows) — done here
        // (before the transaction) because it issues override-aware schedule reads.
        var missCounts = await CountMissesAsync(context, childProfile, weekLogs, pricing, weekStart, weekEnd);

        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var (payout, creditedInstances, totalInstances) = await ApplyRoutinePayoutAsync(
                context, childProfile, weekLogs, weekEnd);

            var (weekdayLost, weekendLost, addedPerInverse, reductions) =
                await ApplyScreenTimeReductionAsync(
                    context, childProfile, weekLogs, pricing, missCounts, weekStart, nextWeekStart);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new WeeklyReconciliationResult
            {
                UserId = userId,
                UserName = childProfile.DisplayName,
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                RoutinePayout = payout,
                RoutineInstancesCredited = creditedInstances,
                RoutineInstancesTotal = totalInstances,
                WeekdayMinutesLost = weekdayLost,
                WeekendMinutesLost = weekendLost,
                InverseFillAddedMinutesPerRoutine = addedPerInverse,
                ScreenTimeReductions = reductions
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to reconcile week for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Pays the flat routine pool: WeeklyRoutinePayout × (credited instances ÷ total scheduled
    /// instances). Computed as an exact fraction of the pool (not a sum of rounded slices) so the
    /// ceiling is always exactly the pool (§3.2). Idempotent per (user, week): a null-chore
    /// ChoreEarning row stamped with the week end marks the pool as already paid. UNCHANGED.
    /// </summary>
    private async Task<(decimal Payout, int Credited, int Total)> ApplyRoutinePayoutAsync(
        ApplicationDbContext context, ChildProfile childProfile, List<ChoreLog> weekLogs,
        DateOnly weekEnd)
    {
        var routineLogs = weekLogs.Where(l => l.ChoreDefinition.Kind == ChoreKind.Routine).ToList();
        var totalInstances = routineLogs.Count;
        var creditedInstances = routineLogs.Count(l => CreditedStatuses.Contains(l.Status));

        if (totalInstances == 0)
        {
            return (0m, 0, 0);
        }

        var payout = Math.Round(
            childProfile.WeeklyRoutinePayout * creditedInstances / totalInstances, 2,
            MidpointRounding.AwayFromZero);

        var defaultAccount = childProfile.LedgerAccounts.FirstOrDefault();
        if (payout <= 0 || defaultAccount == null)
        {
            return (payout, creditedInstances, totalInstances);
        }

        // Idempotency: the routine pool payout is the one ChoreEarning row for the week with no chore.
        var alreadyPaid = await context.LedgerTransactions.AnyAsync(t =>
            t.UserId == childProfile.UserId &&
            t.Type == TransactionType.ChoreEarning &&
            t.WeekEndDate == weekEnd &&
            t.ChoreDefinitionId == null);

        if (alreadyPaid)
        {
            _logger.LogDebug("Routine pool already paid for {UserId} week ending {WeekEnd}",
                childProfile.UserId, weekEnd);
            return (payout, creditedInstances, totalInstances);
        }

        context.LedgerTransactions.Add(new LedgerTransaction
        {
            LedgerAccountId = defaultAccount.Id,
            UserId = childProfile.UserId,
            ChoreDefinitionId = null,
            WeekEndDate = weekEnd,
            Amount = payout,
            Type = TransactionType.ChoreEarning,
            Description = $"Routine pool ({creditedInstances}/{totalInstances} done)",
            TransactionDate = weekEnd,
            CreatedAt = DateTime.UtcNow
        });

        return (payout, creditedInstances, totalInstances);
    }

    /// <summary>
    /// Counts this week's missed instances per priced chore, derived from the SCHEDULE
    /// (MECHANICS_AMENDMENT.md §A/§D). SpecificDays: for each scheduled date (overrides honored via
    /// <see cref="IChoreScheduleService.GetChoresForDateAsync"/>) a miss = no credited log and not
    /// Help. WeeklyFrequency: misses = max(0, target − credited reps). Only chores present in
    /// <paramref name="pricing"/> participate; every one gets an entry (0 when clean) so streaks reset.
    /// </summary>
    private async Task<Dictionary<int, int>> CountMissesAsync(
        ApplicationDbContext context, ChildProfile childProfile, List<ChoreLog> weekLogs,
        WeekPricing pricing, DateOnly weekStart, DateOnly weekEnd)
    {
        var misses = pricing.ChorePrices.Keys.ToDictionary(id => id, _ => 0);
        if (misses.Count == 0)
        {
            return misses;
        }

        var pricedChoreIds = misses.Keys.ToList();
        var pricedChores = await context.ChoreDefinitions
            .Where(c => pricedChoreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        // Index logs by (chore, date) for SpecificDays lookups and keep credited-rep counts for flex.
        var logsByChoreDate = weekLogs
            .GroupBy(l => (l.ChoreDefinitionId, l.Date))
            .ToDictionary(g => g.Key, g => g.ToList());

        // SpecificDays: enumerate the 7 dates so per-date overrides (Add/Remove/Move) apply.
        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            var choresToday = await _scheduleService.GetChoresForDateAsync(date);
            foreach (var chore in choresToday)
            {
                if (chore.ScheduleType != ChoreScheduleType.SpecificDays) continue;
                if (!misses.ContainsKey(chore.Id)) continue; // only priced chores
                if (chore.AssignedUserId != childProfile.UserId) continue;

                var logs = logsByChoreDate.GetValueOrDefault((chore.Id, date));
                var credited = logs != null && logs.Any(l => CreditedStatuses.Contains(l.Status));
                var help = logs != null && logs.Any(l => l.Status == ChoreStatus.Help);

                // A miss is a scheduled (chore, date) with no credit and no protective Help request.
                if (!credited && !help)
                {
                    misses[chore.Id]++;
                }
            }
        }

        // WeeklyFrequency: shortfall against the weekly target from credited reps anywhere in the week.
        foreach (var (choreId, chore) in pricedChores)
        {
            if (chore.ScheduleType != ChoreScheduleType.WeeklyFrequency) continue;

            var creditedReps = weekLogs.Count(l =>
                l.ChoreDefinitionId == choreId && CreditedStatuses.Contains(l.Status));
            misses[choreId] = Math.Max(0, chore.WeeklyTargetCount - creditedReps);
        }

        return misses;
    }

    /// <summary>
    /// Applies the screen-time side of reconciliation (MECHANICS_AMENDMENT.md §A/§B/§D):
    /// per-chore loss = misses × per-instance price × streak multiplier, attributed to the chore's
    /// pool; each pool's raw total is clamped to that pool's budget (the "miss everything = budget"
    /// guarantee); redemption earn-back for over-target / busted-week reps (half the per-instance
    /// price, capped per pool at the applied loss) nets down next week's loss. Writes one Deduction
    /// <see cref="ScreenTimeEntry"/> per contributing chore (raw per-chore loss) and one EarnBack per
    /// redeeming chore (raw earn-back); the <see cref="ChildWeeklyScreenTimeBudget"/> snapshot holds
    /// the clamped, net (post-earn-back) values. Idempotent per (child, next week).
    /// </summary>
    private async Task<(int WeekdayLost, int WeekendLost, int AddedPerInverse, List<ChoreScreenTimeReduction> Reductions)>
        ApplyScreenTimeReductionAsync(
            ApplicationDbContext context, ChildProfile childProfile, List<ChoreLog> weekLogs,
            WeekPricing pricing, Dictionary<int, int> missCounts,
            DateOnly weekStart, DateOnly nextWeekStart)
    {
        var reductions = new List<ChoreScreenTimeReduction>();

        // Idempotency guard: only process screen time once per (child, upcoming week). Re-running must
        // not double-increment streaks or re-write entries.
        var existingBudget = await context.ChildWeeklyScreenTimeBudgets
            .FirstOrDefaultAsync(b => b.ChildProfileId == childProfile.Id && b.WeekStartDate == nextWeekStart);
        if (existingBudget != null)
        {
            return (existingBudget.WeekdayMinutesLost, existingBudget.WeekendMinutesLost,
                existingBudget.InverseFillAddedMinutesPerRoutine, reductions);
        }

        var states = await context.ChoreScreenTimeStates
            .Where(s => s.ChildProfileId == childProfile.Id)
            .ToDictionaryAsync(s => s.ChoreDefinitionId);

        var pricedChoreIds = pricing.ChorePrices.Keys.ToList();
        var choresById = await context.ChoreDefinitions
            .Where(c => pricedChoreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        // Raw (pre-clamp) minutes lost per pool, and the ledger rows we will write.
        var poolRawLoss = new Dictionary<ScreenTimePool, int>
        {
            [ScreenTimePool.Weekday] = 0,
            [ScreenTimePool.Weekend] = 0
        };
        var entries = new List<ScreenTimeEntry>();
        var now = DateTime.UtcNow;

        foreach (var (choreId, price) in pricing.ChorePrices)
        {
            if (!choresById.TryGetValue(choreId, out var chore))
            {
                continue;
            }

            if (!states.TryGetValue(choreId, out var state))
            {
                state = new ChoreScreenTimeState
                {
                    ChoreDefinitionId = choreId,
                    ChildProfileId = childProfile.Id
                };
                context.ChoreScreenTimeStates.Add(state);
                states[choreId] = state;
            }

            var misses = missCounts.GetValueOrDefault(choreId, 0);

            if (misses == 0)
            {
                // A clean week resets this chore's streak to 0 (§B / §5.3).
                state.ConsecutiveMissWeeks = 0;
                state.CurrentWeeklyMinutesLost = 0;
            }
            else
            {
                state.ConsecutiveMissWeeks += 1;
                var multiplier = CompoundingMultiplier(state.ConsecutiveMissWeeks);
                var choreLoss = ToIntMinutes(misses * price.PerInstanceMinutes * multiplier);

                poolRawLoss[price.Pool] += choreLoss;
                state.CurrentWeeklyMinutesLost = choreLoss;

                if (choreLoss > 0)
                {
                    entries.Add(new ScreenTimeEntry
                    {
                        ChildProfileId = childProfile.Id,
                        WeekStartDate = nextWeekStart,
                        Pool = price.Pool,
                        Kind = ScreenTimeEntryKind.Deduction,
                        ChoreDefinitionId = choreId,
                        Minutes = -choreLoss, // negative removes budget
                        StreakMultiplier = multiplier,
                        Note = $"{chore.Name}: {misses} missed × {price.PerInstanceMinutes}m × {multiplier:0.##}",
                        CreatedAt = now
                    });
                }

                reductions.Add(new ChoreScreenTimeReduction
                {
                    ChoreDefinitionId = choreId,
                    ChoreName = chore.Name,
                    MissedOccurrences = misses,
                    ConsecutiveMissWeeks = state.ConsecutiveMissWeeks,
                    MinutesLost = choreLoss
                });
            }

            state.LastEvaluatedWeekStart = weekStart;
            state.ModifiedAt = now;
        }

        // Clamp each pool's raw loss to its budget — the per-pool cap that makes "miss everything =
        // exactly the budget" hold even after the streak multiplier pushes a chore over its share (§A/§B).
        var appliedWeekday = Math.Min(poolRawLoss[ScreenTimePool.Weekday], pricing.WeekdayBudgetMinutes);
        var appliedWeekend = Math.Min(poolRawLoss[ScreenTimePool.Weekend], pricing.WeekendBudgetMinutes);

        // Redemption earn-back (§D): over-target reps in a made-target week, plus ALL credited reps in a
        // busted week, earn ST back (half the per-instance price) unless the child chose Money.
        var poolEarnBack = new Dictionary<ScreenTimePool, int>
        {
            [ScreenTimePool.Weekday] = 0,
            [ScreenTimePool.Weekend] = 0
        };

        foreach (var (choreId, price) in pricing.ChorePrices)
        {
            if (!choresById.TryGetValue(choreId, out var chore)) continue;
            if (chore.ScheduleType != ChoreScheduleType.WeeklyFrequency) continue;

            var creditedReps = weekLogs
                .Where(l => l.ChoreDefinitionId == choreId && CreditedStatuses.Contains(l.Status))
                .OrderBy(l => l.Id)
                .ToList();
            if (creditedReps.Count == 0) continue;

            var madeTarget = creditedReps.Count >= chore.WeeklyTargetCount;
            // Made target → only the over-target reps redeem; busted → every credited rep redeems.
            var redemptiveReps = madeTarget
                ? creditedReps.Skip(chore.WeeklyTargetCount)
                : creditedReps;

            var earningReps = redemptiveReps.Count(l => l.RedemptionChoice != RedemptionChoice.Money);
            if (earningReps == 0) continue;

            var perRepEarn = (int)Math.Round(price.PerInstanceMinutes * 0.5, MidpointRounding.AwayFromZero);
            var choreEarnBack = earningReps * perRepEarn;
            if (choreEarnBack <= 0) continue;

            poolEarnBack[price.Pool] += choreEarnBack;

            entries.Add(new ScreenTimeEntry
            {
                ChildProfileId = childProfile.Id,
                WeekStartDate = nextWeekStart,
                Pool = price.Pool,
                Kind = ScreenTimeEntryKind.EarnBack,
                ChoreDefinitionId = choreId,
                Minutes = choreEarnBack, // positive restores budget
                StreakMultiplier = null,
                Note = $"Redemption earn-back: {chore.Name} ({earningReps} rep(s) × {perRepEarn}m)",
                CreatedAt = now
            });
        }

        // Cap earn-back per pool at that pool's applied loss (redemption recovers, never mints surplus).
        var weekdayEarnBack = Math.Min(poolEarnBack[ScreenTimePool.Weekday], appliedWeekday);
        var weekendEarnBack = Math.Min(poolEarnBack[ScreenTimePool.Weekend], appliedWeekend);

        var netWeekdayLost = appliedWeekday - weekdayEarnBack;
        var netWeekendLost = appliedWeekend - weekendEarnBack;

        context.ScreenTimeEntries.AddRange(entries);

        // Vacuum-fill soft targets (§C): added_minutes(routine) = AppliedWeeklyLoss(post-clamp) × share.
        // The snapshot has a single per-routine scalar, so we persist the equal-split value (= the
        // average of the share-weighted targets, since QOL shares sum to 100%). Per-routine
        // share-weighting from QolShare is applied at display time; persisting it needs a schema change
        // deferred to the QOL phase.
        var totalAppliedLoss = appliedWeekday + appliedWeekend;
        var inverseFillCount = await context.ChoreDefinitions.CountAsync(c =>
            c.AssignedUserId == childProfile.UserId && c.IsActive && c.IsInverseFill);
        var addedPerInverse = inverseFillCount > 0 ? totalAppliedLoss / inverseFillCount : 0;

        // Base pool minutes = the child's full weekly pool (the penalty budget from pricing is the
        // at-risk cap on MinutesLost, not the whole allotment); MinutesLost = the net applied loss.
        context.ChildWeeklyScreenTimeBudgets.Add(new ChildWeeklyScreenTimeBudget
        {
            ChildProfileId = childProfile.Id,
            WeekStartDate = nextWeekStart,
            WeekdayBasePoolMinutes = ToIntMinutes(childProfile.WeekdayScreenTimeHours * 60),
            WeekendBasePoolMinutes = ToIntMinutes(childProfile.WeekendScreenTimeHours * 60),
            WeekdayMinutesLost = netWeekdayLost,
            WeekendMinutesLost = netWeekendLost,
            InverseFillAddedMinutesPerRoutine = addedPerInverse,
            CreatedAt = now
        });

        return (netWeekdayLost, netWeekendLost, addedPerInverse, reductions);
    }

    public async Task<DateOnly?> GetLastReconciliationDateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LastReconciliationKey);

        if (setting != null && DateOnly.TryParse(setting.Value, out var date))
        {
            return date;
        }

        return null;
    }

    public async Task<DateOnly> GetWeekEndToReconcileAsync()
    {
        // The most recently COMPLETED week ends the day before the current week starts.
        var currentWeekStart = await _familySettingsService.GetWeekStartForDateAsync(_dateProvider.Today);
        return currentWeekStart.AddDays(-1);
    }

    public async Task<bool> IsReconciliationNeededAsync()
    {
        var weekEndToReconcile = await GetWeekEndToReconcileAsync();
        var lastReconciliation = await GetLastReconciliationDateAsync();

        // Needed until we have reconciled the most recently completed week.
        return lastReconciliation == null || lastReconciliation < weekEndToReconcile;
    }

    private async Task RecordReconciliationAsync(DateOnly weekEndDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LastReconciliationKey);

        if (setting == null)
        {
            setting = new AppSetting
            {
                Key = LastReconciliationKey,
                Value = weekEndDate.ToString("O"),
                Description = "Date of last weekly chore reconciliation",
                DataType = SettingDataType.String
            };
            context.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = weekEndDate.ToString("O");
            setting.ModifiedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
    }
}
