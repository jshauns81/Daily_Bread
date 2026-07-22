using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Result of weekly reconciliation for a single child under the screen-time model
/// (MECHANICS_AMENDMENT_II.md). Money is earn-only; the two outputs are the flat routine-pool payout
/// and next week's screen-time reduction (bounded prices, per-pool cap, proportional late-repair).
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

    /// <summary>Weekday-pool minutes removed next week: capped applied loss net of late-repair credit.</summary>
    public int WeekdayMinutesLost { get; init; }

    /// <summary>Weekend-pool minutes removed next week: capped applied loss net of late-repair credit.</summary>
    public int WeekendMinutesLost { get; init; }

    /// <summary>Extra minutes added to each vacuum-fill routine target next week (display-only).</summary>
    public int InverseFillAddedMinutesPerRoutine { get; init; }

    /// <summary>Per-chore screen-time reductions that contributed this week.</summary>
    public List<ChoreScreenTimeReduction> ScreenTimeReductions { get; init; } = [];

    public bool HadScreenTimeLoss => WeekdayMinutesLost > 0 || WeekendMinutesLost > 0;
}

/// <summary>Record of a single chore's screen-time contribution for a reconciled week.</summary>
public class ChoreScreenTimeReduction
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public int MissedOccurrences { get; init; }
    public int RepairedOccurrences { get; init; }

    /// <summary>Raw per-chore minutes lost (misses × per-occurrence price), before the pool cap.</summary>
    public int MinutesLost { get; init; }
}

/// <summary>
/// Pure per-pool loss math (MECHANICS_AMENDMENT_II.md — Calculation). Kept static and dependency-free
/// so the formula can be exhaustively tested without a database, and so the service and any future
/// caller agree exactly. Rounds once, at the end.
/// </summary>
public static class ReconciliationMath
{
    /// <summary>The capped loss for a pool before repair: min(rawLoss, poolCap), never negative.</summary>
    public static int AppliedLoss(int rawLoss, int poolCap) =>
        Math.Max(0, Math.Min(rawLoss, poolCap));

