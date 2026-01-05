using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for displaying an achievement with progress and bonus information.
/// </summary>
public class AchievementDisplay
{
    public int Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? HiddenHint { get; init; }
    public required string Icon { get; init; }
    public string? LockedIcon { get; init; }
    public AchievementCategory Category { get; init; }
    public AchievementRarity Rarity { get; init; }
    public int Points { get; init; }
    public int SortOrder { get; init; }
    
    // Visibility
    public bool IsHidden { get; init; }
    public bool IsLegendary { get; init; }
    public bool IsVisibleBeforeUnlock { get; init; }
    
    // Earned status
    public bool IsEarned { get; init; }
    public DateTime? EarnedAt { get; init; }
    public bool IsNew { get; init; } // Just earned, hasn't been seen
    
    // Progress
    public int CurrentProgress { get; init; }
    public int TargetProgress { get; init; }
    public int ProgressPercent { get; init; }
    public bool ShowProgress { get; init; }
    
    // Bonus
    public bool HasBonus { get; init; }
    public string? BonusDescription { get; init; }
    public AchievementBonusType? BonusType { get; init; }
}

/// <summary>
/// Service interface for achievements.
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Gets all visible achievements with earned status and progress for a user.
    /// Hidden achievements are only returned if earned or if IsVisibleBeforeUnlock is true.
    /// </summary>
    Task<List<AchievementDisplay>> GetAllAchievementsAsync(string userId);

    /// <summary>
    /// Gets earned achievements for a user.
    /// </summary>
    Task<List<AchievementDisplay>> GetEarnedAchievementsAsync(string userId);

    /// <summary>
    /// Gets newly earned achievements that haven't been seen.
    /// </summary>
    Task<List<AchievementDisplay>> GetUnseenAchievementsAsync(string userId);

    /// <summary>
    /// Marks achievements as seen by the user.
    /// </summary>
    Task MarkAchievementsAsSeenAsync(string userId);

    /// <summary>
    /// Checks and awards any newly earned achievements.
    /// Returns list of newly awarded achievements.
    /// </summary>
    Task<List<AchievementDisplay>> CheckAndAwardAchievementsAsync(string userId);

    /// <summary>
    /// Gets total achievement points earned by user.
    /// </summary>
    Task<int> GetTotalPointsAsync(string userId);

    /// <summary>
    /// Gets achievement statistics for a user.
    /// </summary>
    Task<AchievementStats> GetStatsAsync(string userId);

    /// <summary>
    /// Awards a specific achievement to a user manually.
    /// Used for special awards by parents or triggered by specific events.
    /// </summary>
    Task<AchievementDisplay?> AwardAchievementAsync(string userId, string achievementCode, string? notes = null);

    /// <summary>
    /// Gets progress for all achievements for a user.
    /// </summary>
    Task<Dictionary<int, AchievementEvaluationResult>> GetProgressAsync(string userId);

    /// <summary>
    /// Seeds the default achievements (called on startup).
    /// </summary>
    Task SeedAchievementsAsync();
}

/// <summary>
/// Achievement statistics for a user.
/// </summary>
public record AchievementStats
{
    public int TotalAchievements { get; init; }
    public int EarnedAchievements { get; init; }
    public int TotalPoints { get; init; }
    public int EarnedPoints { get; init; }
    public int HiddenRemaining { get; init; }
    public int LegendaryEarned { get; init; }
    public Dictionary<AchievementCategory, int> ByCategory { get; init; } = new();
    public Dictionary<AchievementRarity, int> ByRarity { get; init; } = new();
}

