using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
/// A help request from a child requiring parent attention.
/// </summary>
public class HelpRequestItem
{
    public int ChoreLogId { get; init; }
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public required string ChildName { get; init; }
    public string? ChildUserId { get; init; }
    public string? Reason { get; init; }
    public DateOnly Date { get; init; }
    public DateTime? RequestedAt { get; init; }
}

/// <summary>
/// Summary of a child's today progress for parent overview.
/// </summary>
public class ChildTodayProgress
{
    public int ProfileId { get; init; }
    public required string DisplayName { get; init; }
    public string? UserId { get; init; }
    public int TotalChores { get; init; }
    public int CompletedChores { get; init; }
    public int ApprovedChores { get; init; }
    public int PendingChores { get; init; }
    public int HelpRequests { get; init; }
    
    public int ProgressPercent => TotalChores > 0 
        ? (int)Math.Round((CompletedChores + ApprovedChores) * 100.0 / TotalChores) 
        : 0;
    
    public bool IsComplete => TotalChores > 0 && (CompletedChores + ApprovedChores) >= TotalChores;
    public bool HasHelpRequests => HelpRequests > 0;
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
    public decimal EarnValue { get; init; }
    public decimal Value => EarnValue; // Backward compatibility
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
    public List<HelpRequestItem> HelpRequests { get; init; } = [];
    public List<ChildTodayProgress> ChildrenProgress { get; init; } = [];
    public int TodayCompletedCount { get; init; }
    public int TodayPendingCount { get; init; }
    public int TodayApprovedCount { get; init; }
    public int TodayHelpCount { get; init; }
    public int TodayTotalChores { get; init; }
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
    
    /// <summary>
    /// Chores that are pending (not yet completed) - for swipeable action view.
    /// </summary>
    public List<TrackerChoreItem> PendingChores => TodayChores
        .Where(c => c.IsPending || c.IsHelp)
        .ToList();
    
    /// <summary>
    /// Chores that are done (completed or approved) - collapsed section.
    /// </summary>
    public List<TrackerChoreItem> CompletedChores => TodayChores
        .Where(c => c.IsCompleted || c.IsApproved)
        .ToList();
    
    /// <summary>
    /// Weekly flexible chores with progress data.
    /// </summary>
    public WeeklyProgressSummary? WeeklyProgress { get; init; }
    
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
    
