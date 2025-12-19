using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Summary of a child's balance for dashboard display.
/// </summary>
public class ChildBalanceSummary
{
    public int ProfileId { get; init; }
    public required string DisplayName { get; init; }
    public decimal Balance { get; init; }
    public bool CanCashOut { get; init; }
}

/// <summary>
/// A pending chore awaiting approval.
/// </summary>
public class PendingApprovalItem
{
    public int ChoreLogId { get; init; }
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public required string ChildName { get; init; }
    public string? ChildUserId { get; init; }
    public decimal Value { get; init; }
    public DateOnly Date { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>
/// Recent activity item for the dashboard feed.
/// </summary>
public class RecentActivityItem
{
    public required string Description { get; init; }
    public required string Category { get; init; } // "chore", "approval", "payout", "bonus", "penalty"
    public DateTime Timestamp { get; init; }
    public string? ChildName { get; init; }
    public decimal? Amount { get; init; }
}

/// <summary>
/// Weekly comparison stats.
/// </summary>
public class WeeklyComparison
{
    public decimal ThisWeekEarnings { get; init; }
    public decimal LastWeekEarnings { get; init; }
    public int ThisWeekChoresCompleted { get; init; }
    public int LastWeekChoresCompleted { get; init; }
    
    public decimal EarningsChange => ThisWeekEarnings - LastWeekEarnings;
    public int EarningsChangePercent => LastWeekEarnings > 0 
        ? (int)Math.Round((EarningsChange / LastWeekEarnings) * 100) 
        : (ThisWeekEarnings > 0 ? 100 : 0);
    
    public int ChoresChange => ThisWeekChoresCompleted - LastWeekChoresCompleted;
    public bool IsImprovement => EarningsChange >= 0;
}

/// <summary>
/// Dashboard data for Parent role.
/// </summary>
public class ParentDashboardData
{
    public List<PendingApprovalItem> PendingApprovals { get; init; } = [];
    public List<ChildBalanceSummary> ChildrenBalances { get; init; } = [];
    public List<RecentActivityItem> RecentActivity { get; init; } = [];
    public int TodayCompletedCount { get; init; }
    public int TodayPendingCount { get; init; }
    public int TodayApprovedCount { get; init; }
}

/// <summary>
/// Dashboard data for Child role.
/// </summary>
public class ChildDashboardData
{
    public required string DisplayName { get; init; }
    public decimal Balance { get; init; }
    public decimal CashOutThreshold { get; init; }
    public bool CanCashOut { get; init; }
    public int TodayCompletedCount { get; init; }
    public int TodayPendingCount { get; init; }
    public int TodayTotalCount { get; init; }
    public decimal TodayEarnings { get; init; }
    public decimal TodayPotentialEarnings { get; init; }
    public int CurrentStreak { get; init; }
    public int BestStreak { get; init; }
    public List<TrackerChoreItem> TodayChores { get; init; } = [];
    
    // New features
    public SavingsGoalProgress? PrimaryGoal { get; init; }
    public List<AchievementDisplay> RecentAchievements { get; init; } = [];
    public List<AchievementDisplay> NewAchievements { get; init; } = [];
    public int TotalAchievementPoints { get; init; }
    public int AchievementsEarned { get; init; }
    public int TotalAchievements { get; init; }
    public WeeklyComparison WeeklyStats { get; init; } = new();
    public decimal TotalLifetimeEarnings { get; init; }
}

/// <summary>
/// Service interface for dashboard data aggregation.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets dashboard data for a parent user.
    /// </summary>
    Task<ParentDashboardData> GetParentDashboardAsync();

    /// <summary>
    /// Gets dashboard data for a child user.
    /// </summary>
    Task<ChildDashboardData> GetChildDashboardAsync(string userId);

    /// <summary>
    /// Gets the count of pending approvals (for nav badge).
    /// </summary>
    Task<int> GetPendingApprovalsCountAsync();

    /// <summary>
    /// Quick approve a chore from the dashboard.
    /// </summary>
    Task<ServiceResult> QuickApproveAsync(int choreLogId, string parentUserId);
}

/// <summary>
/// Service for aggregating dashboard data.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IChildProfileService _profileService;
    private readonly ITrackerService _trackerService;
    private readonly IPayoutService _payoutService;
    private readonly ILedgerService _ledgerService;
    private readonly ISavingsGoalService _savingsGoalService;
    private readonly IAchievementService _achievementService;

