using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

public sealed class WorkflowTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private readonly DateOnly _today = new(2026, 6, 21);

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

        var profile = new ChildProfile
        {
            UserId = ChildId,
            User = child,
            DisplayName = "Child"
        };
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
    public async Task Completion_Approval_Earnings_And_Payout_Follow_The_Real_Workflow()
    {
        var tracker = CreateTrackerService();
        var choreId = await GetChoreIdAsync();

        var completion = await tracker.ToggleChoreCompletionAsync(choreId, _today, ChildId, isParent: false);

        Assert.True(completion.Success, completion.ErrorMessage);
        Assert.Equal(ChoreStatus.Completed, completion.Data);
        Assert.Empty(await GetTransactionsAsync());

        var unauthorizedApproval = await tracker.SetChoreStatusAsync(
            choreId,
            _today,
            ChoreStatus.Approved,
            ChildId);

        Assert.False(unauthorizedApproval.Success);
        Assert.Equal("Only parents can perform this action.", unauthorizedApproval.ErrorMessage);
        Assert.Equal(ChoreStatus.Completed, await GetChoreStatusAsync());

        var approval = await tracker.ToggleChoreCompletionAsync(choreId, _today, ParentId, isParent: true);

        Assert.True(approval.Success, approval.ErrorMessage);
        Assert.Equal(ChoreStatus.Approved, approval.Data);
        var earning = Assert.Single(await GetTransactionsAsync());
        Assert.Equal(TransactionType.ChoreEarning, earning.Type);
        Assert.Equal(25m, earning.Amount);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var payoutService = new PayoutService(
            _contextFactory,
            CreateDateProvider().Object,
            cache);
        var accountId = await GetAccountIdAsync();

        var payout = await payoutService.ProcessAccountCashOutAsync(
            accountId,
            10m,
            ParentId,
            "Allowance paid");

        Assert.True(payout.Success, payout.ErrorMessage);
        var transactions = await GetTransactionsAsync();
        Assert.Contains(transactions, transaction =>
            transaction.Type == TransactionType.Payout && transaction.Amount == -10m);
        Assert.Equal(15m, transactions.Sum(transaction => transaction.Amount));
    }

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
                var log = await context.ChoreLogs
                    .Include(item => item.ChoreDefinition)
                    .SingleAsync(item => item.ChoreDefinitionId == choreDefinitionId && item.Date == date);
                return ServiceResult<ChoreLog>.Ok(log);
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

    private async Task<int> GetAccountIdAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerAccounts.Select(item => item.Id).SingleAsync();
    }

    private async Task<ChoreStatus> GetChoreStatusAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChoreLogs.Select(item => item.Status).SingleAsync();
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
