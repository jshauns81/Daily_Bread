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
/// Guards the importance-share screen-time pricing math (MECHANICS_AMENDMENT.md §A): pool budgets are
/// a fixed share of the pool hours, per-instance minutes split proportionally to importance, and
/// missing every scheduled instance loses exactly the pool budget (never more, up to rounding).
/// </summary>
public sealed class ScreenTimePricingServiceTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

    // Monday 2026-07-06 → week runs Mon 06 through Sun 12 (default WeekStartDay = Monday).
    private static readonly DateOnly AnyDateInWeek = new(2026, 7, 6);

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
            WeekdayScreenTimeHours = 40m,
            WeekendScreenTimeHours = 20m,
            WeekdayAtRiskPercent = 30,
            WeekendAtRiskPercent = 50
        };
        context.ChildProfiles.Add(profile);

        await context.SaveChangesAsync();
        _childProfileId = profile.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ============================================================
    // Pure ScreenTimePricing.PriceInstance math
    // ============================================================

    [Fact]
    public void PriceInstance_Splits_The_Pool_Budget_Proportionally_To_Importance()
    {
        // Weekday pool: budget 720, Σimportance-across-instances = 16.
        Assert.Equal(90, ScreenTimePricing.PriceInstance(importance: 2, poolImportanceSum: 16, poolBudgetMinutes: 720));
        Assert.Equal(135, ScreenTimePricing.PriceInstance(importance: 3, poolImportanceSum: 16, poolBudgetMinutes: 720));
        Assert.Equal(45, ScreenTimePricing.PriceInstance(importance: 1, poolImportanceSum: 16, poolBudgetMinutes: 720));
    }

    [Fact]
    public void PriceInstance_Missing_Every_Instance_Loses_Exactly_The_Budget()
    {
        const int budget = 720;
        // Instance importances: three 2s, two 3s, four 1s → Σ = 16.
        int[] instanceImportances = [2, 2, 2, 3, 3, 1, 1, 1, 1];
        var sum = instanceImportances.Sum();

        var total = instanceImportances.Sum(imp => ScreenTimePricing.PriceInstance(imp, sum, budget));

        Assert.Equal(budget, total);
    }

    [Fact]
    public void PriceInstance_Zero_Importance_Sum_Returns_Zero()
    {
        Assert.Equal(0, ScreenTimePricing.PriceInstance(importance: 5, poolImportanceSum: 0, poolBudgetMinutes: 720));
    }

    [Fact]
    public void PriceInstance_Adding_A_Higher_Importance_Chore_Lowers_Everyone_Elses_Per_Instance_Minutes()
    {
        // Before: one chore importance 2 alone (Σ = 8) prices at 180 min.
        var before = ScreenTimePricing.PriceInstance(importance: 2, poolImportanceSum: 8, poolBudgetMinutes: 720);

        // After adding an importance-8 chore the pool sum rises to 16; the same chore now costs less.
        var after = ScreenTimePricing.PriceInstance(importance: 2, poolImportanceSum: 16, poolBudgetMinutes: 720);

        Assert.Equal(180, before);
        Assert.True(after < before);
        Assert.Equal(90, after);
    }

    // ============================================================
    // Service-level pricing over a seeded week
    // ============================================================

    [Fact]
    public async Task GetWeekPricing_Prices_Weekday_SpecificDays_And_WeeklyFrequency_Instances()
    {
        // Chore A: Mon/Wed/Fri (3 weekday instances), importance 2.
        var choreA = MakeChore("Trash", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday);
        // Chore B: Tue/Thu (2 weekday instances), importance 3.
        var choreB = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 3,
            activeDays: DaysOfWeek.Tuesday | DaysOfWeek.Thursday);
        // Chore C: WeeklyFrequency target 4 (4 weekday instances by the flex rule), importance 1.
        var choreC = MakeChore("Walk Gemma", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 4);

        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.ChoreDefinitions.AddRange(choreA, choreB, choreC);
            await context.SaveChangesAsync();
        }

        var service = CreateService();
        var pricing = await service.GetWeekPricingAsync(_childProfileId, AnyDateInWeek);

        // Budgets: 40h × 30% × 60 = 720, 20h × 50% × 60 = 600.
        Assert.Equal(720, pricing.WeekdayBudgetMinutes);
        Assert.Equal(600, pricing.WeekendBudgetMinutes);

        // Weekday Σimportance-across-instances = 2×3 + 3×2 + 1×4 = 16.
        var priceA = pricing.ChorePrices[choreA.Id];
        Assert.Equal(ScreenTimePool.Weekday, priceA.Pool);
        Assert.Equal(3, priceA.ScheduledInstances);
        Assert.Equal(90, priceA.PerInstanceMinutes); // 2/16 × 720

        var priceB = pricing.ChorePrices[choreB.Id];
        Assert.Equal(2, priceB.ScheduledInstances);
        Assert.Equal(135, priceB.PerInstanceMinutes); // 3/16 × 720

        var priceC = pricing.ChorePrices[choreC.Id];
        Assert.Equal(ScreenTimePool.Weekday, priceC.Pool);
        Assert.Equal(4, priceC.ScheduledInstances);
        Assert.Equal(45, priceC.PerInstanceMinutes); // 1/16 × 720

        // Miss everything on weekdays = exactly the pool budget.
        var totalWeekdayLoss =
            priceA.ScheduledInstances * priceA.PerInstanceMinutes +
            priceB.ScheduledInstances * priceB.PerInstanceMinutes +
            priceC.ScheduledInstances * priceC.PerInstanceMinutes;
        Assert.Equal(720, totalWeekdayLoss);
    }

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

    private ScreenTimePricingService CreateService()
    {
        var familySettings = new FamilySettingsService(_contextFactory, new FixedDateProvider(AnyDateInWeek));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var scheduleService = new ChoreScheduleService(
            _contextFactory, familySettings, cache, NullLogger<ChoreScheduleService>.Instance);

        return new ScreenTimePricingService(_contextFactory, familySettings, scheduleService);
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
