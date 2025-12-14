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

        return await _context.ChoreDefinitions
            .Where(c => c.IsActive)
            .Where(c => (c.ActiveDays & dayOfWeek) == dayOfWeek)
            .Where(c => c.StartDate == null || c.StartDate <= date)
            .Where(c => c.EndDate == null || c.EndDate >= date)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<List<ChoreDefinition>> GetChoresForUserOnDateAsync(string userId, DateOnly date)
    {
        var dayOfWeek = GetDayOfWeekFlag(date.DayOfWeek);

        return await _context.ChoreDefinitions
            .Where(c => c.IsActive)
            .Where(c => c.AssignedUserId == userId)
            .Where(c => (c.ActiveDays & dayOfWeek) == dayOfWeek)
            .Where(c => c.StartDate == null || c.StartDate <= date)
            .Where(c => c.EndDate == null || c.EndDate >= date)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<bool> IsChoreActiveOnDateAsync(int choreDefinitionId, DateOnly date)
    {
        var dayOfWeek = GetDayOfWeekFlag(date.DayOfWeek);

        return await _context.ChoreDefinitions
            .Where(c => c.Id == choreDefinitionId)
            .Where(c => c.IsActive)
            .Where(c => (c.ActiveDays & dayOfWeek) == dayOfWeek)
            .Where(c => c.StartDate == null || c.StartDate <= date)
            .Where(c => c.EndDate == null || c.EndDate >= date)
            .AnyAsync();
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
