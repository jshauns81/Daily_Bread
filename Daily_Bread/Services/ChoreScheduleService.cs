using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

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
    /// Gets the week start date (Sunday) for a given date.
    /// </summary>
    DateOnly GetWeekStartDate(DateOnly date);

    /// <summary>
    /// Gets the week end date (Saturday) for a given date.
    /// </summary>
    DateOnly GetWeekEndDate(DateOnly date);

    /// <summary>
    /// Gets the completion count for a weekly frequency chore in the specified week.
    /// </summary>
    Task<int> GetWeeklyCompletionCountAsync(int choreDefinitionId, DateOnly anyDateInWeek);

    /// <summary>
    /// Gets weekly progress for all weekly frequency chores for a user in the specified week.
    /// </summary>
    Task<Dictionary<int, ChoreScheduleWeeklyProgress>> GetWeeklyProgressForUserAsync(string userId, DateOnly anyDateInWeek);

    /// <summary>
    /// Checks if a weekly frequency chore has met its target for the week.
    /// </summary>
    Task<bool> IsWeeklyTargetMetAsync(int choreDefinitionId, DateOnly anyDateInWeek);

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
    
    /// <summary>
    /// Invalidates the cached ChoreDefinitions.
    /// Call this when chore definitions are created, updated, or deleted.
    /// </summary>
    void InvalidateChoreDefinitionsCache();
}

/// <summary>
/// Represents the weekly progress for a frequency-based chore.
/// NOTE: Use Services.WeeklyChoreProgress from WeeklyProgressService for the full implementation.
/// This is a legacy wrapper for backward compatibility.
/// </summary>
public class ChoreScheduleWeeklyProgress
{
    public int ChoreDefinitionId { get; set; }
    public string ChoreName { get; set; } = string.Empty;
    public int TargetCount { get; set; }
    public int CompletedCount { get; set; }
    public int ApprovedCount { get; set; }
    public bool IsTargetMet => ApprovedCount >= TargetCount;
    public int RemainingCount => Math.Max(0, TargetCount - ApprovedCount);
}

