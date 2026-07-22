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
/// Guards bounded per-occurrence pricing (MECHANICS_AMENDMENT_II.md): each occurrence is worth
/// Importance × 6 minutes (capped at 60), independent of any other chore, and the pool budgets are a
/// fixed share of the pool hours (the aggregate cap applied at reconciliation).
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
            WeekendAtRiskPercent = 50   // explicit for the 600-minute budget assertion below
        };
        context.ChildProfiles.Add(profile);

        await context.SaveChangesAsync();
        _childProfileId = profile.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ============================================================
    // Pure per-occurrence price
    // ============================================================

    [Fact]
    public void PriceOccurrence_Is_Importance_Times_Six()
    {
        Assert.Equal(0, ScreenTimePricing.PriceOccurrence(0));
        Assert.Equal(6, ScreenTimePricing.PriceOccurrence(1));
        Assert.Equal(12, ScreenTimePricing.PriceOccurrence(2));
        Assert.Equal(18, ScreenTimePricing.PriceOccurrence(3));
        Assert.Equal(30, ScreenTimePricing.PriceOccurrence(5));
        Assert.Equal(60, ScreenTimePricing.PriceOccurrence(10));
    }

    [Fact]
    public void PriceOccurrence_Caps_At_Sixty_And_Floors_At_Zero()
    {
        Assert.Equal(60, ScreenTimePricing.PriceOccurrence(11));   // guardrail
        Assert.Equal(60, ScreenTimePricing.PriceOccurrence(100));
        Assert.Equal(0, ScreenTimePricing.PriceOccurrence(-3));
    }

    [Fact]
    public void PriceOccurrence_Does_Not_Depend_On_Other_Chores()
    {
        // The whole point of the amendment: a chore's price is stable no matter how many others exist.
        Assert.Equal(ScreenTimePricing.PriceOccurrence(2), ScreenTimePricing.PriceOccurrence(2));
    }

    // ============================================================
    // Service-level pricing over a seeded week
    // ============================================================

    [Fact]
    public async Task GetWeekPricing_Prices_Weekday_SpecificDays_And_WeeklyFrequency_Instances()
    {
        var choreA = MakeChore("Trash", ChoreScheduleType.SpecificDays, importance: 2,
            activeDays: DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday); // 3 inst
        var choreB = MakeChore("Dishes", ChoreScheduleType.SpecificDays, importance: 3,
            activeDays: DaysOfWeek.Tuesday | DaysOfWeek.Thursday);                     // 2 inst
        var choreC = MakeChore("Walk Gemma", ChoreScheduleType.WeeklyFrequency, importance: 1,
            activeDays: DaysOfWeek.All, weeklyTargetCount: 4);                          // 4 inst

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

        // Prices are Importance × 6, regardless of instance count.
        var priceA = pricing.ChorePrices[choreA.Id];
        Assert.Equal(ScreenTimePool.Weekday, priceA.Pool);
        Assert.Equal(3, priceA.ScheduledInstances);
        Assert.Equal(12, priceA.PerInstanceMinutes); // 2 × 6

        var priceB = pricing.ChorePrices[choreB.Id];
        Assert.Equal(2, priceB.ScheduledInstances);
        Assert.Equal(18, priceB.PerInstanceMinutes); // 3 × 6

        var priceC = pricing.ChorePrices[choreC.Id];
        Assert.Equal(ScreenTimePool.Weekday, priceC.Pool);
        Assert.Equal(4, priceC.ScheduledInstances);
        Assert.Equal(6, priceC.PerInstanceMinutes); // 1 × 6

        // Missing everything on weekdays = the sum of fixed prices (well under the 720 cap here).
        var totalWeekdayLoss =
            priceA.ScheduledInstances * priceA.PerInstanceMinutes +
            priceB.ScheduledInstances * priceB.PerInstanceMinutes +
            priceC.ScheduledInstances * priceC.PerInstanceMinutes;
        Assert.Equal(96, totalWeekdayLoss); // 36 + 36 + 24
    }

    [Fact]
    public async Task A_Lone_Chore_No_Longer_Owns_The_Whole_Pool()
    {
        // The Vacuum-Room regression: a single weekend chore used to inherit 100% of the weekend
        // budget. Now it is worth exactly Importance × 6.
        var vacuum = MakeChore("Vacuum Room", ChoreScheduleType.SpecificDays, importance: 10,
            activeDays: DaysOfWeek.Saturday);
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.ChoreDefinitions.Add(vacuum);
            await context.SaveChangesAsync();
        }

        var pricing = await CreateService().GetWeekPricingAsync(_childProfileId, AnyDateInWeek);
        var price = pricing.ChorePrices[vacuum.Id];

        Assert.Equal(ScreenTimePool.Weekend, price.Pool);
        Assert.Equal(60, price.PerInstanceMinutes); // not 600
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
