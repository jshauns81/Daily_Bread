using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Daily_Bread.Services;

/// <summary>
/// Result of evaluating an achievement condition.
/// </summary>
public record AchievementEvaluationResult
{
    /// <summary>
    /// Whether the achievement condition is met.
    /// </summary>
    public bool IsMet { get; init; }
    
    /// <summary>
    /// Current progress value (for progress-based achievements).
    /// </summary>
    public int CurrentValue { get; init; }
    
    /// <summary>
    /// Target value required for unlock.
    /// </summary>
    public int TargetValue { get; init; }
    
    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int ProgressPercent => TargetValue > 0 ? Math.Min(100, (int)((CurrentValue * 100.0) / TargetValue)) : 0;
    
    /// <summary>
    /// Optional metadata about the evaluation.
    /// </summary>
    public string? Metadata { get; init; }
    
    public static AchievementEvaluationResult Met(int current = 1, int target = 1, string? metadata = null) 
        => new() { IsMet = true, CurrentValue = current, TargetValue = target, Metadata = metadata };
    
    public static AchievementEvaluationResult NotMet(int current, int target, string? metadata = null) 
        => new() { IsMet = false, CurrentValue = current, TargetValue = target, Metadata = metadata };
    
    public static AchievementEvaluationResult NotApplicable() 
        => new() { IsMet = false, CurrentValue = 0, TargetValue = 0 };
}

/// <summary>
/// Interface for evaluating achievement unlock conditions.
/// </summary>
public interface IAchievementConditionEvaluator
{
    /// <summary>
    /// Evaluates whether a specific achievement condition is met for a user.
    /// </summary>
    /// <param name="userId">The user to evaluate for.</param>
    /// <param name="achievement">The achievement to evaluate.</param>
    /// <returns>Evaluation result with progress information.</returns>
    Task<AchievementEvaluationResult> EvaluateAsync(string userId, Achievement achievement);
    
    /// <summary>
    /// Evaluates all unearned achievements for a user.
    /// </summary>
    /// <param name="userId">The user to evaluate for.</param>
    /// <returns>Dictionary of achievement ID to evaluation result.</returns>
    Task<Dictionary<int, AchievementEvaluationResult>> EvaluateAllAsync(string userId);
}