    /// <summary>
    /// Toggle a chore's completion status from the dashboard.
    /// Returns the new status and updated chore item.
    /// </summary>
    Task<ServiceResult<TrackerChoreItem>> ToggleChoreFromDashboardAsync(
        int choreDefinitionId, 
        DateOnly date, 
        string userId);
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
    private readonly IWeeklyProgressService _weeklyProgressService;
    private readonly IChoreNotificationService _choreNotificationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DashboardService> _logger;

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
        IAchievementService achievementService,
        IWeeklyProgressService weeklyProgressService,
        IChoreNotificationService choreNotificationService,
        UserManager<ApplicationUser> userManager,
        ILogger<DashboardService> logger)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _profileService = profileService;
        _trackerService = trackerService;
        _payoutService = payoutService;
        _ledgerService = ledgerService;
        _savingsGoalService = savingsGoalService;
        _achievementService = achievementService;
        _weeklyProgressService = weeklyProgressService;
        _choreNotificationService = choreNotificationService;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ParentDashboardData> GetParentDashboardAsync()
    {
        var today = _dateProvider.Today;

        await using var context = await _contextFactory.CreateDbContextAsync();

        // =============================================================================
        // OPTIMIZATION: Load ALL relevant ChoreLogs in a SINGLE query
        // Previously: Multiple separate queries for pending, help, today, activity
        // Now: One query, filter in memory
        // =============================================================================
        
        // Load all ChoreLogs that we might need:
        // - All with Status == Completed (pending approvals - any date)
        // - All with Status == Help (help requests - any date)
        // - All for today (for stats)
        // - Recent completed/approved (for activity feed)
        var allRelevantLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(cl => cl.ApprovedByUser)
            .Where(cl => 
                cl.Status == ChoreStatus.Completed ||  // Pending approvals
                cl.Status == ChoreStatus.Help ||       // Help requests
                cl.Date == today ||                    // Today's stats
                (cl.Status == ChoreStatus.Approved && cl.ApprovedAt != null)) // Recent activity
            .ToListAsync();
        
        _logger.LogDebug("Parent dashboard: Loaded {Count} relevant ChoreLogs in single query", allRelevantLogs.Count);

        // =============================================================================
        // Now filter in memory - no additional DB queries for ChoreLogs
        // =============================================================================

        // Pending approvals (completed but not approved chores)
        var pendingApprovals = allRelevantLogs
            .Where(cl => cl.Status == ChoreStatus.Completed)
            .OrderByDescending(cl => cl.CompletedAt)
            .Take(20)
            .Select(cl => new PendingApprovalItem
            {
                ChoreLogId = cl.Id,
                ChoreDefinitionId = cl.ChoreDefinitionId,
                ChoreName = cl.ChoreDefinition.Name,
                ChildName = cl.ChoreDefinition.AssignedUser?.UserName ?? "Unassigned",
                ChildUserId = cl.ChoreDefinition.AssignedUserId,
                EarnValue = cl.ChoreDefinition.EarnValue,
                Date = cl.Date,
                CompletedAt = cl.CompletedAt
            })
            .ToList();

        // Help requests (chores with Help status)
        var helpRequests = allRelevantLogs
            .Where(cl => cl.Status == ChoreStatus.Help)
            .OrderByDescending(cl => cl.HelpRequestedAt)
            .Select(cl => new HelpRequestItem
            {
                ChoreLogId = cl.Id,
                ChoreDefinitionId = cl.ChoreDefinitionId,
                ChoreName = cl.ChoreDefinition.Name,
                ChildName = cl.ChoreDefinition.AssignedUser?.UserName ?? "Unassigned",
                ChildUserId = cl.ChoreDefinition.AssignedUserId,
                Reason = cl.HelpReason,
                Date = cl.Date,
                RequestedAt = cl.HelpRequestedAt
            })
            .ToList();

        // Today's logs (for stats and per-child progress)
        var todayLogs = allRelevantLogs
            .Where(cl => cl.Date == today)
            .ToList();

        // Get children balances (still needs separate call - different table)
        var childProfiles = await _profileService.GetAllChildProfilesAsync();
        var childrenBalances = childProfiles.Select(p => new ChildBalanceSummary
        {
            ProfileId = p.ProfileId,
            DisplayName = p.DisplayName,
            Balance = p.TotalBalance,
            CanCashOut = p.TotalBalance >= DefaultCashOutThreshold
        }).ToList();

        // Get recent activity - pass pre-loaded logs to avoid re-querying
        var recentActivity = await GetRecentActivityAsync(10, allRelevantLogs);

        // Build per-child progress for today (in memory from pre-loaded data)
        var childrenProgress = new List<ChildTodayProgress>();
        foreach (var profile in childProfiles)
        {
            var childTodayLogs = todayLogs.Where(l => l.ChoreDefinition.AssignedUserId == profile.UserId).ToList();
            var childHelpCount = helpRequests.Count(hr => hr.ChildUserId == profile.UserId);
            
            childrenProgress.Add(new ChildTodayProgress
            {
                ProfileId = profile.ProfileId,
                DisplayName = profile.DisplayName,
                UserId = profile.UserId,
                TotalChores = childTodayLogs.Count,
                CompletedChores = childTodayLogs.Count(l => l.Status == ChoreStatus.Completed),
                ApprovedChores = childTodayLogs.Count(l => l.Status == ChoreStatus.Approved),
                PendingChores = childTodayLogs.Count(l => l.Status == ChoreStatus.Pending),
                HelpRequests = childHelpCount
            });
        }

        return new ParentDashboardData
        {
            PendingApprovals = pendingApprovals,
            ChildrenBalances = childrenBalances,
            RecentActivity = recentActivity,
            HelpRequests = helpRequests,
            ChildrenProgress = childrenProgress,
            TodayCompletedCount = todayLogs.Count(l => l.Status == ChoreStatus.Completed),
            TodayPendingCount = todayLogs.Count(l => l.Status == ChoreStatus.Pending),
            TodayApprovedCount = todayLogs.Count(l => l.Status == ChoreStatus.Approved),
            TodayHelpCount = todayLogs.Count(l => l.Status == ChoreStatus.Help),
            TodayTotalChores = todayLogs.Count
        };
    }

    public async Task<ChildDashboardData> GetChildDashboardAsync(string userId)
    {
        var today = _dateProvider.Today;
        
        // =============================================================================
        // OPTIMIZATION: Load ALL ChoreLogs for this child in a SINGLE query
        // This covers: today's chores, weekly progress, weekly comparison, and streaks
        // Previously: Multiple separate queries (TrackerService, WeeklyProgress, Streaks, Comparison)
        // Now: One query, filter in memory for different purposes
        // =============================================================================
        
        // Calculate date ranges we need
        var startOfThisWeek = today.AddDays(-(int)today.DayOfWeek);
        var startOfLastWeek = startOfThisWeek.AddDays(-7);
        var streakStartDate = today.AddDays(-365);
        
        // Load ALL ChoreLogs for this user from 365 days ago to today
        // This single query replaces: TrackerService, StreaksAsync, WeeklyComparison
        await using var context = await _contextFactory.CreateDbContextAsync();
        var allUserLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(cl => cl.ApprovedByUser)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= streakStartDate && cl.Date <= today)
            .ToListAsync();
        
        _logger.LogDebug("Child dashboard: Loaded {Count} ChoreLogs for user {UserId} in single query (365 days)", 
            allUserLogs.Count, userId);

        // Get child profile
        var profile = await _profileService.GetProfileByUserIdAsync(userId);
        var displayName = profile?.DisplayName ?? "there";

        // Get balance (still needs separate query - different table)
        var balance = await _ledgerService.GetUserBalanceAsync(userId);
        var balanceSummary = await _payoutService.GetBalanceSummaryAsync(userId);

        // =============================================================================
        // Filter in memory for different data needs
        // =============================================================================
        
        // Today's logs
        var todayLogs = allUserLogs.Where(cl => cl.Date == today).ToList();
        
        // Get today's chores using TrackerService (still needed for ChoreDefinitions + schedule logic)
        // But the ChoreLogs part is already loaded
        var todayChores = await _trackerService.GetTrackerItemsForUserOnDateAsync(userId, today);

        // Get weekly progress for weekly flexible chores
        var weeklyProgress = await _weeklyProgressService.GetWeeklyProgressForUserAsync(userId, today);

        // Calculate streaks from pre-loaded data (in memory)
        var (currentStreak, bestStreak) = CalculateStreaksFromLogs(allUserLogs, today);

        // Calculate today's earnings
        var todayEarnings = todayChores
            .Where(c => c.Status == ChoreStatus.Approved)
            .Sum(c => c.Value);
        var todayPotential = todayChores.Sum(c => c.Value);

        // Get primary savings goal (still needs separate query - different table)
        var primaryGoal = await _savingsGoalService.GetPrimaryGoalAsync(userId);

        // Check and award any new achievements (still needs service call)
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

        // Calculate weekly comparison from pre-loaded data (in memory)
        var weeklyStats = await GetWeeklyComparisonFromLogsAsync(userId, allUserLogs, today, startOfThisWeek, startOfLastWeek);

        // Get lifetime earnings (still needs separate query - LedgerTransactions table)
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
            WeeklyProgress = weeklyProgress,
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
        _logger.LogInformation("QuickApproveAsync called: ChoreLogId={ChoreLogId}, ParentUserId={ParentUserId}", 
            choreLogId, parentUserId);

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var choreLog = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .FirstOrDefaultAsync(cl => cl.Id == choreLogId);

        if (choreLog == null)
        {
            _logger.LogWarning("QuickApproveAsync: ChoreLog {ChoreLogId} not found", choreLogId);
            return ServiceResult.Fail("Chore not found.");
        }

        if (choreLog.Status != ChoreStatus.Completed)
        {
            _logger.LogWarning("QuickApproveAsync: ChoreLog {ChoreLogId} status is {Status}, not Completed", 
                choreLogId, choreLog.Status);
            return ServiceResult.Fail("Only completed chores can be approved.");
        }

        var childUserId = choreLog.ChoreDefinition.AssignedUserId;
        var choreName = choreLog.ChoreDefinition.Name;
        var earnedAmount = choreLog.ChoreDefinition.EarnValue;

        _logger.LogInformation(
            "QuickApprove: ChoreLogId={ChoreLogId}, ChoreName={ChoreName}, ChildUserId={ChildUserId}, EarnedAmount={EarnedAmount}",
            choreLogId, choreName, childUserId ?? "null", earnedAmount);

        choreLog.Status = ChoreStatus.Approved;
        choreLog.ApprovedByUserId = parentUserId;
        choreLog.ApprovedAt = DateTime.UtcNow;
        choreLog.ModifiedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        await _ledgerService.ReconcileChoreLogTransactionAsync(choreLogId);

        // Broadcast dashboard change to parent and child
        var affectedUserIds = new List<string> { parentUserId };
        if (!string.IsNullOrEmpty(childUserId))
        {
            affectedUserIds.Add(childUserId);
        }
        
        // Await notifications to ensure they're sent before returning
        await _choreNotificationService.NotifyDashboardChangedAsync([.. affectedUserIds]);

        // Send blessing notification to child
        if (!string.IsNullOrEmpty(childUserId) && earnedAmount > 0)
        {
            var parentUser = await _userManager.FindByIdAsync(parentUserId);
            var parentName = parentUser?.UserName;
            
            _logger.LogInformation(
                "QuickApprove: Sending BlessingGranted notification to ChildUserId={ChildUserId}, ChoreName={ChoreName}, Amount={Amount}",
                childUserId, choreName, earnedAmount);
            
            await _choreNotificationService.NotifyBlessingGrantedAsync(
                childUserId,
                choreName,
                earnedAmount,
                parentName);
        }
        else
        {
            _logger.LogWarning(
                "QuickApprove: Skipping BlessingGranted notification - ChildUserId={ChildUserId}, EarnedAmount={EarnedAmount}",
                childUserId ?? "null", earnedAmount);
        }

        _logger.LogInformation("QuickApproveAsync completed successfully: ChoreLogId={ChoreLogId}", choreLogId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<TrackerChoreItem>> ToggleChoreFromDashboardAsync(
        int choreDefinitionId, 
        DateOnly date, 
        string userId)
    {
        // Toggle the chore using the tracker service
        var result = await _trackerService.ToggleChoreCompletionAsync(
            choreDefinitionId,
            date,
            userId,
            isParent: false);

        if (!result.Success)
        {
            return ServiceResult<TrackerChoreItem>.Fail(result.ErrorMessage!);
        }

        // Get the updated chore item to return
        var todayChores = await _trackerService.GetTrackerItemsForUserOnDateAsync(userId, date);
        var updatedChore = todayChores.FirstOrDefault(c => c.ChoreDefinitionId == choreDefinitionId);

        if (updatedChore == null)
        {
            return ServiceResult<TrackerChoreItem>.Fail("Could not find updated chore.");
        }

        return ServiceResult<TrackerChoreItem>.Ok(updatedChore);
    }

    /// <summary>
    /// Gets recent activity items for the dashboard feed.
    /// Overload that accepts pre-loaded ChoreLogs to avoid re-querying.
    /// </summary>
    private async Task<List<RecentActivityItem>> GetRecentActivityAsync(int count, List<ChoreLog> preLoadedLogs)
    {
        var activities = new List<RecentActivityItem>();

        // Use pre-loaded logs for chore activity (already includes navigation properties)
        var recentLogs = preLoadedLogs
            .Where(cl => cl.Status == ChoreStatus.Approved || cl.Status == ChoreStatus.Completed)
            .OrderByDescending(cl => cl.ModifiedAt ?? cl.CreatedAt)
            .Take(count)
            .ToList();

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
                    Amount = log.ChoreDefinition.EarnValue
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

        // Get recent transactions (payouts, bonuses, penalties) - still need DB query
        await using var context = await _contextFactory.CreateDbContextAsync();
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
                Description = txn.Description ?? $"{category} transaction",
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

    /// <summary>
    /// Gets recent activity items for the dashboard feed.
    /// Original method - queries database for ChoreLogs.
    /// </summary>
    private async Task<List<RecentActivityItem>> GetRecentActivityAsync(int count)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Load chore logs for activity feed
        var recentLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(cl => cl.ApprovedByUser)
            .Where(cl => cl.Status == ChoreStatus.Approved || cl.Status == ChoreStatus.Completed)
            .OrderByDescending(cl => cl.ModifiedAt ?? cl.CreatedAt)
            .Take(count)
            .ToListAsync();

        // Delegate to the overload with pre-loaded logs
        return await GetRecentActivityAsync(count, recentLogs);
    }

    /// <summary>
    /// Calculates streaks from pre-loaded ChoreLogs (in memory).
    /// </summary>
    private (int current, int best) CalculateStreaksFromLogs(List<ChoreLog> allLogs, DateOnly today)
    {
        // Group by date for efficient lookup
        var choresByDate = allLogs
            .GroupBy(cl => cl.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        var currentStreak = 0;
        var bestStreak = 0;
        var runningStreak = 0;
        var currentDate = today;
        var countingCurrent = true;

        // Look back up to 365 days
        for (int i = 0; i < 365; i++)
        {
            // Get chores for this date from the pre-loaded dictionary
            if (!choresByDate.TryGetValue(currentDate, out var choresForDate) || choresForDate.Count == 0)
            {
                // No chores scheduled for this date, continue checking
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

    /// <summary>
    /// Calculates weekly comparison from pre-loaded ChoreLogs (chore counts in memory, earnings still from DB).
    /// </summary>
    private async Task<WeeklyComparison> GetWeeklyComparisonFromLogsAsync(
        string userId,
        List<ChoreLog> allLogs,
        DateOnly today,
        DateOnly startOfThisWeek,
        DateOnly startOfLastWeek)
    {
        var endOfLastWeek = startOfThisWeek.AddDays(-1);
        
        // Calculate chore counts from pre-loaded logs (in memory)
        var thisWeekChores = allLogs
            .Where(cl => cl.Date >= startOfThisWeek && cl.Date <= today)
            .Count(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved);

        var lastWeekChores = allLogs
            .Where(cl => cl.Date >= startOfLastWeek && cl.Date <= endOfLastWeek)
            .Count(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved);

        // Earnings still need DB query (LedgerTransactions table - not loaded)
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var thisWeekEarnings = await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0 && t.Type == TransactionType.ChoreEarning)
            .Where(t => t.TransactionDate >= startOfThisWeek && t.TransactionDate <= today)
            .SumAsync(t => t.Amount);

        var lastWeekEarnings = await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0 && t.Type == TransactionType.ChoreEarning)
            .Where(t => t.TransactionDate >= startOfLastWeek && t.TransactionDate <= endOfLastWeek)
            .SumAsync(t => t.Amount);

        return new WeeklyComparison
        {
            ThisWeekEarnings = thisWeekEarnings,
            LastWeekEarnings = lastWeekEarnings,
            ThisWeekChoresCompleted = thisWeekChores,
            LastWeekChoresCompleted = lastWeekChores
        };
    }

    /// <summary>
    /// Original CalculateStreaksAsync - kept for backward compatibility if called directly.
    /// </summary>
    private async Task<(int current, int best)> CalculateStreaksAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var today = _dateProvider.Today;
        var startDate = today.AddDays(-365);
        
        // Load all chore logs for the user in the last 365 days
        var allChoresInRange = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= startDate && cl.Date <= today)
            .ToListAsync();

        return CalculateStreaksFromLogs(allChoresInRange, today);
    }

    /// <summary>
    /// Original GetWeeklyComparisonAsync - kept for backward compatibility if called directly.
    /// </summary>
    private async Task<WeeklyComparison> GetWeeklyComparisonAsync(string userId)
    {
        var today = _dateProvider.Today;
        var startOfThisWeek = today.AddDays(-(int)today.DayOfWeek);
        var startOfLastWeek = startOfThisWeek.AddDays(-7);
        var streakStartDate = today.AddDays(-365);
        
        // Load logs for this call
        await using var context = await _contextFactory.CreateDbContextAsync();
        var allLogs = await context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= startOfLastWeek && cl.Date <= today)
            .ToListAsync();

        return await GetWeeklyComparisonFromLogsAsync(userId, allLogs, today, startOfThisWeek, startOfLastWeek);
    }

    private async Task<decimal> GetLifetimeEarningsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LedgerTransactions
            .Where(t => t.UserId == userId && t.Amount > 0)
            .SumAsync(t => t.Amount);
    }
}
