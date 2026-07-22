using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Covers <see cref="ChildProfileService.UpdateScreenTimeSettingsAsync"/>: a happy-path update persists
/// the pool hours, routine payout, and at-risk percents; invalid input is rejected without a write.
/// </summary>
public sealed class ChildProfileScreenTimeSettingsTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private int _profileId;

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
        await context.SaveChangesAsync();

        _profileId = profile.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private ChildProfileService CreateService() => new(_contextFactory);

    [Fact]
    public async Task UpdateScreenTimeSettings_Persists_All_Fields()
    {
        var service = CreateService();

        var result = await service.UpdateScreenTimeSettingsAsync(
            _profileId, weekdayHours: 35m, weekendHours: 18m, weeklyRoutinePayout: 12.50m,
            weekdayAtRiskPercent: 25, weekendAtRiskPercent: 40);

        Assert.True(result.Success);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var profile = await context.ChildProfiles.FirstAsync(p => p.Id == _profileId);
        Assert.Equal(35m, profile.WeekdayScreenTimeHours);
        Assert.Equal(18m, profile.WeekendScreenTimeHours);
        Assert.Equal(12.50m, profile.WeeklyRoutinePayout);
        Assert.Equal(25, profile.WeekdayAtRiskPercent);
        Assert.Equal(40, profile.WeekendAtRiskPercent);
    }

    [Fact]
    public async Task UpdateScreenTimeSettings_Rejects_At_Risk_Above_100()
    {
        var service = CreateService();

        var result = await service.UpdateScreenTimeSettingsAsync(
            _profileId, weekdayHours: 40m, weekendHours: 20m, weeklyRoutinePayout: 10m,
            weekdayAtRiskPercent: 30, weekendAtRiskPercent: 150);

        Assert.False(result.Success);

        // Original defaults untouched.
        await using var context = await _contextFactory.CreateDbContextAsync();
        var profile = await context.ChildProfiles.FirstAsync(p => p.Id == _profileId);
        Assert.Equal(20, profile.WeekendAtRiskPercent);
    }

    [Fact]
    public async Task UpdateScreenTimeSettings_Rejects_Negative_Hours()
    {
        var service = CreateService();

        var result = await service.UpdateScreenTimeSettingsAsync(
            _profileId, weekdayHours: -5m, weekendHours: 20m, weeklyRoutinePayout: 10m,
            weekdayAtRiskPercent: 30, weekendAtRiskPercent: 50);

        Assert.False(result.Success);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
