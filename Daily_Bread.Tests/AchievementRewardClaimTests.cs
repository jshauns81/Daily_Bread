using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards Phase 3's reward-payout contract: a TangibleReward achievement must never touch
/// the balance directly, must produce at most one claim per earn event even if the
/// evaluator re-runs, and approving a claim must credit at most once.
/// </summary>
public sealed class AchievementRewardClaimTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private readonly DateOnly _today = new(2026, 6, 22);

    private int _cashAchievementId;
    private int _itemAchievementId;

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
        var account = new LedgerAccount { ChildProfile = profile, Name = "Main", IsDefault = true, IsActive = true };

        var cashAchievement = new Achievement
        {
            Code = "TEST_CASH_REWARD",
            Name = "Cash Reward Achievement",
            Description = "Earns cash",
            Icon = "🏆",
            IsActive = true,
            Points = 10,
            UnlockConditionType = UnlockConditionType.Manual,
            BonusType = AchievementBonusType.TangibleReward,
            BonusValue = "{\"type\":\"cash\",\"amount\":25.00}"
        };
        var itemAchievement = new Achievement
        {
            Code = "TEST_ITEM_REWARD",
            Name = "Item Reward Achievement",
            Description = "Earns an item",
            Icon = "🎁",
            IsActive = true,
            Points = 10,
            UnlockConditionType = UnlockConditionType.Manual,
            BonusType = AchievementBonusType.TangibleReward,
            BonusValue = "{\"type\":\"item\",\"label\":\"Pokemon booster pack\",\"est_value\":5.00}"
        };

        context.AddRange(profile, account, cashAchievement, itemAchievement);
        await context.SaveChangesAsync();

        _cashAchievementId = cashAchievement.Id;
        _itemAchievementId = itemAchievement.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Earning_A_Cash_Reward_Achievement_Creates_A_Pending_Claim_And_Does_Not_Touch_Balance()
    {
        var userAchievementId = await EarnAsync(_cashAchievementId);
        var service = CreateService();

        var achievement = await GetAchievementAsync(_cashAchievementId);
        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);

        var pending = await service.GetPendingClaimsAsync();
        var claim = Assert.Single(pending);
        Assert.Equal(RewardClaimType.Cash, claim.RewardType);
        Assert.Equal(25.00m, claim.CashAmount);
        Assert.Equal(RewardClaimStatus.PendingApproval, claim.Status);

        Assert.Equal(0m, await GetBalanceAsync());
    }

    [Fact]
    public async Task Re_Running_The_Evaluator_Never_Creates_A_Second_Claim()
    {
        var userAchievementId = await EarnAsync(_cashAchievementId);
        var service = CreateService();
        var achievement = await GetAchievementAsync(_cashAchievementId);

        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);
        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);
        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var claimCount = await context.AchievementRewardClaims
            .CountAsync(c => c.UserAchievementId == userAchievementId);
        Assert.Equal(1, claimCount);
    }

    [Fact]
    public async Task Approving_A_Cash_Claim_Credits_Balance_Exactly_Once_And_Labels_The_Transaction()
    {
        var userAchievementId = await EarnAsync(_cashAchievementId);
        var service = CreateService();
        var achievement = await GetAchievementAsync(_cashAchievementId);
        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);
        var claimId = (await service.GetPendingClaimsAsync()).Single().Id;

        var firstApproval = await service.ApproveClaimAsync(claimId, ParentId);
        Assert.True(firstApproval.Success, firstApproval.ErrorMessage);
        Assert.Equal(25.00m, await GetBalanceAsync());

        await using var context = await _contextFactory.CreateDbContextAsync();
        var transaction = await context.LedgerTransactions.SingleAsync();
        Assert.Equal(TransactionType.AchievementReward, transaction.Type);
        Assert.Equal(25.00m, transaction.Amount);

        // Second approval attempt on the same (now-decided) claim must not credit again.
        var secondApproval = await service.ApproveClaimAsync(claimId, ParentId);
        Assert.False(secondApproval.Success);
        Assert.Equal(25.00m, await GetBalanceAsync());
        Assert.Equal(1, await context.LedgerTransactions.CountAsync());
    }

    [Fact]
    public async Task Approving_An_Item_Claim_Creates_A_Fulfillment_Record_With_No_Balance_Change()
    {
        var userAchievementId = await EarnAsync(_itemAchievementId);
        var service = CreateService();
        var achievement = await GetAchievementAsync(_itemAchievementId);
        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);
        var claimId = (await service.GetPendingClaimsAsync()).Single().Id;

        var result = await service.ApproveClaimAsync(claimId, ParentId);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0m, await GetBalanceAsync());

        await using var context = await _contextFactory.CreateDbContextAsync();
        var claim = await context.AchievementRewardClaims.SingleAsync(c => c.Id == claimId);
        Assert.Equal(RewardClaimStatus.FulfilledByParent, claim.Status);
        Assert.Null(claim.LedgerTransactionId);
        Assert.Equal(5.00m, claim.ItemEstValue);
    }

    [Fact]
    public async Task Rejecting_A_Claim_Has_No_Balance_Effect_And_The_Achievement_Stays_Earned()
    {
        var userAchievementId = await EarnAsync(_cashAchievementId);
        var service = CreateService();
        var achievement = await GetAchievementAsync(_cashAchievementId);
        await service.CreateClaimIfNeededAsync(ChildId, achievement, userAchievementId);
        var claimId = (await service.GetPendingClaimsAsync()).Single().Id;

        var result = await service.RejectClaimAsync(claimId, ParentId, "Not this time");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0m, await GetBalanceAsync());

        await using var context = await _contextFactory.CreateDbContextAsync();
        var claim = await context.AchievementRewardClaims.SingleAsync(c => c.Id == claimId);
        Assert.Equal(RewardClaimStatus.Rejected, claim.Status);

        var userAchievement = await context.UserAchievements.FindAsync(userAchievementId);
        Assert.NotNull(userAchievement);
    }

    private async Task<int> EarnAsync(int achievementId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var userAchievement = new UserAchievement
        {
            UserId = ChildId,
            AchievementId = achievementId,
            EarnedAt = DateTime.UtcNow,
            HasSeen = false
        };
        context.UserAchievements.Add(userAchievement);
        await context.SaveChangesAsync();
        return userAchievement.Id;
    }

    private async Task<Achievement> GetAchievementAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Achievements.SingleAsync(a => a.Id == id);
    }

    private async Task<decimal> GetBalanceAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions.SumAsync(t => (decimal?)t.Amount) ?? 0m;
    }

    private AchievementRewardClaimService CreateService()
    {
        var dateProvider = new Mock<IDateProvider>();
        dateProvider.Setup(d => d.Today).Returns(_today);
        dateProvider.Setup(d => d.UtcNow).Returns(_today.ToDateTime(TimeOnly.MinValue));

        return new AchievementRewardClaimService(
            _contextFactory,
            dateProvider.Object,
            NullLogger<AchievementRewardClaimService>.Instance);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