    /// <summary>
    /// Minutes removed from the pool this week:
    /// <c>appliedLoss − appliedLoss × 0.5 × (repairedValue ÷ rawLoss)</c>, floored at 0, rounded once.
    /// Uncapped: exact half-credit of what was repaired. Capped: each repair restores half of its
    /// proportionate applied share; repairing everything halves the cap but never erases it.
    /// </summary>
    public static int FinalPoolLoss(int rawLoss, int poolCap, int repairedValue)
    {
        var applied = AppliedLoss(rawLoss, poolCap);
        if (rawLoss <= 0)
        {
            return applied; // 0
        }

        var repaired = Math.Min(Math.Max(0, repairedValue), rawLoss);
        var credit = applied * 0.5 * ((double)repaired / rawLoss);
        var final = applied - credit;
        return (int)Math.Round(Math.Max(0.0, final), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The repair credit as a whole number, defined so the ledger entry and the snapshot always
    /// reconcile exactly: <c>appliedLoss − finalPoolLoss</c>.
    /// </summary>
    public static int RepairCredit(int rawLoss, int poolCap, int repairedValue) =>
        AppliedLoss(rawLoss, poolCap) - FinalPoolLoss(rawLoss, poolCap, repairedValue);
}

/// <summary>
/// Runs weekly reconciliation: pays the flat routine pool and computes next week's screen-time
/// budget under MECHANICS_AMENDMENT_II.md — bounded per-occurrence prices (Importance × 6), no streak
/// multiplier, per-pool cap, and proportional half-credit late repair.
/// </summary>
public interface IWeeklyReconciliationService
{
    Task<List<WeeklyReconciliationResult>> RunWeeklyReconciliationAsync(DateOnly weekEndDate);
    Task<WeeklyReconciliationResult> ReconcileChildWeekAsync(string userId, DateOnly weekEndDate);
    Task<DateOnly?> GetLastReconciliationDateAsync();
    Task<bool> IsReconciliationNeededAsync();
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

    /// <summary>Per-chore miss and late-repair tally for a reconciled week.</summary>
    private sealed class MissTally
    {
        public int Misses;
        public int Repaired;
    }

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

        var weekLogs = await context.ChoreLogs
            .Include(l => l.ChoreDefinition)
            .Where(l => l.ChoreDefinition.AssignedUserId == userId)
            .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
            .ToListAsync();

        var pricing = await _pricingService.GetWeekPricingAsync(childProfile.Id, weekStart);

        var missCounts = await CountMissesAsync(context, childProfile, weekLogs, pricing, weekStart, weekEnd);

        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var (payout, creditedInstances, totalInstances) = await ApplyRoutinePayoutAsync(
                context, childProfile, weekLogs, weekEnd);

            var (weekdayLost, weekendLost, addedPerInverse, reductions) =
                await ApplyScreenTimeReductionAsync(
                    context, childProfile, pricing, missCounts, weekStart, nextWeekStart);

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
    /// Pays the flat routine pool: WeeklyRoutinePayout × (credited ÷ total scheduled). Exact fraction
    /// of the pool, idempotent per (user, week) via a null-chore ChoreEarning row. UNCHANGED.
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

        var alreadyPaid = await context.LedgerTransactions.AnyAsync(t =>
            t.UserId == childProfile.UserId &&
            t.Type == TransactionType.ChoreEarning &&
            t.WeekEndDate == weekEnd &&
            t.ChoreDefinitionId == null);

        if (alreadyPaid)
        {
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
    /// Counts this week's missed and late-repaired occurrences per priced chore, from the SCHEDULE
    /// (MECHANICS_AMENDMENT_II.md). SpecificDays: for each scheduled date, Help and Skipped are full
    /// protection (not a miss); a completion on time is not a miss; a completion whose CompletedAt lands
    /// on a LATER local day is a miss AND a late repair; no credit is a miss. WeeklyFrequency: misses =
    /// max(0, target − credited reps), no repair path. Every priced chore gets an entry (0 when clean).
    /// </summary>
    private async Task<Dictionary<int, MissTally>> CountMissesAsync(
        ApplicationDbContext context, ChildProfile childProfile, List<ChoreLog> weekLogs,
        WeekPricing pricing, DateOnly weekStart, DateOnly weekEnd)
    {
        var tallies = pricing.ChorePrices.Keys.ToDictionary(id => id, _ => new MissTally());
        if (tallies.Count == 0)
        {
            return tallies;
        }

        var pricedChoreIds = tallies.Keys.ToList();
        var pricedChores = await context.ChoreDefinitions
            .Where(c => pricedChoreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

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
                if (!tallies.TryGetValue(chore.Id, out var tally)) continue; // only priced chores
                if (chore.AssignedUserId != childProfile.UserId) continue;

                var logs = logsByChoreDate.GetValueOrDefault((chore.Id, date));

                // Full protection: a Help request or a parent excuse (Skipped) is never a miss.
                if (logs != null && logs.Any(l => l.Status == ChoreStatus.Help)) continue;
                if (logs != null && logs.Any(l => l.Status == ChoreStatus.Skipped)) continue;

                var completedLogs = logs?
                    .Where(l => l.Status is ChoreStatus.Completed or ChoreStatus.Approved)
                    .ToList();

                if (completedLogs is { Count: > 0 })
                {
                    // Done on time if any completion lands on or before its scheduled day (a null
                    // CompletedAt can't prove lateness, so it counts as on time).
                    var onTime = completedLogs.Any(l =>
                        l.CompletedAt == null || ToLocalDate(l.CompletedAt.Value) <= date);

                    if (onTime) continue; // not a miss

                    // Completed, but only late → a miss that has been repaired.
                    tally.Misses++;
                    tally.Repaired++;
                    continue;
                }

                // No credit at all → a plain miss.
                tally.Misses++;
            }
        }

        // WeeklyFrequency: shortfall against the weekly target; no per-occurrence late-repair path.
        foreach (var (choreId, chore) in pricedChores)
        {
            if (chore.ScheduleType != ChoreScheduleType.WeeklyFrequency) continue;

            var creditedReps = weekLogs.Count(l =>
                l.ChoreDefinitionId == choreId && CreditedStatuses.Contains(l.Status));
            tallies[choreId].Misses = Math.Max(0, chore.WeeklyTargetCount - creditedReps);
            tallies[choreId].Repaired = 0;
        }

        return tallies;
    }

    /// <summary>
    /// Applies the screen-time side (MECHANICS_AMENDMENT_II.md): per-chore raw loss = misses ×
    /// per-occurrence price (no multiplier), summed per pool; each pool is capped at its budget; then a
    /// proportional half-credit late-repair reduces the capped loss (never below zero). Writes one
    /// Deduction entry per contributing chore (raw) and one EarnBack per pool with a repair credit; the
    /// <see cref="ChildWeeklyScreenTimeBudget"/> snapshot holds the final (capped, post-repair) values.
    /// Idempotent per (child, next week).
    /// </summary>
    private async Task<(int WeekdayLost, int WeekendLost, int AddedPerInverse, List<ChoreScreenTimeReduction> Reductions)>
        ApplyScreenTimeReductionAsync(
            ApplicationDbContext context, ChildProfile childProfile,
            WeekPricing pricing, Dictionary<int, MissTally> missCounts,
            DateOnly weekStart, DateOnly nextWeekStart)
    {
        var reductions = new List<ChoreScreenTimeReduction>();

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

        var poolRawLoss = new Dictionary<ScreenTimePool, int>
        {
            [ScreenTimePool.Weekday] = 0,
            [ScreenTimePool.Weekend] = 0
        };
        var poolRepairedValue = new Dictionary<ScreenTimePool, int>
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

            var tally = missCounts.GetValueOrDefault(choreId) ?? new MissTally();

            if (tally.Misses == 0)
            {
                // A clean week resets this chore's bookkeeping (kept for display; no penalty effect).
                state.ConsecutiveMissWeeks = 0;
                state.CurrentWeeklyMinutesLost = 0;
            }
            else
            {
                state.ConsecutiveMissWeeks += 1;

                var choreLoss = tally.Misses * price.PerInstanceMinutes;      // raw, no multiplier
                var choreRepaired = tally.Repaired * price.PerInstanceMinutes;

                poolRawLoss[price.Pool] += choreLoss;
                poolRepairedValue[price.Pool] += choreRepaired;
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
                        Minutes = -choreLoss,
                        StreakMultiplier = null,
                        Note = $"{chore.Name}: {tally.Misses} missed × {price.PerInstanceMinutes}m",
                        CreatedAt = now
                    });
                }

                reductions.Add(new ChoreScreenTimeReduction
                {
                    ChoreDefinitionId = choreId,
                    ChoreName = chore.Name,
                    MissedOccurrences = tally.Misses,
                    RepairedOccurrences = tally.Repaired,
                    MinutesLost = choreLoss
                });
            }

