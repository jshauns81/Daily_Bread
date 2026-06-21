using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

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

public interface IChoreLogService
{
    Task<ServiceResult<ChoreLog>> GetOrCreateChoreLogAsync(int choreDefinitionId, DateOnly date);
    Task<ServiceResult<ChoreLog>> CreateWeeklyCompletionAsync(int choreDefinitionId, DateOnly date);
    Task<List<ChoreLog>> GetChoreLogsForDateAsync(DateOnly date);
    Task<List<ChoreLog>> GetChoreLogsForUserOnDateAsync(string userId, DateOnly date);
    Task<ServiceResult> UpdateChoreLogStatusAsync(int choreLogId, ChoreStatus status, string userId, bool isParent, string? notes = null);
    Task<ServiceResult> MarkChoreCompletedAsync(int choreDefinitionId, DateOnly date, string userId, bool isParent, string? notes = null);
    Task<ServiceResult> ApproveChoreAsync(int choreLogId, string parentUserId);
    Task<ServiceResult> MarkChoreMissedAsync(int choreLogId, string parentUserId);
    Task<List<ChoreLog>> GetChoreLogsForUserInWeekAsync(string userId, DateOnly anyDateInWeek);
    Task<List<WeeklyChoreWithLogs>> GetWeeklyChoresWithLogsAsync(string userId, DateOnly anyDateInWeek);
}

public class WeeklyChoreWithLogs
{
    public ChoreDefinition ChoreDefinition { get; set; } = null!;
    public int TargetCount { get; set; }
    public int CompletedCount { get; set; }
    public int ApprovedCount { get; set; }
    public List<ChoreLog> LogsThisWeek { get; set; } = [];
    public bool IsTargetMet => ApprovedCount >= TargetCount;
    public int RemainingCount => Math.Max(0, TargetCount - ApprovedCount);
}

