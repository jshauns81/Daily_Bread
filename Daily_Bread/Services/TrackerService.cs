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
    /// <summary>Design-system icon name (Lucide sprite id). Presentation only.</summary>
    public string? LucideIconName { get; init; }
    /// <summary>Tile color slot (1–5); hue follows the active theme.</summary>
    public int TileSlot { get; init; }
    public ChoreKind Kind { get; init; }
    public decimal EarnValue { get; init; }
    /// <summary>Importance weight 0–10 (how important, not minutes) used to price a missed instance. See MECHANICS_AMENDMENT.md §A.</summary>
    public int Importance { get; init; }
    /// <summary>For WeeklyFrequency earning chores: threshold (all-or-nothing) pay. See MECHANICS_AMENDMENT.md §D.</summary>
    public bool AllOrNothing { get; init; }
    public decimal Value => EarnValue; // Backward compatibility
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
    public bool IsExpectation => Kind == ChoreKind.Routine;
    public bool IsEarning => Kind == ChoreKind.Task;
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
    /// Gets chores for a specific user on a date, using pre-loaded ChoreLogs to avoid re-querying.
    /// </summary>
    /// <param name="userId">The user ID to get chores for.</param>
    /// <param name="date">The date to get chores for.</param>
    /// <param name="preLoadedLogs">Pre-loaded ChoreLogs for the user (must include ChoreDefinition and ApprovedByUser).</param>
    Task<List<TrackerChoreItem>> GetTrackerItemsForUserOnDateAsync(string userId, DateOnly date, IEnumerable<ChoreLog> preLoadedLogs);

    /// <summary>
    /// Toggles chore completion status. Returns the new status.
    /// For WeeklyFrequency chores, repeated calls after every row for the day is already
    /// Approved insert a new completion rather than reverting one - use
    /// UndoLastWeeklyCompletionAsync to go backward.
    /// </summary>
    Task<ServiceResult<ChoreStatus>> ToggleChoreCompletionAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent);

    /// <summary>
    /// Reverts the most recently created ChoreLog row for a WeeklyFrequency chore on the given
    /// date. Children may only target a not-yet-approved row (skipping Approved/Help); parents
    /// may also un-approve the newest Approved row. Returns the new status of the reverted row.
    /// </summary>
    Task<ServiceResult<ChoreStatus>> UndoLastWeeklyCompletionAsync(
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
    /// Gets help request details for a specific chore log.
    /// Used by the global help response modal.
    /// </summary>
    Task<HelpRequestDetails?> GetHelpRequestDetailsAsync(int choreLogId);

    /// <summary>
    /// Parent responds to a help request.
    /// </summary>
    /// <param name="choreLogId">The ChoreLog ID with the help request</param>
    /// <param name="parentUserId">The parent user ID responding</param>
    /// <param name="response">The response type (CompletedByParent, Excused, Denied)</param>
    /// <param name="note">Optional note from parent to child</param>
    Task<ServiceResult> RespondToHelpRequestAsync(int choreLogId, string parentUserId, HelpResponse response, string? note = null);

    /// <summary>
    /// Sets the redemption choice (Money / ScreenTime / None) for a redemptive rep and re-runs the
    /// week-level threshold reconcile so a Money choice folds into the payout immediately. Only
    /// affects money for WeeklyFrequency + AllOrNothing chores. See MECHANICS_AMENDMENT.md §D.
    /// </summary>
    Task<ServiceResult> SetRedemptionChoiceAsync(int choreLogId, RedemptionChoice choice, string userId);
}

/// <summary>
/// Details about a help request for displaying in the modal.
/// </summary>
public class HelpRequestDetails
{
    public int ChoreLogId { get; init; }
    public required string ChoreName { get; init; }
    public required string ChildName { get; init; }
    public string? ChildUserId { get; init; }
    public string? HelpReason { get; init; }
    public DateTime? HelpRequestedAt { get; init; }
    public decimal EarnValue { get; init; }
}

