using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Service for determining which chores are active/scheduled for a given date.
/// </summary>
public interface IChoreScheduleService
{
    /// <summary>
    /// Gets all chores scheduled for the specified date.
    /// </summary>
    Task<List<ChoreDefinition>> GetChoresForDateAsync(DateOnly date);

    /// <summary>
    /// Gets chores for a specific user on the specified date.
    /// </summary>
    Task<List<ChoreDefinition>> GetChoresForUserOnDateAsync(string userId, DateOnly date);

    /// <summary>
    /// Checks if a chore is active on the specified date.
    /// </summary>
    Task<bool> IsChoreActiveOnDateAsync(int choreDefinitionId, DateOnly date);

    /// <summary>
    /// Adds a schedule override to add a chore on a specific date.
    /// </summary>
    Task<ServiceResult> AddChoreToDateAsync(int choreDefinitionId, DateOnly date, string createdByUserId, string? assignedUserId = null);

    /// <summary>
    /// Adds a schedule override to remove a chore from a specific date.
    /// </summary>
    Task<ServiceResult> RemoveChoreFromDateAsync(int choreDefinitionId, DateOnly date, string createdByUserId);

    /// <summary>
    /// Moves a chore from one date to another.
    /// </summary>
    Task<ServiceResult> MoveChoreAsync(int choreDefinitionId, DateOnly fromDate, DateOnly toDate, string createdByUserId);

    /// <summary>
    /// Gets all schedule overrides for a date range.
    /// </summary>
    Task<List<ChoreScheduleOverride>> GetOverridesForDateRangeAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Removes a schedule override.
    /// </summary>
    Task<ServiceResult> RemoveOverrideAsync(int overrideId);
}

public class ChoreScheduleService : IChoreScheduleService
{
    private readonly ApplicationDbContext _context;

    public ChoreScheduleService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ChoreDefinition>> GetChoresForDateAsync(DateOnly date)
    {
        var dayOfWeek = GetDayOfWeekFlag(date.DayOfWeek);

        // Get base scheduled chores
        var baseChores = await _context.ChoreDefinitions
            .Where(c => c.IsActive)
            .Where(c => (c.ActiveDays & dayOfWeek) == dayOfWeek)
            .Where(c => c.StartDate == null || c.StartDate <= date)
            .Where(c => c.EndDate == null || c.EndDate >= date)
            .ToListAsync();

        // Get overrides for this date
        var overrides = await _context.ChoreScheduleOverrides
            .Include(o => o.ChoreDefinition)
            .Where(o => o.Date == date)
            .ToListAsync();

        // Remove chores that have Remove overrides
        var removeOverrideChoreIds = overrides
            .Where(o => o.Type == ScheduleOverrideType.Remove)
            .Select(o => o.ChoreDefinitionId)
            .ToHashSet();

        var result = baseChores
            .Where(c => !removeOverrideChoreIds.Contains(c.Id))
            .ToList();

        // Add chores that have Add overrides (and aren't already in the list)
        var addOverrides = overrides
            .Where(o => o.Type == ScheduleOverrideType.Add || o.Type == ScheduleOverrideType.Move)
            .Where(o => o.ChoreDefinition.IsActive)
            .ToList();

        foreach (var addOverride in addOverrides)
        {
            if (!result.Any(c => c.Id == addOverride.ChoreDefinitionId))
            {
                result.Add(addOverride.ChoreDefinition);
            }
        }

        return result.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList();
    }

    public async Task<List<ChoreDefinition>> GetChoresForUserOnDateAsync(string userId, DateOnly date)
    {
        var allChores = await GetChoresForDateAsync(date);
        return allChores.Where(c => c.AssignedUserId == userId).ToList();
    }

    public async Task<bool> IsChoreActiveOnDateAsync(int choreDefinitionId, DateOnly date)
    {
        var chores = await GetChoresForDateAsync(date);
        return chores.Any(c => c.Id == choreDefinitionId);
    }