public class ChoreScheduleService : IChoreScheduleService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ChoreScheduleService> _logger;
    
    // Cache keys and settings
    private const string ActiveChoresCacheKey = "ChoreDefinitions_Active";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public ChoreScheduleService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IFamilySettingsService familySettingsService,
        IMemoryCache cache,
        ILogger<ChoreScheduleService> logger)
    {
        _contextFactory = contextFactory;
        _familySettingsService = familySettingsService;
        _cache = cache;
        _logger = logger;
    }

    public DateOnly GetWeekStartDate(DateOnly date)
    {
        // Synchronous helper - uses default Monday start
        // For configurable week start, use async method via FamilySettingsService
        return ChoreScheduleHelper.GetWeekStartDate(date);
    }

    public DateOnly GetWeekEndDate(DateOnly date)
    {
        // Synchronous helper - uses default Monday start
        return ChoreScheduleHelper.GetWeekEndDate(date);
    }
    
    /// <summary>
    /// Gets all active ChoreDefinitions from cache, or loads them from the database.
    /// ChoreDefinitions rarely change, so we cache them for 5 minutes.
    /// </summary>
    private async Task<List<ChoreDefinition>> GetCachedActiveChoreDefinitionsAsync()
    {
        if (_cache.TryGetValue(ActiveChoresCacheKey, out List<ChoreDefinition>? cachedChores) && cachedChores != null)
        {
            _logger.LogDebug("ChoreDefinitions cache HIT - {Count} chores", cachedChores.Count);
            return cachedChores;
        }
        
        _logger.LogDebug("ChoreDefinitions cache MISS - loading from database");
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chores = await context.ChoreDefinitions
            .Include(c => c.AssignedUser)
            .Where(c => c.IsActive)
            .ToListAsync();
        
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1); // Size for cache limiting if needed
        
        _cache.Set(ActiveChoresCacheKey, chores, cacheOptions);
        
        _logger.LogInformation("ChoreDefinitions cached - {Count} chores for {Duration} minutes", 
            chores.Count, CacheDuration.TotalMinutes);
        
        return chores;
    }
    
    /// <summary>
    /// Invalidates the cached ChoreDefinitions.
    /// </summary>
    public void InvalidateChoreDefinitionsCache()
    {
        _cache.Remove(ActiveChoresCacheKey);
        _logger.LogInformation("ChoreDefinitions cache invalidated");
    }

    public async Task<List<ChoreDefinition>> GetChoresForDateAsync(DateOnly date)
    {
        // Get cached active chores
        var allActiveChores = await GetCachedActiveChoreDefinitionsAsync();
        
        var dayOfWeek = ChoreScheduleHelper.GetDayOfWeekFlag(date.DayOfWeek);

        // Filter in memory for chores scheduled on this day
        var baseChores = allActiveChores
            .Where(c => (c.ActiveDays & dayOfWeek) == dayOfWeek)
            .Where(c => c.StartDate == null || c.StartDate <= date)
            .Where(c => c.EndDate == null || c.EndDate >= date)
            .ToList();

        // Get overrides for this date (these change frequently, so no caching)
        await using var context = await _contextFactory.CreateDbContextAsync();
        var overrides = await context.ChoreScheduleOverrides
            .Include(o => o.ChoreDefinition)
                .ThenInclude(cd => cd.AssignedUser)
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

    public async Task<int> GetWeeklyCompletionCountAsync(int choreDefinitionId, DateOnly anyDateInWeek)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var weekStart = GetWeekStartDate(anyDateInWeek);
        var weekEnd = GetWeekEndDate(anyDateInWeek);

        return await context.ChoreLogs
            .Where(cl => cl.ChoreDefinitionId == choreDefinitionId)
            .Where(cl => cl.Date >= weekStart && cl.Date <= weekEnd)
            .Where(cl => cl.Status == ChoreStatus.Approved)
            .CountAsync();
    }

    public async Task<Dictionary<int, ChoreScheduleWeeklyProgress>> GetWeeklyProgressForUserAsync(string userId, DateOnly anyDateInWeek)
    {
        var weekStart = GetWeekStartDate(anyDateInWeek);
        var weekEnd = GetWeekEndDate(anyDateInWeek);

        // Get weekly frequency chores from cache
        var allActiveChores = await GetCachedActiveChoreDefinitionsAsync();
        var weeklyChores = allActiveChores
            .Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency)
            .Where(c => c.AssignedUserId == userId)
            .Where(c => c.StartDate == null || c.StartDate <= weekEnd)
            .Where(c => c.EndDate == null || c.EndDate >= weekStart)
            .ToList();

        if (weeklyChores.Count == 0)
        {
            return new Dictionary<int, ChoreScheduleWeeklyProgress>();
        }

        var choreIds = weeklyChores.Select(c => c.Id).ToList();

        // Get completion counts for the week (ChoreLogs are not cached - they change frequently)
        await using var context = await _contextFactory.CreateDbContextAsync();
        var completionCounts = await context.ChoreLogs
            .Where(cl => choreIds.Contains(cl.ChoreDefinitionId))
            .Where(cl => cl.Date >= weekStart && cl.Date <= weekEnd)
            .GroupBy(cl => cl.ChoreDefinitionId)
            .Select(g => new
            {
                ChoreDefinitionId = g.Key,
                CompletedCount = g.Count(cl => cl.Status == ChoreStatus.Completed || cl.Status == ChoreStatus.Approved),
                ApprovedCount = g.Count(cl => cl.Status == ChoreStatus.Approved)
            })
            .ToDictionaryAsync(x => x.ChoreDefinitionId);

        var result = new Dictionary<int, ChoreScheduleWeeklyProgress>();

        foreach (var chore in weeklyChores)
        {
            var progress = new ChoreScheduleWeeklyProgress
            {
                ChoreDefinitionId = chore.Id,
                ChoreName = chore.Name,
                TargetCount = chore.WeeklyTargetCount
            };

            if (completionCounts.TryGetValue(chore.Id, out var counts))
            {
                progress.CompletedCount = counts.CompletedCount;
                progress.ApprovedCount = counts.ApprovedCount;
            }

            result[chore.Id] = progress;
        }

        return result;
    }

    public async Task<bool> IsWeeklyTargetMetAsync(int choreDefinitionId, DateOnly anyDateInWeek)
    {
        // Get chore from cache
        var allActiveChores = await GetCachedActiveChoreDefinitionsAsync();
        var chore = allActiveChores.FirstOrDefault(c => c.Id == choreDefinitionId);
        
        if (chore == null || chore.ScheduleType != ChoreScheduleType.WeeklyFrequency)
        {
            return false;
        }

        var completedCount = await GetWeeklyCompletionCountAsync(choreDefinitionId, anyDateInWeek);
        return completedCount >= chore.WeeklyTargetCount;
    }

    public async Task<ServiceResult> AddChoreToDateAsync(int choreDefinitionId, DateOnly date, string createdByUserId, string? assignedUserId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chore = await context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (chore == null)
        {
            return ServiceResult.Fail("Chore not found.");
        }

        // Check if override already exists
        var existingOverride = await context.ChoreScheduleOverrides
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
            context.ChoreScheduleOverrides.Add(newOverride);
        }

        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveChoreFromDateAsync(int choreDefinitionId, DateOnly date, string createdByUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Check if override already exists
        var existingOverride = await context.ChoreScheduleOverrides
            .FirstOrDefaultAsync(o => o.ChoreDefinitionId == choreDefinitionId && o.Date == date);

        if (existingOverride != null)
        {
            if (existingOverride.Type == ScheduleOverrideType.Add)
            {
                // If it was an Add override, just remove it entirely
                context.ChoreScheduleOverrides.Remove(existingOverride);
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
            context.ChoreScheduleOverrides.Add(newOverride);
        }

        await context.SaveChangesAsync();
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

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Create Add/Move override for target date
        var existingOverride = await context.ChoreScheduleOverrides
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
            context.ChoreScheduleOverrides.Add(newOverride);
        }

        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<ChoreScheduleOverride>> GetOverridesForDateRangeAsync(DateOnly startDate, DateOnly endDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.ChoreScheduleOverrides
            .Include(o => o.ChoreDefinition)
            .Include(o => o.OverrideAssignedUser)
            .Where(o => o.Date >= startDate && o.Date <= endDate)
            .OrderBy(o => o.Date)
            .ToListAsync();
    }

    public async Task<ServiceResult> RemoveOverrideAsync(int overrideId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var scheduleOverride = await context.ChoreScheduleOverrides.FindAsync(overrideId);
        if (scheduleOverride == null)
        {
            return ServiceResult.Fail("Override not found.");
        }

        context.ChoreScheduleOverrides.Remove(scheduleOverride);
        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }
}
