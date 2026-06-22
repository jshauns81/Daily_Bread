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
/// Proves ApproveClaimAsync cannot double-credit under a GENUINE race - two independent
/// DbContexts (each backed by its own SQLite connection, not a shared one), dispatched
/// onto separate thread-pool threads via Task.Run, calling ApproveClaimAsync on the same
/// pending claim at the same time.
///
/// The other reward-claim tests (AchievementRewardClaimTests) only call the service
/// sequentially: the second call always sees the first call's already-committed result,
/// so they only prove the cheap in-memory "if (Status != PendingApproval)" check, not the
/// DB-level guarantee. This test forces both calls to read PendingApproval before either
/// writes, using genuinely separate connections (each can hold its own transaction) AND
/// genuinely separate OS threads (SQLite's "async" calls mostly complete synchronously
/// for an in-memory database, so without Task.Run one call would simply finish before
/// the other started). That's the same precondition a real double-click-from-two-tabs
/// race would create in production.
///
/// Verified by deliberately reverting the fix (removing the claim.Version++ in
/// ApproveClaimAsync) and confirming this test fails every time (both calls succeed,
/// double-crediting), then restoring it and confirming this test passes every time, with
/// the losing call always failing via the DbUpdateConcurrencyException path - see the
/// Assert.Contains("decided by someone else", ...) below.
/// </summary>
public sealed class AchievementRewardClaimConcurrencyTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

    private readonly string _dbName = $"rewardclaims_concurrency_{Guid.NewGuid():N}";
    private readonly DateOnly _today = new(2026, 6, 22);

    private SqliteConnection _anchorConnection = null!;
    private TestDbContextFactory _contextFactory = null!;
    private int _claimId;

    private string ConnectionString =>
        $"Data Source={_dbName};Mode=Memory;Cache=Shared;Default Timeout=30";

    public async Task InitializeAsync()
    {
        // Keeps the named shared-cache in-memory database alive for the test's lifetime -
        // SQLite destroys it once its last connection closes.
        _anchorConnection = new SqliteConnection(ConnectionString);
        await _anchorConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "child" };
        var parent = new ApplicationUser { Id = ParentId, UserName = "parent" };
        context.Users.AddRange(child, parent);

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Child" };
        var account = new LedgerAccount { ChildProfile = profile, Name = "Main", IsDefault = true, IsActive = true };

        var achievement = new Achievement
        {
            Code = "TEST_CONCURRENT_CASH_REWARD",
            Name = "Concurrent Cash Reward Achievement",
            Description = "Earns cash",
            Icon = "🏆",
            IsActive = true,
            Points = 10,
            UnlockConditionType = UnlockConditionType.Manual,
            BonusType = AchievementBonusType.TangibleReward,
            BonusValue = "{\"type\":\"cash\",\"amount\":25.00}"
        };

        context.AddRange(profile, account, achievement);
        await context.SaveChangesAsync();

        var userAchievement = new UserAchievement
        {
            UserId = ChildId,
            AchievementId = achievement.Id,
            EarnedAt = DateTime.UtcNow,
            HasSeen = false
        };
        context.UserAchievements.Add(userAchievement);
        await context.SaveChangesAsync();

        var claim = new AchievementRewardClaim
        {
            UserAchievementId = userAchievement.Id,
            UserId = ChildId,
            AchievementId = achievement.Id,
            RewardType = RewardClaimType.Cash,
            CashAmount = 25.00m,
            Status = RewardClaimStatus.PendingApproval,
            CreatedAt = DateTime.UtcNow
        };
        context.AchievementRewardClaims.Add(claim);
        await context.SaveChangesAsync();

        _claimId = claim.Id;
    }

    public async Task DisposeAsync() => await _anchorConnection.DisposeAsync();

    [Fact]
    public async Task Two_Concurrent_Approvals_On_The_Same_Claim_Credit_At_Most_Once()
    {
        var service = CreateService();

        async Task<(bool Success, string? Error, Exception? Thrown)> TryApproveAsync()
        {
            try
            {
                var result = await service.ApproveClaimAsync(_claimId, ParentId);
                return (result.Success, result.ErrorMessage, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex);
            }
        }

        // Task.Run forces each call onto its own thread-pool thread for genuine
        // OS-level parallelism. This matters because SQLite's "async" APIs mostly
        // complete synchronously for an in-memory database (there's no real disk I/O to
        // suspend on) - calling TryApproveAsync() twice without Task.Run would let the
        // first call run start-to-commit on the calling thread before the second call's
        // body ever got a chance to execute, which only exercises the cheap sequential
        // "already decided" check, not a genuine race.
        var task1 = Task.Run(TryApproveAsync);
        var task2 = Task.Run(TryApproveAsync);
        await Task.WhenAll(task1, task2);

        var (success1, error1, thrown1) = await task1;
        var (success2, error2, thrown2) = await task2;

        var successCount = (success1 ? 1 : 0) + (success2 ? 1 : 0);
        Assert.True(successCount == 1,
            $"Expected exactly one of the two concurrent approvals to succeed, got {successCount}. " +
            $"call1: success={success1} error={error1} thrown={thrown1}; call2: success={success2} error={error2} thrown={thrown2}");

        // The loser must fail via the DEEP concurrency-conflict path (claim.Version
        // mismatch -> DbUpdateConcurrencyException), not the shallow up-front
        // "if (Status != PendingApproval)" check - which only catches a SEQUENTIAL
        // second call, not this genuinely concurrent one. If a future change makes this
        // assertion fail while successCount is still 1, the race is no longer being
        // forced (e.g. Task.Run removed) and this test has quietly stopped proving
        // anything beyond what the sequential test already covers.
        var loserError = success1 ? error2 : error1;
        Assert.Contains("decided by someone else", loserError);

        await using var verifyContext = await _contextFactory.CreateDbContextAsync();

        var ledgerCount = await verifyContext.LedgerTransactions.CountAsync(t => t.UserId == ChildId);
        Assert.Equal(1, ledgerCount);

        var balance = await verifyContext.LedgerTransactions
            .Where(t => t.UserId == ChildId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        Assert.Equal(25.00m, balance);

        var claim = await verifyContext.AchievementRewardClaims.SingleAsync(c => c.Id == _claimId);
        Assert.Equal(RewardClaimStatus.Approved, claim.Status);
        Assert.NotNull(claim.LedgerTransactionId);
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

    /// <summary>
    /// Unlike the other test files' TestDbContextFactory (which wraps one shared
    /// SqliteConnection object), this one is built from a connection STRING - EF Core
    /// opens a fresh, independent SqliteConnection per DbContext, which is what makes
    /// real concurrent transactions against the shared-cache database possible.
    /// </summary>
    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
