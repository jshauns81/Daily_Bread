using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Proves ApproveEntryAsync cannot double-approve under a GENUINE race - two independent
/// DbContexts (each its own SQLite connection, not a shared one) dispatched onto separate
/// thread-pool threads via Task.Run, calling ApproveEntryAsync on the same pending entry at
/// the same time. Mirrors AchievementRewardClaimConcurrencyTests's approach for the same
/// reason: a sequential call only proves the cheap "if (Status != PendingApproval)" check,
/// not the DB-level Version-token guarantee.
/// </summary>
public sealed class DrivingLogConcurrencyTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

    private readonly string _dbName = $"drivinglog_concurrency_{Guid.NewGuid():N}";

    private SqliteConnection _anchorConnection = null!;
    private TestDbContextFactory _contextFactory = null!;
    private int _entryId;

    private string ConnectionString =>
        $"Data Source={_dbName};Mode=Memory;Cache=Shared;Default Timeout=30";

    public async Task InitializeAsync()
    {
        _anchorConnection = new SqliteConnection(ConnectionString);
        await _anchorConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "kid" };
        var parent = new ApplicationUser { Id = ParentId, UserName = "mom" };
        context.Users.AddRange(child, parent);

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Kid" };
        context.ChildProfiles.Add(profile);

        var entry = new DrivingLogEntry
        {
            ChildUserId = ChildId,
            Date = new DateOnly(2026, 7, 5),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0),
            DurationMinutes = 60,
            SupervisorName = "Grandma",
            CreatedByUserId = ChildId,
            Status = DrivingLogStatus.PendingApproval,
            CreatedAt = DateTime.UtcNow
        };
        context.DrivingLogEntries.Add(entry);
        await context.SaveChangesAsync();

        _entryId = entry.Id;
    }

    public async Task DisposeAsync() => await _anchorConnection.DisposeAsync();

    [Fact]
    public async Task Two_Concurrent_Approvals_On_The_Same_Entry_Succeed_At_Most_Once()
    {
        var service = CreateService();

        async Task<(bool Success, string? Error)> TryApproveAsync()
        {
            var result = await service.ApproveEntryAsync(_entryId, ParentId);
            return (result.Success, result.ErrorMessage);
        }

        var task1 = Task.Run(TryApproveAsync);
        var task2 = Task.Run(TryApproveAsync);
        await Task.WhenAll(task1, task2);

        var (success1, error1) = await task1;
        var (success2, error2) = await task2;

        var successCount = (success1 ? 1 : 0) + (success2 ? 1 : 0);
        Assert.True(successCount == 1,
            $"Expected exactly one of the two concurrent approvals to succeed, got {successCount}. " +
            $"call1: success={success1} error={error1}; call2: success={success2} error={error2}");

        // The loser fails one of two valid ways depending on timing: it hits the
        // concurrency token mid-flight ("decided by someone else") or re-reads after
        // the winner committed ("already approved"). Both prove the double-approve
        // was rejected.
        var loserError = success1 ? error2 : error1;
        Assert.True(
            loserError!.Contains("decided by someone else") || loserError.Contains("already approved"),
            $"Unexpected loser error: {loserError}");

        await using var verifyContext = await _contextFactory.CreateDbContextAsync();
        var entry = await verifyContext.DrivingLogEntries.SingleAsync(e => e.Id == _entryId);
        Assert.Equal(DrivingLogStatus.Approved, entry.Status);
    }

    private DrivingLogService CreateService() =>
        new(_contextFactory, NullLogger<DrivingLogService>.Instance);

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
