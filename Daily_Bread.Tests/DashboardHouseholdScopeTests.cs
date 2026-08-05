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
/// The 2026-08-04 audit's LEAK-1/2 regression: the parent dashboard (and the
/// approvals queue built from it) must scope every constituent — children,
/// balances, help requests, pending approvals, week earnings — to one
/// household when asked. The unscoped call remains the single-family Blazor
/// path and still sees everything.
/// </summary>
public sealed class DashboardHouseholdScopeTests : IAsyncLifetime
{
    private static readonly Guid HomeId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 7, 22); // a Wednesday

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private TestDbContextFactory _contextFactory = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(_options);

        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        db.Households.AddRange(
            new Household { Id = HomeId, Name = "Home" },
            new Household { Id = OtherId, Name = "Other" });
        db.Users.AddRange(
            new ApplicationUser { Id = "home-kid", UserName = "homekid", HouseholdId = HomeId },
            new ApplicationUser { Id = "other-kid", UserName = "otherkid", HouseholdId = OtherId });

        var homeProfile = new ChildProfile { UserId = "home-kid", DisplayName = "Home Kid" };
        var otherProfile = new ChildProfile { UserId = "other-kid", DisplayName = "Other Kid" };
        db.ChildProfiles.AddRange(homeProfile, otherProfile);
        await db.SaveChangesAsync();

        var homeAccount = new LedgerAccount { ChildProfileId = homeProfile.Id, Name = "Main", IsDefault = true };
        var otherAccount = new LedgerAccount { ChildProfileId = otherProfile.Id, Name = "Main", IsDefault = true };
        db.LedgerAccounts.AddRange(homeAccount, otherAccount);
        await db.SaveChangesAsync();

        db.LedgerTransactions.AddRange(
            new LedgerTransaction
            {
                LedgerAccountId = homeAccount.Id, UserId = "home-kid", Amount = 5m,
                Type = TransactionType.ChoreEarning, TransactionDate = Today
            },
            new LedgerTransaction
            {
                LedgerAccountId = otherAccount.Id, UserId = "other-kid", Amount = 7m,
                Type = TransactionType.ChoreEarning, TransactionDate = Today
            });

        var homeChore = new ChoreDefinition { Name = "Home dishes", AssignedUserId = "home-kid", EarnValue = 1m };
        var otherChore = new ChoreDefinition { Name = "Other dishes", AssignedUserId = "other-kid", EarnValue = 2m };
        db.ChoreDefinitions.AddRange(homeChore, otherChore);
        await db.SaveChangesAsync();

        db.ChoreLogs.AddRange(
            new ChoreLog
            {
                ChoreDefinitionId = homeChore.Id, Date = Today,
                Status = ChoreStatus.Completed, CompletedAt = DateTime.UtcNow
            },
            new ChoreLog
            {
                ChoreDefinitionId = otherChore.Id, Date = Today,
                Status = ChoreStatus.Help, HelpReason = "other family secret",
                HelpRequestedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private DashboardService CreateService()
    {
        var dateProvider = new Mock<IDateProvider>();
        dateProvider.SetupGet(p => p.Today).Returns(Today);

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager
            .Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var profileService = new ChildProfileService(_contextFactory);
        var plannerService = new ChorePlannerService(
            _contextFactory, dateProvider.Object, Mock.Of<IChildProfileService>());

        return new DashboardService(
            _contextFactory,
            dateProvider.Object,
            profileService,
            Mock.Of<ITrackerService>(),
            Mock.Of<IPayoutService>(),
            Mock.Of<ILedgerService>(),
            Mock.Of<ISavingsGoalService>(),
            Mock.Of<IAchievementService>(),
            Mock.Of<IWeeklyProgressService>(),
            Mock.Of<IChoreNotificationService>(),
            plannerService,
            userManager.Object,
            NullLogger<DashboardService>.Instance);
    }

    [Fact]
    public async Task Scoped_Dashboard_Sees_Only_Its_Household()
    {
        var data = await CreateService().GetParentDashboardAsync(HomeId);

        Assert.Single(data.ChildrenProgress);
        Assert.Equal("home-kid", data.ChildrenProgress[0].UserId);
        Assert.Single(data.ChildrenBalances);
        Assert.Equal(5m, data.ChildrenBalances[0].Balance);
        Assert.Single(data.PendingApprovals);
        Assert.Equal("Home dishes", data.PendingApprovals[0].ChoreName);
        Assert.Empty(data.HelpRequests); // the only Help log belongs to the other family
        Assert.Equal(5m, data.ThisWeekEarnings);
        Assert.DoesNotContain(data.ChildrenBalances, b => b.DisplayName == "Other Kid");
    }

    [Fact]
    public async Task Scoped_Dashboard_Never_Carries_Foreign_Help_Text()
    {
        var data = await CreateService().GetParentDashboardAsync(HomeId);
        Assert.DoesNotContain(data.HelpRequests, h => h.Reason == "other family secret");

        var otherData = await CreateService().GetParentDashboardAsync(OtherId);
        Assert.Single(otherData.HelpRequests);
        Assert.Empty(otherData.PendingApprovals);
        Assert.Equal(7m, otherData.ThisWeekEarnings);
    }

    [Fact]
    public async Task Unscoped_Dashboard_Remains_Global_For_The_Web_App()
    {
        var data = await CreateService().GetParentDashboardAsync();

        Assert.Equal(2, data.ChildrenProgress.Count);
        Assert.Equal(2, data.ChildrenBalances.Count);
        Assert.Single(data.PendingApprovals);
        Assert.Single(data.HelpRequests);
        Assert.Equal(12m, data.ThisWeekEarnings);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new ApplicationDbContext(options));
    }
}
