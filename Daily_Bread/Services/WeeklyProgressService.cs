using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Progress tracking for a single weekly chore.
/// </summary>
public class WeeklyChoreProgress
{
    public required ChoreDefinition ChoreDefinition { get; init; }
    
    /// <summary>
    /// Number of times completed this week.
    /// </summary>
    public int CompletedCount { get; init; }
    
    /// <summary>
    /// Target number of completions for the week.
    /// </summary>
    public int TargetCount { get; init; }
    
    /// <summary>
    /// Whether the weekly quota has been met.
    /// </summary>
    public bool QuotaMet => CompletedCount >= TargetCount;
    
    /// <summary>
    /// How many more completions are needed to meet quota.
    /// </summary>
    public int RemainingCount => Math.Max(0, TargetCount - CompletedCount);
    
    /// <summary>
    /// Percentage of quota completed (0-100, can exceed 100 for bonus).
    /// </summary>
    public int PercentComplete => TargetCount > 0 
        ? (int)Math.Round((double)CompletedCount / TargetCount * 100) 
        : 100;
    
    /// <summary>
    /// Total base earnings for completions up to quota.
    /// </summary>
    public decimal EarnedAmount { get; init; }
    
    /// <summary>
    /// Bonus earnings for completions beyond quota (if IsRepeatable).
    /// </summary>
    public decimal BonusAmount { get; init; }
    
    /// <summary>
    /// Total earned (base + bonus).
    /// </summary>
    public decimal TotalEarned => EarnedAmount + BonusAmount;
    
    /// <summary>
    /// Potential earnings if quota is completed (but not yet).
    /// </summary>
    public decimal PotentialEarnings { get; init; }
    
    /// <summary>
    /// Whether more completions beyond quota are allowed.
    /// </summary>
    public bool CanDoMore => ChoreDefinition.IsRepeatable || !QuotaMet;
    
    /// <summary>
    /// If repeatable, the value of the next bonus completion.
    /// Returns 0 if not repeatable or quota not met.
    /// </summary>
    public decimal NextBonusValue { get; init; }
    
    /// <summary>
    /// Logs for this chore this week.
    /// </summary>
    public List<ChoreLog> WeekLogs { get; init; } = [];
}

/// <summary>
/// Summary of all weekly chores progress for a user.
/// </summary>
public class WeeklyProgressSummary
{
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    
    /// <summary>
    /// All weekly flexible chores with their progress.
    /// </summary>
    public List<WeeklyChoreProgress> ChoreProgress { get; init; } = [];
    
    /// <summary>
    /// Total chores with weekly quotas.
    /// </summary>
    public int TotalWeeklyChores => ChoreProgress.Count;
    
    /// <summary>
    /// Chores that have met their quota.
    /// </summary>
    public int ChoresCompleted => ChoreProgress.Count(p => p.QuotaMet);
    
    /// <summary>
    /// Total earned so far from weekly chores.
    /// </summary>
    public decimal TotalEarned => ChoreProgress.Sum(p => p.TotalEarned);
    
    /// <summary>
    /// Total potential earnings if all quotas are met.
    /// </summary>
    public decimal TotalPotential => ChoreProgress.Sum(p => p.PotentialEarnings);
    
    /// <summary>
    /// Days remaining in the week (including today).
    /// </summary>
    public int DaysRemaining { get; init; }
    
    /// <summary>
    /// Whether it's the last day of the week.
    /// </summary>
    public bool IsLastDay => DaysRemaining == 1;
}

/// <summary>
/// Service for tracking weekly chore progress and quotas.
/// </summary>
public interface IWeeklyProgressService
{
    /// <summary>
    /// Gets weekly progress for all weekly chores assigned to a user.
    /// </summary>
    Task<WeeklyProgressSummary> GetWeeklyProgressForUserAsync(string userId, DateOnly? asOfDate = null);
    
    /// <summary>
    /// Gets progress for a specific weekly chore.
    /// </summary>
    Task<WeeklyChoreProgress?> GetChoreProgressAsync(int choreDefinitionId, DateOnly? asOfDate = null);
    
    /// <summary>
    /// Gets progress for multiple weekly chores in a single query.
    /// Returns a dictionary keyed by ChoreDefinitionId.
    /// </summary>
    Task<Dictionary<int, WeeklyChoreProgress>> GetChoreProgressBatchAsync(IEnumerable<int> choreDefinitionIds, DateOnly? asOfDate = null);
    
    /// <summary>
    /// Calculates the earning value for the next completion of a weekly chore.
    /// Handles diminishing returns for repeatable chores.
    /// </summary>
    Task<decimal> CalculateNextCompletionValueAsync(int choreDefinitionId, DateOnly? asOfDate = null);
    
