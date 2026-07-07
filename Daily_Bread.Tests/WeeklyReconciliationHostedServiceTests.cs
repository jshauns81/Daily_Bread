using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Exercises the hosted reconciler's core tick logic directly (BackgroundService timing itself is not
/// unit-tested): it runs reconciliation for the current week end when — and only when — it is due.
/// </summary>
public sealed class WeeklyReconciliationHostedServiceTests
{
    [Fact]
    public async Task Tick_Runs_Reconciliation_For_Completed_Week_End_When_Needed()
    {
        var weekEnd = new DateOnly(2026, 7, 5);
        var reconciliation = new FakeReconciliationService { Needed = true, WeekEndToReconcile = weekEnd };

        await WeeklyReconciliationHostedService.ReconcileIfNeededAsync(
            reconciliation, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(1, reconciliation.RunCount);
        Assert.Equal(weekEnd, reconciliation.LastWeekEnd);
    }

    [Fact]
    public async Task Tick_Does_Nothing_When_Reconciliation_Not_Needed()
    {
        var reconciliation = new FakeReconciliationService { Needed = false };

        await WeeklyReconciliationHostedService.ReconcileIfNeededAsync(
            reconciliation, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(0, reconciliation.RunCount);
    }

    private sealed class FakeReconciliationService : IWeeklyReconciliationService
    {
        public bool Needed { get; init; }
        public DateOnly WeekEndToReconcile { get; init; }
        public int RunCount { get; private set; }
        public DateOnly? LastWeekEnd { get; private set; }

        public Task<bool> IsReconciliationNeededAsync() => Task.FromResult(Needed);

        public Task<DateOnly> GetWeekEndToReconcileAsync() => Task.FromResult(WeekEndToReconcile);

        public Task<List<WeeklyReconciliationResult>> RunWeeklyReconciliationAsync(DateOnly weekEndDate)
        {
            RunCount++;
            LastWeekEnd = weekEndDate;
            return Task.FromResult(new List<WeeklyReconciliationResult>());
        }

        public Task<WeeklyReconciliationResult> ReconcileChildWeekAsync(string userId, DateOnly weekEndDate)
            => throw new NotSupportedException();

        public Task<DateOnly?> GetLastReconciliationDateAsync()
            => throw new NotSupportedException();
    }
}
