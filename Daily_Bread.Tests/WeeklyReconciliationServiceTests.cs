using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards the rewritten screen-time side of weekly reconciliation (MECHANICS_AMENDMENT.md §A/§B/§D):
/// the simplified compounding curve, miss-counting derived from the schedule, importance-share
/// pricing clamped per pool, streak escalation/reset, redemption earn-back (capped), and idempotency.
/// </summary>
public sealed class WeeklyReconciliationServiceTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

    // Default WeekStartDay = Monday. Week A: Mon 2026-07-06 → Sun 2026-07-12.
    private static readonly DateOnly WeekAStart = new(2026, 7, 6);
    private static readonly DateOnly WeekAEnd = new(2026, 7, 12);
    private static readonly DateOnly WeekBStart = new(2026, 7, 13);
    private static readonly DateOnly WeekBEnd = new(2026, 7, 19);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private int _childProfileId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "kid" };
        context.Users.Add(child);

        var profile = new ChildProfile
        {
            UserId = ChildId,
            User = child,
            DisplayName = "Kid",
            WeekdayScreenTimeHours = 40m,   // weekday budget = 40 × 30% × 60 = 720
            WeekendScreenTimeHours = 20m,   // weekend budget = 20 × 50% × 60 = 600
            WeekdayAtRiskPercent = 30,
            WeekendAtRiskPercent = 50
        };
        context.ChildProfiles.Add(profile);

        await context.SaveChangesAsync();
        _childProfileId = profile.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ============================================================
    // Compounding curve
    // ============================================================

    [Fact]
    public void CompoundingMultiplier_Follows_The_Simplified_Curve_And_Freezes_At_Three()
    {
        Assert.Equal(1.0m, WeeklyReconciliationService.CompoundingMultiplier(0));
        Assert.Equal(1.0m, WeeklyReconciliationService.CompoundingMultiplier(1));
        Assert.Equal(1.5m, WeeklyReconciliationService.CompoundingMultiplier(2));
        Assert.Equal(2.0m, WeeklyReconciliationService.CompoundingMultiplier(3));
        Assert.Equal(3.0m, WeeklyReconciliationService.CompoundingMultiplier(4));
        Assert.Equal(3.0m, WeeklyReconciliationService.CompoundingMultiplier(9)); // frozen
    }

    // ============================================================
    // Miss counting from schedule
    // ============================================================

    [Fact]
    public async Task SpecificDays_Chore_Counts_Uncredited_Scheduled_Days_As_Misses_Skipped_Is_Credited()
    {
        // Mon–Fri chore. Approved Mon/Tue, Skipped Wed (credited), no logs Thu/Fri → 2 misses.
        var chore = MakeChore("Trash", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        await AddLogAsync(chore.Id, WeekAStart, ChoreStatus.Approved);            // Mon
        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved); // Tue
        await AddLogAsync(chore.Id, WeekAStart.AddDays(2), ChoreStatus.Skipped);  // Wed (credited)
        // Thu, Fri: no logs → misses.

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        var reduction = Assert.Single(result.ScreenTimeReductions);
        Assert.Equal(2, reduction.MissedOccurrences);
    }

    [Fact]
    public async Task WeeklyFrequency_Chore_Misses_Are_Target_Minus_Credited_Reps()
    {
        // Target 3, one credited rep → 2 misses.
        var chore = MakeChore("Walk Gemma", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 3);
        await SeedChoresAsync(chore);

        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved, allowsMultiple: true);

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        var reduction = Assert.Single(result.ScreenTimeReductions);
        Assert.Equal(2, reduction.MissedOccurrences);
    }

    // ============================================================
    // Pricing integration + per-pool clamp
    // ============================================================

    [Fact]
    public async Task Missing_Everything_On_Weekdays_Loses_Exactly_The_Pool_Budget()
    {
        // Same shape as the pricing test: Σ weekday instance-importance = 16, budget = 720.
        var choreA = MakeChore("A", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday); // 3 inst
        var choreB = MakeChore("B", ChoreScheduleType.SpecificDays, importance: 3,
            activeDays: DaysOfWeek.Tuesday | DaysOfWeek.Thursday);                     // 2 inst
        var choreC = MakeChore("C", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 4);                          // 4 inst
        await SeedChoresAsync(choreA, choreB, choreC);
        // No logs at all → every instance missed.

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(720, result.WeekdayMinutesLost);   // exactly the budget
        Assert.Equal(0, result.WeekendMinutesLost);

        var budget = await GetBudgetAsync(WeekBStart);
        Assert.NotNull(budget);
        Assert.Equal(720, budget!.WeekdayMinutesLost);
        Assert.True(budget.WeekdayMinutesLost <= 720);
    }

    // ============================================================
    // Streak escalation + reset
    // ============================================================

    [Fact]
    public async Task Consecutive_Miss_Weeks_Escalate_Per_Curve_And_Pool_Clamp_Binds()
    {
        // Lone weekday chore: 5 instances, importance 2 → per-instance = 2/10 × 720 = 144.
        var chore = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        // Week A: all 5 missed (no logs) → streak 1, ×1.0, raw 720, applied 720.
        var weekA = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);
        Assert.Equal(720, weekA.WeekdayMinutesLost);
        Assert.Equal(1, (await GetStateAsync(chore.Id))!.ConsecutiveMissWeeks);

        // Week B: all 5 missed again → streak 2, ×1.5, raw 1080, clamped to 720.
        var weekB = await CreateService().ReconcileChildWeekAsync(ChildId, WeekBEnd);
        Assert.Equal(720, weekB.WeekdayMinutesLost); // clamp binds (1080 → 720)
        Assert.Equal(2, (await GetStateAsync(chore.Id))!.ConsecutiveMissWeeks);

        // The Deduction entry (written for week B's upcoming week) records the raw pre-clamp loss
        // and the ×1.5 multiplier.
        var deduction = await GetSingleEntryAsync(WeekBEnd.AddDays(1), ScreenTimeEntryKind.Deduction);
        Assert.Equal(1.5m, deduction.StreakMultiplier);
        Assert.Equal(-1080, deduction.Minutes);
    }

    [Fact]
    public async Task A_Clean_Week_Resets_The_Streak_And_Loss_To_Zero()
    {
        var chore = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        // Week A: all missed → streak 1.
        await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);
        Assert.Equal(1, (await GetStateAsync(chore.Id))!.ConsecutiveMissWeeks);

        // Week B: all 5 days approved → 0 misses → streak reset, no loss.
        for (var i = 0; i < 5; i++)
        {
            await AddLogAsync(chore.Id, WeekBStart.AddDays(i), ChoreStatus.Approved);
        }
        var weekB = await CreateService().ReconcileChildWeekAsync(ChildId, WeekBEnd);

        Assert.Equal(0, weekB.WeekdayMinutesLost);
        Assert.Equal(0, (await GetStateAsync(chore.Id))!.ConsecutiveMissWeeks);
    }

    // ============================================================
    // Redemption earn-back
    // ============================================================

    [Fact]
    public async Task Busted_Week_Credited_Rep_Earns_Half_Its_Price_Back_When_Not_Money()
    {
        // Target 3, importance 1, lone chore → per-instance = 1/3 × 720 = 240.
        var chore = MakeChore("Walk Gemma", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 3);
        await SeedChoresAsync(chore);

        // 1 credited rep (busted), choice = ScreenTime → misses 2 (raw 480), earn-back 120.
        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved,
            allowsMultiple: true, redemption: RedemptionChoice.ScreenTime);

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        // Applied loss 480 − earn-back 120 = 360 net.
        Assert.Equal(360, result.WeekdayMinutesLost);

        var earnBack = await GetSingleEntryAsync(WeekBStart, ScreenTimeEntryKind.EarnBack);
        Assert.Equal(120, earnBack.Minutes);
        Assert.Equal(ScreenTimePool.Weekday, earnBack.Pool);
    }

    [Fact]
    public async Task A_Money_Rep_Earns_No_Screen_Time_Back()
    {
        var chore = MakeChore("Walk Gemma", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 3);
        await SeedChoresAsync(chore);

        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved,
            allowsMultiple: true, redemption: RedemptionChoice.Money);

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(480, result.WeekdayMinutesLost); // full loss, no earn-back
        await using var context = await _contextFactory.CreateDbContextAsync();
        Assert.False(await context.ScreenTimeEntries
            .AnyAsync(e => e.WeekStartDate == WeekBStart && e.Kind == ScreenTimeEntryKind.EarnBack));
    }

    [Fact]
    public async Task Redemption_EarnBack_Is_Capped_At_The_Applied_Loss()
    {
        // Loss chore L (Mon only, imp 1) and made-target chore R (target 1, imp 1) → per-instance 360.
        var choreL = MakeChore("L", ChoreScheduleType.SpecificDays, importance: 1,
            activeDays: DaysOfWeek.Monday);                                   // 1 weekday inst
        var choreR = MakeChore("R", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 1);               // 1 weekday inst
        await SeedChoresAsync(choreL, choreR);
        // Σ weekday importance = 2 → per-instance = 1/2 × 720 = 360.

        // L missed (Mon, no log) → applied loss 360.
        // R: 4 credited reps, target 1 → 3 over-target, all ScreenTime → raw earn-back 3 × 180 = 540.
        for (var i = 0; i < 4; i++)
        {
            await AddLogAsync(choreR.Id, WeekAStart.AddDays(i), ChoreStatus.Approved,
                allowsMultiple: true, redemption: RedemptionChoice.ScreenTime);
        }

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        // Earn-back (540) capped at applied loss (360) → net 0. Never mints surplus.
        Assert.Equal(0, result.WeekdayMinutesLost);

        // The raw EarnBack line item still records the full 540 (snapshot holds the capped net).
        var earnBack = await GetSingleEntryAsync(WeekBStart, ScreenTimeEntryKind.EarnBack);
        Assert.Equal(540, earnBack.Minutes);
    }

    // ============================================================
    // Idempotency
    // ============================================================

    [Fact]
    public async Task Re_Running_Reconciliation_Does_Not_Double_Apply()
    {
        var chore = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        var first = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);
        var second = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(first.WeekdayMinutesLost, second.WeekdayMinutesLost);

        await using var context = await _contextFactory.CreateDbContextAsync();
        // Streak advanced exactly once.
        var state = await context.ChoreScreenTimeStates.SingleAsync(s => s.ChoreDefinitionId == chore.Id);
        Assert.Equal(1, state.ConsecutiveMissWeeks);
        // Exactly one snapshot and one Deduction entry for the upcoming week.
        Assert.Equal(1, await context.ChildWeeklyScreenTimeBudgets.CountAsync(b => b.WeekStartDate == WeekBStart));
        Assert.Equal(1, await context.ScreenTimeEntries
            .CountAsync(e => e.WeekStartDate == WeekBStart && e.Kind == ScreenTimeEntryKind.Deduction));
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ChoreDefinition MakeChore(
        string name,
        ChoreScheduleType scheduleType,
        int importance,
        DaysOfWeek activeDays,
        int weeklyTargetCount = 1) => new()
        {
            Name = name,
            AssignedUserId = ChildId,
            ScheduleType = scheduleType,
            Importance = importance,
            ActiveDays = activeDays,
            WeeklyTargetCount = weeklyTargetCount,
            IsActive = true
        };

    private async Task SeedChoresAsync(params ChoreDefinition[] chores)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ChoreDefinitions.AddRange(chores);
        await context.SaveChangesAsync();
    }

    private async Task AddLogAsync(
        int choreId, DateOnly date, ChoreStatus status,
        bool allowsMultiple = false, RedemptionChoice redemption = RedemptionChoice.None)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ChoreLogs.Add(new ChoreLog
        {
            ChoreDefinitionId = choreId,
            Date = date,
            Status = status,
            AllowsMultiplePerDay = allowsMultiple,
            RedemptionChoice = redemption
        });
        await context.SaveChangesAsync();
    }

    private async Task<ChildWeeklyScreenTimeBudget?> GetBudgetAsync(DateOnly weekStart)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChildWeeklyScreenTimeBudgets
            .FirstOrDefaultAsync(b => b.ChildProfileId == _childProfileId && b.WeekStartDate == weekStart);
    }

    private async Task<ChoreScreenTimeState?> GetStateAsync(int choreId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChoreScreenTimeStates
            .FirstOrDefaultAsync(s => s.ChoreDefinitionId == choreId && s.ChildProfileId == _childProfileId);
    }

    private async Task<ScreenTimeEntry> GetSingleEntryAsync(DateOnly weekStart, ScreenTimeEntryKind kind)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScreenTimeEntries
            .SingleAsync(e => e.ChildProfileId == _childProfileId
                && e.WeekStartDate == weekStart && e.Kind == kind);
    }

    private WeeklyReconciliationService CreateService()
    {
        var dateProvider = new FixedDateProvider(WeekAStart);
        var familySettings = new FamilySettingsService(_contextFactory, dateProvider);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var scheduleService = new ChoreScheduleService(
            _contextFactory, familySettings, cache, NullLogger<ChoreScheduleService>.Instance);
        var pricing = new ScreenTimePricingService(_contextFactory, familySettings, scheduleService);

        return new WeeklyReconciliationService(
            _contextFactory, familySettings, pricing, scheduleService, dateProvider,
            NullLogger<WeeklyReconciliationService>.Instance);
    }

    private sealed class FixedDateProvider(DateOnly today) : IDateProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly Today { get; } = today;
        public DateTime Now => DateTime.UtcNow;
        public DateOnly GetTodayInTimezone(string timezoneId) => Today;
        public string TimeZoneId => "UTC";
        public string TimeZoneDisplayName => "UTC";
        public Task RefreshTimeZoneAsync() => Task.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
