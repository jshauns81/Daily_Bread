using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for displaying an achievement.
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
/// Service interface for achievements.
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Gets all achievements with earned status for a user.
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
    /// Seeds the default achievements (called on startup).
    /// </summary>
    Task SeedAchievementsAsync();
}

/// <summary>
/// Service for managing achievements/badges.
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
            // Getting Started - using Unicode escape sequences
            new() { Code = "FIRST_CHORE", Name = "First Steps", Description = "Complete your first chore", Icon = "\U0001F463", Category = AchievementCategory.GettingStarted, Points = 10, SortOrder = 1 },
            new() { Code = "FIRST_DOLLAR", Name = "Money Maker", Description = "Earn your first dollar", Icon = "\U0001F4B5", Category = AchievementCategory.GettingStarted, Points = 10, SortOrder = 2 },
            new() { Code = "FIRST_GOAL", Name = "Dream Big", Description = "Create your first savings goal", Icon = "\U0001F3AF", Category = AchievementCategory.GettingStarted, Points = 10, SortOrder = 3 },

            // Streaks
            new() { Code = "STREAK_3", Name = "Getting Going", Description = "Complete all chores for 3 days in a row", Icon = "\U0001F525", Category = AchievementCategory.Streaks, Points = 25, SortOrder = 1 },
            new() { Code = "STREAK_7", Name = "Week Warrior", Description = "Complete all chores for 7 days in a row", Icon = "\U0001F525\U0001F525", Category = AchievementCategory.Streaks, Points = 50, SortOrder = 2 },
            new() { Code = "STREAK_14", Name = "Fortnight Fighter", Description = "Complete all chores for 14 days in a row", Icon = "\U0001F525\U0001F525\U0001F525", Category = AchievementCategory.Streaks, Points = 100, SortOrder = 3 },
            new() { Code = "STREAK_30", Name = "Monthly Master", Description = "Complete all chores for 30 days in a row", Icon = "\U0001F3C6", Category = AchievementCategory.Streaks, Points = 200, SortOrder = 4 },

            // Earnings
            new() { Code = "EARNED_10", Name = "Tenner", Description = "Earn a total of $10", Icon = "\U0001F4B0", Category = AchievementCategory.Earnings, Points = 25, SortOrder = 1 },
            new() { Code = "EARNED_25", Name = "Quarter Century", Description = "Earn a total of $25", Icon = "\U0001F4B0\U0001F4B0", Category = AchievementCategory.Earnings, Points = 50, SortOrder = 2 },
            new() { Code = "EARNED_50", Name = "Half Century", Description = "Earn a total of $50", Icon = "\U0001F4B0\U0001F4B0\U0001F4B0", Category = AchievementCategory.Earnings, Points = 75, SortOrder = 3 },
            new() { Code = "EARNED_100", Name = "Benjamin", Description = "Earn a total of $100", Icon = "\U0001F911", Category = AchievementCategory.Earnings, Points = 150, SortOrder = 4 },

            // Consistency
            new() { Code = "CHORES_10", Name = "Helping Hand", Description = "Complete 10 chores", Icon = "\u270B", Category = AchievementCategory.Consistency, Points = 15, SortOrder = 1 },
            new() { Code = "CHORES_50", Name = "Chore Champion", Description = "Complete 50 chores", Icon = "\U0001F947", Category = AchievementCategory.Consistency, Points = 50, SortOrder = 2 },
            new() { Code = "CHORES_100", Name = "Century Club", Description = "Complete 100 chores", Icon = "\U0001F4AF", Category = AchievementCategory.Consistency, Points = 100, SortOrder = 3 },
            new() { Code = "PERFECT_7", Name = "Perfect Week", Description = "Have 7 perfect days (all chores done)", Icon = "\u2B50", Category = AchievementCategory.Consistency, Points = 50, SortOrder = 4 },
            new() { Code = "PERFECT_30", Name = "Perfect Month", Description = "Have 30 perfect days", Icon = "\U0001F31F", Category = AchievementCategory.Consistency, Points = 150, SortOrder = 5 },

            // Special
            new() { Code = "GOAL_COMPLETE", Name = "Goal Getter", Description = "Complete a savings goal", Icon = "\U0001F389", Category = AchievementCategory.Special, Points = 100, SortOrder = 1 },
            new() { Code = "EARLY_BIRD", Name = "Early Bird", Description = "Complete all chores before noon", Icon = "\U0001F305", Category = AchievementCategory.Special, Points = 25, SortOrder = 2 },
        };

        context.Achievements.AddRange(achievements);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Fixes achievement icons that were corrupted during initial seeding.
    /// Uses Unicode escape sequences for reliability.
    /// </summary>
    private async Task FixCorruptedIconsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Using Unicode codepoints that work reliably across all systems
        var iconMappings = new Dictionary<string, string>
        {
            { "FIRST_CHORE", "\U0001F463" },    // ?? footprints
            { "FIRST_DOLLAR", "\U0001F4B5" },   // ?? dollar
            { "FIRST_GOAL", "\U0001F3AF" },     // ?? target
            { "STREAK_3", "\U0001F525" },       // ?? fire
            { "STREAK_7", "\U0001F525\U0001F525" },      // ????
            { "STREAK_14", "\U0001F525\U0001F525\U0001F525" },  // ??????
            { "STREAK_30", "\U0001F3C6" },      // ?? trophy
            { "EARNED_10", "\U0001F4B0" },      // ?? money bag
            { "EARNED_25", "\U0001F4B0\U0001F4B0" },     // ????
            { "EARNED_50", "\U0001F4B0\U0001F4B0\U0001F4B0" },  // ??????
            { "EARNED_100", "\U0001F911" },     // ?? money face
            { "CHORES_10", "\u270B" },          // ? hand
            { "CHORES_50", "\U0001F947" },      // ?? gold medal
            { "CHORES_100", "\U0001F4AF" },     // ?? hundred
            { "PERFECT_7", "\u2B50" },          // ? star
            { "PERFECT_30", "\U0001F31F" },     // ?? glowing star
            { "GOAL_COMPLETE", "\U0001F389" },  // ?? party
            { "EARLY_BIRD", "\U0001F305" }      // ?? sunrise
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
