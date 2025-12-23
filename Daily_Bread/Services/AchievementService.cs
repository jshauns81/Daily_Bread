using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for displaying a blessing (achievement).
/// </summary>
public class AchievementDisplay
{
    public int Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public AchievementCategory Category { get; init; }
    public int Points { get; init; }
    public bool IsEarned { get; init; }
    public DateTime? EarnedAt { get; init; }
    public bool IsNew { get; init; } // Just earned, hasn't been seen
}

/// <summary>
/// Service interface for blessings (achievements).
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Gets all blessings with earned status for a user.
    /// </summary>
    Task<List<AchievementDisplay>> GetAllAchievementsAsync(string userId);

    /// <summary>
    /// Gets earned blessings for a user.
    /// </summary>
    Task<List<AchievementDisplay>> GetEarnedAchievementsAsync(string userId);

    /// <summary>
    /// Gets newly earned blessings that haven't been seen.
    /// </summary>
    Task<List<AchievementDisplay>> GetUnseenAchievementsAsync(string userId);

    /// <summary>
    /// Marks blessings as seen by the user.
    /// </summary>
    Task MarkAchievementsAsSeenAsync(string userId);

    /// <summary>
    /// Checks and awards any newly earned blessings.
    /// Returns list of newly awarded blessings.
    /// </summary>
    Task<List<AchievementDisplay>> CheckAndAwardAchievementsAsync(string userId);

    /// <summary>
    /// Gets total blessing points earned by user.
    /// </summary>
    Task<int> GetTotalPointsAsync(string userId);

    /// <summary>
    /// Seeds the default blessings (called on startup).
    /// </summary>
    Task SeedAchievementsAsync();
}

