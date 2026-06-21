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
/// There is exactly ONE switch statement (<see cref="EvaluateWithContext"/>) that maps
/// UnlockConditionType to its evaluator. Both <see cref="EvaluateAsync"/> (single achievement)
/// and <see cref="EvaluateAllAsync"/> (batch) build an <see cref="AchievementEvaluationContext"/>
/// and delegate to it - this is deliberate, so a condition type can never be "handled in one
/// path but not the other" again.
///
/// HOW TO ADD NEW CONDITION TYPES:
/// 1. Add enum value to UnlockConditionType in Achievement.cs
/// 2. Add a case to the switch in EvaluateWithContext
/// 3. Implement the evaluation logic using only data already on AchievementEvaluationContext
///    (add a new field there + load it in both CreateEvaluationContextAsync and EvaluateAllAsync
///    if you need something new)
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
            var ctx = await CreateEvaluationContextAsync(userId);
            return EvaluateWithContext(achievement, ctx);
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

        var earnedIds = await context.UserAchievements
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync();

        var allActiveAchievements = await context.Achievements
            .Where(a => a.IsActive)
            .ToListAsync();

        var unearnedAchievements = allActiveAchievements
            .Where(a => !earnedIds.Contains(a.Id))
            .ToList();

        if (unearnedAchievements.Count == 0)
        {
            return new Dictionary<int, AchievementEvaluationResult>();
        }

        // =============================================================================
        // OPTIMIZATION: Pre-load ALL data needed for achievement evaluation in bulk
        // Previously: Each EvaluateAsync made 1-2 queries = N+1 problem
        // Now: Load all data upfront, evaluate in memory
        // =============================================================================
        var today = _dateProvider.Today;

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

        // Pre-load user achievements (for category mastery, achievement chains, etc.)
        var allUserAchievements = await context.UserAchievements
            .Include(ua => ua.Achievement)
            .Where(ua => ua.UserId == userId)
            .ToListAsync();

        // Pre-load child profile
        var childProfile = await context.ChildProfiles
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync();

        // Pre-load active chore definition ids (for AllChoreTypesCompleted)
        var activeChoreDefinitionIds = await context.ChoreDefinitions
            .Where(cd => cd.AssignedUserId == userId && cd.IsActive)
            .Select(cd => cd.Id)
            .ToListAsync();

        var weekStartDay = (await _familySettingsService.GetSettingsAsync()).WeekStartDay;

        _logger.LogDebug("Achievement evaluation: Pre-loaded {Logs} logs, {Txns} transactions, {Goals} goals for user {UserId}",
            allChoreLogs.Count, allTransactions.Count, allGoals.Count, userId);

        var evalContext = new AchievementEvaluationContext
        {
            UserId = userId,
            Today = today,
            ChoreLogs = allChoreLogs,
            Transactions = allTransactions,
            SavingsGoals = allGoals,
            UserAchievements = allUserAchievements,
            ChildProfile = childProfile,
            ActiveChoreDefinitionIds = activeChoreDefinitionIds,
            AllActiveAchievements = allActiveAchievements,
            WeekStartDay = weekStartDay
        };

        var results = new Dictionary<int, AchievementEvaluationResult>();

        foreach (var achievement in unearnedAchievements)
        {
            results[achievement.Id] = EvaluateWithContext(achievement, evalContext);
        }

        return results;
    }

    /// <summary>
    /// Context object containing pre-loaded data for achievement evaluation.
    /// Built once per evaluation call (single or batch) and consumed by every evaluator.
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
        public List<int> ActiveChoreDefinitionIds { get; init; } = [];
        public List<Achievement> AllActiveAchievements { get; init; } = [];
        public DayOfWeek WeekStartDay { get; init; } = DayOfWeek.Monday;
    }

    /// <summary>
    /// The single dispatch point for all condition evaluation, used by both
    /// EvaluateAsync and EvaluateAllAsync. See class remarks.
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
                // Not yet implemented - no current achievement uses this condition type.
                UnlockConditionType.SpecificChoreCount => AchievementEvaluationResult.NotApplicable(),
                UnlockConditionType.EarlyCompletion => EvaluateEarlyCompletionWithContext(achievement, ctx),
                UnlockConditionType.FirstChore => EvaluateFirstChoreWithContext(achievement, ctx),
                UnlockConditionType.FirstGoal => EvaluateFirstGoalWithContext(achievement, ctx),
                UnlockConditionType.GoalCompleted => EvaluateGoalCompletedWithContext(achievement, ctx),
                UnlockConditionType.FirstDollar => EvaluateFirstDollarWithContext(achievement, ctx),
                UnlockConditionType.WeeklyEarnings => EvaluateWeeklyEarningsWithContext(achievement, ctx),
                UnlockConditionType.DayTypeCompletion => EvaluateDayTypeCompletionWithContext(achievement, ctx),
                UnlockConditionType.WeekStreak => EvaluateWeekStreakWithContext(achievement, ctx),
                UnlockConditionType.AccountAge => EvaluateAccountAgeWithContext(achievement, ctx),
                UnlockConditionType.BonusChoresCompleted => EvaluateBonusChoresCompletedWithContext(achievement, ctx),
                UnlockConditionType.ChoreRecovery => EvaluateChoreRecoveryWithContext(achievement, ctx),
                UnlockConditionType.HelpRequested => EvaluateHelpRequestedWithContext(achievement, ctx),
                UnlockConditionType.PenaltyFree => EvaluatePenaltyFreeWithContext(achievement, ctx),
                UnlockConditionType.AchievementUnlocked => EvaluateAchievementUnlockedWithContext(achievement, ctx),
                UnlockConditionType.CategoryMastery => EvaluateCategoryMasteryWithContext(achievement, ctx),
                UnlockConditionType.TotalAchievements => EvaluateTotalAchievementsWithContext(achievement, ctx),
                // Not yet implemented - no current achievement uses this condition type.
                UnlockConditionType.LoginStreak => AchievementEvaluationResult.NotApplicable(),
                UnlockConditionType.TimeOfDayCompletion => EvaluateTimeOfDayCompletionWithContext(achievement, ctx),
                UnlockConditionType.CashOut => EvaluateCashOutWithContext(achievement, ctx),
                UnlockConditionType.ChoresInSingleDay => EvaluateChoresInSingleDayWithContext(achievement, ctx),
                UnlockConditionType.AchievementsInPeriod => EvaluateAchievementsInPeriodWithContext(achievement, ctx),
                UnlockConditionType.AllChoreTypesCompleted => EvaluateAllChoreTypesCompletedWithContext(achievement, ctx),
                UnlockConditionType.RarityMastery => EvaluateRarityMasteryWithContext(achievement, ctx),
                UnlockConditionType.ThresholdCompletionStreak => EvaluateThresholdCompletionStreakWithContext(achievement, ctx),
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
    // EVALUATION IMPLEMENTATIONS (all operate on pre-loaded context, no DB access)
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

    private AchievementEvaluationResult EvaluateWeeklyEarningsWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var targetAmount = ParseDecimal(achievement.UnlockConditionValue, "amount", 1);
        var target = (int)targetAmount;

        var weekStart = GetWeekStart(ctx.Today, ctx.WeekStartDay);
        var weekEnd = weekStart.AddDays(6);

        var weekEarnings = ctx.Transactions
            .Where(t => t.Amount > 0 && t.TransactionDate >= weekStart && t.TransactionDate <= weekEnd)
            .Sum(t => t.Amount);

        var current = (int)weekEarnings;
        return weekEarnings >= targetAmount
            ? AchievementEvaluationResult.Met(current, target)
            : AchievementEvaluationResult.NotMet(current, target);
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

    private AchievementEvaluationResult EvaluateWeekStreakWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "weeks", 1);
        var streak = CalculateWeekStreakFromLogs(ctx.ChoreLogs, ctx.Today, ctx.WeekStartDay);

        return streak >= target
            ? AchievementEvaluationResult.Met(streak, target)
            : AchievementEvaluationResult.NotMet(streak, target);
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

    private AchievementEvaluationResult EvaluateChoreRecoveryWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var count = CountChoreRecoveries(ctx.ChoreLogs);

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

    private static AchievementEvaluationResult EvaluateAchievementUnlockedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var requiredCode = ParseString(achievement.UnlockConditionValue, "achievement_code", "");
        if (string.IsNullOrEmpty(requiredCode))
            return AchievementEvaluationResult.NotApplicable();

        var hasRequired = ctx.UserAchievements.Any(ua => ua.Achievement.Code == requiredCode);

        return hasRequired
            ? AchievementEvaluationResult.Met()
            : AchievementEvaluationResult.NotMet(0, 1);
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

    private AchievementEvaluationResult EvaluateChoresInSingleDayWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);

        var maxInDay = ctx.ChoreLogs
            .Where(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)
            .GroupBy(cl => cl.Date)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();

        return maxInDay >= target
            ? AchievementEvaluationResult.Met(maxInDay, target)
            : AchievementEvaluationResult.NotMet(maxInDay, target);
    }

    private AchievementEvaluationResult EvaluateAchievementsInPeriodWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        var days = ParseInt(achievement.UnlockConditionValue, "days", 7);
        var cutoff = ctx.Today.AddDays(-(days - 1));

        var count = ctx.UserAchievements.Count(ua => DateOnly.FromDateTime(ua.EarnedAt) >= cutoff);

        return count >= target
            ? AchievementEvaluationResult.Met(count, target)
            : AchievementEvaluationResult.NotMet(count, target);
    }

    private static AchievementEvaluationResult EvaluateAllChoreTypesCompletedWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var target = ctx.ActiveChoreDefinitionIds.Count;
        if (target == 0)
            return AchievementEvaluationResult.NotApplicable();

        var completedIds = ctx.ChoreLogs
            .Where(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)
            .Select(cl => cl.ChoreDefinitionId)
            .ToHashSet();

        var coveredCount = ctx.ActiveChoreDefinitionIds.Count(id => completedIds.Contains(id));

        return coveredCount >= target
            ? AchievementEvaluationResult.Met(coveredCount, target)
            : AchievementEvaluationResult.NotMet(coveredCount, target);
    }

    private static AchievementEvaluationResult EvaluateRarityMasteryWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var minRarityStr = ParseString(achievement.UnlockConditionValue, "min_rarity", "Epic");
        if (!Enum.TryParse<AchievementRarity>(minRarityStr, true, out var minRarity))
            return AchievementEvaluationResult.NotApplicable();

        var mode = ParseString(achievement.UnlockConditionValue, "mode", "count");
        var earnedAtRarity = ctx.UserAchievements.Count(ua =>
            ua.Achievement.Rarity >= minRarity && ua.AchievementId != achievement.Id);

        if (mode.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var totalAtRarity = ctx.AllActiveAchievements.Count(a => a.Rarity >= minRarity && a.Id != achievement.Id);
            if (totalAtRarity == 0)
                return AchievementEvaluationResult.NotApplicable();

            return earnedAtRarity >= totalAtRarity
                ? AchievementEvaluationResult.Met(earnedAtRarity, totalAtRarity)
                : AchievementEvaluationResult.NotMet(earnedAtRarity, totalAtRarity);
        }

        var target = ParseInt(achievement.UnlockConditionValue, "count", 1);
        return earnedAtRarity >= target
            ? AchievementEvaluationResult.Met(earnedAtRarity, target)
            : AchievementEvaluationResult.NotMet(earnedAtRarity, target);
    }

    private AchievementEvaluationResult EvaluateThresholdCompletionStreakWithContext(Achievement achievement, AchievementEvaluationContext ctx)
    {
        var targetDays = ParseInt(achievement.UnlockConditionValue, "days", 1);
        var percent = ParseInt(achievement.UnlockConditionValue, "percent", 100);

        var streak = CalculateThresholdStreakFromLogs(ctx.ChoreLogs, ctx.Today, percent);

        return streak >= targetDays
            ? AchievementEvaluationResult.Met(streak, targetDays)
            : AchievementEvaluationResult.NotMet(streak, targetDays);
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

        var activeChoreDefinitionIds = await context.ChoreDefinitions
            .Where(cd => cd.AssignedUserId == userId && cd.IsActive)
            .Select(cd => cd.Id)
            .ToListAsync();

        var allActiveAchievements = await context.Achievements
            .Where(a => a.IsActive)
            .ToListAsync();

        var weekStartDay = (await _familySettingsService.GetSettingsAsync()).WeekStartDay;

        return new AchievementEvaluationContext
        {
            UserId = userId,
            Today = today,
            ChoreLogs = allChoreLogs,
            Transactions = allTransactions,
            SavingsGoals = allGoals,
            UserAchievements = allUserAchievements,
            ChildProfile = childProfile,
            ActiveChoreDefinitionIds = activeChoreDefinitionIds,
            AllActiveAchievements = allActiveAchievements,
            WeekStartDay = weekStartDay
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // IN-MEMORY CALCULATION HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Pure equivalent of IFamilySettingsService.GetWeekStartForDateAsync, for use inside
    /// synchronous, pre-loaded-context evaluators.
    /// </summary>
    private static DateOnly GetWeekStart(DateOnly date, DayOfWeek weekStartDay)
    {
        var currentDayOfWeek = (int)date.DayOfWeek;
        var targetDayOfWeek = (int)weekStartDay;
        var daysToSubtract = (currentDayOfWeek - targetDayOfWeek + 7) % 7;
        return date.AddDays(-daysToSubtract);
    }

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

    private static int CalculateWeekStreakFromLogs(List<ChoreLog> allLogs, DateOnly today, DayOfWeek weekStartDay)
    {
        var streak = 0;

        // Check last 52 weeks
        for (int weekOffset = 0; weekOffset < 52; weekOffset++)
        {
            var checkDate = today.AddDays(-7 * weekOffset);
            var weekStart = GetWeekStart(checkDate, weekStartDay);
            var weekEnd = weekStart.AddDays(6);

            var weekLogs = allLogs.Where(l => l.Date >= weekStart && l.Date <= weekEnd).ToList();

            if (weekLogs.Count == 0)
            {
                if (weekOffset == 0) continue; // Current week might be incomplete
                break;
            }

            var allDone = weekLogs.All(l =>
                l.Status == ChoreStatus.Completed ||
                l.Status == ChoreStatus.Approved ||
                l.Status == ChoreStatus.Skipped);

            if (allDone)
                streak++;
            else
                break;
        }

        return streak;
    }

    /// <summary>
    /// A recovery = a date with at least one Missed chore, where the very next date is a
    /// "perfect day" (every log that day Completed/Approved/Skipped - same bar as PerfectDays).
    /// </summary>
    private static int CountChoreRecoveries(List<ChoreLog> allLogs)
    {
        var byDate = allLogs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.ToList());
        var missedDates = byDate.Where(kv => kv.Value.Any(l => l.Status == ChoreStatus.Missed)).Select(kv => kv.Key);

        var recoveries = 0;
        foreach (var missedDate in missedDates)
        {
            var nextDate = missedDate.AddDays(1);
            if (byDate.TryGetValue(nextDate, out var nextLogs) && nextLogs.Count > 0 &&
                nextLogs.All(l => l.Status == ChoreStatus.Completed || l.Status == ChoreStatus.Approved || l.Status == ChoreStatus.Skipped))
            {
                recoveries++;
            }
        }

        return recoveries;
    }

    /// <summary>
    /// Consecutive days (walking back from today) where the percentage of that day's chores
    /// that are Completed/Approved/Skipped is >= percent. Days with zero logged chores are
    /// skipped rather than breaking the streak, matching CalculateCurrentStreakFromLogs.
    /// Help status counts toward the day's total but never toward "met".
    /// </summary>
    private static int CalculateThresholdStreakFromLogs(List<ChoreLog> allLogs, DateOnly today, int thresholdPercent)
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

            var total = choresForDate.Count;
            var met = choresForDate.Count(c =>
                c.Status == ChoreStatus.Completed ||
                c.Status == ChoreStatus.Approved ||
                c.Status == ChoreStatus.Skipped);

            var percent = (met * 100.0) / total;

            if (percent >= thresholdPercent)
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