    /// <summary>
    /// Checks if a weekly chore can be completed on the given date.
    /// Returns false if quota met and not repeatable.
    /// </summary>
    Task<bool> CanCompleteChoreAsync(int choreDefinitionId, DateOnly date);
    
    /// <summary>
    /// Gets the number of completions for a chore in the current week.
    /// </summary>
    Task<int> GetWeeklyCompletionCountAsync(int choreDefinitionId, DateOnly? asOfDate = null);
}

public class WeeklyProgressService : IWeeklyProgressService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IDateProvider _dateProvider;

    public WeeklyProgressService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IFamilySettingsService familySettingsService,
        IDateProvider dateProvider)
    {
        _contextFactory = contextFactory;
        _familySettingsService = familySettingsService;
        _dateProvider = dateProvider;
    }

    public async Task<WeeklyProgressSummary> GetWeeklyProgressForUserAsync(string userId, DateOnly? asOfDate = null)
    {
        var date = asOfDate ?? _dateProvider.Today;
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(date);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(date);
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Get all weekly flexible chores for this user
        var weeklyChores = await context.ChoreDefinitions
            .Where(c => c.IsActive)
            .Where(c => c.AssignedUserId == userId)
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .ToListAsync();
        
        // Get all logs for these chores within the week
        var choreIds = weeklyChores.Select(c => c.Id).ToList();
        var weekLogs = await context.ChoreLogs
            .Where(l => choreIds.Contains(l.ChoreDefinitionId))
            .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
            .Where(l => l.Status == ChoreStatus.Approved || l.Status == ChoreStatus.Completed)
            .ToListAsync();
        
        var progressList = new List<WeeklyChoreProgress>();
        
        foreach (var chore in weeklyChores)
        {
            var choreLogs = weekLogs.Where(l => l.ChoreDefinitionId == chore.Id).ToList();
            var completedCount = choreLogs.Count;
            var targetCount = chore.WeeklyTargetCount;
            
            // Calculate earnings
            var (earnedAmount, bonusAmount) = CalculateEarnings(chore, completedCount);
            var potentialEarnings = chore.EarnValue * targetCount;
            var nextBonusValue = CalculateNextBonusValue(chore, completedCount);
            
            progressList.Add(new WeeklyChoreProgress
            {
                ChoreDefinition = chore,
                CompletedCount = completedCount,
                TargetCount = targetCount,
                EarnedAmount = earnedAmount,
                BonusAmount = bonusAmount,
                PotentialEarnings = potentialEarnings,
                NextBonusValue = nextBonusValue,
                WeekLogs = choreLogs
            });
        }
        
        // Calculate days remaining
        var daysRemaining = (weekEnd.ToDateTime(TimeOnly.MinValue) - date.ToDateTime(TimeOnly.MinValue)).Days + 1;
        
        return new WeeklyProgressSummary
        {
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            ChoreProgress = progressList,
            DaysRemaining = Math.Max(1, daysRemaining)
        };
    }

    public async Task<WeeklyChoreProgress?> GetChoreProgressAsync(int choreDefinitionId, DateOnly? asOfDate = null)
    {
        // =============================================================================
        // OPTIMIZATION: Delegate to batch method for consistent query execution
        // This ensures a single code path and can benefit from any batch optimizations
        // =============================================================================
        var results = await GetChoreProgressBatchAsync([choreDefinitionId], asOfDate);
        return results.TryGetValue(choreDefinitionId, out var progress) ? progress : null;
    }

    /// <summary>
    /// Gets progress for multiple weekly chores in a single database query.
    /// Eliminates N+1 queries when loading tracker items.
    /// </summary>
    public async Task<Dictionary<int, WeeklyChoreProgress>> GetChoreProgressBatchAsync(
        IEnumerable<int> choreDefinitionIds, 
        DateOnly? asOfDate = null)
    {
        var result = new Dictionary<int, WeeklyChoreProgress>();
        var choreIdList = choreDefinitionIds.ToList();
        
        if (choreIdList.Count == 0)
        {
            return result;
        }
        
        var date = asOfDate ?? _dateProvider.Today;
        var weekStart = await _familySettingsService.GetWeekStartForDateAsync(date);
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(date);
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Single query: get all weekly chores from the requested list
        var weeklyChores = await context.ChoreDefinitions
            .Where(c => choreIdList.Contains(c.Id))
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .ToListAsync();
        
        if (weeklyChores.Count == 0)
        {
            return result;
        }
        
        var validChoreIds = weeklyChores.Select(c => c.Id).ToList();
        
        // Single query: get all logs for these chores within the week
        var weekLogs = await context.ChoreLogs
            .Where(l => validChoreIds.Contains(l.ChoreDefinitionId))
            .Where(l => l.Date >= weekStart && l.Date <= weekEnd)
            .Where(l => l.Status == ChoreStatus.Approved || l.Status == ChoreStatus.Completed)
            .ToListAsync();
        
        // Group logs by chore for efficient lookup
        var logsByChore = weekLogs.GroupBy(l => l.ChoreDefinitionId)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        // Build progress for each chore
        foreach (var chore in weeklyChores)
        {
            var choreLogs = logsByChore.TryGetValue(chore.Id, out var logs) ? logs : [];
            var completedCount = choreLogs.Count;
            var (earnedAmount, bonusAmount) = CalculateEarnings(chore, completedCount);
            
            result[chore.Id] = new WeeklyChoreProgress
            {
                ChoreDefinition = chore,
                CompletedCount = completedCount,
                TargetCount = chore.WeeklyTargetCount,
                EarnedAmount = earnedAmount,
                BonusAmount = bonusAmount,
                PotentialEarnings = chore.EarnValue * chore.WeeklyTargetCount,
                NextBonusValue = CalculateNextBonusValue(chore, completedCount),
                WeekLogs = choreLogs
            };
        }
        
        return result;
    }

    public async Task<decimal> CalculateNextCompletionValueAsync(int choreDefinitionId, DateOnly? asOfDate = null)
    {
        var progress = await GetChoreProgressAsync(choreDefinitionId, asOfDate);
        if (progress == null)
        {
            return 0;
        }
        
        var chore = progress.ChoreDefinition;
        var completedCount = progress.CompletedCount;
        
        // If under quota, return full value
        if (completedCount < chore.WeeklyTargetCount)
        {
            return chore.EarnValue;
        }
        
        // If at/over quota and not repeatable, return 0
        if (!chore.IsRepeatable)
        {
            return 0;
        }
        
        // Repeatable: diminishing returns
        // Bonus completions: 50% → 25% → 12.5% → ...
        var bonusCompletions = completedCount - chore.WeeklyTargetCount;
        var multiplier = Math.Pow(0.5, bonusCompletions + 1);
        return Math.Round(chore.EarnValue * (decimal)multiplier, 2);
    }

    public async Task<bool> CanCompleteChoreAsync(int choreDefinitionId, DateOnly date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chore = await context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (chore == null || !chore.IsActive)
        {
            return false;
        }
        
        // Daily fixed chores can always be completed on their scheduled days
        if (chore.ScheduleType == ChoreScheduleType.SpecificDays)
        {
            return true;
        }
        
        // Weekly flexible chores - use GetChoreProgressAsync which delegates to batch
        var progress = await GetChoreProgressAsync(choreDefinitionId, date);
        if (progress == null)
        {
            return false;
        }
        
        // Can complete if under quota OR if repeatable
        return progress.CompletedCount < chore.WeeklyTargetCount || chore.IsRepeatable;
    }

    public async Task<int> GetWeeklyCompletionCountAsync(int choreDefinitionId, DateOnly? asOfDate = null)
    {
        var progress = await GetChoreProgressAsync(choreDefinitionId, asOfDate);
        return progress?.CompletedCount ?? 0;
    }

    /// <summary>
    /// Calculates base and bonus earnings for a weekly chore.
    /// </summary>
    private static (decimal earned, decimal bonus) CalculateEarnings(ChoreDefinition chore, int completedCount)
    {
        if (completedCount == 0)
        {
            return (0, 0);
        }
        
        // Base earnings: up to quota * value
        var baseCompletions = Math.Min(completedCount, chore.WeeklyTargetCount);
        var earned = baseCompletions * chore.EarnValue;
        
        // Bonus earnings: beyond quota with diminishing returns
        decimal bonus = 0;
        if (chore.IsRepeatable && completedCount > chore.WeeklyTargetCount)
        {
            var bonusCompletions = completedCount - chore.WeeklyTargetCount;
            for (int i = 0; i < bonusCompletions; i++)
            {
                // 50% → 25% → 12.5% → ...
                var multiplier = Math.Pow(0.5, i + 1);
                bonus += Math.Round(chore.EarnValue * (decimal)multiplier, 2);
            }
        }
        
        return (earned, bonus);
    }

    /// <summary>
    /// Calculates the value of the next bonus completion.
    /// </summary>
    private static decimal CalculateNextBonusValue(ChoreDefinition chore, int completedCount)
    {
        if (!chore.IsRepeatable)
        {
            return 0;
        }
        
        if (completedCount < chore.WeeklyTargetCount)
        {
            return 0; // Not in bonus territory yet
        }
        
        var bonusCompletions = completedCount - chore.WeeklyTargetCount;
        var multiplier = Math.Pow(0.5, bonusCompletions + 1);
        return Math.Round(chore.EarnValue * (decimal)multiplier, 2);
    }
}
