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
/// Guards the soft-delete contract for parent-managed achievements: deactivating an
/// achievement must stop it from being newly earned, but must never erase a child's
/// already-earned history (UserAchievement rows, earned points, earned stats).
/// </summary>
public sealed class AchievementManagementTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private int _earnedAchievementId;
    private int _unearnedAchievementId;

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
        context.Users.Add(child);

        var earned = new Achievement
        {
            Code = "TEST_EARNED",
            Name = "Test Earned",
            Description = "An achievement the child already has",
            Icon = "🏆",
            IsActive = true,
            Points = 25,
            UnlockConditionType = UnlockConditionType.Manual
        };
        var unearned = new Achievement
        {
            Code = "TEST_UNEARNED",
            Name = "Test Unearned",
            Description = "An achievement nobody has earned",
            Icon = "⭐",
            IsActive = true,
            Points = 10,
            UnlockConditionType = UnlockConditionType.Manual
        };
        context.Achievements.AddRange(earned, unearned);
        await context.SaveChangesAsync();

        _earnedAchievementId = earned.Id;
        _unearnedAchievementId = unearned.Id;

        context.UserAchievements.Add(new UserAchievement
        {
            UserId = ChildId,
            AchievementId = earned.Id,
            EarnedAt = DateTime.UtcNow,
            HasSeen = true
        });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Deactivating_An_Earned_Achievement_Keeps_The_Row_And_Earned_History()
    {
        var managementService = new AchievementManagementService(_contextFactory);
        var achievementService = CreateAchievementService();

        // Sanity check: before deactivation, the earned achievement shows up earned.
        var before = await achievementService.GetEarnedAchievementsAsync(ChildId);
        Assert.Contains(before, a => a.Id == _earnedAchievementId);

        var result = await managementService.ToggleActiveAsync(_earnedAchievementId);
        Assert.True(result.Success, result.ErrorMessage);

        await using var context = await _contextFactory.CreateDbContextAsync();

        // The achievement row itself must survive - soft delete only, never a hard delete.
        var achievement = await context.Achievements.FindAsync(_earnedAchievementId);
        Assert.NotNull(achievement);
        Assert.False(achievement!.IsActive);

        // The earned link must survive untouched.
        var userAchievement = await context.UserAchievements
            .SingleAsync(ua => ua.UserId == ChildId && ua.AchievementId == _earnedAchievementId);
        Assert.NotNull(userAchievement);

        // The child's earned history must still show it after deactivation.
        var earnedAfter = await achievementService.GetEarnedAchievementsAsync(ChildId);
        var stillShown = Assert.Single(earnedAfter, a => a.Id == _earnedAchievementId);
        Assert.True(stillShown.IsEarned);

        // Earned stats (points/count) must not shrink because of the deactivation.
        var stats = await achievementService.GetStatsAsync(ChildId);
        Assert.Equal(1, stats.EarnedAchievements);
        Assert.Equal(25, stats.EarnedPoints);
    }

    [Fact]
    public async Task Deactivating_An_Unearned_Achievement_Hides_It_From_The_Earnable_List()
    {
        var managementService = new AchievementManagementService(_contextFactory);
        var achievementService = CreateAchievementService();

        var result = await managementService.ToggleActiveAsync(_unearnedAchievementId);
        Assert.True(result.Success, result.ErrorMessage);

        var all = await achievementService.GetAllAchievementsAsync(ChildId);
        Assert.DoesNotContain(all, a => a.Id == _unearnedAchievementId);
    }

    private AchievementService CreateAchievementService()
    {
        var evaluator = new Mock<IAchievementConditionEvaluator>();
        evaluator
            .Setup(service => service.EvaluateAllAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<int, AchievementEvaluationResult>());

        var bonusService = Mock.Of<IAchievementBonusService>();
        var dateProvider = new Mock<IDateProvider>();
        dateProvider.SetupGet(p => p.Today).Returns(DateOnly.FromDateTime(DateTime.UtcNow));

        return new AchievementService(
            _contextFactory,
            evaluator.Object,
            bonusService,
            dateProvider.Object,
            NullLogger<AchievementService>.Instance);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
