using Daily_Bread.Data;
using Daily_Bread.Data.Models;
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
    
    // Status helpers
    public bool IsCompleted => Status is ChoreStatus.Completed or ChoreStatus.Approved;
    public bool IsApproved => Status == ChoreStatus.Approved;
    public bool IsMissed => Status == ChoreStatus.Missed;
    public bool IsSkipped => Status == ChoreStatus.Skipped;
    public bool IsPending => Status == ChoreStatus.Pending;
    public bool IsHelp => Status == ChoreStatus.Help;
    
    // Chore type helpers
    public bool IsExpectation => EarnValue == 0 && PenaltyValue > 0;
    public bool IsEarning => EarnValue > 0;
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

    public TrackerService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IChoreScheduleService scheduleService,
        IChoreLogService choreLogService,
        ILedgerService ledgerService)
    {
        _contextFactory = contextFactory;
        _scheduleService = scheduleService;
        _choreLogService = choreLogService;
        _ledgerService = ledgerService;
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

        var items = new List<TrackerChoreItem>();

        foreach (var chore in scheduledChores)
        {
            if (existingLogs.TryGetValue(chore.Id, out var log))
            {
                items.Add(CreateTrackerItem(chore, log));
            }
            else
            {
                items.Add(CreateTrackerItem(chore, null));
            }
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

        var items = new List<TrackerChoreItem>();

        foreach (var chore in scheduledChores)
        {
            if (existingLogs.TryGetValue(chore.Id, out var log))
            {
                items.Add(CreateTrackerItem(chore, log));
            }
            else
            {
                items.Add(CreateTrackerItem(chore, null));
            }
        }

        return items.OrderBy(i => i.ChoreName).ToList();
    }

    public async Task<ServiceResult<ChoreStatus>> ToggleChoreCompletionAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent)
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

        ChoreStatus newStatus;

        // Determine new status based on current status
        if (choreLog.Status == ChoreStatus.Pending)
        {
            // Mark as completed - auto-approve if configured
            newStatus = autoApprove ? ChoreStatus.Approved : ChoreStatus.Completed;
        }
        else if (choreLog.Status == ChoreStatus.Completed && !isParent)
        {
            // Child can toggle back to pending
            newStatus = ChoreStatus.Pending;
        }
        else if (choreLog.Status == ChoreStatus.Completed && isParent)
        {
            // Parent can approve
            newStatus = ChoreStatus.Approved;
        }
        else if (choreLog.Status == ChoreStatus.Approved)
        {
            // Toggle back: auto-approve chores go to pending, manual ones go to completed
            if (isParent)
            {
                newStatus = autoApprove ? ChoreStatus.Pending : ChoreStatus.Completed;
            }
            else
            {
                // Child can toggle auto-approved chores back to pending
                newStatus = autoApprove ? ChoreStatus.Pending : ChoreStatus.Approved;
            }
        }
        else if (choreLog.Status == ChoreStatus.Help)
        {
            // From Help status, only parent can change
            if (!isParent)
            {
                return ServiceResult<ChoreStatus>.Fail("Waiting for parent response to your help request.");
            }
            // Parent toggling a help request marks it as completed
            newStatus = ChoreStatus.Approved;
        }
        else
        {
            // For Missed/Skipped, only parent can change
            if (!isParent)
            {
                return ServiceResult<ChoreStatus>.Fail("Only parents can modify missed or skipped chores.");
            }
            newStatus = ChoreStatus.Pending;
        }

        var updateResult = await _choreLogService.UpdateChoreLogStatusAsync(
            choreLog.Id,
            newStatus,
            userId,
            isParent || autoApprove);

        if (!updateResult.Success)
        {
            return ServiceResult<ChoreStatus>.Fail(updateResult.ErrorMessage!);
        }

        await _ledgerService.ReconcileChoreLogTransactionAsync(choreLog.Id);

        return ServiceResult<ChoreStatus>.Ok(newStatus);
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

        var choreLog = logResult.Data!;

        var updateResult = await _choreLogService.UpdateChoreLogStatusAsync(
            choreLog.Id,
            status,
            userId,
            isParent: true);

        if (!updateResult.Success)
        {
            return updateResult;
        }

        await _ledgerService.ReconcileChoreLogTransactionAsync(choreLog.Id);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MarkChoreMissedAsync(int choreDefinitionId, DateOnly date, string userId)
    {
        return await SetChoreStatusAsync(choreDefinitionId, date, ChoreStatus.Missed, userId);
    }

    public async Task<ServiceResult> MarkChoreSkippedAsync(int choreDefinitionId, DateOnly date, string userId)
    {
        return await SetChoreStatusAsync(choreDefinitionId, date, ChoreStatus.Skipped, userId);
    }

    public async Task<ServiceResult> ResetChoreToPendingAsync(int choreDefinitionId, DateOnly date, string userId)
    {
        return await SetChoreStatusAsync(choreDefinitionId, date, ChoreStatus.Pending, userId);
    }
    
    public async Task<ServiceResult> RequestHelpAsync(int choreDefinitionId, DateOnly date, string userId, string reason)
    {
        var logResult = await _choreLogService.GetOrCreateChoreLogAsync(choreDefinitionId, date);
        if (!logResult.Success)
        {
            return ServiceResult.Fail(logResult.ErrorMessage!);
        }

        var choreLog = logResult.Data!;
        
        // Can only request help on pending chores
        if (choreLog.Status != ChoreStatus.Pending)
        {
            return ServiceResult.Fail("Can only request help on pending chores.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var logToUpdate = await context.ChoreLogs.FindAsync(choreLog.Id);
        
        if (logToUpdate == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }
        
        logToUpdate.Status = ChoreStatus.Help;
        logToUpdate.HelpReason = reason;
        logToUpdate.HelpRequestedAt = DateTime.UtcNow;
        logToUpdate.ModifiedAt = DateTime.UtcNow;
        
        await context.SaveChangesAsync();
        
        // TODO: Send notification to parents
        
        return ServiceResult.Ok();
    }
    
    public async Task<ServiceResult> RespondToHelpRequestAsync(int choreLogId, string parentUserId, HelpResponse response)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var choreLog = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .FirstOrDefaultAsync(c => c.Id == choreLogId);
        
        if (choreLog == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }
        
        if (choreLog.Status != ChoreStatus.Help)
        {
            return ServiceResult.Fail("This chore is not waiting for help.");
        }

        switch (response)
        {
            case HelpResponse.CompletedByParent:
                // Parent did it for them - mark as approved, child gets credit
                choreLog.Status = ChoreStatus.Approved;
                choreLog.ApprovedByUserId = parentUserId;
                choreLog.ApprovedAt = DateTime.UtcNow;
                choreLog.CompletedByUserId = parentUserId;
                choreLog.CompletedAt = DateTime.UtcNow;
                break;
                
            case HelpResponse.Excused:
                // Excused - no penalty, no earning
                choreLog.Status = ChoreStatus.Skipped;
                choreLog.ApprovedByUserId = parentUserId;
                choreLog.ApprovedAt = DateTime.UtcNow;
                break;
                
            case HelpResponse.Denied:
                // Denied - back to pending, child must do it
                choreLog.Status = ChoreStatus.Pending;
                choreLog.HelpReason = null;
                choreLog.HelpRequestedAt = null;
                break;
        }
        
        choreLog.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        
        // Reconcile ledger
        await _ledgerService.ReconcileChoreLogTransactionAsync(choreLogId);
        
        return ServiceResult.Ok();
    }

    private static TrackerChoreItem CreateTrackerItem(ChoreDefinition chore, ChoreLog? log)
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
            HelpRequestedAt = log?.HelpRequestedAt
        };
    }
}
