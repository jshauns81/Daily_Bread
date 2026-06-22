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
/// Guards the fix for the GOAT-payout bug: RarityMastery's "all" mode must mean "holds
/// every CURRENTLY ACTIVE achievement at/above the rarity," not "has ever earned enough
/// at-or-above-rarity achievements including ones since deactivated." Without the fix, a
/// deactivated-but-earned achievement could pad the earned count past the active-only
/// total while the user is still missing an achievement that's actually active today -
/// which now matters because TangibleReward can make "all" mode pay out real money.
/// </summary>
public sealed class RarityMasteryAllModeTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private readonly DateOnly _today = new(2026, 6, 22);

    private Achievement _allModeAchievement = null!;
    private int _activeEpicAchievementId;

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

        // The one Epic+ achievement that's actually active today - the child has NOT earned it.
        var activeEpic = new Achievement
        {
            Code = "ACTIVE_EPIC",
            Name = "Active Epic",
            Description = "An active epic achievement",
            Icon = "💎",
            IsActive = true,
            Rarity = AchievementRarity.Epic,
            Points = 100,
            UnlockConditionType = UnlockConditionType.Manual
        };

        // An Epic+ achievement the child earned in the past, since deactivated by a parent.
        var deactivatedEpic = new Achievement
        {
            Code = "DEACTIVATED_EPIC",
            Name = "Deactivated Epic",
            Description = "An epic achievement no longer active",
            Icon = "🌟",
            IsActive = false,
            Rarity = AchievementRarity.Epic,
            Points = 100,
            UnlockConditionType = UnlockConditionType.Manual
        };

        _allModeAchievement = new Achievement
        {
            Code = "GOAT",
            Name = "GOAT",
            Description = "Hold every epic-or-better achievement",
            Icon = "🐐",
            IsActive = true,
            Rarity = AchievementRarity.Legendary,
            Points = 1000,
            UnlockConditionType = UnlockConditionType.RarityMastery,
            UnlockConditionValue = "{\"min_rarity\":\"Epic\",\"mode\":\"all\"}"
        };

        context.AddRange(activeEpic, deactivatedEpic, _allModeAchievement);
        await context.SaveChangesAsync();

        _activeEpicAchievementId = activeEpic.Id;

        // The child earned ONLY the now-deactivated epic achievement - never earned the
        // currently-active one.
        context.UserAchievements.Add(new UserAchievement
        {
            UserId = ChildId,
            AchievementId = deactivatedEpic.Id,
            EarnedAt = DateTime.UtcNow,
            HasSeen = true
        });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task All_Mode_Does_Not_Falsely_Satisfy_When_Only_A_Deactivated_Achievement_Was_Earned()
    {
        var evaluator = CreateEvaluator();

        var result = await evaluator.EvaluateAsync(ChildId, _allModeAchievement);

        Assert.False(result.IsMet);
    }

    /// <summary>
    /// Guards against the fix over-filtering: once the child ALSO holds the one
    /// currently-active Epic+ achievement (on top of the deactivated one from setup),
    /// "all" mode must correctly fire rather than staying stuck NotMet forever.
    /// </summary>
    [Fact]
    public async Task All_Mode_Is_Satisfied_Once_Every_Currently_Active_Achievement_Is_Earned()
    {
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.UserAchievements.Add(new UserAchievement
            {
                UserId = ChildId,
                AchievementId = _activeEpicAchievementId,
                EarnedAt = DateTime.UtcNow,
                HasSeen = true
            });
            await context.SaveChangesAsync();
        }

        var evaluator = CreateEvaluator();

        var result = await evaluator.EvaluateAsync(ChildId, _allModeAchievement);

        Assert.True(result.IsMet);
    }

    private AchievementConditionEvaluator CreateEvaluator()
    {
        var dateProvider = new Mock<IDateProvider>();
        dateProvider.Setup(d => d.Today).Returns(_today);
        dateProvider.Setup(d => d.UtcNow).Returns(_today.ToDateTime(TimeOnly.MinValue));

        var familySettings = new Mock<IFamilySettingsService>();
        familySettings.Setup(f => f.GetSettingsAsync()).ReturnsAsync(new FamilySettings());

        return new AchievementConditionEvaluator(
            _contextFactory,
            dateProvider.Object,
            familySettings.Object,
            NullLogger<AchievementConditionEvaluator>.Instance);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