/// <summary>
/// Data-driven achievement condition evaluator.
/// 
/// HOW TO ADD NEW CONDITION TYPES:
/// 1. Add enum value to UnlockConditionType in Achievement.cs
/// 2. Add case in EvaluateAsync switch statement
/// 3. Implement the evaluation logic
/// 4. Document the JSON format in the enum's XML comments
/// 
/// All evaluations are designed to be:
/// - Idempotent: Running multiple times produces same result
/// - Deterministic: Given same data, always returns same result
/// - Efficient: Minimizes database queries where possible
/// </summary>
public class AchievementConditionEvaluator : IAchievementConditionEvaluator
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly ILogger<AchievementConditionEvaluator> _logger;

    public AchievementConditionEvaluator(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider,
        IFamilySettingsService familySettingsService,
        ILogger<AchievementConditionEvaluator> logger)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _familySettingsService = familySettingsService;
        _logger = logger;
    }

    public async Task<AchievementEvaluationResult> EvaluateAsync(string userId, Achievement achievement)
    {
        try
        {
            return achievement.UnlockConditionType switch
            {
                UnlockConditionType.Manual => AchievementEvaluationResult.NotApplicable(),
                UnlockConditionType.ChoresCompleted => await EvaluateChoresCompletedAsync(userId, achievement),
                UnlockConditionType.StreakDays => await EvaluateStreakDaysAsync(userId, achievement),
                UnlockConditionType.TotalEarned => await EvaluateTotalEarnedAsync(userId, achievement),
                UnlockConditionType.BalanceReached => await EvaluateBalanceReachedAsync(userId, achievement),
                UnlockConditionType.PerfectDays => await EvaluatePerfectDaysAsync(userId, achievement),
                UnlockConditionType.EarlyCompletion => await EvaluateEarlyCompletionAsync(userId, achievement),
                UnlockConditionType.FirstChore => await EvaluateFirstChoreAsync(userId, achievement),
                UnlockConditionType.FirstGoal => await EvaluateFirstGoalAsync(userId, achievement),
                UnlockConditionType.GoalCompleted => await EvaluateGoalCompletedAsync(userId, achievement),
                UnlockConditionType.FirstDollar => await EvaluateFirstDollarAsync(userId, achievement),
                UnlockConditionType.WeeklyEarnings => await EvaluateWeeklyEarningsAsync(userId, achievement),
                UnlockConditionType.DayTypeCompletion => await EvaluateDayTypeCompletionAsync(userId, achievement),
                UnlockConditionType.WeekStreak => await EvaluateWeekStreakAsync(userId, achievement),
                UnlockConditionType.AccountAge => await EvaluateAccountAgeAsync(userId, achievement),
                UnlockConditionType.BonusChoresCompleted => await EvaluateBonusChoresCompletedAsync(userId, achievement),
                UnlockConditionType.HelpRequested => await EvaluateHelpRequestedAsync(userId, achievement),
                UnlockConditionType.PenaltyFree => await EvaluatePenaltyFreeAsync(userId, achievement),
                UnlockConditionType.AchievementUnlocked => await EvaluateAchievementUnlockedAsync(userId, achievement),
                UnlockConditionType.CategoryMastery => await EvaluateCategoryMasteryAsync(userId, achievement),
                UnlockConditionType.TotalAchievements => await EvaluateTotalAchievementsAsync(userId, achievement),
                UnlockConditionType.TimeOfDayCompletion => await EvaluateTimeOfDayCompletionAsync(userId, achievement),
                UnlockConditionType.CashOut => await EvaluateCashOutAsync(userId, achievement),
                _ => AchievementEvaluationResult.NotApplicable()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating achievement {AchievementCode} for user {UserId}", 
                achievement.Code, userId);
            return AchievementEvaluationResult.NotApplicable();
        }
    }

    public async Task<Dictionary<int, AchievementEvaluationResult>> EvaluateAllAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Get all active achievements not yet earned by this user
        var earnedIds = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync();

        var unearnedAchievements = await context.Achievements
            .Where(a => a.IsActive && !earnedIds.Contains(a.Id))
            .ToListAsync();

        // =============================================================================
        // OPTIMIZATION: Pre-load ALL data needed for achievement evaluation in bulk
        // Previously: Each EvaluateAsync made 1-2 queries = N+1 problem
        // Now: Load all data upfront, evaluate in memory
        // =============================================================================
        var today = _dateProvider.Today;
        var startDate = today.AddDays(-365);
        
        // Pre-load all ChoreLogs for this user (used by many evaluations)
        var allChoreLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .ToListAsync();
        
        // Pre-load all LedgerTransactions for this user
        var allTransactions = await context.LedgerTransactions
            .Where(t => t.UserId == userId)
            .ToListAsync();
        
        // Pre-load savings goals
        var allGoals = await context.SavingsGoals
            .Where(g => g.UserId == userId)
            .ToListAsync();
        
        // Pre-load user achievements (for category mastery, etc.)
        var allUserAchievements = await context.UserAchievements
            .Include(ua => ua.Achievement)
            .Where(ua => ua.UserId == userId)
            .ToListAsync();
        
        // Pre-load child profile
        var childProfile = await context.ChildProfiles
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync();
        
        _logger.LogDebug("Achievement evaluation: Pre-loaded {Logs} logs, {Txns} transactions, {Goals} goals for user {UserId}",
            allChoreLogs.Count, allTransactions.Count, allGoals.Count, userId);

        // Create a context object with pre-loaded data
        var evalContext = new AchievementEvaluationContext
        {
            UserId = userId,
            Today = today,
            ChoreLogs = allChoreLogs,
            Transactions = allTransactions,
            SavingsGoals = allGoals,
            UserAchievements = allUserAchievements,
            ChildProfile = childProfile
        };

        var results = new Dictionary<int, AchievementEvaluationResult>();
        
        foreach (var achievement in unearnedAchievements)
        {
            results[achievement.Id] = EvaluateWithContext(achievement, evalContext);
        }

        return results;
    }
    
    /// <summary>
    /// Context object containing pre-loaded data for batch achievement evaluation.
    /// </summary>
    private class AchievementEvaluationContext
    {
        public required string UserId { get; init; }
        public DateOnly Today { get; init; }
        public List<ChoreLog> ChoreLogs { get; init; } = [];
        public List<LedgerTransaction> Transactions { get; init; } = [];
        public List<SavingsGoal> SavingsGoals { get; init; } = [];
        public List<UserAchievement> UserAchievements { get; init; } = [];
        public ChildProfile? ChildProfile { get; init; }
    }
    
    /// <summary>
    /// Evaluates an achievement using pre-loaded context data (no DB queries).
    /// </summary>
    private AchievementEvaluationResult EvaluateWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        try
        {
            return achievement.UnlockConditionType switch
            {
                UnlockConditionType.Manual => AchievementEvaluationResult.NotApplicable(),
                UnlockConditionType.ChoresCompleted => EvaluateChoresCompletedWithContext(achievement, ctx),
                UnlockConditionType.StreakDays => EvaluateStreakDaysWithContext(achievement, ctx),
                UnlockConditionType.TotalEarned => EvaluateTotalEarnedWithContext(achievement, ctx),
                UnlockConditionType.BalanceReached => EvaluateBalanceReachedWithContext(achievement, ctx),
                UnlockConditionType.PerfectDays => EvaluatePerfectDaysWithContext(achievement, ctx),
                UnlockConditionType.EarlyCompletion => EvaluateEarlyCompletionWithContext(achievement, ctx),
                UnlockConditionType.FirstChore => EvaluateFirstChoreWithContext(achievement, ctx),
                UnlockConditionType.FirstGoal => EvaluateFirstGoalWithContext(achievement, ctx),
                UnlockConditionType.GoalCompleted => EvaluateGoalCompletedWithContext(achievement, ctx),
                UnlockConditionType.FirstDollar => EvaluateFirstDollarWithContext(achievement, ctx),
                UnlockConditionType.DayTypeCompletion => EvaluateDayTypeCompletionWithContext(achievement, ctx),
                UnlockConditionType.AccountAge => EvaluateAccountAgeWithContext(achievement, ctx),
                UnlockConditionType.BonusChoresCompleted => EvaluateBonusChoresCompletedWithContext(achievement, ctx),
                UnlockConditionType.HelpRequested => EvaluateHelpRequestedWithContext(achievement, ctx),
                UnlockConditionType.PenaltyFree => EvaluatePenaltyFreeWithContext(achievement, ctx),
                UnlockConditionType.CategoryMastery => EvaluateCategoryMasteryWithContext(achievement, ctx),
                UnlockConditionType.TotalAchievements => EvaluateTotalAchievementsWithContext(achievement, ctx),
                UnlockConditionType.TimeOfDayCompletion => EvaluateTimeOfDayCompletionWithContext(achievement, ctx),
                UnlockConditionType.CashOut => EvaluateCashOutWithContext(achievement, ctx),
                // These still need async calls (complex logic or external services)
                _ => AchievementEvaluationResult.NotApplicable()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating achievement {AchievementCode} for user {UserId}", 
                achievement.Code, ctx.UserId);
            return AchievementEvaluationResult.NotApplicable();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // IN-MEMORY EVALUATION IMPLEMENTATIONS (using pre-loaded context)
    // ═══════════════════════════════════════════════════════════════

    private AchievementEvaluationResult EvaluateChoresCompletedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var count = ctx.ChoreLogs.Count(cl => 
            cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved);

        return count >= target 
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluateStreakDaysWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var targetDays = ParseInt(achievement.UnlockConditionValue, "days", 1);
        var streak = CalculateCurrentStreakFromLogs(ctx.ChoreLogs, ctx.Today);
        
        return streak >= targetDays
            ? AchievementEvaluationResult.Met(streak, targetDays)
            : AchievementEvaluationResult.NotMet(streak, targetDays);
    }

    private AchievementEvaluationResult EvaluateTotalEarnedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var targetAmount = ParseDecimal(achievement.UnlockConditionValue, "amount", 1);
        var target = (int)targetAmount;
        var total = ctx.Transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var current = (int)total;
        
        return total >= targetAmount
            ? AchievementEvaluationResult.Met(current, target)
            : AchievementEvaluationResult.NotMet(current, target);
    }

    private AchievementEvaluationResult EvaluateBalanceReachedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var targetAmount = ParseDecimal(achievement.UnlockConditionValue, "amount", 1);
        var target = (int)targetAmount;
        var balance = ctx.Transactions.Sum(t => t.Amount);
        var current = (int)balance;
        
        return balance >= targetAmount
            ? AchievementEvaluationResult.Met(current, target)
            : AchievementEvaluationResult.NotMet(Math.Max(0, current), target);
    }

    private AchievementEvaluationResult EvaluatePerfectDaysWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var perfectDays = CalculatePerfectDaysFromLogs(ctx.ChoreLogs, ctx.Today);
        
        return perfectDays >= target
            ? AchievementEvaluationResult.Met(perfectDays, target)
            : AchievementEvaluationResult.NotMet(perfectDays, target);
    }

    private AchievementEvaluationResult EvaluateEarlyCompletionWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var beforeHour = ParseInt(achievement.UnlockConditionValue, "before_hour", 12);
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        
        var earlyDays = ctx.ChoreLogs
            .Where(cl => cl.CompletedAt.HasValue && cl.Date <= ctx.Today)
            .GroupBy(cl => cl.Date)
            .Count(g => g.All(cl => 
                cl.CompletedAt.HasValue && 
                cl.CompletedAt.Value.Hour < beforeHour &&
                (cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)));
        
        return earlyDays >= target
            ? AchievementEvaluationResult.Met(earlyDays, target)
            : AchievementEvaluationResult.NotMet(earlyDays, target);
    }

    private AchievementEvaluationResult EvaluateFirstChoreWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var hasCompleted = ctx.ChoreLogs.Any(cl => 
            cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved);

        return hasCompleted
            ? AchievementEvaluationResult.Met()
            : AchievementEvaluationResult.NotMet(0, 1);
    }

    private AchievementEvaluationResult EvaluateFirstGoalWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        return ctx.SavingsGoals.Count > 0
            ? AchievementEvaluationResult.Met()
            : AchievementEvaluationResult.NotMet(0, 1);
    }

    private AchievementEvaluationResult EvaluateGoalCompletedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var count = ctx.SavingsGoals.Count(g => g.IsCompleted);

        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluateFirstDollarWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var total = ctx.Transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);

        return total >= 1
            ? AchievementEvaluationResult.Met()
            : AchievementEvaluationResult.NotMet((int)(total * 100), 100);
    }

    private AchievementEvaluationResult EvaluateDayTypeCompletionWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var dayType = ParseString(achievement.UnlockConditionValue, "day_type", "Weekend");
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        
        var completedDates = ctx.ChoreLogs
            .Where(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)
            .Select(cl => cl.Date)
            .ToList();

        var count = dayType.ToLower() switch
        {
            "weekend" => completedDates.Count(d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday),
            "weekday" => completedDates.Count(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday),
            "monday" => completedDates.Count(d => d.DayOfWeek == DayOfWeek.Monday),
            "friday" => completedDates.Count(d => d.DayOfWeek == DayOfWeek.Friday),
            _ => 0
        };
        
        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluateAccountAgeWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "days", 1);

        if (ctx.ChildProfile == null)
            return AchievementEvaluationResult.NotMet(0, target);

        var days = (int)(DateTime.UtcNow - ctx.ChildProfile.CreatedAt).TotalDays;
        return days >= target
            ? AchievementEvaluationResult.Met(days, target)
            : AchievementEvaluationResult.NotMet(days, target);
    }

    private AchievementEvaluationResult EvaluateBonusChoresCompletedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var count = ctx.Transactions.Count(t => 
            t.Description != null && t.Description.StartsWith("Bonus:"));
        
        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluateHelpRequestedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var count = ctx.ChoreLogs.Count(cl => cl.HelpRequestedAt.HasValue);
        
        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluatePenaltyFreeWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "days", 1);
        
        var lastPenalty = ctx.Transactions
            .Where(t => t.Type == TransactionType.ChoreDeduction || t.Type == TransactionType.Penalty)
            .OrderByDescending(t => t.TransactionDate)
            .Select(t => t.TransactionDate)
            .FirstOrDefault();

        int penaltyFreeDays;
        if (lastPenalty == default)
        {
            if (ctx.ChildProfile == null)
                return AchievementEvaluationResult.NotMet(0, target);
            penaltyFreeDays = (int)(DateTime.UtcNow - ctx.ChildProfile.CreatedAt).TotalDays;
        }
        else
        {
            penaltyFreeDays = ctx.Today.DayNumber - lastPenalty.DayNumber;
        }
        
        return penaltyFreeDays >= target
            ? AchievementEvaluationResult.Met(penaltyFreeDays, target)
            : AchievementEvaluationResult.NotMet(penaltyFreeDays, target);
    }

    private AchievementEvaluationResult EvaluateCategoryMasteryWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var categoryStr = ParseString(achievement.UnlockConditionValue, "category", "");
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        
        if (!Enum.TryParse<AchievementCategory>(categoryStr, true, out var category))
            return AchievementEvaluationResult.NotApplicable();
        
        var count = ctx.UserAchievements.Count(ua => ua.Achievement.Category == category);
        
        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluateTotalAchievementsWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var count = ctx.UserAchievements.Count;
        
        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private AchievementEvaluationResult EvaluateTimeOfDayCompletionWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var hourStart = ParseInt(achievement.UnlockConditionValue, "hour_start", 0);
        var hourEnd = ParseInt(achievement.UnlockConditionValue, "hour_end", 24);
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        
        var matchingCount = ctx.ChoreLogs.Count(cl => 
            cl.CompletedAt.HasValue &&
            (cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved) &&
            cl.CompletedAt.Value.Hour >= hourStart && 
            cl.CompletedAt.Value.Hour < hourEnd);
        
        return matchingCount >= target
            ? AchievementEvaluationResult.Met(matchingCount, target)
            : AchievementEvaluationResult.NotMet(matchingCount, target);
    }

    private AchievementEvaluationResult EvaluateCashOutWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var targetCount = ParseInt(achievement.UnlockConditionValue, "count", 0);
        var targetAmount = ParseDecimal(achievement.UnlockConditionValue, "total_amount", 0);
        
        var payouts = ctx.Transactions.Where(t => t.Type == TransactionType.Payout).ToList();
        
        if (targetCount > 0)
        {
            var count = payouts.Count;
            return count >= targetCount
                ? AchievementEvaluationResult.Met(count, targetCount)
                : AchievementEvaluationResult.NotMet(count, targetCount);
        }
        
        if (targetAmount > 0)
        {
            var total = payouts.Sum(t => Math.Abs(t.Amount));
            var target = (int)targetAmount;
            var current = (int)total;
            return total >= targetAmount
                ? AchievementEvaluationResult.Met(current, target)
                : AchievementEvaluationResult.NotMet(current, target);
        }
        
        return AchievementEvaluationResult.NotApplicable();
    }

    // ═══════════════════════════════════════════════════════════════
    // ASYNC EVALUATION IMPLEMENTATIONS (for single achievement calls)
    // These create context on-demand and delegate to WithContext methods
    // ═══════════════════════════════════════════════════════════════

    private async Task<AchievementEvaluationResult> EvaluateChoresCompletedAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateChoresCompletedWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateStreakDaysAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateStreakDaysWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateTotalEarnedAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateTotalEarnedWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateBalanceReachedAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateBalanceReachedWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluatePerfectDaysAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluatePerfectDaysWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateEarlyCompletionAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateEarlyCompletionWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateFirstChoreAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateFirstChoreWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateFirstGoalAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateFirstGoalWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateGoalCompletedAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateGoalCompletedWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateFirstDollarAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateFirstDollarWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateWeeklyEarningsAsync(string userId, Achievement achievement)
    {
        // This one needs week calculation - do inline
        var targetAmount = ParseDecimal(achievement.UnlockConditionValue, "amount", 1);
        var target = (int)targetAmount;
        
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(_dateProvider.Today);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(_dateProvider.Today);
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        var weekEarnings = await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0)
            .Where(t => t.TransactionDate >= weekStart && t.TransactionDate <= weekEnd)
            .SumAsync(t => t.Amount);
        
        var current = (int)weekEarnings;
        return weekEarnings >= targetAmount
            ? AchievementEvaluationResult.Met(current, target)
            : AchievementEvaluationResult.NotMet(current, target);
    }

    private async Task<AchievementEvaluationResult> EvaluateDayTypeCompletionAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateDayTypeCompletionWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateWeekStreakAsync(string userId, Achievement achievement)
    {
        // This one needs complex week calculation
        var target = ParseInt(achievement.UnlockConditionValue, "weeks", 1);
        var weekStreak = await CalculateWeekStreakAsync(userId);
        
        return weekStreak >= target
            ? AchievementEvaluationResult.Met(weekStreak, target)
            : AchievementEvaluationResult.NotMet(weekStreak, target);
    }

    private async Task<AchievementEvaluationResult> EvaluateAccountAgeAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateAccountAgeWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateBonusChoresCompletedAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateBonusChoresCompletedWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateHelpRequestedAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateHelpRequestedWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluatePenaltyFreeAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluatePenaltyFreeWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateAchievementUnlockedAsync(string userId, Achievement achievement)
    {
        var requiredCode = ParseString(achievement.UnlockConditionValue, "achievement_code", "");
        if (string.IsNullOrEmpty(requiredCode))
            return AchievementEvaluationResult.NotApplicable();
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        var hasRequired = await context.UserAchievements
            .Include(ua => ua.Achievement)
            .Where(ua => ua.UserId == userId && ua.Achievement.Code == requiredCode)
            .AnyAsync();
        
        return hasRequired
            ? AchievementEvaluationResult.Met()
            : AchievementEvaluationResult.NotMet(0, 1);
    }

    private async Task<AchievementEvaluationResult> EvaluateCategoryMasteryAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateCategoryMasteryWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateTotalAchievementsAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateTotalAchievementsWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateTimeOfDayCompletionAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateTimeOfDayCompletionWithContext(achievement, ctx);
    }

    private async Task<AchievementEvaluationResult> EvaluateCashOutAsync(string userId, Achievement achievement)
    {
        var ctx = await CreateEvaluationContextAsync(userId);
        return EvaluateCashOutWithContext(achievement, ctx);
    }

    /// <summary>
    /// Creates evaluation context by loading all necessary data.
    /// Used for single achievement evaluation (not batched).
    /// </summary>
    private async Task<AchievementEvaluationContext> CreateEvaluationContextAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var today = _dateProvider.Today;
        
        var allChoreLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .ToListAsync();
        
        var allTransactions = await context.LedgerTransactions
            .Where(t => t.UserId == userId)
            .ToListAsync();
        
        var allGoals = await context.SavingsGoals
            .Where(g => g.UserId == userId)
            .ToListAsync();
        
        var allUserAchievements = await context.UserAchievements
            .Include(ua => ua.Achievement)
            .Where(ua => ua.UserId == userId)
            .ToListAsync();
        
        var childProfile = await context.ChildProfiles
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync();
        
        return new AchievementEvaluationContext
        {
            UserId = userId,
            Today = today,
            ChoreLogs = allChoreLogs,
            Transactions = allTransactions,
            SavingsGoals = allGoals,
            UserAchievements = allUserAchievements,
            ChildProfile = childProfile
        };
    }

    private async Task<int> CalculateWeekStreakAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var today = _dateProvider.Today;
        var streak = 0;
        
        // Check last 52 weeks
        for (int weekOffset = 0; weekOffset < 52; weekOffset++)
        {
            var checkDate = today.AddDays(-7 * weekOffset);
            var weekStart = await _familySettingsService.GetWeekStartForDateAsync(checkDate);
            var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(checkDate);
            
            var weekLogs = await context.ChoreLogs
                .Include(cl => cl.ChoreDefinition)
                .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
                .Where(cl => cl.Date >= weekStart && cl.Date <= weekEnd)
                .ToListAsync();

            if (weekLogs.Count == 0)
            {
                if (weekOffset == 0) continue; // Current week might be incomplete
                break;
            }

            var allDone = weekLogs.All(cl => 
                cl.Status == ChoreStatus.Completed || 
                cl.Status == ChoreStatus.Approved || 
                cl.Status == ChoreStatus.Skipped);

            if (allDone)
                streak++;
            else
                break;
        }

        return streak;
    }

    // ═══════════════════════════════════════════════════════════════
    // IN-MEMORY CALCULATION HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static int CalculateCurrentStreakFromLogs(List<ChoreLog> allLogs, DateOnly today)
    {
        var choresByDate = allLogs
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

    private static int CalculatePerfectDaysFromLogs(List<ChoreLog> allLogs, DateOnly today)
    {
        return allLogs
            .Where(cl => cl.Date <= today)
            .GroupBy(cl => cl.Date)
            .Count(g => 
            {
                var total = g.Count();
                var completed = g.Count(cl => 
                    cl.Status == ChoreStatus.Completed || 
                    cl.Status == ChoreStatus.Approved || 
                    cl.Status == ChoreStatus.Skipped);
                return total > 0 && completed == total;
            });
    }

    // ═══════════════════════════════════════════════════════════════
    // JSON PARSING HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static int ParseInt(string? json, string key, int defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;
        
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.GetInt32();
            }
        }
        catch
        {
            // Invalid JSON, return default
        }
        return defaultValue;
    }

    private static decimal ParseDecimal(string? json, string key, decimal defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;
        
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.GetDecimal();
            }
        }
        catch
        {
            // Invalid JSON, return default
        }
        return defaultValue;
    }

    private static string ParseString(string? json, string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;
        
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.GetString() ?? defaultValue;
            }
        }
        catch
        {
            // Invalid JSON, return default
        }
        return defaultValue;
    }
}
