using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards the MONEY side of threshold pay (MECHANICS_AMENDMENT.md §D): a WeeklyFrequency +
/// AllOrNothing chore pays EarnValue × target as a single reversal-safe week-level transaction
/// (recomputed from the whole week on every call), plus EarnValue × 0.5 per over-target rep that
/// chose Money. A busted week pays $0 with no redemptive money.
/// </summary>
public sealed class ThresholdPayServiceTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const decimal EarnValue = 5m;
    private const int Target = 3;

    // Week starts Monday (the FamilySettings default), so this week is Mon 2026-07-06 .. Sun 2026-07-12.
    private static readonly DateOnly WeekStart = new(2026, 7, 6);
    private static readonly DateOnly WeekEnd = new(2026, 7, 12);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private int _choreId;
    private int _nonAllOrNothingChoreId;

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

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Kid" };
        context.ChildProfiles.Add(profile);
        context.LedgerAccounts.Add(new LedgerAccount
        {
            ChildProfile = profile,
            Name = "Main",
            IsDefault = true,
            IsActive = true
        });

        var chore = new ChoreDefinition
        {
            Name = "Walk Gemma",
            AssignedUserId = ChildId,
            AssignedUser = child,
            EarnValue = EarnValue,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = Target,
            AllOrNothing = true,
            IsRepeatable = true
        };
        context.ChoreDefinitions.Add(chore);

        var nonAllOrNothing = new ChoreDefinition
        {
            Name = "Practice piano",
            AssignedUserId = ChildId,
            AssignedUser = child,
            EarnValue = EarnValue,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = Target,
            AllOrNothing = false
        };
        context.ChoreDefinitions.Add(nonAllOrNothing);

        await context.SaveChangesAsync();
        _choreId = chore.Id;
        _nonAllOrNothingChoreId = nonAllOrNothing.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private LedgerService CreateLedgerService()
    {
        var dateProvider = new FakeDateProvider(WeekStart);
        var familySettings = new FamilySettingsService(_contextFactory, dateProvider);
        return new LedgerService(
            _contextFactory,
            dateProvider,
            familySettings,
            NullLogger<LedgerService>.Instance);
    }

    private async Task<int> AddLogAsync(
        DateOnly date, ChoreStatus status, RedemptionChoice choice = RedemptionChoice.None, int? choreId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var log = new ChoreLog
        {
            ChoreDefinitionId = choreId ?? _choreId,
            Date = date,
            Status = status,
            RedemptionChoice = choice,
            AllowsMultiplePerDay = true
        };
        context.ChoreLogs.Add(log);
        await context.SaveChangesAsync();
        return log.Id;
    }

    private async Task SetStatusAsync(int logId, ChoreStatus status)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var log = await context.ChoreLogs.FindAsync(logId);
        log!.Status = status;
        await context.SaveChangesAsync();
    }

    private async Task<List<LedgerTransaction>> WeekLevelTransactionsAsync(int? choreId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions
            .Where(t => t.ChoreDefinitionId == (choreId ?? _choreId) && t.ChoreLogId == null)
            .ToListAsync();
    }

    [Fact]
    public async Task Hitting_Target_Pays_EarnValue_Times_Target_As_A_Single_Transaction()
    {
        await AddLogAsync(WeekStart, ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(2), ChoreStatus.Approved);

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);

        var tx = Assert.Single(await WeekLevelTransactionsAsync());
        Assert.Equal(TransactionType.ChoreEarning, tx.Type);
        Assert.Equal(EarnValue * Target, tx.Amount); // 15
        Assert.Equal(WeekEnd, tx.WeekEndDate);
        Assert.Null(tx.ChoreLogId);

        // Idempotent: reconciling again re-derives to the same single row, no double pay.
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);
        var again = Assert.Single(await WeekLevelTransactionsAsync());
        Assert.Equal(EarnValue * Target, again.Amount);
    }

    [Fact]
    public async Task Excused_And_Completed_Reps_Count_Toward_The_Target()
    {
        await AddLogAsync(WeekStart, ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Skipped);   // excused / parent-completed
        await AddLogAsync(WeekStart.AddDays(2), ChoreStatus.Completed); // completed but not yet approved

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);

        var tx = Assert.Single(await WeekLevelTransactionsAsync());
        Assert.Equal(EarnValue * Target, tx.Amount); // a sick day must not nuke the pay
    }

    [Fact]
    public async Task Un_Approving_An_Earlier_Rep_Out_Of_Order_Recomputes_The_Week_To_Zero()
    {
        var firstId = await AddLogAsync(WeekStart, ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(2), ChoreStatus.Approved);

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);
        Assert.Equal(EarnValue * Target, Assert.Single(await WeekLevelTransactionsAsync()).Amount);

        // Reverse the FIRST rep (out of order). Target is no longer met -> payout must drop to $0
        // and the week-level transaction must be removed. This is the key reversal-safety guarantee.
        await SetStatusAsync(firstId, ChoreStatus.Pending);
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);

        Assert.Empty(await WeekLevelTransactionsAsync());
    }

    [Fact]
    public async Task Over_Target_Rep_Choosing_Money_Adds_Half_Earn_Value()
    {
        await AddLogAsync(WeekStart, ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(2), ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(3), ChoreStatus.Approved, RedemptionChoice.Money); // 4th, redemptive

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);

        var tx = Assert.Single(await WeekLevelTransactionsAsync());
        Assert.Equal(EarnValue * Target + EarnValue * 0.5m, tx.Amount); // 15 + 2.5 = 17.5
    }

    [Theory]
    [InlineData(RedemptionChoice.ScreenTime)]
    [InlineData(RedemptionChoice.None)]
    public async Task Over_Target_Rep_Not_Choosing_Money_Adds_No_Money(RedemptionChoice choice)
    {
        await AddLogAsync(WeekStart, ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(2), ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(3), ChoreStatus.Approved, choice); // ST/None -> no money here

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);

        var tx = Assert.Single(await WeekLevelTransactionsAsync());
        Assert.Equal(EarnValue * Target, tx.Amount); // base only, no redemptive money
    }

    [Fact]
    public async Task Busted_Week_Pays_Zero_Even_With_A_Money_Redemption_Choice()
    {
        // Only 2 of 3 credited -> hard cash cliff.
        await AddLogAsync(WeekStart, ChoreStatus.Approved);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Approved, RedemptionChoice.Money);

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_choreId, WeekEnd, ChildId);

        Assert.Empty(await WeekLevelTransactionsAsync());
    }

    [Fact]
    public async Task Non_AllOrNothing_Weekly_Chore_Gets_No_Week_Level_Threshold_Transaction()
    {
        await AddLogAsync(WeekStart, ChoreStatus.Approved, choreId: _nonAllOrNothingChoreId);
        await AddLogAsync(WeekStart.AddDays(1), ChoreStatus.Approved, choreId: _nonAllOrNothingChoreId);
        await AddLogAsync(WeekStart.AddDays(2), ChoreStatus.Approved, choreId: _nonAllOrNothingChoreId);

        var service = CreateLedgerService();
        await service.ReconcileWeeklyThresholdAsync(_nonAllOrNothingChoreId, WeekEnd, ChildId);

        // Non-AllOrNothing chores keep the existing per-log path; the week-level method is a no-op.
        Assert.Empty(await WeekLevelTransactionsAsync(_nonAllOrNothingChoreId));
    }

    private sealed class FakeDateProvider(DateOnly today) : IDateProvider
    {
        public DateTime UtcNow => today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        public DateOnly Today => today;
        public DateTime Now => today.ToDateTime(new TimeOnly(12, 0));
        public string TimeZoneId => "UTC";
        public string TimeZoneDisplayName => "UTC";
        public DateOnly GetTodayInTimezone(string timezoneId) => today;
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
