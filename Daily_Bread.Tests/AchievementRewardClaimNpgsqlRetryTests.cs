using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Proves ApproveClaimAsync's CreateExecutionStrategy().ExecuteAsync wrapper (added to fix
/// "execution strategy does not support user-initiated transactions") actually works against
/// the real provider/config it was written for - Npgsql with EnableRetryOnFailure - and that a
/// transient failure injected specifically on the LedgerTransaction INSERT (the window AFTER
/// the entity is tracked as Added but BEFORE commit) still results in exactly one credit, not
/// two. EF Core does not reset a DbContext's change tracker between execution-strategy
/// retries, so a fault landing in that exact window is the one that would expose a double
/// insert - a fault on the earlier claim-fetch SELECT would not, since nothing is tracked yet.
///
/// This CANNOT be done against SQLite: SQLite's default execution strategy isn't a retrying
/// one, so it never enforces (or exercises) the "no user-initiated transactions under a
/// retrying strategy" rule the bug was about. A SQLite test passing here would prove nothing
/// about the actual fix and would be false confidence.
///
/// Requires a real Postgres reachable via the DAILYBREAD_TEST_PG_CONNECTION env var. If unset,
/// this test no-ops to Passed rather than failing CI/dev machines that don't have Postgres -
/// this is an opt-in infrastructure test, not part of the default safety net. It was run for
/// real against a throwaway postgres:16-alpine container (schema via EnsureCreatedAsync,
/// EnableRetryOnFailure matching Program.cs) and confirmed to FAIL (two ledger rows) against
/// ApproveClaimAsync before the fresh-DbContext-per-retry-attempt fix, and PASS (one row)
/// after it; see the deploy conversation for that run's output.
/// </summary>
public sealed class AchievementRewardClaimNpgsqlRetryTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

    private static string? ConnectionString => Environment.GetEnvironmentVariable("DAILYBREAD_TEST_PG_CONNECTION");

    private readonly DateOnly _today = new(2026, 6, 22);
    private OneShotTransientFaultInterceptor _fault = null!;
    private TestDbContextFactory _contextFactory = null!;
    private int _claimId;

    public async Task InitializeAsync()
    {
        if (ConnectionString is null)
            return;

        _fault = new OneShotTransientFaultInterceptor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString, npgsqlOptions =>
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null))
            .AddInterceptors(_fault)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Fresh schema each run - this is a throwaway database, never the live one.
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "child" };
        var parent = new ApplicationUser { Id = ParentId, UserName = "parent" };
        context.Users.AddRange(child, parent);

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Child" };
        var account = new LedgerAccount { ChildProfile = profile, Name = "Main", IsDefault = true, IsActive = true };

        var achievement = new Achievement
        {
            Code = "TEST_NPGSQL_RETRY_CASH_REWARD",
            Name = "Npgsql Retry Cash Reward Achievement",
            Description = "Earns cash",
            Icon = "🏆",
            IsActive = true,
            Points = 10,
            UnlockConditionType = UnlockConditionType.Manual,
            BonusType = AchievementBonusType.TangibleReward,
            BonusValue = "{\"type\":\"cash\",\"amount\":1.00}"
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
            CashAmount = 1.00m,
            Status = RewardClaimStatus.PendingApproval,
            CreatedAt = DateTime.UtcNow
        };
        context.AchievementRewardClaims.Add(claim);
        await context.SaveChangesAsync();

        _claimId = claim.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApproveClaimAsync_Against_Real_Npgsql_With_Retry_Strategy_Does_Not_Throw_On_Manual_Transaction()
    {
        if (ConnectionString is null)
            return; // See class summary: opt-in infra test, no-ops without a real Postgres.

        var service = CreateService();

        var result = await service.ApproveClaimAsync(_claimId, ParentId);

        Assert.True(result.Success, $"Expected approval to succeed, got: {result.ErrorMessage}");
    }

    [Fact]
    public async Task ApproveClaimAsync_Credits_Exactly_Once_Despite_A_Forced_Transient_Failure_On_The_Ledger_Insert()
    {
        if (ConnectionString is null)
            return; // See class summary: opt-in infra test, no-ops without a real Postgres.

        var service = CreateService();
        _fault.Arm();

        var result = await service.ApproveClaimAsync(_claimId, ParentId);

        Assert.True(result.Success, $"Expected approval to succeed after the forced retry, got: {result.ErrorMessage}");
        Assert.True(_fault.HasFired, "Expected the fault interceptor to have actually thrown once on the " +
            "LedgerTransaction INSERT - otherwise this test isn't proving anything about the dangerous " +
            "retry window and is giving false confidence.");

        await using var verifyContext = await _contextFactory.CreateDbContextAsync();

        var ledgerRows = await verifyContext.LedgerTransactions
            .Where(t => t.UserId == ChildId && t.Type == TransactionType.AchievementReward)
            .ToListAsync();
        var ledgerRow = Assert.Single(ledgerRows);
        Assert.Equal(1.00m, ledgerRow.Amount);

        var claim = await verifyContext.AchievementRewardClaims.SingleAsync(c => c.Id == _claimId);
        Assert.Equal(RewardClaimStatus.Approved, claim.Status);
        Assert.Equal(ledgerRow.Id, claim.LedgerTransactionId);

        // A second approve attempt on the same claim, post-retry, must cleanly fail rather
        // than crediting again - proves the forced retry didn't leave the claim re-approvable.
        var secondAttempt = await service.ApproveClaimAsync(_claimId, ParentId);
        Assert.False(secondAttempt.Success);
        Assert.Contains("already approved", secondAttempt.ErrorMessage);
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
    /// Throws one synthetic transient PostgresException (SqlState 40001 - serialization
    /// failure, a code Npgsql's retrying execution strategy treats as retryable) the first
    /// time it sees a command whose text starts with INSERT, then gets out of the way
    /// permanently. This deliberately targets the dangerous window: AFTER the LedgerTransaction
    /// has been added to the change tracker and its insert is in flight, not the earlier
    /// claim-fetch SELECT. A fault injected on the SELECT only proves retry-after-read works;
    /// it never exercises whether a retry can double-insert an already-tracked entity.
    ///
    /// The LedgerTransaction insert needs its database-generated Id back, so Npgsql sends it
    /// as "INSERT ... RETURNING id" and EF Core executes it via the reader path, not the
    /// non-query path - both are covered so the match fires regardless of which path EF picks.
    /// </summary>
    private sealed class OneShotTransientFaultInterceptor : DbCommandInterceptor
    {
        private int _armed; // starts disarmed - InitializeAsync's own seed inserts must not trigger this
        public bool HasFired { get; private set; }

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmedAndInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmedAndInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void ThrowIfArmedAndInsert(System.Data.Common.DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                return;

            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                HasFired = true;
                throw new PostgresException("Simulated transient failure for test", "ERROR", "ERROR", "40001");
            }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