public class ChoreLogService : IChoreLogService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IChoreScheduleService _scheduleService;

    public ChoreLogService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider,
        IChoreScheduleService scheduleService)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _scheduleService = scheduleService;
    }

    public async Task<ServiceResult<ChoreLog>> GetOrCreateChoreLogAsync(int choreDefinitionId, DateOnly date)
    {
        if (!await _scheduleService.IsChoreActiveOnDateAsync(choreDefinitionId, date))
        {
            return ServiceResult<ChoreLog>.Fail("Chore is not scheduled for this date.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Weekly chores can have more than one row for the same (chore, date) - one per
        // completion. When several exist, prefer an open Pending row; otherwise fall back
        // to the most recently created row. For SpecificDays chores there is always at most
        // one row, so this ordering is a no-op there.
        var existingLog = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Where(c => c.ChoreDefinitionId == choreDefinitionId && c.Date == date)
            .OrderBy(c => c.Status == ChoreStatus.Pending ? 0 : 1)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync();

        if (existingLog != null)
        {
            return ServiceResult<ChoreLog>.Ok(existingLog);
        }

        var choreDefinition = await context.ChoreDefinitions.FindAsync(choreDefinitionId);

        var choreLog = new ChoreLog
        {
            ChoreDefinitionId = choreDefinitionId,
            Date = date,
            Status = ChoreStatus.Pending,
            AllowsMultiplePerDay = choreDefinition?.ScheduleType == ChoreScheduleType.WeeklyFrequency,
            CreatedAt = DateTime.UtcNow
        };

        context.ChoreLogs.Add(choreLog);
        await context.SaveChangesAsync();

        await context.Entry(choreLog).Reference(c => c.ChoreDefinition).LoadAsync();

        return ServiceResult<ChoreLog>.Ok(choreLog);
    }

    /// <summary>
    /// Unconditionally inserts a fresh ChoreLog row for a WeeklyFrequency chore, regardless of
    /// whether other rows already exist for that (chore, date). This is the "log another
    /// completion" path - use GetOrCreateChoreLogAsync to find/reuse an existing open row instead.
    /// </summary>
    public async Task<ServiceResult<ChoreLog>> CreateWeeklyCompletionAsync(int choreDefinitionId, DateOnly date)
    {
        if (!await _scheduleService.IsChoreActiveOnDateAsync(choreDefinitionId, date))
        {
            return ServiceResult<ChoreLog>.Fail("Chore is not scheduled for this date.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        var choreDefinition = await context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (choreDefinition == null)
        {
            return ServiceResult<ChoreLog>.Fail("Chore not found.");
        }

        if (choreDefinition.ScheduleType != ChoreScheduleType.WeeklyFrequency)
        {
            return ServiceResult<ChoreLog>.Fail("CreateWeeklyCompletionAsync is only valid for WeeklyFrequency chores.");
        }

        var choreLog = new ChoreLog
        {
            ChoreDefinitionId = choreDefinitionId,
            Date = date,
            Status = ChoreStatus.Pending,
            AllowsMultiplePerDay = true,
            CreatedAt = DateTime.UtcNow
        };

        context.ChoreLogs.Add(choreLog);
        await context.SaveChangesAsync();

        await context.Entry(choreLog).Reference(c => c.ChoreDefinition).LoadAsync();

        return ServiceResult<ChoreLog>.Ok(choreLog);
    }

    public async Task<List<ChoreLog>> GetChoreLogsForDateAsync(DateOnly date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Include(c => c.LedgerTransaction)
            .Where(c => c.Date == date)
            .OrderBy(c => c.ChoreDefinition.SortOrder)
            .ThenBy(c => c.ChoreDefinition.Name)
            .ToListAsync();
    }

    public async Task<List<ChoreLog>> GetChoreLogsForUserOnDateAsync(string userId, DateOnly date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.ChoreLogs
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
        await using var context = await _contextFactory.CreateDbContextAsync();

        var choreLog = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .FirstOrDefaultAsync(c => c.Id == choreLogId);

        if (choreLog == null)
        {
            return ServiceResult.Fail("Chore log not found.");
        }

        var today = _dateProvider.Today;
        if (!isParent && choreLog.Date != today)
        {
            return ServiceResult.Fail("Children can only modify today's chores.");
        }

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

        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MarkChoreCompletedAsync(
        int choreDefinitionId,
        DateOnly date,
        string userId,
        bool isParent,
        string? notes = null)
    {
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

        if (choreLog.Status == ChoreStatus.Approved)
        {
            // Weekly chores: an already-approved row for today doesn't block another
            // completion - insert a new row instead of failing. Daily chores keep the
            // existing single-row "already approved" guard.
            if (choreLog.ChoreDefinition?.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            {
                var newLogResult = await CreateWeeklyCompletionAsync(choreDefinitionId, date);
                if (!newLogResult.Success)
                {
                    return ServiceResult.Fail(newLogResult.ErrorMessage!);
                }
                choreLog = newLogResult.Data!;
            }
            else
            {
                return ServiceResult.Fail("This chore has already been approved.");
            }
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var choreDefinition = await context.ChoreDefinitions.FindAsync(choreDefinitionId);

        if (choreDefinition != null && choreDefinition.AutoApprove)
        {
            var logToUpdate = await context.ChoreLogs.FindAsync(choreLog.Id);
            if (logToUpdate != null)
            {
                logToUpdate.Status = ChoreStatus.Approved;
                logToUpdate.CompletedByUserId = userId;
                logToUpdate.CompletedAt = DateTime.UtcNow;
                logToUpdate.ApprovedByUserId = "SYSTEM";
                logToUpdate.ApprovedAt = DateTime.UtcNow;
                logToUpdate.ModifiedAt = DateTime.UtcNow;

                if (!string.IsNullOrWhiteSpace(notes))
                {
                    logToUpdate.Notes = notes;
                }

                await context.SaveChangesAsync();
            }
            return ServiceResult.Ok();
        }

        return await UpdateChoreLogStatusAsync(choreLog.Id, ChoreStatus.Completed, userId, isParent, notes);
    }

    public async Task<ServiceResult> ApproveChoreAsync(int choreLogId, string parentUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var choreLog = await context.ChoreLogs.FindAsync(choreLogId);
        
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

    // NOTE: as of this writing, neither GetChoreLogsForUserInWeekAsync nor
    // GetWeeklyChoresWithLogsAsync below has any callers anywhere in the codebase - they're
    // unused. Both compute week boundaries via IChoreScheduleService.GetWeekStartDate/
    // GetWeekEndDate (the hardcoded-Sunday-start ChoreScheduleHelper), while the live weekly
    // progress/earnings paths (WeeklyProgressService, LedgerService) use
    // IFamilySettingsService.GetWeekStartForDateAsync/GetWeekEndForDateAsync (the
    // DB-configurable WeekStartDay setting). These two definitions can disagree. It's harmless
    // today only because these methods are dead code. If either is ever given a caller, switch
    // it to IFamilySettingsService first, or it resurrects a second week-boundary definition
    // that silently disagrees with the one everything else uses.
    public async Task<List<ChoreLog>> GetChoreLogsForUserInWeekAsync(string userId, DateOnly anyDateInWeek)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var weekStart = _scheduleService.GetWeekStartDate(anyDateInWeek);
        var weekEnd = _scheduleService.GetWeekEndDate(anyDateInWeek);

        return await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Include(c => c.LedgerTransaction)
            .Where(c => c.Date >= weekStart && c.Date <= weekEnd)
            .Where(c => c.ChoreDefinition.AssignedUserId == userId)
            .Where(c => c.ChoreDefinition.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .OrderBy(c => c.Date)
            .ThenBy(c => c.ChoreDefinition.SortOrder)
            .ThenBy(c => c.ChoreDefinition.Name)
            .ToListAsync();
    }

    // See the week-boundary note above GetChoreLogsForUserInWeekAsync - applies here too.
    public async Task<List<WeeklyChoreWithLogs>> GetWeeklyChoresWithLogsAsync(string userId, DateOnly anyDateInWeek)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var weekStart = _scheduleService.GetWeekStartDate(anyDateInWeek);
        var weekEnd = _scheduleService.GetWeekEndDate(anyDateInWeek);

        var weeklyChores = await context.ChoreDefinitions
            .Where(c => c.IsActive)
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .Where(c => c.AssignedUserId == userId)
            .Where(c => c.StartDate == null || c.StartDate <= weekEnd)
            .Where(c => c.EndDate == null || c.EndDate >= weekStart)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        if (weeklyChores.Count == 0)
        {
            return [];
        }

        var choreIds = weeklyChores.Select(c => c.Id).ToList();

        var logs = await context.ChoreLogs
            .Include(c => c.ChoreDefinition)
            .Include(c => c.LedgerTransaction)
            .Where(cl => choreIds.Contains(cl.ChoreDefinitionId))
            .Where(cl => cl.Date >= weekStart && cl.Date <= weekEnd)
            .OrderBy(cl => cl.Date)
            .ToListAsync();

        var logsByChore = logs.GroupBy(l => l.ChoreDefinitionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return weeklyChores.Select(chore =>
        {
            var choreLogs = logsByChore.GetValueOrDefault(chore.Id, []);
            return new WeeklyChoreWithLogs
            {
                ChoreDefinition = chore,
                TargetCount = chore.WeeklyTargetCount,
                CompletedCount = choreLogs.Count(l => l.Status == ChoreStatus.Completed || l.Status == ChoreStatus.Approved),
                ApprovedCount = choreLogs.Count(l => l.Status == ChoreStatus.Approved),
                LogsThisWeek = choreLogs
            };
        }).ToList();
    }
}