    // Default cash out threshold - should match AppSettings
    private const decimal DefaultCashOutThreshold = 10.00m;

    public DashboardService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider,
        IChildProfileService profileService,
        ITrackerService trackerService,
        IPayoutService payoutService,
        ILedgerService ledgerService,
        ISavingsGoalService savingsGoalService,
        IAchievementService achievementService)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _profileService = profileService;
        _trackerService = trackerService;
        _payoutService = payoutService;
        _ledgerService = ledgerService;
        _savingsGoalService = savingsGoalService;
        _achievementService = achievementService;
    }

    public async Task<ParentDashboardData> GetParentDashboardAsync()
    {
        var today = _dateProvider.Today;

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Get pending approvals (completed but not approved chores)
        var pendingApprovals = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Where(cl => cl.Status == ChoreStatus.Completed)
            .OrderByDescending(cl => cl.CompletedAt)
            .Take(20)
            .Select(cl => new PendingApprovalItem
            {
                ChoreLogId = cl.Id,
                ChoreDefinitionId = cl.ChoreDefinitionId,
                ChoreName = cl.ChoreDefinition.Name,
                ChildName = cl.ChoreDefinition.AssignedUser != null 
                    ? cl.ChoreDefinition.AssignedUser.UserName ?? "Unknown"
                    : "Unassigned",
                ChildUserId = cl.ChoreDefinition.AssignedUserId,
                Value = cl.ChoreDefinition.Value,
                Date = cl.Date,
                CompletedAt = cl.CompletedAt
            })
            .ToListAsync();

        // Get children balances
        var childProfiles = await _profileService.GetAllChildProfilesAsync();
        var childrenBalances = childProfiles.Select(p => new ChildBalanceSummary
        {
            ProfileId = p.ProfileId,
            DisplayName = p.DisplayName,
            Balance = p.TotalBalance,
            CanCashOut = p.TotalBalance >= DefaultCashOutThreshold
        }).ToList();

        // Get recent activity (last 10 items)
        var recentActivity = await GetRecentActivityAsync(10);

        // Get today's stats
        var todayLogs = await context.ChoreLogs
            .Where(cl => cl.Date == today)
            .ToListAsync();

        return new ParentDashboardData
        {
            PendingApprovals = pendingApprovals,
            ChildrenBalances = childrenBalances,
            RecentActivity = recentActivity,
            TodayCompletedCount = todayLogs.Count(l => l.Status == ChoreStatus.Completed),
            TodayPendingCount = todayLogs.Count(l => l.Status == ChoreStatus.Pending),
            TodayApprovedCount = todayLogs.Count(l => l.Status == ChoreStatus.Approved)
        };
    }

    public async Task<ChildDashboardData> GetChildDashboardAsync(string userId)
    {
        var today = _dateProvider.Today;

        // Get child profile
        var profile = await _profileService.GetProfileByUserIdAsync(userId);
        var displayName = profile?.DisplayName ?? "there";

        // Get balance
        var balance = await _ledgerService.GetUserBalanceAsync(userId);
        var balanceSummary = await _payoutService.GetBalanceSummaryAsync(userId);

        // Get today's chores
        var todayChores = await _trackerService.GetTrackerItemsForUserOnDateAsync(userId, today);

        // Calculate streaks
        var (currentStreak, bestStreak) = await CalculateStreaksAsync(userId);

        // Calculate today's earnings
        var todayEarnings = todayChores
            .Where(c => c.Status == ChoreStatus.Approved)
            .Sum(c => c.Value);
        var todayPotential = todayChores.Sum(c => c.Value);

        // Get primary savings goal
        var primaryGoal = await _savingsGoalService.GetPrimaryGoalAsync(userId);

        // Check and award any new achievements
        var newAchievements = await _achievementService.CheckAndAwardAchievementsAsync(userId);

        // Get recently earned achievements (last 5)
        var earnedAchievements = await _achievementService.GetEarnedAchievementsAsync(userId);
        var recentAchievements = earnedAchievements
            .OrderByDescending(a => a.EarnedAt)
            .Take(5)
            .ToList();

        // Get total achievements stats
        var allAchievements = await _achievementService.GetAllAchievementsAsync(userId);
        var totalPoints = await _achievementService.GetTotalPointsAsync(userId);

        // Get weekly comparison
        var weeklyStats = await GetWeeklyComparisonAsync(userId);

        // Get lifetime earnings
        var lifetimeEarnings = await GetLifetimeEarningsAsync(userId);

        return new ChildDashboardData
        {
            DisplayName = displayName,
            Balance = balance,
            CashOutThreshold = balanceSummary?.CashOutThreshold ?? DefaultCashOutThreshold,
            CanCashOut = balanceSummary?.CanCashOut ?? false,
            TodayCompletedCount = todayChores.Count(c => c.IsCompleted),
            TodayPendingCount = todayChores.Count(c => c.IsPending),
            TodayTotalCount = todayChores.Count,
            TodayEarnings = todayEarnings,
            TodayPotentialEarnings = todayPotential,
            CurrentStreak = currentStreak,
            BestStreak = bestStreak,
            TodayChores = todayChores,
            PrimaryGoal = primaryGoal,
            RecentAchievements = recentAchievements,
            NewAchievements = newAchievements,
            TotalAchievementPoints = totalPoints,
            AchievementsEarned = earnedAchievements.Count,
            TotalAchievements = allAchievements.Count,
            WeeklyStats = weeklyStats,
            TotalLifetimeEarnings = lifetimeEarnings
        };
    }

    public async Task<int> GetPendingApprovalsCountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ChoreLogs
            .CountAsync(cl => cl.Status == ChoreStatus.Completed);
    }

    public async Task<ServiceResult> QuickApproveAsync(int choreLogId, string parentUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var choreLog = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .FirstOrDefaultAsync(cl => cl.Id == choreLogId);

        if (choreLog == null)
            return ServiceResult.Fail("Chore not found.");

        if (choreLog.Status != ChoreStatus.Completed)
            return ServiceResult.Fail("Only completed chores can be approved.");

        choreLog.Status = ChoreStatus.Approved;
        choreLog.ApprovedByUserId = parentUserId;
        choreLog.ApprovedAt = DateTime.UtcNow;
        choreLog.ModifiedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        await _ledgerService.ReconcileChoreLogTransactionAsync(choreLogId);

        return ServiceResult.Ok();
    }

    private async Task<List<RecentActivityItem>> GetRecentActivityAsync(int count)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var activities = new List<RecentActivityItem>();

        // Get recent chore completions/approvals
        var recentLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(cl => cl.ApprovedByUser)
            .Where(cl => cl.Status == ChoreStatus.Approved || cl.Status == ChoreStatus.Completed)
            .OrderByDescending(cl => cl.ModifiedAt ?? cl.CreatedAt)
            .Take(count)
            .ToListAsync();

        foreach (var log in recentLogs)
        {
            var childName = log.ChoreDefinition.AssignedUser?.UserName ?? "Unknown";
            
            if (log.Status == ChoreStatus.Approved && log.ApprovedAt.HasValue)
            {
                activities.Add(new RecentActivityItem
                {
                    Description = $"'{log.ChoreDefinition.Name}' approved",
                    Category = "approval",
                    Timestamp = log.ApprovedAt.Value,
                    ChildName = childName,
                    Amount = log.ChoreDefinition.Value
                });
            }
            else if (log.Status == ChoreStatus.Completed && log.CompletedAt.HasValue)
            {
                activities.Add(new RecentActivityItem
                {
                    Description = $"'{log.ChoreDefinition.Name}' completed",
                    Category = "chore",
                    Timestamp = log.CompletedAt.Value,
                    ChildName = childName,
                    Amount = null
                });
            }
        }

        // Get recent transactions (payouts, bonuses, penalties)
        var recentTransactions = await context.LedgerTransactions
            .Include(t => t.User)
            .Where(t => t.Type == TransactionType.Payout || 
                        t.Type == TransactionType.Bonus || 
                        t.Type == TransactionType.Penalty)
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .ToListAsync();

        foreach (var txn in recentTransactions)
        {
            var category = txn.Type switch
            {
                TransactionType.Payout => "payout",
                TransactionType.Bonus => "bonus",
                TransactionType.Penalty => "penalty",
                _ => "other"
            };

            activities.Add(new RecentActivityItem
            {
                Description = txn.Description,
                Category = category,
                Timestamp = txn.CreatedAt,
                ChildName = txn.User?.UserName,
                Amount = txn.Amount
            });
        }

        // Sort by timestamp and take top items
        return activities
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    private async Task<(int current, int best)> CalculateStreaksAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var today = _dateProvider.Today;
        var currentStreak = 0;
        var bestStreak = 0;
        var runningStreak = 0;
        var currentDate = today;
        var countingCurrent = true;

        // Look back up to 365 days
        for (int i = 0; i < 365; i++)
        {
            // Get chores for this date
            var choresForDate = await context.ChoreLogs
                .Include(cl => cl.ChoreDefinition)
                .Where(cl => cl.Date == currentDate && cl.ChoreDefinition.AssignedUserId == userId)
                .ToListAsync();

            // If no chores scheduled for this date, continue checking previous days
            if (choresForDate.Count == 0)
            {
                // For today, no chores means we continue
                // For past days, no scheduled chores doesn't break streak
                if (currentDate == today)
                {
                    currentDate = currentDate.AddDays(-1);
                    continue;
                }
                currentDate = currentDate.AddDays(-1);
                continue;
            }

            // Check if all chores were completed (Completed or Approved)
            var allCompleted = choresForDate.All(c => 
                c.Status == ChoreStatus.Completed || 
                c.Status == ChoreStatus.Approved ||
                c.Status == ChoreStatus.Skipped);

            if (allCompleted)
            {
                runningStreak++;
                if (countingCurrent)
                    currentStreak = runningStreak;
                bestStreak = Math.Max(bestStreak, runningStreak);
                currentDate = currentDate.AddDays(-1);
            }
            else
            {
                countingCurrent = false;
                bestStreak = Math.Max(bestStreak, runningStreak);
                runningStreak = 0;
                currentDate = currentDate.AddDays(-1);
            }
        }

        return (currentStreak, bestStreak);
    }

    private async Task<WeeklyComparison> GetWeeklyComparisonAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var today = _dateProvider.Today;
        var startOfThisWeek = today.AddDays(-(int)today.DayOfWeek);
        var startOfLastWeek = startOfThisWeek.AddDays(-7);

        // This week's stats
        var thisWeekEarnings = await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0 && t.Type == TransactionType.ChoreEarning)
            .Where(t => t.TransactionDate >= startOfThisWeek && t.TransactionDate <= today)
            .SumAsync(t => t.Amount);

        var thisWeekChores = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= startOfThisWeek && cl.Date <= today)
            .Where(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)
            .CountAsync();

        // Last week's stats
        var endOfLastWeek = startOfThisWeek.AddDays(-1);
        var lastWeekEarnings = await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0 && t.Type == TransactionType.ChoreEarning)
            .Where(t => t.TransactionDate >= startOfLastWeek && t.TransactionDate <= endOfLastWeek)
            .SumAsync(t => t.Amount);

        var lastWeekChores = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= startOfLastWeek && cl.Date <= endOfLastWeek)
            .Where(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved)
            .CountAsync();

        return new WeeklyComparison
        {
            ThisWeekEarnings = thisWeekEarnings,
            LastWeekEarnings = lastWeekEarnings,
            ThisWeekChoresCompleted = thisWeekChores,
            LastWeekChoresCompleted = lastWeekChores
        };
    }

    private async Task<decimal> GetLifetimeEarningsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0)
            .SumAsync(t => t.Amount);
    }
}
