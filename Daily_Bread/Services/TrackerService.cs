using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// View model for displaying a chore in the tracker.
/// </summary>
public class TrackerChoreItem
{
    public int ChoreDefinitionId { get; init; }
    public int? ChoreLogId { get; init; }
    public required string ChoreName { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public decimal EarnValue { get; init; }
    public decimal PenaltyValue { get; init; }
    public decimal Value => EarnValue > 0 ? EarnValue : PenaltyValue; // Backward compatibility
    public ChoreStatus Status { get; init; }
    public string? AssignedUserId { get; init; }
    public string? AssignedUserName { get; init; }
    public string? ApprovedByUserName { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? HelpReason { get; init; }
    public DateTime? HelpRequestedAt { get; init; }
    
    // Schedule type
    public ChoreScheduleType ScheduleType { get; init; }
    
    // Weekly chore properties
    public int WeeklyTargetCount { get; init; }
    public int WeeklyCompletedCount { get; init; }
    public bool IsRepeatable { get; init; }
    
    /// <summary>
    /// For weekly chores: what this specific completion will earn.
    /// Accounts for diminishing returns on bonus completions.
    /// </summary>
    public decimal? NextCompletionValue { get; init; }
    
    // Status helpers
    public bool IsCompleted => Status is ChoreStatus.Completed or ChoreStatus.Approved;
    public bool IsApproved => Status == ChoreStatus.Approved;
    public bool IsMissed => Status == ChoreStatus.Missed;
    public bool IsSkipped => Status == ChoreStatus.Skipped;
    public bool IsPending => Status == ChoreStatus.Pending;
    public bool IsHelp => Status == ChoreStatus.Help;
    
    // Chore type helpers
    public bool IsExpectation => EarnValue == 0; // Any chore with no earning value is a daily task/expectation
    public bool IsEarning => EarnValue > 0;
    public bool IsWeeklyFlexible => ScheduleType == ChoreScheduleType.WeeklyFrequency;
    public bool IsDailyFixed => ScheduleType == ChoreScheduleType.SpecificDays;
    
    // Weekly progress helpers
    public bool IsWeeklyQuotaMet => WeeklyCompletedCount >= WeeklyTargetCount;
    public int WeeklyRemainingCount => Math.Max(0, WeeklyTargetCount - WeeklyCompletedCount);
    public bool CanDoMore => IsRepeatable || !IsWeeklyQuotaMet;
}

/// <summary>
/// Service for providing tracker UI data.
/// </summary>
public interface ITrackerService
{
    /// <summary>
    /// Gets all chores for a date with their current status.
    /// </summary>
    Task<List<TrackerChoreItem>> GetTrackerItemsForDateAsync(DateOnly date);

    /// <summary>
    /// Gets chores for a specific user on a date.
    /// </summary>
    Task<List<TrackerChoreItem>> GetTrackerItemsForUserOnDateAsync(string userId, DateOnly date);

    /// <summary>
    /// Toggles chore completion status. Returns the new status.
    /// </summary>
    Task<ServiceResult<ChoreStatus>> ToggleChoreCompletionAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent);

    /// <summary>
    /// Sets a specific status for a chore log. Parent only.
    /// </summary>
    Task<ServiceResult> SetChoreStatusAsync(
        int choreDefinitionId,
        DateOnly date,
        ChoreStatus status,
        string userId);

    /// <summary>
    /// Marks a chore as missed. Parent only.
    /// </summary>
    Task<ServiceResult> MarkChoreMissedAsync(int choreDefinitionId, DateOnly date, string userId);

    /// <summary>
    /// Marks a chore as skipped/excused. Parent only.
    /// </summary>
    Task<ServiceResult> MarkChoreSkippedAsync(int choreDefinitionId, DateOnly date, string userId);

    /// <summary>
    /// Resets a chore to pending status. Parent only.
    /// </summary>
    Task<ServiceResult> ResetChoreToPendingAsync(int choreDefinitionId, DateOnly date, string userId);
    
    /// <summary>
    /// Child requests help with a chore. Notifies parents.
    /// </summary>
    Task<ServiceResult> RequestHelpAsync(int choreDefinitionId, DateOnly date, string userId, string reason);
    
    /// <summary>
    /// Parent responds to a help request.
    /// </summary>
    Task<ServiceResult> RespondToHelpRequestAsync(int choreLogId, string parentUserId, HelpResponse response);
}

/// <summary>
/// Parent's response to a help request.
/// </summary>
public enum HelpResponse
{
    /// <summary>Parent completed the chore for the child - child gets credit.</summary>
    CompletedByParent,
    /// <summary>Chore is excused - no penalty, no earning.</summary>
    Excused,
    /// <summary>Request denied - child must do it.</summary>
    Denied
}

public class TrackerService : ITrackerService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IChoreScheduleService _scheduleService;
    private readonly IChoreLogService _choreLogService;
    private readonly ILedgerService _ledgerService;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IWeeklyProgressService _weeklyProgressService;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IAchievementService _achievementService;
    private readonly IChoreNotificationService _choreNotificationService;
    private readonly IDateProvider _dateProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<TrackerService> _logger;