/// <summary>
/// Parent's response to a help request.
/// </summary>
public enum HelpResponse
{
    /// <summary>Parent completed the chore for the child - child gets credit.</summary>
    CompletedByParent,
    /// <summary>Chore is excused - counts as done: pays its routine slice and takes no screen-time hit.</summary>
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
    private readonly INtfyAlertService _ntfyAlertService;
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
        INtfyAlertService ntfyAlertService,
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
        _ntfyAlertService = ntfyAlertService;
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
        var existingLogs = (await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(c => c.ApprovedByUser)
            .Where(c => c.Date == date)
            .ToListAsync())
            .GroupBy(c => c.ChoreDefinitionId)
            .ToDictionary(g => g.Key, g => SelectRepresentativeLog(g)!);

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

        // Preserve sort order from ChoreScheduleService (SortOrder then Name)
        // ChoreDefinition doesn't have SortOrder in TrackerChoreItem, so we maintain
        // the order from scheduledChores which is already correctly sorted
        return items;
    }

    public async Task<List<TrackerChoreItem>> GetTrackerItemsForUserOnDateAsync(string userId, DateOnly date)
    {
        var scheduledChores = await _scheduleService.GetChoresForUserOnDateAsync(userId, date);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var existingLogs = (await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .Include(c => c.ApprovedByUser)
            .Where(c => c.Date == date)
            .Where(c => c.ChoreDefinition.AssignedUserId == userId)
            .ToListAsync())
            .GroupBy(c => c.ChoreDefinitionId)
            .ToDictionary(g => g.Key, g => SelectRepresentativeLog(g)!);

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

        // Preserve sort order from ChoreScheduleService (SortOrder then Name)
        return items;
    }

    /// <summary>
    /// Gets chores for a specific user on a date, using pre-loaded ChoreLogs to avoid re-querying.
    /// </summary>
    public async Task<List<TrackerChoreItem>> GetTrackerItemsForUserOnDateAsync(
        string userId, 
        DateOnly date, 
        IEnumerable<ChoreLog> preLoadedLogs)
    {
        var scheduledChores = await _scheduleService.GetChoresForUserOnDateAsync(userId, date);

        // Filter pre-loaded logs for today's date and pick one representative row per chore
        var existingLogs = preLoadedLogs
            .Where(l => l.Date == date)
            .GroupBy(l => l.ChoreDefinitionId)
            .ToDictionary(g => g.Key, g => SelectRepresentativeLog(g)!);

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

        // Preserve sort order from ChoreScheduleService (SortOrder then Name)
        return items;
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
        
        // =============================================================================
        // OPTIMIZATION: Capture data needed for post-success notifications BEFORE
        // the transaction, eliminating the re-query after successful update
        // Previously: After success, re-queried ChoreLog to get assignedUserId/choreName
        // Now: Capture these values from the initial load inside the transaction
        // =============================================================================
        string? capturedAssignedUserId = null;
        string? capturedChoreName = null;
        
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
                
                // Capture values for post-success notifications (before any modifications)
                capturedAssignedUserId = choreLog.ChoreDefinition.AssignedUserId;
                capturedChoreName = choreLog.ChoreDefinition.Name;
                
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

                // WeeklyFrequency + AllOrNothing chores pay via a single week-level threshold
                // transaction that is re-derived from the whole week on every rep change (so an
                // out-of-order un-approve correctly drops the payout to $0). Runs on both approve
                // and un-approve. See MECHANICS_AMENDMENT.md §D.
                if (choreDefinition.ScheduleType == ChoreScheduleType.WeeklyFrequency
                    && choreDefinition.AllOrNothing
                    && !string.IsNullOrEmpty(choreDefinition.AssignedUserId))
                {
                    var thresholdWeekEnd = await _familySettingsService.GetWeekEndForDateAsync(choreLog.Date);
                    var thresholdResult = await _ledgerService.ReconcileWeeklyThresholdAsync(
                        context, choreDefinition, thresholdWeekEnd, choreDefinition.AssignedUserId);

                    if (!thresholdResult.Success)
                    {
                        await transaction.RollbackAsync();
                        return ServiceResult<ChoreStatus>.Fail(thresholdResult.ErrorMessage!);
                    }
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

        // After successful status change, use captured values (NO re-query needed)
        if (result.Success && (newStatus == ChoreStatus.Completed || newStatus == ChoreStatus.Approved))
        {
            if (!string.IsNullOrEmpty(capturedAssignedUserId))
            {
                // Broadcast dashboard change to affected users
                var affectedUserIds = new HashSet<string> { actingUserId };
                if (capturedAssignedUserId != actingUserId)
                {
                    affectedUserIds.Add(capturedAssignedUserId);
                }
                
                // Exclude the acting user - they're already refreshing locally
                _ = _choreNotificationService.NotifyDashboardChangedAsync(affectedUserIds.ToArray(), excludeUserId: actingUserId);
                
                // Achievement check (fire-and-forget)
                var userIdForAchievement = capturedAssignedUserId; // Capture for closure
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var newlyAwarded = await _achievementService.CheckAndAwardAchievementsAsync(userIdForAchievement);
                        if (newlyAwarded.Count > 0)
                        {
                            _logger.LogInformation(
                                "User {UserId} earned {Count} achievement(s) after chore completion: {Codes}",
                                userIdForAchievement, newlyAwarded.Count, 
                                string.Join(", ", newlyAwarded.Select(a => a.Code)));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking achievements for user {UserId}", userIdForAchievement);
                    }
                });
            }
        }
        // Also notify on other status changes (Missed, Skipped, Pending reset)
        else if (result.Success)
        {
            var affectedUserIds = new HashSet<string> { actingUserId };
            if (!string.IsNullOrEmpty(capturedAssignedUserId) && capturedAssignedUserId != actingUserId)
            {
                affectedUserIds.Add(capturedAssignedUserId);
            }
            
            // Exclude the acting user - they're already refreshing locally
            _ = _choreNotificationService.NotifyDashboardChangedAsync(affectedUserIds.ToArray(), excludeUserId: actingUserId);
            
            // Check if this is an "undo" scenario
            if (!string.IsNullOrEmpty(capturedAssignedUserId) && 
                capturedAssignedUserId != actingUserId &&
                !string.IsNullOrEmpty(capturedChoreName) &&
                (newStatus == ChoreStatus.Pending || newStatus == ChoreStatus.Completed || newStatus == ChoreStatus.Missed))
            {
                // Get parent name for the notification
                var parentUser = await _userManager.FindByIdAsync(actingUserId);
                var parentName = parentUser?.UserName;
                
                _logger.LogInformation(
                    "Sending ChoreUndone notification: ChildUserId={ChildUserId}, ChoreName={ChoreName}",
                    capturedAssignedUserId, capturedChoreName);
                
                _ = _choreNotificationService.NotifyChoreUndoneAsync(capturedAssignedUserId, capturedChoreName, parentName);
            }
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

        // Weekly chores: once every row for today is Approved, tapping again means "log
        // another completion" (insert a new row), not "un-approve the existing one" - that
        // un-approve action now lives in UndoLastWeeklyCompletionAsync. SpecificDays chores
        // never reach this branch (GetOrCreateChoreLogAsync only ever has one row for them)
        // and fall through to the original state machine below, unchanged.
        if (choreDefinition?.ScheduleType == ChoreScheduleType.WeeklyFrequency
            && choreLog.Status == ChoreStatus.Approved)
        {
            if (!await _weeklyProgressService.CanCompleteChoreAsync(choreDefinitionId, date))
            {
                return ServiceResult<ChoreStatus>.Fail("Weekly quota already met for this chore.");
            }

            var newLogResult = await _choreLogService.CreateWeeklyCompletionAsync(choreDefinitionId, date);
            if (!newLogResult.Success)
            {
                return ServiceResult<ChoreStatus>.Fail(newLogResult.ErrorMessage!);
            }

            choreLog = newLogResult.Data!;
            newStatus = autoApprove ? ChoreStatus.Approved : ChoreStatus.Completed;
            // Auto-approve doesn't require parent role - mirrors the Pending branch below.
        }
        // Determine new status based on current status
        else if (choreLog.Status == ChoreStatus.Pending)
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
            // Reached only for SpecificDays chores - weekly Approved rows are handled
            // in the branch above instead.
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

    public async Task<ServiceResult<ChoreStatus>> UndoLastWeeklyCompletionAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent) // Note: this parameter is now only a hint, actual role is verified server-side
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var choreDefinition = await context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (choreDefinition == null)
        {
            return ServiceResult<ChoreStatus>.Fail("Chore not found.");
        }

        if (choreDefinition.ScheduleType != ChoreScheduleType.WeeklyFrequency)
        {
            return ServiceResult<ChoreStatus>.Fail("UndoLastWeeklyCompletionAsync is only valid for WeeklyFrequency chores.");
        }

        var actualIsParent = await IsUserInRoleAsync(userId, "Parent");

        // Candidate rows for today, newest first. Help rows are never eligible - they're
        // resolved only via RespondToHelpRequestAsync.
        var todaysLogs = await context.ChoreLogs
            .Where(l => l.ChoreDefinitionId == choreDefinitionId && l.Date == date)
            .Where(l => l.Status != ChoreStatus.Help)
            .OrderByDescending(l => l.Id)
            .ToListAsync();

        // Children may only undo their own not-yet-approved completion; parents may also
        // un-approve the newest Approved row.
        var target = actualIsParent
            ? todaysLogs.FirstOrDefault()
            : todaysLogs.FirstOrDefault(l => l.Status != ChoreStatus.Approved);

        if (target == null)
        {
            return actualIsParent
                ? ServiceResult<ChoreStatus>.Fail("Nothing to undo.")
                : ServiceResult<ChoreStatus>.Fail("This has already been approved - ask a parent to undo it.");
        }

        return await UpdateStatusAtomicallyAsync(
            target.Id,
            ChoreStatus.Pending,
            userId,
            requiresParent: target.Status == ChoreStatus.Approved);
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
        
        // ntfy: reliable native push to parents' phones - the alert that must land.
        _ = _ntfyAlertService.SendHelpAlertAsync(childName, choreName, reason, choreLog.Id);

        // Web push (best-effort, esp. on iOS) with choreLogId for deep linking
        _ = _pushNotificationService.SendHelpRequestNotificationAsync(choreLog.Id, childName, choreName, reason);
        
        // SignalR real-time notification
        _ = _choreNotificationService.NotifyHelpRequestedAsync(choreLog.Id, userId, choreName, childName);
        
        // Also notify dashboard changed so parents see updated help count
        // Exclude the acting user (child) - they're already refreshing locally
        _ = _choreNotificationService.NotifyDashboardChangedAsync(new[] { userId }, excludeUserId: userId);
        
        return ServiceResult.Ok();
    }
    
    public async Task<HelpRequestDetails?> GetHelpRequestDetailsAsync(int choreLogId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var choreLog = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
            .FirstOrDefaultAsync(c => c.Id == choreLogId);
        
        if (choreLog == null)
        {
            _logger.LogWarning("GetHelpRequestDetails: ChoreLog {ChoreLogId} not found", choreLogId);
            return null;
        }
        
        return new HelpRequestDetails
        {
            ChoreLogId = choreLog.Id,
            ChoreName = choreLog.ChoreDefinition.Name,
            ChildName = choreLog.ChoreDefinition.AssignedUser?.UserName ?? "Child",
            ChildUserId = choreLog.ChoreDefinition.AssignedUserId,
            HelpReason = choreLog.HelpReason,
            HelpRequestedAt = choreLog.HelpRequestedAt,
            EarnValue = choreLog.ChoreDefinition.EarnValue
        };
    }

    public async Task<ServiceResult> RespondToHelpRequestAsync(int choreLogId, string parentUserId, HelpResponse response, string? note = null)
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
        // For other responses, save the parent's note if provided
        string? notesToSave = response == HelpResponse.Denied 
            ? "__CLEAR_HELP_FIELDS__" 
            : note;

        // Parent-only action, verified server-side
        var result = await UpdateStatusAtomicallyAsync(
            choreLogId,
            newStatus,
            parentUserId,
            requiresParent: true,
            notes: notesToSave);

        if (!result.Success)
        {
            return ServiceResult.Fail(result.ErrorMessage!);
        }
        
        _logger.LogInformation(
            "Help response: ChoreLogId={ChoreLogId}, Response={Response}, NewStatus={NewStatus}, ChildUserId={ChildUserId}, Note={Note}",
            choreLogId, response, newStatus, childUserId ?? "null", note ?? "none");
        
        // Send notification to child about the help response
        if (!string.IsNullOrEmpty(childUserId))
        {
            var parentUser = await _userManager.FindByIdAsync(parentUserId);
            var parentName = parentUser?.UserName;
            
            _logger.LogInformation(
                "Sending HelpResponded notification: ChildUserId={ChildUserId}, ChoreName={ChoreName}, Response={Response}, Note={Note}",
                childUserId, choreName, response, note ?? "none");
            
            // Await to ensure notification is sent before returning
            await _choreNotificationService.NotifyHelpRespondedAsync(
                childUserId,
                choreName,
                response.ToString(),
                parentName,
                note);
            
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

    public async Task<ServiceResult> SetRedemptionChoiceAsync(int choreLogId, RedemptionChoice choice, string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var choreLog = await context.ChoreLogs
                    .Include(c => c.ChoreDefinition)
                    .FirstOrDefaultAsync(c => c.Id == choreLogId);

                if (choreLog == null)
                {
                    return ServiceResult.Fail("Chore log not found.");
                }

                choreLog.RedemptionChoice = choice;
                choreLog.ModifiedAt = DateTime.UtcNow;
                choreLog.Version++; // Manual concurrency increment

                // A Money choice on a redemptive rep folds into the week-level payout immediately;
                // re-derive it from the whole week. See MECHANICS_AMENDMENT.md §D.
                var choreDefinition = choreLog.ChoreDefinition;
                if (choreDefinition.ScheduleType == ChoreScheduleType.WeeklyFrequency
                    && choreDefinition.AllOrNothing
                    && !string.IsNullOrEmpty(choreDefinition.AssignedUserId))
                {
                    var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(choreLog.Date);
                    var thresholdResult = await _ledgerService.ReconcileWeeklyThresholdAsync(
                        context, choreDefinition, weekEnd, choreDefinition.AssignedUserId);

                    if (!thresholdResult.Success)
                    {
                        await transaction.RollbackAsync();
                        return ServiceResult.Fail(thresholdResult.ErrorMessage!);
                    }
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Redemption choice set: ChoreLogId={ChoreLogId}, Choice={Choice}, ActingUser={ActingUserId}",
                    choreLogId, choice, userId);

                return ServiceResult.Ok();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict setting redemption choice on ChoreLog {ChoreLogId}", choreLogId);
                return ServiceResult.Fail("This chore was modified by someone else. Please refresh and try again.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to set redemption choice on ChoreLog {ChoreLogId}", choreLogId);
                return ServiceResult.Fail($"Failed to set redemption choice: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Weekly chores can have multiple ChoreLog rows for the same date - one per completion.
    /// Picks one representative row per chore for the card's single status badge/swipe-state:
    /// the oldest still-Pending row if one exists, otherwise the newest row. WeeklyCompletedCount
    /// itself comes from the separate WeeklyProgressService batch lookup, so this choice only
    /// affects which status badge/swipe-state the card shows, not the count.
    /// </summary>
    private static ChoreLog? SelectRepresentativeLog(IEnumerable<ChoreLog> logsForChore)
    {
        var logs = logsForChore.ToList();
        if (logs.Count == 0)
        {
            return null;
        }

        return logs
            .Where(l => l.Status == ChoreStatus.Pending)
            .OrderBy(l => l.Id)
            .FirstOrDefault()
            ?? logs.OrderByDescending(l => l.Id).First();
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
            LucideIconName = chore.LucideIconName,
            TileSlot = chore.TileSlot,
            Kind = chore.Kind,
            EarnValue = chore.EarnValue,
            Importance = chore.Importance,
            AllOrNothing = chore.AllOrNothing,
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