/// <summary>
/// Service for managing achievements/badges.
/// 
/// ARCHITECTURE:
/// - Uses IAchievementConditionEvaluator for data-driven unlock evaluation
/// - Uses IAchievementBonusService for granting and managing bonuses
/// - Respects hidden achievement visibility rules
/// - Tracks progress for progress-based achievements
/// </summary>
public class AchievementService : IAchievementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IAchievementConditionEvaluator _conditionEvaluator;
    private readonly IAchievementBonusService _bonusService;
    private readonly IDateProvider _dateProvider;
    private readonly ILogger<AchievementService> _logger;

    public AchievementService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IAchievementConditionEvaluator conditionEvaluator,
        IAchievementBonusService bonusService,
        IDateProvider dateProvider,
        ILogger<AchievementService> logger)
    {
        _contextFactory = contextFactory;
        _conditionEvaluator = conditionEvaluator;
        _bonusService = bonusService;
        _dateProvider = dateProvider;
        _logger = logger;
    }

    public async Task<List<AchievementDisplay>> GetAllAchievementsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var achievements = await context.Achievements
            .Where(a => a.IsActive)
            .OrderBy(a => a.Category)
            .ThenBy(a => a.SortOrder)
            .ThenBy(a => a.Rarity)
            .ToListAsync();

        var earnedMap = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .ToDictionaryAsync(ua => ua.AchievementId, ua => ua);

        // Get progress for all unearned achievements
        var progressMap = await _conditionEvaluator.EvaluateAllAsync(userId);

        var result = new List<AchievementDisplay>();

        foreach (var a in achievements)
        {
            earnedMap.TryGetValue(a.Id, out var userAchievement);
            var isEarned = userAchievement != null;
            
            // Hidden achievement visibility rules:
            // - If earned: always show
            // - If not earned and IsHidden and !IsVisibleBeforeUnlock: don't show
            // - If not earned and IsHidden and IsVisibleBeforeUnlock: show with obscured info
            if (!isEarned && a.IsHidden && !a.IsVisibleBeforeUnlock)
                continue;

            progressMap.TryGetValue(a.Id, out var progress);

            result.Add(CreateDisplay(a, userAchievement, progress, isEarned));
        }

        return result;
    }

    public async Task<List<AchievementDisplay>> GetEarnedAchievementsAsync(string userId)
    {
        var all = await GetAllAchievementsAsync(userId);
        return all.Where(a => a.IsEarned).ToList();
    }

    public async Task<List<AchievementDisplay>> GetUnseenAchievementsAsync(string userId)
    {
        var all = await GetAllAchievementsAsync(userId);
        return all.Where(a => a.IsNew).ToList();
    }

    public async Task MarkAchievementsAsSeenAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var unseen = await context.UserAchievements
            .Where(ua => ua.UserId == userId && !ua.HasSeen)
            .ToListAsync();

        foreach (var ua in unseen)
        {
            ua.HasSeen = true;
        }

        await context.SaveChangesAsync();
    }

    public async Task<List<AchievementDisplay>> CheckAndAwardAchievementsAsync(string userId)
    {
        var newlyAwarded = new List<AchievementDisplay>();

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Get all unearned active achievements
        var earnedAchievementIds = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync();

        var unearnedAchievements = await context.Achievements
            .Where(a => a.IsActive && !earnedAchievementIds.Contains(a.Id))
            .ToListAsync();

        if (unearnedAchievements.Count == 0)
        {
            return newlyAwarded;
        }

        // =============================================================================
        // OPTIMIZATION: Use EvaluateAllAsync to evaluate all achievements in one batch
        // Previously: Called EvaluateAsync for EACH unearned achievement individually
        // Now: Single batched evaluation that shares data loading across all checks
        // =============================================================================
        var evaluations = await _conditionEvaluator.EvaluateAllAsync(userId);

        foreach (var achievement in unearnedAchievements)
        {
            // Skip manual-only achievements
            if (achievement.UnlockConditionType == UnlockConditionType.Manual)
                continue;

            // Get evaluation from batch results (already computed)
            if (!evaluations.TryGetValue(achievement.Id, out var evaluation))
            {
                // No evaluation result - skip
                continue;
            }

            if (evaluation.IsMet)
            {
                var userAchievement = new UserAchievement
                {
                    UserId = userId,
                    AchievementId = achievement.Id,
                    EarnedAt = DateTime.UtcNow,
                    HasSeen = false
                };

                context.UserAchievements.Add(userAchievement);

                // Update progress if tracked
                await UpdateProgressAsync(context, userId, achievement, evaluation);

                // Grant bonus if applicable
                await _bonusService.GrantBonusAsync(userId, achievement);

                newlyAwarded.Add(CreateDisplay(achievement, userAchievement, evaluation, true));

                _logger.LogInformation("User {UserId} earned achievement {Code}", userId, achievement.Code);
            }
            else if (achievement.ProgressTarget.HasValue)
            {
                // Update progress even if not yet earned
                await UpdateProgressAsync(context, userId, achievement, evaluation);
            }
        }

        if (newlyAwarded.Count > 0)
        {
            await context.SaveChangesAsync();
        }

        return newlyAwarded;
    }

    public async Task<int> GetTotalPointsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.UserAchievements
            .Include(ua => ua.Achievement)
            .Where(ua => ua.UserId == userId)
            .SumAsync(ua => ua.Achievement.Points);
    }

    public async Task<AchievementStats> GetStatsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var allAchievements = await context.Achievements
            .Where(a => a.IsActive)
            .ToListAsync();

        var earnedIds = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync();

        var earnedAchievements = allAchievements.Where(a => earnedIds.Contains(a.Id)).ToList();

        return new AchievementStats
        {
            TotalAchievements = allAchievements.Count,
            EarnedAchievements = earnedAchievements.Count,
            TotalPoints = allAchievements.Sum(a => a.Points),
            EarnedPoints = earnedAchievements.Sum(a => a.Points),
            HiddenRemaining = allAchievements.Count(a => a.IsHidden && !earnedIds.Contains(a.Id)),
            LegendaryEarned = earnedAchievements.Count(a => a.IsLegendary),
            ByCategory = earnedAchievements.GroupBy(a => a.Category).ToDictionary(g => g.Key, g => g.Count()),
            ByRarity = earnedAchievements.GroupBy(a => a.Rarity).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public async Task<AchievementDisplay?> AwardAchievementAsync(string userId, string achievementCode, string? notes = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var achievement = await context.Achievements
            .FirstOrDefaultAsync(a => a.Code == achievementCode && a.IsActive);

        if (achievement == null)
        {
            _logger.LogWarning("Attempted to award non-existent achievement {Code}", achievementCode);
            return null;
        }

        // Check if already earned
        var existing = await context.UserAchievements
            .FirstOrDefaultAsync(ua => ua.UserId == userId && ua.AchievementId == achievement.Id);

        if (existing != null)
        {
            _logger.LogInformation("User {UserId} already has achievement {Code}", userId, achievementCode);
            return null;
        }

        var userAchievement = new UserAchievement
        {
            UserId = userId,
            AchievementId = achievement.Id,
            EarnedAt = DateTime.UtcNow,
            HasSeen = false,
            Notes = notes
        };

        context.UserAchievements.Add(userAchievement);
        await context.SaveChangesAsync();

        // Grant bonus if applicable
        await _bonusService.GrantBonusAsync(userId, achievement);

        _logger.LogInformation("Manually awarded achievement {Code} to user {UserId}", achievementCode, userId);

        return CreateDisplay(achievement, userAchievement, null, true);
    }

    public async Task<Dictionary<int, AchievementEvaluationResult>> GetProgressAsync(string userId)
    {
        return await _conditionEvaluator.EvaluateAllAsync(userId);
    }

    public async Task SeedAchievementsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var existingCodes = await context.Achievements.Select(a => a.Code).ToListAsync();
        var achievementsToAdd = GetSeedAchievements()
            .Where(a => !existingCodes.Contains(a.Code))
            .ToList();

        if (achievementsToAdd.Count > 0)
        {
            context.Achievements.AddRange(achievementsToAdd);
            await context.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} new achievements", achievementsToAdd.Count);
        }

        // Update existing achievements with new fields if needed
        await UpdateExistingAchievementsAsync(context);
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private AchievementDisplay CreateDisplay(
        Achievement achievement,
        UserAchievement? userAchievement,
        AchievementEvaluationResult? progress,
        bool isEarned)
    {
        var showHiddenInfo = !achievement.IsHidden || isEarned;
        
        return new AchievementDisplay
        {
            Id = achievement.Id,
            Code = achievement.Code,
            Name = showHiddenInfo ? achievement.Name : "???",
            Description = showHiddenInfo ? achievement.Description : (achievement.HiddenHint ?? "A secret achievement..."),
            HiddenHint = achievement.HiddenHint,
            Icon = isEarned ? achievement.Icon : (achievement.LockedIcon ?? (achievement.IsHidden ? EmojiConstants.QuestionMark : achievement.Icon)),
            LockedIcon = achievement.LockedIcon,
            Category = achievement.Category,
            Rarity = achievement.Rarity,
            Points = achievement.Points,
            SortOrder = achievement.SortOrder,
            IsHidden = achievement.IsHidden,
            IsLegendary = achievement.IsLegendary,
            IsVisibleBeforeUnlock = achievement.IsVisibleBeforeUnlock,
            IsEarned = isEarned,
            EarnedAt = userAchievement?.EarnedAt,
            IsNew = userAchievement != null && !userAchievement.HasSeen,
            CurrentProgress = progress?.CurrentValue ?? 0,
            TargetProgress = progress?.TargetValue ?? (achievement.ProgressTarget ?? 0),
            ProgressPercent = progress?.ProgressPercent ?? 0,
            ShowProgress = achievement.ProgressTarget.HasValue && !isEarned,
            HasBonus = achievement.BonusType.HasValue && achievement.BonusType != AchievementBonusType.None,
            BonusDescription = achievement.BonusDescription,
            BonusType = achievement.BonusType
        };
    }

    private async Task UpdateProgressAsync(
        ApplicationDbContext context, 
        string userId, 
        Achievement achievement, 
        AchievementEvaluationResult evaluation)
    {
        if (!achievement.ProgressTarget.HasValue)
            return;

        var progress = await context.AchievementProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.AchievementId == achievement.Id);

        if (progress == null)
        {
            progress = new AchievementProgress
            {
                UserId = userId,
                AchievementId = achievement.Id,
                TargetValue = achievement.ProgressTarget.Value,
                StartedAt = DateTime.UtcNow
            };
            context.AchievementProgress.Add(progress);
        }

        progress.CurrentValue = evaluation.CurrentValue;
        progress.LastUpdatedAt = DateTime.UtcNow;
        progress.Metadata = evaluation.Metadata;
    }

    private async Task UpdateExistingAchievementsAsync(ApplicationDbContext context)
    {
        var seedData = GetSeedAchievements().ToDictionary(a => a.Code);
        var existing = await context.Achievements.ToListAsync();
        var updated = false;

        foreach (var achievement in existing)
        {
            if (seedData.TryGetValue(achievement.Code, out var seed))
            {
                // Update fields that may have been added or changed
                if (achievement.UnlockConditionType == UnlockConditionType.Manual && 
                    seed.UnlockConditionType != UnlockConditionType.Manual)
                {
                    achievement.UnlockConditionType = seed.UnlockConditionType;
                    achievement.UnlockConditionValue = seed.UnlockConditionValue;
                    achievement.ProgressTarget = seed.ProgressTarget;
                    updated = true;
                }

                // Update icons if corrupted
                if (achievement.Icon != seed.Icon)
                {
                    achievement.Icon = seed.Icon;
                    updated = true;
                }

                // Add rarity if missing
                if (achievement.Rarity == 0)
                {
                    achievement.Rarity = seed.Rarity;
                    updated = true;
                }
            }
        }

        if (updated)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Updated existing achievements with new fields");
        }
    }

    /// <summary>
    /// Returns seed data for 60+ achievements.
    /// 
    /// ADDING NEW ACHIEVEMENTS:
    /// 1. Add new Achievement object to this list
    /// 2. Set UnlockConditionType and UnlockConditionValue for automatic evaluation
    /// 3. Set ProgressTarget for progress-based achievements
    /// 4. Set BonusType and BonusValue for achievements that grant bonuses
    /// 5. Run application to seed new achievements
    /// </summary>
    private List<Achievement> GetSeedAchievements()
    {
        var achievements = new List<Achievement>();

        // ═══════════════════════════════════════════════════════════════
        // GETTING STARTED (Category 1) - Easy achievements for new users
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "FIRST_CHORE", Name = "First Steps", Description = "Complete your first chore",
                Icon = EmojiConstants.Footprints, Category = AchievementCategory.GettingStarted,
                Rarity = AchievementRarity.Common, Points = 10, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.FirstChore
            },
            new Achievement
            {
                Code = "FIRST_DOLLAR", Name = "First Wages", Description = "Earn your first dollar",
                Icon = EmojiConstants.Dollar, Category = AchievementCategory.GettingStarted,
                Rarity = AchievementRarity.Common, Points = 10, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.FirstDollar
            },
            new Achievement
            {
                Code = "FIRST_GOAL", Name = "Dream Big", Description = "Create your first savings goal",
                Icon = EmojiConstants.Target, Category = AchievementCategory.GettingStarted,
                Rarity = AchievementRarity.Common, Points = 10, SortOrder = 3,
                UnlockConditionType = UnlockConditionType.FirstGoal
            },
            new Achievement
            {
                Code = "FIRST_WEEK", Name = "Weekly Warrior", Description = "Complete a full week of chores",
                Icon = EmojiConstants.Calendar, Category = AchievementCategory.GettingStarted,
                Rarity = AchievementRarity.Common, Points = 25, SortOrder = 4,
                UnlockConditionType = UnlockConditionType.PerfectDays, UnlockConditionValue = "{\"count\": 7}",
                ProgressTarget = 7
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // STREAKS (Category 2) - Consecutive day achievements
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "STREAK_3", Name = "Getting Going", Description = "Complete all chores for 3 days in a row",
                Icon = EmojiConstants.Fire, Category = AchievementCategory.Streaks,
                Rarity = AchievementRarity.Common, Points = 25, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 3}",
                ProgressTarget = 3
            },
            new Achievement
            {
                Code = "STREAK_7", Name = "Week Warrior", Description = "Complete all chores for 7 days in a row",
                Icon = EmojiConstants.FireDouble, Category = AchievementCategory.Streaks,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 7}",
                ProgressTarget = 7,
                BonusType = AchievementBonusType.StreakProtection, BonusValue = "{\"count\": 1}",
                BonusDescription = "1x Streak Protection"
            },
            new Achievement
            {
                Code = "STREAK_14", Name = "Fortnight Fighter", Description = "Complete all chores for 14 days in a row",
                Icon = EmojiConstants.FireTriple, Category = AchievementCategory.Streaks,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 3,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 14}",
                ProgressTarget = 14,
                BonusType = AchievementBonusType.PointMultiplier, BonusValue = "{\"multiplier\": 1.1, \"duration_days\": 7}",
                BonusDescription = "10% point bonus for 7 days"
            },
            new Achievement
            {
                Code = "STREAK_30", Name = "Monthly Master", Description = "Complete all chores for 30 days in a row",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Streaks,
                Rarity = AchievementRarity.Epic, Points = 200, SortOrder = 4,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 30}",
                ProgressTarget = 30,
                BonusType = AchievementBonusType.BonusPoints, BonusValue = "{\"amount\": 10.00}",
                BonusDescription = "$10 bonus!"
            },
            new Achievement
            {
                Code = "STREAK_60", Name = "Unstoppable", Description = "Complete all chores for 60 days in a row",
                Icon = EmojiConstants.Star, Category = AchievementCategory.Streaks,
                Rarity = AchievementRarity.Epic, Points = 400, SortOrder = 5,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 60}",
                ProgressTarget = 60,
                BonusType = AchievementBonusType.TrustIncrease, BonusValue = "{\"level_increase\": 1}",
                BonusDescription = "Increased autonomy level"
            },
            new Achievement
            {
                Code = "STREAK_90", Name = "Legendary Dedication", Description = "Complete all chores for 90 days in a row",
                Icon = EmojiConstants.GlowingStar, Category = AchievementCategory.Streaks,
                Rarity = AchievementRarity.Legendary, Points = 750, SortOrder = 6,
                IsLegendary = true,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 90}",
                ProgressTarget = 90,
                BonusType = AchievementBonusType.ProfileBadge, BonusValue = "{\"badge_key\": \"legendary_streak\"}",
                BonusDescription = "Legendary Streak badge"
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // EARNINGS (Category 3) - Money milestones
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "EARNED_10", Name = "Tenner", Description = "Earn a total of $10",
                Icon = EmojiConstants.MoneyBag, Category = AchievementCategory.Earnings,
                Rarity = AchievementRarity.Common, Points = 25, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 10}",
                ProgressTarget = 10
            },
            new Achievement
            {
                Code = "EARNED_25", Name = "Quarter Century", Description = "Earn a total of $25",
                Icon = EmojiConstants.MoneyBagDouble, Category = AchievementCategory.Earnings,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 25}",
                ProgressTarget = 25
            },
            new Achievement
            {
                Code = "EARNED_50", Name = "Half Century", Description = "Earn a total of $50",
                Icon = EmojiConstants.MoneyBagTriple, Category = AchievementCategory.Earnings,
                Rarity = AchievementRarity.Rare, Points = 75, SortOrder = 3,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 50}",
                ProgressTarget = 50
            },
            new Achievement
            {
                Code = "EARNED_100", Name = "Benjamin", Description = "Earn a total of $100",
                Icon = EmojiConstants.MoneyFace, Category = AchievementCategory.Earnings,
                Rarity = AchievementRarity.Epic, Points = 150, SortOrder = 4,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 100}",
                ProgressTarget = 100,
                BonusType = AchievementBonusType.BonusPoints, BonusValue = "{\"amount\": 5.00}",
                BonusDescription = "$5 bonus!"
            },
            new Achievement
            {
                Code = "EARNED_250", Name = "Quarter Grand", Description = "Earn a total of $250",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Earnings,
                Rarity = AchievementRarity.Epic, Points = 250, SortOrder = 5,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 250}",
                ProgressTarget = 250
            },
            new Achievement
            {
                Code = "EARNED_500", Name = "Half Grand", Description = "Earn a total of $500",
                Icon = EmojiConstants.Star, Category = AchievementCategory.Earnings,
                Rarity = AchievementRarity.Legendary, Points = 500, SortOrder = 6,
                IsLegendary = true,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 500}",
                ProgressTarget = 500
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // CONSISTENCY (Category 4) - Total completions and perfect days
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "CHORES_10", Name = "Helping Hand", Description = "Complete 10 chores",
                Icon = EmojiConstants.RaisedHand, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Common, Points = 15, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.ChoresCompleted, UnlockConditionValue = "{\"count\": 10}",
                ProgressTarget = 10
            },
            new Achievement
            {
                Code = "CHORES_50", Name = "Chore Champion", Description = "Complete 50 chores",
                Icon = EmojiConstants.GoldMedal, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.ChoresCompleted, UnlockConditionValue = "{\"count\": 50}",
                ProgressTarget = 50
            },
            new Achievement
            {
                Code = "CHORES_100", Name = "Century Club", Description = "Complete 100 chores",
                Icon = EmojiConstants.HundredPoints, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 3,
                UnlockConditionType = UnlockConditionType.ChoresCompleted, UnlockConditionValue = "{\"count\": 100}",
                ProgressTarget = 100
            },
            new Achievement
            {
                Code = "CHORES_250", Name = "Dedicated Worker", Description = "Complete 250 chores",
                Icon = EmojiConstants.Muscle, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Epic, Points = 175, SortOrder = 4,
                UnlockConditionType = UnlockConditionType.ChoresCompleted, UnlockConditionValue = "{\"count\": 250}",
                ProgressTarget = 250
            },
            new Achievement
            {
                Code = "CHORES_500", Name = "Half Thousand", Description = "Complete 500 chores",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Legendary, Points = 350, SortOrder = 5,
                IsLegendary = true,
                UnlockConditionType = UnlockConditionType.ChoresCompleted, UnlockConditionValue = "{\"count\": 500}",
                ProgressTarget = 500
            },
            new Achievement
            {
                Code = "PERFECT_7", Name = "Perfect Week", Description = "Have 7 perfect days (all chores done)",
                Icon = EmojiConstants.Star, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 10,
                UnlockConditionType = UnlockConditionType.PerfectDays, UnlockConditionValue = "{\"count\": 7}",
                ProgressTarget = 7
            },
            new Achievement
            {
                Code = "PERFECT_30", Name = "Perfect Month", Description = "Have 30 perfect days",
                Icon = EmojiConstants.GlowingStar, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Rare, Points = 150, SortOrder = 11,
                UnlockConditionType = UnlockConditionType.PerfectDays, UnlockConditionValue = "{\"count\": 30}",
                ProgressTarget = 30
            },
            new Achievement
            {
                Code = "PERFECT_100", Name = "Perfect Hundred", Description = "Have 100 perfect days",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Consistency,
                Rarity = AchievementRarity.Epic, Points = 300, SortOrder = 12,
                UnlockConditionType = UnlockConditionType.PerfectDays, UnlockConditionValue = "{\"count\": 100}",
                ProgressTarget = 100
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // TIME-BASED (Category 8) - Early bird, time of day
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "EARLY_BIRD", Name = "Early Bird", Description = "Complete all daily chores before noon",
                Icon = EmojiConstants.Sunrise, Category = AchievementCategory.TimeBased,
                Rarity = AchievementRarity.Uncommon, Points = 25, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.EarlyCompletion, UnlockConditionValue = "{\"before_hour\": 12, \"count\": 1}"
            },
            new Achievement
            {
                Code = "EARLY_BIRD_7", Name = "Morning Person", Description = "Complete all chores before noon for 7 days",
                Icon = EmojiConstants.Sun, Category = AchievementCategory.TimeBased,
                Rarity = AchievementRarity.Rare, Points = 75, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.EarlyCompletion, UnlockConditionValue = "{\"before_hour\": 12, \"count\": 7}",
                ProgressTarget = 7
            },
            new Achievement
            {
                Code = "DAWN_PATROL", Name = "Dawn Patrol", Description = "Complete a chore before 7 AM",
                Icon = EmojiConstants.Moon, Category = AchievementCategory.TimeBased,
                Rarity = AchievementRarity.Rare, Points = 50, SortOrder = 3,
                UnlockConditionType = UnlockConditionType.TimeOfDayCompletion, 
                UnlockConditionValue = "{\"hour_start\": 0, \"hour_end\": 7, \"count\": 1}"
            },
            new Achievement
            {
                Code = "WEEKEND_WARRIOR", Name = "Weekend Warrior", Description = "Complete 20 chores on weekends",
                Icon = EmojiConstants.Party, Category = AchievementCategory.TimeBased,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 5,
                UnlockConditionType = UnlockConditionType.DayTypeCompletion, 
                UnlockConditionValue = "{\"day_type\": \"Weekend\", \"count\": 20}",
                ProgressTarget = 20
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // SAVINGS (Category 10) - Goals and savings
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "GOAL_COMPLETE", Name = "Goal Getter", Description = "Complete a savings goal",
                Icon = EmojiConstants.Party, Category = AchievementCategory.Savings,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.GoalCompleted, UnlockConditionValue = "{\"count\": 1}"
            },
            new Achievement
            {
                Code = "GOAL_COMPLETE_3", Name = "Triple Saver", Description = "Complete 3 savings goals",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Savings,
                Rarity = AchievementRarity.Epic, Points = 200, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.GoalCompleted, UnlockConditionValue = "{\"count\": 3}",
                ProgressTarget = 3
            },
            new Achievement
            {
                Code = "BALANCE_25", Name = "Quarter Saved", Description = "Have $25 in your balance",
                Icon = EmojiConstants.MoneyBag, Category = AchievementCategory.Savings,
                Rarity = AchievementRarity.Uncommon, Points = 35, SortOrder = 5,
                UnlockConditionType = UnlockConditionType.BalanceReached, UnlockConditionValue = "{\"amount\": 25}",
                ProgressTarget = 25
            },
            new Achievement
            {
                Code = "BALANCE_50", Name = "Halfway There", Description = "Have $50 in your balance",
                Icon = EmojiConstants.MoneyBagDouble, Category = AchievementCategory.Savings,
                Rarity = AchievementRarity.Rare, Points = 60, SortOrder = 6,
                UnlockConditionType = UnlockConditionType.BalanceReached, UnlockConditionValue = "{\"amount\": 50}",
                ProgressTarget = 50
            },
            new Achievement
            {
                Code = "FIRST_CASHOUT", Name = "Payday!", Description = "Cash out your earnings for the first time",
                Icon = EmojiConstants.Dollar, Category = AchievementCategory.Savings,
                Rarity = AchievementRarity.Common, Points = 25, SortOrder = 10,
                UnlockConditionType = UnlockConditionType.CashOut, UnlockConditionValue = "{\"count\": 1}"
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // SPECIAL (Category 5) - Misc special achievements
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "BONUS_WORKER", Name = "Overachiever", Description = "Complete 5 bonus chores beyond weekly target",
                Icon = EmojiConstants.Sparkles, Category = AchievementCategory.Special,
                Rarity = AchievementRarity.Uncommon, Points = 40, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.BonusChoresCompleted, UnlockConditionValue = "{\"count\": 5}",
                ProgressTarget = 5
            },
            new Achievement
            {
                Code = "BONUS_WORKER_20", Name = "Extra Miler", Description = "Complete 20 bonus chores beyond weekly target",
                Icon = EmojiConstants.GoldMedal, Category = AchievementCategory.Special,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.BonusChoresCompleted, UnlockConditionValue = "{\"count\": 20}",
                ProgressTarget = 20
            },
            new Achievement
            {
                Code = "WEEK_STREAK_4", Name = "Month of Excellence", Description = "Complete 4 consecutive full weeks",
                Icon = EmojiConstants.Calendar, Category = AchievementCategory.Special,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 5,
                UnlockConditionType = UnlockConditionType.WeekStreak, UnlockConditionValue = "{\"weeks\": 4}",
                ProgressTarget = 4
            },
            new Achievement
            {
                Code = "PENALTY_FREE_30", Name = "Clean Record", Description = "Go 30 days without any penalties",
                Icon = EmojiConstants.CheckMark, Category = AchievementCategory.Special,
                Rarity = AchievementRarity.Rare, Points = 75, SortOrder = 10,
                UnlockConditionType = UnlockConditionType.PenaltyFree, UnlockConditionValue = "{\"days\": 30}",
                ProgressTarget = 30,
                BonusType = AchievementBonusType.PenaltyReduction, BonusValue = "{\"reduction_percent\": 0.25, \"duration_days\": 14}",
                BonusDescription = "25% penalty reduction for 14 days"
            },
            new Achievement
            {
                Code = "HELP_ASKER", Name = "Team Player", Description = "Ask for help when you need it",
                Icon = EmojiConstants.RaisingHands, Category = AchievementCategory.Special,
                Rarity = AchievementRarity.Common, Points = 15, SortOrder = 15,
                UnlockConditionType = UnlockConditionType.HelpRequested, UnlockConditionValue = "{\"count\": 1}"
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // SECRET/HIDDEN (Category 6) - Hidden until unlocked
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "SECRET_EARLY_ADOPTER", Name = "Pioneer", 
                Description = "Be one of the first to use Daily Bread",
                HiddenHint = "Something about being early...",
                Icon = EmojiConstants.Rainbow, Category = AchievementCategory.Secret,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 1,
                IsHidden = true, IsVisibleBeforeUnlock = false,
                UnlockConditionType = UnlockConditionType.AccountAge, UnlockConditionValue = "{\"days\": 7}",
                BonusType = AchievementBonusType.ProfileBadge, BonusValue = "{\"badge_key\": \"pioneer\"}",
                BonusDescription = "Pioneer badge"
            },
            new Achievement
            {
                Code = "SECRET_PERFECTIONIST", Name = "Perfectionist", 
                Description = "Complete all chores perfectly for 2 weeks straight",
                HiddenHint = "Perfection has its rewards...",
                Icon = EmojiConstants.GlowingStar, Category = AchievementCategory.Secret,
                Rarity = AchievementRarity.Epic, Points = 150, SortOrder = 2,
                IsHidden = true, IsVisibleBeforeUnlock = false,
                UnlockConditionType = UnlockConditionType.StreakDays, UnlockConditionValue = "{\"days\": 14}"
            },
            new Achievement
            {
                Code = "SECRET_COMEBACK", Name = "Comeback Kid", 
                Description = "Return and complete chores after a break",
                HiddenHint = "Everyone deserves a second chance...",
                Icon = EmojiConstants.Muscle, Category = AchievementCategory.Secret,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 3,
                IsHidden = true, IsVisibleBeforeUnlock = false,
                UnlockConditionType = UnlockConditionType.ChoreRecovery, UnlockConditionValue = "{\"count\": 1}"
            },
            new Achievement
            {
                Code = "SECRET_SAVER", Name = "Secret Saver", 
                Description = "Save up $100 without cashing out",
                HiddenHint = "Patience is a virtue...",
                Icon = EmojiConstants.MoneyBag, Category = AchievementCategory.Secret,
                Rarity = AchievementRarity.Epic, Points = 150, SortOrder = 4,
                IsHidden = true, IsVisibleBeforeUnlock = false,
                UnlockConditionType = UnlockConditionType.BalanceReached, UnlockConditionValue = "{\"amount\": 100}"
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // LEGENDARY (Category 7) - Ultimate achievements
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "LEGEND_YEAR", Name = "Year of Dedication", 
                Description = "Be an active member for a full year",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Legendary,
                Rarity = AchievementRarity.Legendary, Points = 500, SortOrder = 1,
                IsLegendary = true,
                UnlockConditionType = UnlockConditionType.AccountAge, UnlockConditionValue = "{\"days\": 365}",
                BonusType = AchievementBonusType.ProfileBadge, BonusValue = "{\"badge_key\": \"year_dedication\"}",
                BonusDescription = "Year of Dedication badge"
            },
            new Achievement
            {
                Code = "LEGEND_THOUSAND", Name = "The Thousand", 
                Description = "Complete 1000 chores",
                Icon = EmojiConstants.GlowingStar, Category = AchievementCategory.Legendary,
                Rarity = AchievementRarity.Legendary, Points = 750, SortOrder = 2,
                IsLegendary = true,
                UnlockConditionType = UnlockConditionType.ChoresCompleted, UnlockConditionValue = "{\"count\": 1000}",
                ProgressTarget = 1000
            },
            new Achievement
            {
                Code = "LEGEND_GRAND", Name = "Grand Earner", 
                Description = "Earn a total of $1000",
                Icon = EmojiConstants.Star, Category = AchievementCategory.Legendary,
                Rarity = AchievementRarity.Legendary, Points = 1000, SortOrder = 3,
                IsLegendary = true,
                UnlockConditionType = UnlockConditionType.TotalEarned, UnlockConditionValue = "{\"amount\": 1000}",
                ProgressTarget = 1000
            },
        });

        // ═══════════════════════════════════════════════════════════════
        // MASTERY (Category 11) - Achievement achievements
        // ═══════════════════════════════════════════════════════════════
        achievements.AddRange(new[]
        {
            new Achievement
            {
                Code = "ACHIEVEMENT_10", Name = "Getting Started", 
                Description = "Earn 10 achievements",
                Icon = EmojiConstants.GoldMedal, Category = AchievementCategory.Mastery,
                Rarity = AchievementRarity.Uncommon, Points = 50, SortOrder = 1,
                UnlockConditionType = UnlockConditionType.TotalAchievements, UnlockConditionValue = "{\"count\": 10}",
                ProgressTarget = 10
            },
            new Achievement
            {
                Code = "ACHIEVEMENT_25", Name = "Achievement Hunter", 
                Description = "Earn 25 achievements",
                Icon = EmojiConstants.Trophy, Category = AchievementCategory.Mastery,
                Rarity = AchievementRarity.Rare, Points = 100, SortOrder = 2,
                UnlockConditionType = UnlockConditionType.TotalAchievements, UnlockConditionValue = "{\"count\": 25}",
                ProgressTarget = 25
            },
            new Achievement
            {
                Code = "ACHIEVEMENT_50", Name = "Achievement Master", 
                Description = "Earn 50 achievements",
                Icon = EmojiConstants.Star, Category = AchievementCategory.Mastery,
                Rarity = AchievementRarity.Epic, Points = 250, SortOrder = 3,
                UnlockConditionType = UnlockConditionType.TotalAchievements, UnlockConditionValue = "{\"count\": 50}",
                ProgressTarget = 50,
                BonusType = AchievementBonusType.ProfileBadge, BonusValue = "{\"badge_key\": \"achievement_master\"}",
                BonusDescription = "Achievement Master badge"
            },
            new Achievement
            {
                Code = "STREAK_MASTER", Name = "Streak Master", 
                Description = "Earn 3 streak achievements",
                Icon = EmojiConstants.Fire, Category = AchievementCategory.Mastery,
                Rarity = AchievementRarity.Rare, Points = 75, SortOrder = 10,
                UnlockConditionType = UnlockConditionType.CategoryMastery, 
                UnlockConditionValue = "{\"category\": \"Streaks\", \"count\": 3}",
                ProgressTarget = 3
            },
            new Achievement
            {
                Code = "EARNING_MASTER", Name = "Earning Expert", 
                Description = "Earn 3 earnings achievements",
                Icon = EmojiConstants.MoneyBag, Category = AchievementCategory.Mastery,
                Rarity = AchievementRarity.Rare, Points = 75, SortOrder = 11,
                UnlockConditionType = UnlockConditionType.CategoryMastery, 
                UnlockConditionValue = "{\"category\": \"Earnings\", \"count\": 3}",
                ProgressTarget = 3
            },
        });

        return achievements;
    }
}
