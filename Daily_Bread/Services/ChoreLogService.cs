using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Result object for service operations.
/// </summary>
public class ServiceResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ServiceResult Ok() => new() { Success = true };
    public static ServiceResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data) => new() { Success = true, Data = data };
    public new static ServiceResult<T> Fail(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Service for managing chore logs with role-based date restrictions.
/// </summary>
public interface IChoreLogService
{
    /// <summary>
    /// Gets or creates a chore log for the specified chore and date.
    /// </summary>
    Task<ServiceResult<ChoreLog>> GetOrCreateChoreLogAsync(int choreDefinitionId, DateOnly date);

    /// <summary>
    /// Gets all chore logs for a date.
    /// </summary>
    Task<List<ChoreLog>> GetChoreLogsForDateAsync(DateOnly date);

    /// <summary>
    /// Gets chore logs for a specific user on a date.
    /// </summary>
    Task<List<ChoreLog>> GetChoreLogsForUserOnDateAsync(string userId, DateOnly date);

    /// <summary>
    /// Updates a chore log status. Enforces role-based date restrictions.
    /// </summary>
    /// <param name="choreLogId">The chore log to update.</param>
    /// <param name="status">New status.</param>
    /// <param name="userId">User making the change.</param>
    /// <param name="isParent">Whether the user has Parent role.</param>
    /// <param name="notes">Optional notes.</param>
    Task<ServiceResult> UpdateChoreLogStatusAsync(int choreLogId, ChoreStatus status, string userId, bool isParent, string? notes = null);

    /// <summary>
    /// Marks a chore as completed. Child role can only complete today's chores.
    /// </summary>
    Task<ServiceResult> MarkChoreCompletedAsync(int choreDefinitionId, DateOnly date, string userId, bool isParent, string? notes = null);

    /// <summary>
    /// Approves a completed chore. Parent role only.
    /// </summary>
    Task<ServiceResult> ApproveChoreAsync(int choreLogId, string parentUserId);

    /// <summary>
    /// Marks a chore as missed. Parent role only.
    /// </summary>
    Task<ServiceResult> MarkChoreMissedAsync(int choreLogId, string parentUserId);
}

public class ChoreLogService : IChoreLogService
{
    private readonly ApplicationDbContext _context;
    private readonly IDateProvider _dateProvider;
    private readonly IChoreScheduleService _scheduleService;

    public ChoreLogService(
        ApplicationDbContext context,
        IDateProvider dateProvider,
        IChoreScheduleService scheduleService)
    {
        _context = context;
        _dateProvider = dateProvider;
        _scheduleService = scheduleService;
    }

    public async Task<ServiceResult<ChoreLog>> GetOrCreateChoreLogAsync(int choreDefinitionId, DateOnly date)
    {
        // Check if chore is active on this date
        if (!await _scheduleService.IsChoreActiveOnDateAsync(choreDefinitionId, date))
        {
            return ServiceResult<ChoreLog>.Fail("Chore is not scheduled for this date.");
        }

        var existingLog = await _context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .FirstOrDefaultAsync(c => c.ChoreDefinitionId == choreDefinitionId && c.Date == date);

        if (existingLog != null)
        {
            return ServiceResult<ChoreLog>.Ok(existingLog);
        }

        var choreLog = new ChoreLog
        {
            ChoreDefinitionId = choreDefinitionId,
            Date = date,
            Status = ChoreStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChoreLogs.Add(choreLog);
        await _context.SaveChangesAsync();

        // Reload with navigation properties
        await _context.Entry(choreLog).Reference(c => c.ChoreDefinition).LoadAsync();

        return ServiceResult<ChoreLog>.Ok(choreLog);
    }

    public async Task<List<ChoreLog>> GetChoreLogsForDateAsync(DateOnly date)
    {
        return await _context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Include(c => c.LedgerTransaction)
            .Where(c => c.Date == date)
            .OrderBy(c => c.ChoreDefinition.SortOrder)
            .ThenBy(c => c.ChoreDefinition.Name)
            .ToListAsync();
    }

    public async Task<List<ChoreLog>> GetChoreLogsForUserOnDateAsync(string userId, DateOnly date)
    {
        return await _context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Include(c => c.LedgerTransaction)
            .Where(c => c.Date == date)
            .Where(c => c.ChoreDefinition.AssignedUserId == userId)
            .OrderBy(c => c.ChoreDefinition.SortOrder)
            .ThenBy(c => c.ChoreDefinition.Name)
            .ToListAsync();
    }

    public async Task<ServiceResult> UpdateChoreLogStatusAsync(
        int choreLogId,
        ChoreStatus status,
        string userId,
        bool isParent,
        string? notes = null)
    {
        var choreLog = await _context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .FirstOrDefaultAsync(c => c.Id == choreLogId);

        if (choreLog == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }

        // Enforce today-only rule for Child role
        var today = _dateProvider.Today;
        if (!isParent && choreLog.Date != today)
        {
            return ServiceResult.Fail("Children can only modify today's chores.");
        }

        // Children cannot set Approved or Missed status
        if (!isParent && (status == ChoreStatus.Approved || status == ChoreStatus.Missed))
        {
            return ServiceResult.Fail("Only parents can approve chores or mark them as missed.");
        }

        choreLog.Status = status;
        choreLog.ModifiedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(notes))
        {
            choreLog.Notes = notes;
        }

        if (status == ChoreStatus.Completed)
        {
            choreLog.CompletedByUserId = userId;
            choreLog.CompletedAt = DateTime.UtcNow;
        }
        else if (status == ChoreStatus.Approved)
        {
            choreLog.ApprovedByUserId = userId;
            choreLog.ApprovedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MarkChoreCompletedAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent,
        string? notes = null)
    {
        // Enforce today-only rule for Child role
        var today = _dateProvider.Today;
        if (!isParent && date != today)
        {
            return ServiceResult.Fail("Children can only complete today's chores.");
        }

        var logResult = await GetOrCreateChoreLogAsync(choreDefinitionId, date);
        if (!logResult.Success)
        {
            return ServiceResult.Fail(logResult.ErrorMessage!);
        }

        var choreLog = logResult.Data!;

        // Don't allow re-completing already approved chores
        if (choreLog.Status == ChoreStatus.Approved)
        {
            return ServiceResult.Fail("This chore has already been approved.");
        }

        // Check if chore should be auto-approved
        var choreDefinition = await _context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (choreDefinition != null && choreDefinition.AutoApprove)
        {
            // Auto-approve: Set directly to Approved status
            choreLog.Status = ChoreStatus.Approved;
            choreLog.CompletedByUserId = userId;
            choreLog.CompletedAt = DateTime.UtcNow;
            choreLog.ApprovedByUserId = "SYSTEM"; // Mark as system-approved
            choreLog.ApprovedAt = DateTime.UtcNow;
            choreLog.ModifiedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(notes))
            {
                choreLog.Notes = notes;
            }

            await _context.SaveChangesAsync();
            return ServiceResult.Ok();
        }

        // Standard flow: mark as completed, requires parent approval
        return await UpdateChoreLogStatusAsync(choreLog.Id, ChoreStatus.Completed, userId, isParent, notes);
    }

    public async Task<ServiceResult> ApproveChoreAsync(int choreLogId, string parentUserId)
    {
        var choreLog = await _context.ChoreLogs.FindAsync(choreLogId);
        if (choreLog == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }

        if (choreLog.Status != ChoreStatus.Completed)
        {
            return ServiceResult.Fail("Only completed chores can be approved.");
        }

        return await UpdateChoreLogStatusAsync(choreLogId, ChoreStatus.Approved, parentUserId, isParent: true);
    }

    public async Task<ServiceResult> MarkChoreMissedAsync(int choreLogId, string parentUserId)
    {
        return await UpdateChoreLogStatusAsync(choreLogId, ChoreStatus.Missed, parentUserId, isParent: true);
    }
}
