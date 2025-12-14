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
    /// Gets today's date in UTC.
    /// </summary>
    DateOnly Today { get; }

    /// <summary>
    /// Gets today's date in the specified timezone.
    /// </summary>
    DateOnly GetTodayInTimezone(string timezoneId);
}

/// <summary>
/// Default implementation using system clock.
/// </summary>
public class SystemDateProvider : IDateProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public DateOnly GetTodayInTimezone(string timezoneId)
    {
        try
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
            return DateOnly.FromDateTime(localTime);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fall back to UTC if timezone not found
            return Today;
        }
    }
}
