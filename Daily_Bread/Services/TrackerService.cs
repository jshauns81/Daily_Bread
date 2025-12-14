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
    public decimal Value { get; init; }
    public ChoreStatus Status { get; init; }
    public string? AssignedUserId { get; init; }
    public string? AssignedUserName { get; init; }
    public string? ApprovedByUserName { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public bool IsCompleted => Status is ChoreStatus.Completed or ChoreStatus.Approved;
    public bool IsApproved => Status == ChoreStatus.Approved;
    public bool IsMissed => Status == ChoreStatus.Missed;
    public bool IsSkipped => Status == ChoreStatus.Skipped;
    public bool IsPending => Status == ChoreStatus.Pending;
}

/// <summary>
/// Service for providing tracker UI data.
/// </summary>
public interface ITrackerService
{
    /// <summary>
    /// Gets all chores for a date with their current status.
    /// Creates ChoreLog entries for scheduled chores that don't have one.
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
    /// Marks a chore as skipped. Parent only.
    /// </summary>
    Task<ServiceResult> MarkChoreSkippedAsync(int choreDefinitionId, DateOnly date, string userId);

    /// <summary>
    /// Resets a chore to pending status. Parent only.
    /// </summary>
    Task<ServiceResult> ResetChoreToPendingAsync(int choreDefinitionId, DateOnly date, string userId);
}

public class TrackerService : ITrackerService
{
    private readonly ApplicationDbContext _context;
    private readonly IChoreScheduleService _scheduleService;
    private readonly IChoreLogService _choreLogService;
    private readonly ILedgerService _ledgerService;

    public TrackerService(
        ApplicationDbContext context,
        IChoreScheduleService scheduleService,
        IChoreLogService choreLogService,
        ILedgerService ledgerService)
    {
        _context = context;
        _scheduleService = scheduleService;
        _choreLogService = choreLogService;
        _ledgerService = ledgerService;
    }

    public async Task<List<TrackerChoreItem>> GetTrackerItemsForDateAsync(DateOnly date)
    {
        // Get all scheduled chores for this date
        var scheduledChores = await _scheduleService.GetChoresForDateAsync(date);

        // Get existing logs for this date
        var existingLogs = await _context.ChoreLogs
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
                // No log exists yet - show as pending
                items.Add(CreateTrackerItem(chore, null));
            }
        }

        return items.OrderBy(i => i.ChoreName).ToList();
    }

    public async Task<List<TrackerChoreItem>> GetTrackerItemsForUserOnDateAsync(string userId, DateOnly date)
    {
        // Get scheduled chores for this user on this date
        var scheduledChores = await _scheduleService.GetChoresForUserOnDateAsync(userId, date);

        // Get existing logs for this user on this date
        var existingLogs = await _context.ChoreLogs
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
        // Get or create the chore log
        var logResult = await _choreLogService.GetOrCreateChoreLogAsync(choreDefinitionId, date);
        if (!logResult.Success)
        {
            return ServiceResult<ChoreStatus>.Fail(logResult.ErrorMessage!);
        }

        var choreLog = logResult.Data!;
        ChoreStatus newStatus;

        // Determine new status based on current status
        if (choreLog.Status == ChoreStatus.Pending)
        {
            // Mark as completed
            newStatus = ChoreStatus.Completed;
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
        else if (choreLog.Status == ChoreStatus.Approved && isParent)
        {
            // Parent can unapprove back to completed
            newStatus = ChoreStatus.Completed;
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
            isParent);

        if (!updateResult.Success)
        {
            return ServiceResult<ChoreStatus>.Fail(updateResult.ErrorMessage!);
        }

        // Reconcile ledger transaction
        await _ledgerService.ReconcileChoreLogTransactionAsync(choreLog.Id);

        return ServiceResult<ChoreStatus>.Ok(newStatus);
    }

    public async Task<ServiceResult> SetChoreStatusAsync(
        int choreDefinitionId,
        DateOnly date,
        ChoreStatus status,
        string userId)
    {
        // Get or create the chore log
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

        // Reconcile ledger transaction
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

    private static TrackerChoreItem CreateTrackerItem(ChoreDefinition chore, ChoreLog? log)
    {
        return new TrackerChoreItem
        {
            ChoreDefinitionId = chore.Id,
            ChoreLogId = log?.Id,
            ChoreName = chore.Name,
            Description = chore.Description,
            Value = chore.Value,
            Status = log?.Status ?? ChoreStatus.Pending,
            AssignedUserId = chore.AssignedUserId,
            AssignedUserName = chore.AssignedUser?.UserName,
            ApprovedByUserName = log?.ApprovedByUser?.UserName,
            ApprovedAt = log?.ApprovedAt
        };
    }
}
