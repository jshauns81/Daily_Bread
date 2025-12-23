using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Service for managing family-wide settings.
/// </summary>
public interface IFamilySettingsService
{
    /// <summary>
    /// Gets the current family settings. Creates default settings if none exist.
    /// </summary>
    Task<FamilySettings> GetSettingsAsync();
    
    /// <summary>
    /// Updates the family settings.
    /// </summary>
    Task<ServiceResult> UpdateSettingsAsync(FamilySettings settings);
    
    /// <summary>
    /// Gets the start date of the current week based on WeekStartDay setting.
    /// </summary>
    Task<DateOnly> GetCurrentWeekStartAsync();
    
    /// <summary>
    /// Gets the end date of the current week (day before WeekStartDay).
    /// </summary>
    Task<DateOnly> GetCurrentWeekEndAsync();
    
    /// <summary>
    /// Gets the week start date for a given date.
    /// </summary>
    Task<DateOnly> GetWeekStartForDateAsync(DateOnly date);
    
    /// <summary>
    /// Gets the week end date for a given date.
    /// </summary>
    Task<DateOnly> GetWeekEndForDateAsync(DateOnly date);
}

public class FamilySettingsService : IFamilySettingsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    
    // Cache to avoid repeated DB calls for settings
    private FamilySettings? _cachedSettings;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public FamilySettingsService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
    }

    public async Task<FamilySettings> GetSettingsAsync()
    {
        // Return cached settings if still valid
        if (_cachedSettings != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedSettings;
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var settings = await context.FamilySettings.FirstOrDefaultAsync();
        
        if (settings == null)
        {
            // Create default settings
            settings = new FamilySettings();
            context.FamilySettings.Add(settings);
            await context.SaveChangesAsync();
        }
        
        // Update cache
        _cachedSettings = settings;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
        
        return settings;
    }

    public async Task<ServiceResult> UpdateSettingsAsync(FamilySettings settings)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var existing = await context.FamilySettings.FirstOrDefaultAsync();
        
        if (existing == null)
        {
            context.FamilySettings.Add(settings);
        }
        else
        {
            existing.DailyExpectationPenalty = settings.DailyExpectationPenalty;
            existing.WeeklyIncompletePenaltyPercent = settings.WeeklyIncompletePenaltyPercent;
            existing.WeekStartDay = settings.WeekStartDay;
            existing.CashOutThreshold = settings.CashOutThreshold;
            existing.EnableConfetti = settings.EnableConfetti;
            existing.EnableStreaks = settings.EnableStreaks;
            existing.ModifiedAt = DateTime.UtcNow;
        }
        
        await context.SaveChangesAsync();
        
        // Invalidate cache
        _cachedSettings = null;
        _cacheExpiry = DateTime.MinValue;
        
        return ServiceResult.Ok();
    }

    public async Task<DateOnly> GetCurrentWeekStartAsync()
    {
        return await GetWeekStartForDateAsync(_dateProvider.Today);
    }

    public async Task<DateOnly> GetCurrentWeekEndAsync()
    {
        return await GetWeekEndForDateAsync(_dateProvider.Today);
    }

    public async Task<DateOnly> GetWeekStartForDateAsync(DateOnly date)
    {
        var settings = await GetSettingsAsync();
        var weekStartDay = settings.WeekStartDay;
        
        // Calculate days to subtract to get to the start of the week
        var currentDayOfWeek = (int)date.DayOfWeek;
        var targetDayOfWeek = (int)weekStartDay;
        
        var daysToSubtract = (currentDayOfWeek - targetDayOfWeek + 7) % 7;
        
        return date.AddDays(-daysToSubtract);
    }

    public async Task<DateOnly> GetWeekEndForDateAsync(DateOnly date)
    {
        var weekStart = await GetWeekStartForDateAsync(date);
        return weekStart.AddDays(6);
    }
}