/// <summary>
/// Service for managing blessings/badges.
/// </summary>
public class AchievementService : IAchievementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILedgerService _ledgerService;
    private readonly IDateProvider _dateProvider;

    public AchievementService(
        IDbContextFactory<ApplicationDbContext> contextFactory, 
        ILedgerService ledgerService,
        IDateProvider dateProvider)
    {
        _contextFactory = contextFactory;
        _ledgerService = ledgerService;
        _dateProvider = dateProvider;
    }

    public async Task<List<AchievementDisplay>> GetAllAchievementsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var achievements = await context.Achievements
            .Where(a => a.IsActive)
            .OrderBy(a => a.Category)
            .ThenBy(a => a.SortOrder)
            .ToListAsync();

        var earnedIds = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .ToDictionaryAsync(ua => ua.AchievementId, ua => ua);

        return achievements.Select(a =>
        {
            earnedIds.TryGetValue(a.Id, out var userAchievement);
            return new AchievementDisplay
            {
                Id = a.Id,
                Code = a.Code,
                Name = a.Name,
                Description = a.Description,
                Icon = a.Icon,
                Category = a.Category,
                Points = a.Points,
                IsEarned = userAchievement != null,
                EarnedAt = userAchievement?.EarnedAt,
                IsNew = userAchievement != null && !userAchievement.HasSeen
            };
        }).ToList();
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

        // Get user's current stats
        var balance = await _ledgerService.GetUserBalanceAsync(userId);
        var totalEarned = await GetTotalEarnedAsync(userId);
        var currentStreak = await CalculateCurrentStreakAsync(userId);
        var totalChoresCompleted = await GetTotalChoresCompletedAsync(userId);
        var perfectDays = await GetPerfectDaysCountAsync(userId);

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Get all achievements not yet earned
        var earnedAchievementIds = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync();

        var unearnedAchievements = await context.Achievements
            .Where(a => a.IsActive && !earnedAchievementIds.Contains(a.Id))
            .ToListAsync();

        foreach (var achievement in unearnedAchievements)
        {
            bool earned = CheckAchievementCriteria(
                achievement.Code, 
                balance, 
                totalEarned, 
                currentStreak, 
                totalChoresCompleted,
                perfectDays);

            if (earned)
            {
                var userAchievement = new UserAchievement
                {
                    UserId = userId,
                    AchievementId = achievement.Id,
                    EarnedAt = DateTime.UtcNow,
                    HasSeen = false
                };

                context.UserAchievements.Add(userAchievement);

                newlyAwarded.Add(new AchievementDisplay
                {
                    Id = achievement.Id,
                    Code = achievement.Code,
                    Name = achievement.Name,
                    Description = achievement.Description,
                    Icon = achievement.Icon,
                    Category = achievement.Category,
                    Points = achievement.Points,
                    IsEarned = true,
                    EarnedAt = DateTime.UtcNow,
                    IsNew = true
                });
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

    public async Task SeedAchievementsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Check if already seeded first (before trying to fix icons)
        if (await context.Achievements.AnyAsync())
        {
            // Fix any corrupted icons from previous seeding
            await FixCorruptedIconsAsync();
            return; // Already seeded
        }

        var achievements = new List<Achievement>
        {
            // Getting Started - using EmojiConstants for reliability
            new() { Code = "FIRST_CHORE", Name = "First Steps", Description = "Complete your first labor", Icon = EmojiConstants.Footprints, Category = AchievementCategory.GettingStarted, Points = 10, SortOrder = 1 },
            new() { Code = "FIRST_DOLLAR", Name = "First Wages", Description = "Earn your first dollar in wages", Icon = EmojiConstants.Dollar, Category = AchievementCategory.GettingStarted, Points = 10, SortOrder = 2 },
            new() { Code = "FIRST_GOAL", Name = "Dream Big", Description = "Create your first savings goal", Icon = EmojiConstants.Target, Category = AchievementCategory.GettingStarted, Points = 10, SortOrder = 3 },

            // Streaks
            new() { Code = "STREAK_3", Name = "Getting Going", Description = "Complete all labors for 3 days in a row", Icon = EmojiConstants.Fire, Category = AchievementCategory.Streaks, Points = 25, SortOrder = 1 },
            new() { Code = "STREAK_7", Name = "Week Warrior", Description = "Complete all labors for 7 days in a row", Icon = EmojiConstants.FireDouble, Category = AchievementCategory.Streaks, Points = 50, SortOrder = 2 },
            new() { Code = "STREAK_14", Name = "Fortnight Fighter", Description = "Complete all labors for 14 days in a row", Icon = EmojiConstants.FireTriple, Category = AchievementCategory.Streaks, Points = 100, SortOrder = 3 },
            new() { Code = "STREAK_30", Name = "Monthly Master", Description = "Complete all labors for 30 days in a row", Icon = EmojiConstants.Trophy, Category = AchievementCategory.Streaks, Points = 200, SortOrder = 4 },

            // Wages (Earnings)
            new() { Code = "EARNED_10", Name = "Tenner", Description = "Earn a total of $10 in wages", Icon = EmojiConstants.MoneyBag, Category = AchievementCategory.Earnings, Points = 25, SortOrder = 1 },
            new() { Code = "EARNED_25", Name = "Quarter Century", Description = "Earn a total of $25 in wages", Icon = EmojiConstants.MoneyBagDouble, Category = AchievementCategory.Earnings, Points = 50, SortOrder = 2 },
            new() { Code = "EARNED_50", Name = "Half Century", Description = "Earn a total of $50 in wages", Icon = EmojiConstants.MoneyBagTriple, Category = AchievementCategory.Earnings, Points = 75, SortOrder = 3 },
            new() { Code = "EARNED_100", Name = "Benjamin", Description = "Earn a total of $100 in wages", Icon = EmojiConstants.MoneyFace, Category = AchievementCategory.Earnings, Points = 150, SortOrder = 4 },

            // Consistency
            new() { Code = "CHORES_10", Name = "Helping Hand", Description = "Complete 10 labors", Icon = EmojiConstants.RaisedHand, Category = AchievementCategory.Consistency, Points = 15, SortOrder = 1 },
            new() { Code = "CHORES_50", Name = "Labor Champion", Description = "Complete 50 labors", Icon = EmojiConstants.GoldMedal, Category = AchievementCategory.Consistency, Points = 50, SortOrder = 2 },
            new() { Code = "CHORES_100", Name = "Century Club", Description = "Complete 100 labors", Icon = EmojiConstants.HundredPoints, Category = AchievementCategory.Consistency, Points = 100, SortOrder = 3 },
            new() { Code = "PERFECT_7", Name = "Perfect Week", Description = "Have 7 perfect days (all labors done)", Icon = EmojiConstants.Star, Category = AchievementCategory.Consistency, Points = 50, SortOrder = 4 },
            new() { Code = "PERFECT_30", Name = "Perfect Month", Description = "Have 30 perfect days", Icon = EmojiConstants.GlowingStar, Category = AchievementCategory.Consistency, Points = 150, SortOrder = 5 },

            // Special
            new() { Code = "GOAL_COMPLETE", Name = "Goal Getter", Description = "Complete a savings goal", Icon = EmojiConstants.Party, Category = AchievementCategory.Special, Points = 100, SortOrder = 1 },
            new() { Code = "EARLY_BIRD", Name = "Early Bird", Description = "Complete all labors before noon", Icon = EmojiConstants.Sunrise, Category = AchievementCategory.Special, Points = 25, SortOrder = 2 },
        };

        context.Achievements.AddRange(achievements);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Fixes achievement icons that were corrupted during initial seeding.
    /// Uses EmojiConstants for reliability.
    /// </summary>
    private async Task FixCorruptedIconsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Using EmojiConstants that work reliably across all systems
        var iconMappings = new Dictionary<string, string>
        {
            { "FIRST_CHORE", EmojiConstants.Footprints },
            { "FIRST_DOLLAR", EmojiConstants.Dollar },
            { "FIRST_GOAL", EmojiConstants.Target },
            { "STREAK_3", EmojiConstants.Fire },
            { "STREAK_7", EmojiConstants.FireDouble },
            { "STREAK_14", EmojiConstants.FireTriple },
            { "STREAK_30", EmojiConstants.Trophy },
            { "EARNED_10", EmojiConstants.MoneyBag },
            { "EARNED_25", EmojiConstants.MoneyBagDouble },
            { "EARNED_50", EmojiConstants.MoneyBagTriple },
            { "EARNED_100", EmojiConstants.MoneyFace },
            { "CHORES_10", EmojiConstants.RaisedHand },
            { "CHORES_50", EmojiConstants.GoldMedal },
            { "CHORES_100", EmojiConstants.HundredPoints },
            { "PERFECT_7", EmojiConstants.Star },
            { "PERFECT_30", EmojiConstants.GlowingStar },
            { "GOAL_COMPLETE", EmojiConstants.Party },
            { "EARLY_BIRD", EmojiConstants.Sunrise }
        };

        var achievements = await context.Achievements.ToListAsync();
        var updated = false;

        foreach (var achievement in achievements)
        {
            if (iconMappings.TryGetValue(achievement.Code, out var correctIcon))
            {
                if (achievement.Icon != correctIcon)
                {
                    achievement.Icon = correctIcon;
                    updated = true;
                }
            }
        }

        if (updated)
        {
            await context.SaveChangesAsync();
        }
    }

    private bool CheckAchievementCriteria(
        string code, 
        decimal balance, 
        decimal totalEarned, 
        int currentStreak, 
        int totalChoresCompleted,
        int perfectDays)
    {
        return code switch
        {
            // Getting Started
            "FIRST_CHORE" => totalChoresCompleted >= 1,
            "FIRST_DOLLAR" => totalEarned >= 1,
            "FIRST_GOAL" => false, // Checked separately when goal is created

            // Streaks
            "STREAK_3" => currentStreak >= 3,
            "STREAK_7" => currentStreak >= 7,
            "STREAK_14" => currentStreak >= 14,
            "STREAK_30" => currentStreak >= 30,

            // Earnings
            "EARNED_10" => totalEarned >= 10,
            "EARNED_25" => totalEarned >= 25,
            "EARNED_50" => totalEarned >= 50,
            "EARNED_100" => totalEarned >= 100,

            // Consistency
            "CHORES_10" => totalChoresCompleted >= 10,
            "CHORES_50" => totalChoresCompleted >= 50,
            "CHORES_100" => totalChoresCompleted >= 100,
            "PERFECT_7" => perfectDays >= 7,
            "PERFECT_30" => perfectDays >= 30,

            // Special - these are awarded separately
            "GOAL_COMPLETE" => false,
            "EARLY_BIRD" => false,

            _ => false
        };
    }

    private async Task<decimal> GetTotalEarnedAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0)
            .SumAsync(t => t.Amount);
    }

    private async Task<int> GetTotalChoresCompletedAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)
            .CountAsync();
    }

    private async Task<int> GetPerfectDaysCountAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var today = _dateProvider.Today;
        
        // Get all dates with chores assigned to this user
        var datesWithChores = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId && cl.Date <= today)
            .GroupBy(cl => cl.Date)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Count(),
                Completed = g.Count(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved || cl.Status == ChoreStatus.Skipped)
            })
            .ToListAsync();

        return datesWithChores.Count(d => d.Total > 0 && d.Completed == d.Total);
    }

    private async Task<int> CalculateCurrentStreakAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var today = _dateProvider.Today;
        
        // Optimized: Load all relevant data in one query instead of 365 separate queries
        var startDate = today.AddDays(-365);
        var allChoresInRange = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= startDate && cl.Date <= today)
            .ToListAsync();

        var choresByDate = allChoresInRange
            .GroupBy(cl => cl.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var streak = 0;
        var currentDate = today;

        for (int i = 0; i < 365; i++)
        {
            if (!choresByDate.TryGetValue(currentDate, out var choresForDate) || choresForDate.Count == 0)
            {
                currentDate = currentDate.AddDays(-1);
                continue;
            }

            var allCompleted = choresForDate.All(c => 
                c.Status == ChoreStatus.Completed || 
                c.Status == ChoreStatus.Approved ||
                c.Status == ChoreStatus.Skipped);

            if (allCompleted)
            {
                streak++;
                currentDate = currentDate.AddDays(-1);
            }
            else
            {
                break;
            }
        }

        return streak;
    }
}
