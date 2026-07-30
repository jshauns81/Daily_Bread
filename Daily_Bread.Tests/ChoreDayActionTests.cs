using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// The Activity day actions (excuse/miss/reset) as exposed through
/// TrackerService.MarkChoreSkippedAsync / MarkChoreMissedAsync /
/// ResetChoreToPendingAsync: parent-only, work on any date, money is
/// earn-only (excuse and miss never create transactions; resetting an
/// approval removes its earning).
/// </summary>
public sealed class ChoreDayActionTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private readonly DateOnly _today = new(2026, 7, 22);
    private readonly DateOnly _pastDate = new(2026, 7, 15);
    private readonly DateOnly _unscheduledDate = new(2026, 7, 1);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "child" };
        var parent = new ApplicationUser { Id = ParentId, UserName = "parent" };
        context.Users.AddRange(child, parent);

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Child" };
        var account = new LedgerAccount
        {
            ChildProfile = profile,
            Name = "Main",
            IsDefault = true,
            IsActive = true
        };
        var chore = new ChoreDefinition
        {
            Name = "Wash dishes",
            AssignedUserId = ChildId,
            AssignedUser = child,
            EarnValue = 25m,
            AutoApprove = false,
            ScheduleType = ChoreScheduleType.SpecificDays
        };
        var log = new ChoreLog
        {
            ChoreDefinition = chore,
            Date = _today,
            Status = ChoreStatus.Pending
        };

        context.AddRange(profile, account, chore, log);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Parent_Excuse_Sets_Skipped_And_Creates_No_Transaction()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        var result = await tracker.MarkChoreSkippedAsync(choreId, _today, ParentId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ChoreStatus.Skipped, await GetChoreStatusAsync(_today));
        Assert.Empty(await GetTransactionsAsync());
    }

    [Fact]
    public async Task Parent_Miss_On_Completed_Sets_Missed_Without_Deduction()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        var completion = await tracker.ToggleChoreCompletionAsync(choreId, _today, ChildId, isParent: false);
        Assert.True(completion.Success, completion.ErrorMessage);
        Assert.Equal(ChoreStatus.Completed, completion.Data);

        var result = await tracker.MarkChoreMissedAsync(choreId, _today, ParentId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ChoreStatus.Missed, await GetChoreStatusAsync(_today));
        // Money is earn-only: a miss never deducts.
        Assert.Empty(await GetTransactionsAsync());
    }

    [Fact]
    public async Task Reset_Restores_Missed_And_Skipped_To_Pending()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        Assert.True((await tracker.MarkChoreMissedAsync(choreId, _today, ParentId)).Success);
        Assert.True((await tracker.ResetChoreToPendingAsync(choreId, _today, ParentId)).Success);
        Assert.Equal(ChoreStatus.Pending, await GetChoreStatusAsync(_today));

        Assert.True((await tracker.MarkChoreSkippedAsync(choreId, _today, ParentId)).Success);
        Assert.True((await tracker.ResetChoreToPendingAsync(choreId, _today, ParentId)).Success);
        Assert.Equal(ChoreStatus.Pending, await GetChoreStatusAsync(_today));
    }

    [Fact]
    public async Task Reset_Of_Approved_Removes_The_Earning()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        Assert.True((await tracker.ToggleChoreCompletionAsync(choreId, _today, ChildId, isParent: false)).Success);
        var approval = await tracker.ToggleChoreCompletionAsync(choreId, _today, ParentId, isParent: true);
        Assert.True(approval.Success, approval.ErrorMessage);
        Assert.Equal(ChoreStatus.Approved, approval.Data);
        Assert.Single(await GetTransactionsAsync());

        var reset = await tracker.ResetChoreToPendingAsync(choreId, _today, ParentId);

        Assert.True(reset.Success, reset.ErrorMessage);
        Assert.Equal(ChoreStatus.Pending, await GetChoreStatusAsync(_today));
        Assert.Empty(await GetTransactionsAsync());
    }

    [Fact]
    public async Task Child_Cannot_Excuse_Miss_Or_Reset()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        foreach (var attempt in new[]
        {
            await tracker.MarkChoreSkippedAsync(choreId, _today, ChildId),
            await tracker.MarkChoreMissedAsync(choreId, _today, ChildId),
            await tracker.ResetChoreToPendingAsync(choreId, _today, ChildId)
        })
        {
            Assert.False(attempt.Success);
            Assert.Equal("Only parents can perform this action.", attempt.ErrorMessage);
        }

        Assert.Equal(ChoreStatus.Pending, await GetChoreStatusAsync(_today));
    }

    [Fact]
    public async Task Action_On_Unscheduled_Date_Fails()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        var result = await tracker.MarkChoreSkippedAsync(choreId, _unscheduledDate, ParentId);

        Assert.False(result.Success);
        Assert.Equal("Chore is not scheduled for this date.", result.ErrorMessage);
    }

    [Fact]
    public async Task Actions_Work_On_Past_Dates()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        // No log exists for the past date — the log is created on demand.
        var excuse = await tracker.MarkChoreSkippedAsync(choreId, _pastDate, ParentId);
        Assert.True(excuse.Success, excuse.ErrorMessage);
        Assert.Equal(ChoreStatus.Skipped, await GetChoreStatusAsync(_pastDate));

        var reset = await tracker.ResetChoreToPendingAsync(choreId, _pastDate, ParentId);
        Assert.True(reset.Success, reset.ErrorMessage);
        Assert.Equal(ChoreStatus.Pending, await GetChoreStatusAsync(_pastDate));
    }

    /// <summary>
    /// Real TrackerService + LedgerService over in-memory SQLite (the
    /// WorkflowTests harness). IChoreLogService is mocked with get-or-create
    /// semantics: existing (chore, date) log wins; the unscheduled date fails
    /// like the real schedule check; otherwise a Pending log is created.
    /// </summary>
    private TrackerService CreateTrackerService()
    {
        var dateProvider = CreateDateProvider();
        var familySettings = new Mock<IFamilySettingsService>();
        var ledger = new LedgerService(
            _contextFactory,
            dateProvider.Object,
            familySettings.Object,
            NullLogger<LedgerService>.Instance);

        var choreLogService = new Mock<IChoreLogService>();
        choreLogService
            .Setup(service => service.GetOrCreateChoreLogAsync(It.IsAny<int>(), It.IsAny<DateOnly>()))
            .Returns(async (int choreDefinitionId, DateOnly date) =>
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var existing = await context.ChoreLogs
                    .Include(item => item.ChoreDefinition)
                    .SingleOrDefaultAsync(item => item.ChoreDefinitionId == choreDefinitionId && item.Date == date);
                if (existing != null)
                {
                    return ServiceResult<ChoreLog>.Ok(existing);
                }

                if (date == _unscheduledDate)
                {
                    return ServiceResult<ChoreLog>.Fail("Chore is not scheduled for this date.");
                }

                var created = new ChoreLog
                {
                    ChoreDefinitionId = choreDefinitionId,
                    Date = date,
                    Status = ChoreStatus.Pending
                };
                context.ChoreLogs.Add(created);
                await context.SaveChangesAsync();

                return ServiceResult<ChoreLog>.Ok(await context.ChoreLogs
                    .Include(item => item.ChoreDefinition)
                    .SingleAsync(item => item.Id == created.Id));
            });

        var child = new ApplicationUser { Id = ChildId, UserName = "child" };
        var parent = new ApplicationUser { Id = ParentId, UserName = "parent" };
        var userManager = CreateUserManager();
        userManager
            .Setup(manager => manager.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string userId) => userId == ParentId ? parent : child);
        userManager
            .Setup(manager => manager.IsInRoleAsync(It.IsAny<ApplicationUser>(), "Parent"))
            .ReturnsAsync((ApplicationUser user, string _) => user.Id == ParentId);

        var achievements = new Mock<IAchievementService>();
        achievements
            .Setup(service => service.CheckAndAwardAchievementsAsync(It.IsAny<string>()))
            .ReturnsAsync([]);

        return new TrackerService(
            _contextFactory,
            Mock.Of<IChoreScheduleService>(),
            choreLogService.Object,
            ledger,
            Mock.Of<IPushNotificationService>(),
            Mock.Of<INtfyAlertService>(),
            Mock.Of<IWeeklyProgressService>(),
            familySettings.Object,
            achievements.Object,
            Mock.Of<IChoreNotificationService>(),
            dateProvider.Object,
            userManager.Object,
            NullLogger<TrackerService>.Instance);
    }

    private Mock<IDateProvider> CreateDateProvider()
    {
        var provider = new Mock<IDateProvider>();
        provider.SetupGet(item => item.Today).Returns(_today);
        provider.SetupGet(item => item.UtcNow).Returns(_today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        provider.SetupGet(item => item.Now).Returns(_today.ToDateTime(new TimeOnly(12, 0)));
        return provider;
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    private async Task<int> GetChoreIdAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChoreDefinitions.Select(item => item.Id).SingleAsync();
    }

    private async Task<ChoreStatus> GetChoreStatusAsync(DateOnly date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChoreLogs
            .Where(item => item.Date == date)
            .Select(item => item.Status)
            .SingleAsync();
    }

    private async Task<List<LedgerTransaction>> GetTransactionsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions.AsNoTracking().ToListAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