    public TrackerService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IChoreScheduleService scheduleService,
        IChoreLogService choreLogService,
        ILedgerService ledgerService,
        IPushNotificationService pushNotificationService,
        IWeeklyProgressService weeklyProgressService,
        IFamilySettingsService familySettingsService,
        IAchievementService achievementService,
        IChoreNotificationService choreNotificationService,
        IDateProvider dateProvider,
        UserManager<ApplicationUser> userManager,
        ILogger<TrackerService> logger)
    {
        _contextFactory = contextFactory;
        _scheduleService = scheduleService;
        _choreLogService = choreLogService;
        _ledgerService = ledgerService;
        _pushNotificationService = pushNotificationService;
        _weeklyProgressService = weeklyProgressService;
        _familySettingsService = familySettingsService;
        _achievementService = achievementService;
        _choreNotificationService = choreNotificationService;
        _dateProvider = dateProvider;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<List<TrackerChoreItem>> GetTrackerItemsForDateAsync(DateOnly date)
    {
        var scheduledChores = await _scheduleService.GetChoresForDateAsync(date);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var existingLogs = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(c => c.ApprovedByUser)
            .Where(c => c.Date == date)
            .ToDictionaryAsync(c => c.ChoreDefinitionId);

        var weeklyChoreIds = scheduledChores
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .Select(c => c.Id)
            .ToList();
        
        var weeklyProgress = await _weeklyProgressService.GetChoreProgressBatchAsync(weeklyChoreIds, date);

        var items = new List<TrackerChoreItem>();

        foreach (var chore in scheduledChores)
        {
            weeklyProgress.TryGetValue(chore.Id, out var progress);
            items.Add(existingLogs.TryGetValue(chore.Id, out var log)
                ? CreateTrackerItem(chore, log, progress)
                : CreateTrackerItem(chore, null, progress));
        }

        return items.OrderBy(i => i.ChoreName).ToList();
    }

    public async Task<List<TrackerChoreItem>> GetTrackerItemsForUserOnDateAsync(string userId, DateOnly date)
    {
        var scheduledChores = await _scheduleService.GetChoresForUserOnDateAsync(userId, date);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var existingLogs = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(c => c.ApprovedByUser)
            .Where(c => c.Date == date)
            .Where(c => c.ChoreDefinition.AssignedUserId == userId)
            .ToDictionaryAsync(c => c.ChoreDefinitionId);

        var weeklyChoreIds = scheduledChores
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .Select(c => c.Id)
            .ToList();
        
        var weeklyProgress = await _weeklyProgressService.GetChoreProgressBatchAsync(weeklyChoreIds, date);

        var items = new List<TrackerChoreItem>();

        foreach (var chore in scheduledChores)
        {
            weeklyProgress.TryGetValue(chore.Id, out var progress);
            items.Add(existingLogs.TryGetValue(chore.Id, out var log)
                ? CreateTrackerItem(chore, log, progress)
                : CreateTrackerItem(chore, null, progress));
        }

        return items.OrderBy(i => i.ChoreName).ToList();
    }

    /// <summary>
    /// Atomically updates chore status and reconciles ledger.
    /// All status changes that affect money go through this method.
    /// Also triggers achievement checks after successful completion/approval.
    /// </summary>
    private async Task<ServiceResult<ChoreStatus>> UpdateStatusAtomicallyAsync(
        int choreLogId,
        ChoreStatus newStatus,
        string actingUserId,
        bool requiresParent,
        string? notes = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Use execution strategy to handle retries with transactions
        var strategy = context.Database.CreateExecutionStrategy();
        
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            
            try
            {
                // Load chore log with related data
                var choreLog = await context.ChoreLogs
                    .Include(c => c.ChoreDefinition)
                    .Include(c => c.LedgerTransaction)
                    .FirstOrDefaultAsync(c => c.Id == choreLogId);

                if (choreLog == null)
                {
                    return ServiceResult<ChoreStatus>.Fail("Chore log not found.");
                }
                
                // SERVER-SIDE AUTHORIZATION: verify role from database
                if (requiresParent)
                {
                    var isParent = await IsUserInRoleAsync(actingUserId, "Parent");
                    if (!isParent)
                    {
                        _logger.LogWarning(
                            "Authorization failed: User {UserId} attempted parent-only action on ChoreLog {ChoreLogId}",
                            actingUserId, choreLogId);
                        return ServiceResult<ChoreStatus>.Fail("Only parents can perform this action.");
                    }
                }
                
                var oldStatus = choreLog.Status;
                var choreDefinition = choreLog.ChoreDefinition;
                
                // Validate date restriction for children (non-parents can only modify today)
                var isParentUser = await IsUserInRoleAsync(actingUserId, "Parent");
                if (!isParentUser && choreLog.Date != _dateProvider.Today)
                {
                    return ServiceResult<ChoreStatus>.Fail("Children can only modify today's chores.");
                }

                // Update status
                choreLog.Status = newStatus;
                choreLog.ModifiedAt = DateTime.UtcNow;
                choreLog.Version++; // Manual concurrency increment
                
                // Handle special notes marker for clearing help fields
                if (notes == "__CLEAR_HELP_FIELDS__")
                {
                    choreLog.HelpReason = null;
                    choreLog.HelpRequestedAt = null;
                    // Don't set notes for this special case
                }
                else if (!string.IsNullOrWhiteSpace(notes))
                {
                    choreLog.Notes = notes;
                }

                // Set completion/approval metadata
                if (newStatus == ChoreStatus.Completed)
                {
                    choreLog.CompletedByUserId = actingUserId;
                    choreLog.CompletedAt = DateTime.UtcNow;
                }
                else if (newStatus == ChoreStatus.Approved)
                {
                    choreLog.ApprovedByUserId = actingUserId;
                    choreLog.ApprovedAt = DateTime.UtcNow;
                    if (choreLog.CompletedAt == null)
                    {
                        choreLog.CompletedByUserId = actingUserId;
                        choreLog.CompletedAt = DateTime.UtcNow;
                    }
                }
                
                // Reconcile ledger using same context (atomic)
                var reconcileResult = await _ledgerService.ReconcileChoreLogTransactionAsync(context, choreLog);
                
                if (!reconcileResult.Success)
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<ChoreStatus>.Fail(reconcileResult.ErrorMessage!);
                }

                // Save all changes atomically
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                // Audit log
                _logger.LogInformation(
                    "ChoreLog status changed: Id={ChoreLogId}, Chore={ChoreName}, Date={Date}, " +
                    "OldStatus={OldStatus}, NewStatus={NewStatus}, ActingUser={ActingUserId}, " +
                    "TransactionId={TransactionId}, Amount={Amount}",
                    choreLogId, choreDefinition.Name, choreLog.Date,
                    oldStatus, newStatus, actingUserId,
                    reconcileResult.Transaction?.Id, reconcileResult.Amount);

                return ServiceResult<ChoreStatus>.Ok(newStatus);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex,
                    "Concurrency conflict updating ChoreLog {ChoreLogId}",
                    choreLogId);
                return ServiceResult<ChoreStatus>.Fail("This chore was modified by someone else. Please refresh and try again.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update ChoreLog {ChoreLogId} status", choreLogId);
                return ServiceResult<ChoreStatus>.Fail($"Failed to update chore: {ex.Message}");
            }
        });

        // After successful status change, check for achievement unlocks (fire-and-forget)
        // This runs outside the transaction so achievement failures don't affect chore updates
        if (result.Success && (newStatus == ChoreStatus.Completed || newStatus == ChoreStatus.Approved))
        {
            // Get the assigned user ID for achievement check and SignalR notification
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            var log = await ctx.ChoreLogs
                .Include(c => c.ChoreDefinition)
                .FirstOrDefaultAsync(c => c.Id == choreLogId);
            
            var assignedUserId = log?.ChoreDefinition?.AssignedUserId;
            if (!string.IsNullOrEmpty(assignedUserId))
            {
                // Broadcast dashboard change to affected users
                // Both the acting user (parent/child) and the assigned user should refresh
                var affectedUserIds = new HashSet<string> { actingUserId };
                if (assignedUserId != actingUserId)
                {
                    affectedUserIds.Add(assignedUserId);
                }
                
                _ = _choreNotificationService.NotifyDashboardChangedAsync([.. affectedUserIds]);
                
                // Achievement check (fire-and-forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var newlyAwarded = await _achievementService.CheckAndAwardAchievementsAsync(assignedUserId);
                        if (newlyAwarded.Count > 0)
                        {
                            _logger.LogInformation(
                                "User {UserId} earned {Count} achievement(s) after chore completion: {Codes}",
                                assignedUserId, newlyAwarded.Count, 
                                string.Join(", ", newlyAwarded.Select(a => a.Code)));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking achievements for user {UserId}", assignedUserId);
                    }
                });
            }
        }
        // Also notify on other status changes (Missed, Skipped, Pending reset)
        else if (result.Success)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            var log = await ctx.ChoreLogs
                .Include(c => c.ChoreDefinition)
                .FirstOrDefaultAsync(c => c.Id == choreLogId);
            
            var assignedUserId = log?.ChoreDefinition?.AssignedUserId;
            var affectedUserIds = new HashSet<string> { actingUserId };
            if (!string.IsNullOrEmpty(assignedUserId) && assignedUserId != actingUserId)
            {
                affectedUserIds.Add(assignedUserId);
            }
            
            _ = _choreNotificationService.NotifyDashboardChangedAsync([.. affectedUserIds]);
        }

        return result;
    }
    
    /// <summary>
    /// Server-side role check using UserManager.
    /// </summary>
    private async Task<bool> IsUserInRoleAsync(string userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;
        return await _userManager.IsInRoleAsync(user, roleName);
    }

    public async Task<ServiceResult<ChoreStatus>> ToggleChoreCompletionAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent) // Note: this parameter is now only a hint, actual role is verified server-side
    {
        var logResult = await _choreLogService.GetOrCreateChoreLogAsync(choreDefinitionId, date);
        if (!logResult.Success)
        {
            return ServiceResult<ChoreStatus>.Fail(logResult.ErrorMessage!);
        }

        var choreLog = logResult.Data!;
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        var choreDefinition = await context.ChoreDefinitions.FindAsync(choreDefinitionId);
        var autoApprove = choreDefinition?.AutoApprove ?? true;
        
        // Verify actual role from server
        var actualIsParent = await IsUserInRoleAsync(userId, "Parent");

        ChoreStatus newStatus;
        bool requiresParent = false;

        // Determine new status based on current status
        if (choreLog.Status == ChoreStatus.Pending)
        {
            newStatus = autoApprove ? ChoreStatus.Approved : ChoreStatus.Completed;
            // Auto-approve doesn't require parent role
        }
        else if (choreLog.Status == ChoreStatus.Completed && !actualIsParent)
        {
            newStatus = ChoreStatus.Pending;
        }
        else if (choreLog.Status == ChoreStatus.Completed && actualIsParent)
        {
            newStatus = ChoreStatus.Approved;
            requiresParent = true;
        }
        else if (choreLog.Status == ChoreStatus.Approved)
        {
            if (actualIsParent)
            {
                newStatus = autoApprove ? ChoreStatus.Pending : ChoreStatus.Completed;
                requiresParent = !autoApprove; // Un-approving requires parent unless auto-approve
            }
            else
            {
                // Child can only toggle auto-approved chores
                if (!autoApprove)
                {
                    return ServiceResult<ChoreStatus>.Fail("Only parents can modify approved chores.");
                }
                newStatus = ChoreStatus.Pending;
            }
        }
        else if (choreLog.Status == ChoreStatus.Help)
        {
            if (!actualIsParent)
            {
                return ServiceResult<ChoreStatus>.Fail("Waiting for parent response to your help request.");
            }
            newStatus = ChoreStatus.Approved;
            requiresParent = true;
        }
        else // Missed or Skipped
        {
            if (!actualIsParent)
            {
                return ServiceResult<ChoreStatus>.Fail("Only parents can modify missed or skipped chores.");
            }
            newStatus = ChoreStatus.Pending;
            requiresParent = true;
        }

        return await UpdateStatusAtomicallyAsync(choreLog.Id, newStatus, userId, requiresParent);
    }

    public async Task<ServiceResult> SetChoreStatusAsync(
        int choreDefinitionId,
        DateOnly date,
        ChoreStatus status,
        string userId)
    {
        var logResult = await _choreLogService.GetOrCreateChoreLogAsync(choreDefinitionId, date);
        if (!logResult.Success)
        {
            return ServiceResult.Fail(logResult.ErrorMessage!);
        }

        // SetChoreStatusAsync is always a parent-only method
        var result = await UpdateStatusAtomicallyAsync(
            logResult.Data!.Id,
            status,
            userId,
            requiresParent: true);

        return result.Success ? ServiceResult.Ok() : ServiceResult.Fail(result.ErrorMessage!);
    }

    public async Task<ServiceResult> MarkChoreMissedAsync(int choreDefinitionId, DateOnly date, string userId)
        => await SetChoreStatusAsync(choreDefinitionId, date, ChoreStatus.Missed, userId);

    public async Task<ServiceResult> MarkChoreSkippedAsync(int choreDefinitionId, DateOnly date, string userId)
        => await SetChoreStatusAsync(choreDefinitionId, date, ChoreStatus.Skipped, userId);

    public async Task<ServiceResult> ResetChoreToPendingAsync(int choreDefinitionId, DateOnly date, string userId)
        => await SetChoreStatusAsync(choreDefinitionId, date, ChoreStatus.Pending, userId);
    
    public async Task<ServiceResult> RequestHelpAsync(int choreDefinitionId, DateOnly date, string userId, string reason)
    {
        var logResult = await _choreLogService.GetOrCreateChoreLogAsync(choreDefinitionId, date);
        if (!logResult.Success)
        {
            return ServiceResult.Fail(logResult.ErrorMessage!);
        }

        var choreLog = logResult.Data!;
        
        if (choreLog.Status != ChoreStatus.Pending)
        {
            return ServiceResult.Fail("Can only request help on pending chores.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var choreDefinition = await context.ChoreDefinitions
            .Include(c => c.AssignedUser)
            .FirstOrDefaultAsync(c => c.Id == choreDefinitionId);
        
        var logToUpdate = await context.ChoreLogs.FindAsync(choreLog.Id);
        
        if (logToUpdate == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }
        
        logToUpdate.Status = ChoreStatus.Help;
        logToUpdate.HelpReason = reason;
        logToUpdate.HelpRequestedAt = DateTime.UtcNow;
        logToUpdate.ModifiedAt = DateTime.UtcNow;
        logToUpdate.Version++;
        
        await context.SaveChangesAsync();
        
        _logger.LogInformation(
            "Help requested: ChoreLogId={ChoreLogId}, UserId={UserId}",
            choreLog.Id, userId);
        
        var childName = choreDefinition?.AssignedUser?.UserName ?? "Your child";
        var choreName = choreDefinition?.Name ?? "a chore";
        
        // Push notification (existing)
        _ = _pushNotificationService.SendHelpRequestNotificationAsync(childName, choreName, reason);
        
        // SignalR real-time notification
        _ = _choreNotificationService.NotifyHelpRequestedAsync(choreLog.Id, userId, choreName, childName);
        
        // Also notify dashboard changed so parents see updated help count
        _ = _choreNotificationService.NotifyDashboardChangedAsync(userId);
        
        return ServiceResult.Ok();
    }
    
    public async Task<ServiceResult> RespondToHelpRequestAsync(int choreLogId, string parentUserId, HelpResponse response)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var choreLog = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .FirstOrDefaultAsync(c => c.Id == choreLogId);
        
        if (choreLog == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }
        
        if (choreLog.Status != ChoreStatus.Help)
        {
            return ServiceResult.Fail("This chore is not waiting for help.");
        }

        // Capture child info before status change
        var childUserId = choreLog.ChoreDefinition.AssignedUserId;
        var choreName = choreLog.ChoreDefinition.Name;
        var earnedAmount = choreLog.ChoreDefinition.EarnValue;

        ChoreStatus newStatus = response switch
        {
            HelpResponse.CompletedByParent => ChoreStatus.Approved,
            HelpResponse.Excused => ChoreStatus.Skipped,
            HelpResponse.Denied => ChoreStatus.Pending,
            _ => throw new ArgumentException("Invalid response type")
        };

        // For denied requests, we need to clear help fields as part of the update
        // Pass notes to signal this (hacky but avoids refactoring UpdateStatusAtomicallyAsync)
        string? notes = response == HelpResponse.Denied ? "__CLEAR_HELP_FIELDS__" : null;

        // Parent-only action, verified server-side
        var result = await UpdateStatusAtomicallyAsync(
            choreLogId,
            newStatus,
            parentUserId,
            requiresParent: true,
            notes: notes);

        if (!result.Success)
        {
            return ServiceResult.Fail(result.ErrorMessage!);
        }
        
        _logger.LogInformation(
            "Help response: ChoreLogId={ChoreLogId}, Response={Response}, NewStatus={NewStatus}, ChildUserId={ChildUserId}",
            choreLogId, response, newStatus, childUserId ?? "null");
        
        // Send notification to child about the help response
        if (!string.IsNullOrEmpty(childUserId))
        {
            var parentUser = await _userManager.FindByIdAsync(parentUserId);
            var parentName = parentUser?.UserName;
            
            _logger.LogInformation(
                "Sending HelpResponded notification: ChildUserId={ChildUserId}, ChoreName={ChoreName}, Response={Response}",
                childUserId, choreName, response);
            
            // Await to ensure notification is sent before returning
            await _choreNotificationService.NotifyHelpRespondedAsync(
                childUserId,
                choreName,
                response.ToString(),
                parentName);
            
            // If the chore was completed by parent (approved), also send blessing notification
            if (response == HelpResponse.CompletedByParent && earnedAmount > 0)
            {
                _logger.LogInformation(
                    "Sending BlessingGranted notification: ChildUserId={ChildUserId}, ChoreName={ChoreName}, Amount={Amount}",
                    childUserId, choreName, earnedAmount);
                
                await _choreNotificationService.NotifyBlessingGrantedAsync(
                    childUserId,
                    choreName,
                    earnedAmount,
                    parentName);
            }
        }
        else
        {
            _logger.LogWarning("Cannot send help response notification: childUserId is null for ChoreLogId={ChoreLogId}", choreLogId);
        }
        
        return ServiceResult.Ok();
    }

    private static TrackerChoreItem CreateTrackerItem(
        ChoreDefinition chore, ChoreLog? log, WeeklyChoreProgress? weeklyProgress = null)
    {
        return new TrackerChoreItem
        {
            ChoreDefinitionId = chore.Id,
            ChoreLogId = log?.Id,
            ChoreName = chore.Name,
            Description = chore.Description,
            Icon = chore.Icon,
            EarnValue = chore.EarnValue,
            PenaltyValue = chore.PenaltyValue,
            Status = log?.Status ?? ChoreStatus.Pending,
            AssignedUserId = chore.AssignedUserId,
            AssignedUserName = chore.AssignedUser?.UserName,
            ApprovedByUserName = log?.ApprovedByUser?.UserName,
            ApprovedAt = log?.ApprovedAt,
            HelpReason = log?.HelpReason,
            HelpRequestedAt = log?.HelpRequestedAt,
            ScheduleType = chore.ScheduleType,
            WeeklyTargetCount = chore.WeeklyTargetCount,
            WeeklyCompletedCount = weeklyProgress?.CompletedCount ?? 0,
            IsRepeatable = chore.IsRepeatable,
            NextCompletionValue = weeklyProgress?.NextBonusValue ?? chore.EarnValue
        };
    }
}
