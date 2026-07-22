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
/// Guards the screen-time side of weekly reconciliation under MECHANICS_AMENDMENT_II.md: bounded
/// per-occurrence prices (Importance × 6), miss-counting from the schedule, the per-pool cap, NO streak
/// multiplier, proportional half-credit late repair, and idempotency. The pure per-pool math lives in
/// <see cref="ReconciliationMathTests"/>; these tests exercise it end-to-end over seeded weeks.
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
            WeekendScreenTimeHours = 20m,   // weekend budget = 20 × 50% × 60 = 600 (explicit below)
            WeekdayAtRiskPercent = 30,
            WeekendAtRiskPercent = 50
        };
        context.ChildProfiles.Add(profile);

        await context.SaveChangesAsync();
        _childProfileId = profile.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ============================================================
    // Miss counting from schedule
    // ============================================================

    [Fact]
    public async Task SpecificDays_Chore_Counts_Uncredited_Days_As_Misses_Help_And_Skipped_Protect()
    {
        // Mon–Fri chore. Approved Mon/Tue (on time), Skipped Wed (excused), no logs Thu/Fri → 2 misses.
        var chore = MakeChore("Trash", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        await AddLogAsync(chore.Id, WeekAStart, ChoreStatus.Approved);            // Mon
        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved); // Tue
        await AddLogAsync(chore.Id, WeekAStart.AddDays(2), ChoreStatus.Skipped);  // Wed (excused)

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        var reduction = Assert.Single(result.ScreenTimeReductions);
        Assert.Equal(2, reduction.MissedOccurrences);
        Assert.Equal(0, reduction.RepairedOccurrences);
    }

    [Fact]
    public async Task WeeklyFrequency_Chore_Misses_Are_Target_Minus_Credited_Reps()
    {
        var chore = MakeChore("Walk Gemma", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 3);
        await SeedChoresAsync(chore);

        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved, allowsMultiple: true);

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        var reduction = Assert.Single(result.ScreenTimeReductions);
        Assert.Equal(2, reduction.MissedOccurrences);
    }

    // ============================================================
    // Bounded pricing + pool sum
    // ============================================================

    [Fact]
    public async Task Missing_Everything_Sums_The_Fixed_Prices_Under_The_Cap()
    {
        var choreA = MakeChore("A", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday); // 3 × 12 = 36
        var choreB = MakeChore("B", ChoreScheduleType.SpecificDays, importance: 3,
            activeDays: DaysOfWeek.Tuesday | DaysOfWeek.Thursday);                     // 2 × 18 = 36
        var choreC = MakeChore("C", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 4);                          // 4 × 6  = 24
        await SeedChoresAsync(choreA, choreB, choreC);

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(96, result.WeekdayMinutesLost);   // 36 + 36 + 24, under the 720 cap
        Assert.Equal(0, result.WeekendMinutesLost);

        var budget = await GetBudgetAsync(WeekBStart);
        Assert.NotNull(budget);
        Assert.Equal(96, budget!.WeekdayMinutesLost);
    }

    // ============================================================
    // No streak escalation (amendment II rule 3)
    // ============================================================

    [Fact]
    public async Task Repeated_Miss_Weeks_Do_Not_Escalate_The_Loss()
    {
        // Lone weekday chore: 5 instances, importance 2 → price 12 each → raw 60, under the cap.
        var chore = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        var weekA = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);
        var weekB = await CreateService().ReconcileChildWeekAsync(ChildId, WeekBEnd);

        Assert.Equal(60, weekA.WeekdayMinutesLost);
        Assert.Equal(60, weekB.WeekdayMinutesLost); // identical — no multiplier

        var deduction = await GetSingleEntryAsync(WeekBEnd.AddDays(1), ScreenTimeEntryKind.Deduction);
        Assert.Null(deduction.StreakMultiplier);
        Assert.Equal(-60, deduction.Minutes);
    }

    [Fact]
    public async Task A_Clean_Week_Resets_The_Loss_To_Zero()
    {
        var chore = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Weekdays);
        await SeedChoresAsync(chore);

        await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        for (var i = 0; i < 5; i++)
        {
            await AddLogAsync(chore.Id, WeekBStart.AddDays(i), ChoreStatus.Approved);
        }
        var weekB = await CreateService().ReconcileChildWeekAsync(ChildId, WeekBEnd);

        Assert.Equal(0, weekB.WeekdayMinutesLost);
    }

    // ============================================================
    // Late repair (amendment II rules 7–8)
    // ============================================================

    [Fact]
    public async Task Late_Completion_Counts_As_A_Full_Miss_Repair_Credit_Deferred()
    {
        // Importance 5 chore Mon/Tue → price 30 each. Mon missed; Tue completed late (Wed). Both are
        // misses; the half-credit repair is deferred to the parent-approved marker (Amendment II §8),
        // so for now the full 60 is lost and no EarnBack is written. The pure repair formula is still
        // guarded by ReconciliationMathTests for when §8 lands.
        var chore = MakeChore("Yard", ChoreScheduleType.SpecificDays, importance: 5,
            activeDays: DaysOfWeek.Monday | DaysOfWeek.Tuesday);
        await SeedChoresAsync(chore);

        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Approved,
            completedAt: new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc));

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(60, result.WeekdayMinutesLost);

        var reduction = Assert.Single(result.ScreenTimeReductions);
        Assert.Equal(2, reduction.MissedOccurrences);
        Assert.Equal(0, reduction.RepairedOccurrences);

        await using var context = await _contextFactory.CreateDbContextAsync();
        Assert.False(await context.ScreenTimeEntries
            .AnyAsync(e => e.WeekStartDate == WeekBStart && e.Kind == ScreenTimeEntryKind.EarnBack));
    }

    [Fact]
    public async Task Daily_Chore_Splits_Misses_Across_Weekday_And_Weekend_Pools()
    {
        // Importance 5 chore every day (price 30). Miss the whole week → 5 weekday misses (150) in the
        // weekday pool and 2 weekend misses (60) in the weekend pool, priced by the actual day of each
        // occurrence (Amendment II §5) — not lumped into one pool.
        var chore = MakeChore("Feed Pet", ChoreScheduleType.SpecificDays, importance: 5,
            activeDays: DaysOfWeek.All);
        await SeedChoresAsync(chore);

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(150, result.WeekdayMinutesLost); // Mon–Fri × 30
        Assert.Equal(60, result.WeekendMinutesLost);  // Sat–Sun × 30
    }

    [Fact]
    public async Task On_Time_Completion_Is_Not_A_Miss_And_Earns_No_Repair()
    {
        var chore = MakeChore("Yard", ChoreScheduleType.SpecificDays, importance: 5,
            activeDays: DaysOfWeek.Monday | DaysOfWeek.Tuesday);
        await SeedChoresAsync(chore);

        // Both done on their own day → no misses at all.
        await AddLogAsync(chore.Id, WeekAStart, ChoreStatus.Completed,
            completedAt: new DateTime(2026, 7, 6, 18, 0, 0, DateTimeKind.Utc));
        await AddLogAsync(chore.Id, WeekAStart.AddDays(1), ChoreStatus.Completed,
            completedAt: new DateTime(2026, 7, 7, 18, 0, 0, DateTimeKind.Utc));

        var result = await CreateService().ReconcileChildWeekAsync(ChildId, WeekAEnd);

        Assert.Equal(0, result.WeekdayMinutesLost);
        Assert.Empty(result.ScreenTimeReductions);
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
        bool allowsMultiple = false, DateTime? completedAt = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ChoreLogs.Add(new ChoreLog
        {
            ChoreDefinitionId = choreId,
            Date = date,
            Status = status,
            AllowsMultiplePerDay = allowsMultiple,
            CompletedAt = completedAt
        });
        await context.SaveChangesAsync();
    }

    private async Task<ChildWeeklyScreenTimeBudget?> GetBudgetAsync(DateOnly weekStart)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChildWeeklyScreenTimeBudgets
            .FirstOrDefaultAsync(b => b.ChildProfileId == _childProfileId && b.WeekStartDate == weekStart);
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