    public async Task<ServiceResult> AddChoreToDateAsync(int choreDefinitionId, DateOnly date, string createdByUserId, string? assignedUserId = null)
    {
        var chore = await _context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (chore == null)
        {
            return ServiceResult.Fail("Chore not found.");
        }

        // Check if override already exists
        var existingOverride = await _context.ChoreScheduleOverrides
            .FirstOrDefaultAsync(o => o.ChoreDefinitionId == choreDefinitionId && o.Date == date);

        if (existingOverride != null)
        {
            // Update existing override
            existingOverride.Type = ScheduleOverrideType.Add;
            existingOverride.OverrideAssignedUserId = assignedUserId;
            existingOverride.CreatedByUserId = createdByUserId;
            existingOverride.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new override
            var newOverride = new ChoreScheduleOverride
            {
                ChoreDefinitionId = choreDefinitionId,
                Date = date,
                Type = ScheduleOverrideType.Add,
                OverrideAssignedUserId = assignedUserId,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChoreScheduleOverrides.Add(newOverride);
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveChoreFromDateAsync(int choreDefinitionId, DateOnly date, string createdByUserId)
    {
        // Check if override already exists
        var existingOverride = await _context.ChoreScheduleOverrides
            .FirstOrDefaultAsync(o => o.ChoreDefinitionId == choreDefinitionId && o.Date == date);

        if (existingOverride != null)
        {
            if (existingOverride.Type == ScheduleOverrideType.Add)
            {
                // If it was an Add override, just remove it entirely
                _context.ChoreScheduleOverrides.Remove(existingOverride);
            }
            else
            {
                // Update to Remove
                existingOverride.Type = ScheduleOverrideType.Remove;
                existingOverride.CreatedByUserId = createdByUserId;
                existingOverride.CreatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // Create new Remove override
            var newOverride = new ChoreScheduleOverride
            {
                ChoreDefinitionId = choreDefinitionId,
                Date = date,
                Type = ScheduleOverrideType.Remove,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChoreScheduleOverrides.Add(newOverride);
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MoveChoreAsync(int choreDefinitionId, DateOnly fromDate, DateOnly toDate, string createdByUserId)
    {
        if (fromDate == toDate)
        {
            return ServiceResult.Fail("Cannot move to the same date.");
        }

        // Create Remove override for source date
        await RemoveChoreFromDateAsync(choreDefinitionId, fromDate, createdByUserId);

        // Create Add/Move override for target date
        var existingOverride = await _context.ChoreScheduleOverrides
            .FirstOrDefaultAsync(o => o.ChoreDefinitionId == choreDefinitionId && o.Date == toDate);

        if (existingOverride != null)
        {
            existingOverride.Type = ScheduleOverrideType.Move;
            existingOverride.CreatedByUserId = createdByUserId;
            existingOverride.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            var newOverride = new ChoreScheduleOverride
            {
                ChoreDefinitionId = choreDefinitionId,
                Date = toDate,
                Type = ScheduleOverrideType.Move,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChoreScheduleOverrides.Add(newOverride);
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<ChoreScheduleOverride>> GetOverridesForDateRangeAsync(DateOnly startDate, DateOnly endDate)
    {
        return await _context.ChoreScheduleOverrides
            .Include(o => o.ChoreDefinition)
            .Include(o => o.OverrideAssignedUser)
            .Where(o => o.Date >= startDate && o.Date <= endDate)
            .OrderBy(o => o.Date)
            .ToListAsync();
    }

    public async Task<ServiceResult> RemoveOverrideAsync(int overrideId)
    {
        var scheduleOverride = await _context.ChoreScheduleOverrides.FindAsync(overrideId);
        if (scheduleOverride == null)
        {
            return ServiceResult.Fail("Override not found.");
        }

        _context.ChoreScheduleOverrides.Remove(scheduleOverride);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Converts System.DayOfWeek to our DaysOfWeek flags enum.
    /// </summary>
    private static DaysOfWeek GetDayOfWeekFlag(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Sunday => DaysOfWeek.Sunday,
        DayOfWeek.Monday => DaysOfWeek.Monday,
        DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
        DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
        DayOfWeek.Thursday => DaysOfWeek.Thursday,
        DayOfWeek.Friday => DaysOfWeek.Friday,
        DayOfWeek.Saturday => DaysOfWeek.Saturday,
        _ => DaysOfWeek.None
    };
}