            state.LastEvaluatedWeekStart = weekStart;
            state.ModifiedAt = now;
        }

        // Per-pool: cap, then proportional half-credit repair (MECHANICS_AMENDMENT_II.md — Calculation).
        var netWeekdayLost = ReconciliationMath.FinalPoolLoss(
            poolRawLoss[ScreenTimePool.Weekday], pricing.WeekdayBudgetMinutes,
            poolRepairedValue[ScreenTimePool.Weekday]);
        var netWeekendLost = ReconciliationMath.FinalPoolLoss(
            poolRawLoss[ScreenTimePool.Weekend], pricing.WeekendBudgetMinutes,
            poolRepairedValue[ScreenTimePool.Weekend]);

        var weekdayRepairCredit = ReconciliationMath.RepairCredit(
            poolRawLoss[ScreenTimePool.Weekday], pricing.WeekdayBudgetMinutes,
            poolRepairedValue[ScreenTimePool.Weekday]);
        var weekendRepairCredit = ReconciliationMath.RepairCredit(
            poolRawLoss[ScreenTimePool.Weekend], pricing.WeekendBudgetMinutes,
            poolRepairedValue[ScreenTimePool.Weekend]);

        AddRepairEntry(entries, childProfile.Id, nextWeekStart, ScreenTimePool.Weekday, weekdayRepairCredit, now);
        AddRepairEntry(entries, childProfile.Id, nextWeekStart, ScreenTimePool.Weekend, weekendRepairCredit, now);

        context.ScreenTimeEntries.AddRange(entries);

        // Vacuum-fill soft targets (display-only): use the post-cap applied loss (pre-repair).
        var totalAppliedLoss =
            ReconciliationMath.AppliedLoss(poolRawLoss[ScreenTimePool.Weekday], pricing.WeekdayBudgetMinutes) +
            ReconciliationMath.AppliedLoss(poolRawLoss[ScreenTimePool.Weekend], pricing.WeekendBudgetMinutes);
        var inverseFillCount = await context.ChoreDefinitions.CountAsync(c =>
            c.AssignedUserId == childProfile.UserId && c.IsActive && c.IsInverseFill);
        var addedPerInverse = inverseFillCount > 0 ? totalAppliedLoss / inverseFillCount : 0;

        context.ChildWeeklyScreenTimeBudgets.Add(new ChildWeeklyScreenTimeBudget
        {
            ChildProfileId = childProfile.Id,
            WeekStartDate = nextWeekStart,
            WeekdayBasePoolMinutes = (int)Math.Round(childProfile.WeekdayScreenTimeHours * 60, MidpointRounding.AwayFromZero),
            WeekendBasePoolMinutes = (int)Math.Round(childProfile.WeekendScreenTimeHours * 60, MidpointRounding.AwayFromZero),
            WeekdayMinutesLost = netWeekdayLost,
            WeekendMinutesLost = netWeekendLost,
            InverseFillAddedMinutesPerRoutine = addedPerInverse,
            CreatedAt = now
        });

        return (netWeekdayLost, netWeekendLost, addedPerInverse, reductions);
    }

    private static void AddRepairEntry(
        List<ScreenTimeEntry> entries, int childProfileId, DateOnly nextWeekStart,
        ScreenTimePool pool, int credit, DateTime now)
    {
        if (credit <= 0)
        {
            return;
        }

        entries.Add(new ScreenTimeEntry
        {
            ChildProfileId = childProfileId,
            WeekStartDate = nextWeekStart,
            Pool = pool,
            Kind = ScreenTimeEntryKind.EarnBack,
            ChoreDefinitionId = null,
            Minutes = credit, // positive restores budget
            StreakMultiplier = null,
            Note = "Late repair credit",
            CreatedAt = now
        });
    }

    /// <summary>Converts a UTC timestamp to the family-local calendar date (for on-time vs late).</summary>
    private DateOnly ToLocalDate(DateTime utcTime)
    {
        var tz = ResolveTimeZone(_dateProvider.TimeZoneId);
        var asUtc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(asUtc, tz));
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
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
        var currentWeekStart = await _familySettingsService.GetWeekStartForDateAsync(_dateProvider.Today);
        return currentWeekStart.AddDays(-1);
    }

    public async Task<bool> IsReconciliationNeededAsync()
    {
        var weekEndToReconcile = await GetWeekEndToReconcileAsync();
        var lastReconciliation = await GetLastReconciliationDateAsync();
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
