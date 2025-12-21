using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Abstraction for getting the current date/time.
/// Enables consistent "today" determination and testability.
/// </summary>
public interface IDateProvider
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets today's date in the configured family timezone.
    /// </summary>
    DateOnly Today { get; }

    /// <summary>
    /// Gets the current date and time in the configured family timezone.
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// Gets today's date in the specified timezone.
    /// </summary>
    DateOnly GetTodayInTimezone(string timezoneId);

    /// <summary>
    /// Gets the configured timezone ID.
    /// </summary>
    string TimeZoneId { get; }

    /// <summary>
    /// Gets user-friendly timezone display name.
    /// </summary>
    string TimeZoneDisplayName { get; }

    /// <summary>
    /// Refreshes the cached timezone setting from the database.
    /// Call this after changing the timezone setting.
    /// </summary>
    Task RefreshTimeZoneAsync();
}

/// <summary>
/// Default implementation using system clock with configurable timezone.
/// </summary>
public class SystemDateProvider : IDateProvider
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private string _timeZoneId = AppSettingKeys.DefaultTimeZone;
    private TimeZoneInfo _timeZone;
    private bool _initialized;

    public SystemDateProvider(IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
        // Initialize with default timezone, will be updated on first access
        _timeZone = GetTimeZoneInfo(AppSettingKeys.DefaultTimeZone);
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today
    {
        get
        {
            EnsureInitialized();
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
            return DateOnly.FromDateTime(localTime);
        }
    }

    public DateTime Now
    {
        get
        {
            EnsureInitialized();
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        }
    }

    public string TimeZoneId
    {
        get
        {
            EnsureInitialized();
            return _timeZoneId;
        }
    }

    public string TimeZoneDisplayName
    {
        get
        {
            EnsureInitialized();
            return _timeZone.DisplayName;
        }
    }

    public DateOnly GetTodayInTimezone(string timezoneId)
    {
        try
        {
            var timezone = GetTimeZoneInfo(timezoneId);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
            return DateOnly.FromDateTime(localTime);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fall back to configured timezone if specified timezone not found
            return Today;
        }
    }

    public async Task RefreshTimeZoneAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == AppSettingKeys.TimeZone);

        var timeZoneId = setting?.Value ?? AppSettingKeys.DefaultTimeZone;
        _timeZone = GetTimeZoneInfo(timeZoneId);
        _timeZoneId = timeZoneId;
        _initialized = true;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            // Synchronously load timezone on first access
            // This is a compromise - ideally we'd initialize async at startup
            Task.Run(async () => await RefreshTimeZoneAsync()).GetAwaiter().GetResult();
        }
    }

    private static TimeZoneInfo GetTimeZoneInfo(string timeZoneId)
    {
        try
        {
            // Try IANA timezone ID first (cross-platform)
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                // Try Windows timezone ID as fallback
                return TimeZoneInfo.FindSystemTimeZoneById(ConvertIanaToWindows(timeZoneId));
            }
            catch
            {
                // Fall back to UTC if nothing works
                return TimeZoneInfo.Utc;
            }
        }
    }

    /// <summary>
    /// Maps common IANA timezone IDs to Windows equivalents.
    /// </summary>
    private static string ConvertIanaToWindows(string ianaId)
    {
        return ianaId switch
        {
            "America/New_York" => "Eastern Standard Time",
            "America/Chicago" => "Central Standard Time",
            "America/Denver" => "Mountain Standard Time",
            "America/Los_Angeles" => "Pacific Standard Time",
            "America/Phoenix" => "US Mountain Standard Time",
            "America/Anchorage" => "Alaskan Standard Time",
            "Pacific/Honolulu" => "Hawaiian Standard Time",
            "Europe/London" => "GMT Standard Time",
            "Europe/Paris" => "Romance Standard Time",
            "Europe/Berlin" => "W. Europe Standard Time",
            "Asia/Tokyo" => "Tokyo Standard Time",
            "Australia/Sydney" => "AUS Eastern Standard Time",
            _ => ianaId
        };
    }
}

/// <summary>
/// Common US timezone options for the settings UI.
/// </summary>
public static class TimeZoneOptions
{
    public static readonly List<(string Id, string DisplayName)> USTimeZones =
    [
        ("America/New_York", "Eastern Time (ET)"),
        ("America/Chicago", "Central Time (CT)"),
        ("America/Denver", "Mountain Time (MT)"),
        ("America/Los_Angeles", "Pacific Time (PT)"),
        ("America/Phoenix", "Arizona (no DST)"),
        ("America/Anchorage", "Alaska Time (AKT)"),
        ("Pacific/Honolulu", "Hawaii Time (HT)")
    ];
}
