using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Completion status for a single day.
/// </summary>
public enum DayCompletionStatus
{
    /// <summary>No chores scheduled for this day.</summary>
    NoChores,
    /// <summary>All chores completed/approved.</summary>
    AllComplete,
    /// <summary>Some chores completed, some pending/missed.</summary>
    PartialComplete,
    /// <summary>No chores completed.</summary>
    NoneComplete,
    /// <summary>Day is in the future.</summary>
    Future
}

/// <summary>
/// Summary of a single day for calendar display.
/// </summary>
public class DaySummary
{
    public DateOnly Date { get; init; }
    public DayCompletionStatus Status { get; init; }
    public int TotalChores { get; init; }
    public int CompletedChores { get; init; }
    public int ApprovedChores { get; init; }
    public int MissedChores { get; init; }
    public int PendingChores { get; init; }
    public decimal EarnedAmount { get; init; }
    public decimal PotentialAmount { get; init; }
    
    /// <summary>
    /// Percentage of chores completed (0-100).
    /// </summary>
    public int CompletionPercentage => TotalChores > 0 
        ? (int)Math.Round((CompletedChores + ApprovedChores) * 100.0 / TotalChores) 
        : 0;
}

/// <summary>
/// Monthly summary for calendar display.
/// </summary>
public class MonthSummary
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM");
    public List<DaySummary> Days { get; init; } = [];
    
    // Aggregate stats
    public int TotalChores => Days.Sum(d => d.TotalChores);
    public int TotalCompleted => Days.Sum(d => d.CompletedChores + d.ApprovedChores);
    public int TotalMissed => Days.Sum(d => d.MissedChores);
    public decimal TotalEarned => Days.Sum(d => d.EarnedAmount);
    public int DaysWithAllComplete => Days.Count(d => d.Status == DayCompletionStatus.AllComplete);
    public int CurrentStreak { get; init; }
    public int LongestStreak { get; init; }
}

/// <summary>
/// Service interface for calendar-related data.
/// </summary>
public interface ICalendarService
{
    /// <summary>
    /// Gets summary data for a specific month.
    /// </summary>
    Task<MonthSummary> GetMonthSummaryAsync(int year, int month, string? userId = null);

    /// <summary>
    /// Gets summary data for a date range.
    /// </summary>
    Task<List<DaySummary>> GetDateRangeSummaryAsync(DateOnly startDate, DateOnly endDate, string? userId = null);

    /// <summary>
    /// Gets summary for a single day.
    /// </summary>
    Task<DaySummary> GetDaySummaryAsync(DateOnly date, string? userId = null);
}

/// <summary>
/// Service for calendar-related data aggregation.
/// </summary>
public class CalendarService : ICalendarService
{
    private readonly ApplicationDbContext _context;
    private readonly IDateProvider _dateProvider;

    public CalendarService(
        ApplicationDbContext context,
        IDateProvider dateProvider)
    {
        _context = context;
        _dateProvider = dateProvider;
    }

    public async Task<MonthSummary> GetMonthSummaryAsync(int year, int month, string? userId = null)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        var days = await GetDateRangeSummaryAsync(firstDay, lastDay, userId);

        // Calculate streaks
        var (currentStreak, longestStreak) = CalculateStreaks(days, _dateProvider.Today);

        return new MonthSummary
        {
            Year = year,
            Month = month,
            Days = days,
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak
        };
    }

    public async Task<List<DaySummary>> GetDateRangeSummaryAsync(DateOnly startDate, DateOnly endDate, string? userId = null)
    {
        var today = _dateProvider.Today;
        var summaries = new List<DaySummary>();

        // Get all chore logs in the date range
        var logsQuery = _context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.Date >= startDate && cl.Date <= endDate);

        if (!string.IsNullOrEmpty(userId))
        {
            logsQuery = logsQuery.Where(cl => cl.ChoreDefinition.AssignedUserId == userId);
        }

        var logs = await logsQuery.ToListAsync();
        var logsByDate = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.ToList());

        // Get all chore definitions that might be active in this range
        var choreDefinitions = await _context.ChoreDefinitions
            .Where(cd => cd.IsActive || cd.ChoreLogs.Any(cl => cl.Date >= startDate && cl.Date <= endDate))
            .Where(cd => string.IsNullOrEmpty(userId) || cd.AssignedUserId == userId)
            .ToListAsync();

        // Process each day
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var summary = GetDaySummaryInternal(date, today, logsByDate, choreDefinitions);
            summaries.Add(summary);
        }

        return summaries;
    }

    public async Task<DaySummary> GetDaySummaryAsync(DateOnly date, string? userId = null)
    {
        var today = _dateProvider.Today;

        // Get logs for this specific date
        var logsQuery = _context.ChoreLogs
            .Include(cl => cl.ChoreDefinition)
            .Where(cl => cl.Date == date);

        if (!string.IsNullOrEmpty(userId))
        {
            logsQuery = logsQuery.Where(cl => cl.ChoreDefinition.AssignedUserId == userId);
        }

        var logs = await logsQuery.ToListAsync();
        var logsByDate = new Dictionary<DateOnly, List<ChoreLog>> { { date, logs } };

        var choreDefinitions = await _context.ChoreDefinitions
            .Where(cd => cd.IsActive)
            .Where(cd => string.IsNullOrEmpty(userId) || cd.AssignedUserId == userId)
            .ToListAsync();

        return GetDaySummaryInternal(date, today, logsByDate, choreDefinitions);
    }

    private static DaySummary GetDaySummaryInternal(
        DateOnly date,
        DateOnly today,
        Dictionary<DateOnly, List<ChoreLog>> logsByDate,
        List<ChoreDefinition> choreDefinitions)
    {
        // Count scheduled chores for this date
        var scheduledChores = choreDefinitions
            .Where(cd => IsChoreScheduledForDate(cd, date))
            .ToList();

        // Future dates
        if (date > today)
        {
            return new DaySummary
            {
                Date = date,
                Status = DayCompletionStatus.Future,
                TotalChores = scheduledChores.Count,
                PotentialAmount = scheduledChores.Sum(cd => cd.Value)
            };
        }

        // Get logs for this date
        logsByDate.TryGetValue(date, out var dayLogs);
        dayLogs ??= [];

        // If we have logs for chores that aren't in our current definitions (deleted/deactivated),
        // include them in the count
        var loggedChoreIds = dayLogs.Select(l => l.ChoreDefinitionId).ToHashSet();
        var scheduledChoreIds = scheduledChores.Select(c => c.Id).ToHashSet();
        
        // Total = scheduled + any logged that weren't in scheduled
        var totalChores = scheduledChores.Count + loggedChoreIds.Except(scheduledChoreIds).Count();

        if (totalChores == 0)
        {
            return new DaySummary
            {
                Date = date,
                Status = DayCompletionStatus.NoChores,
                TotalChores = 0
            };
        }

        // Calculate stats from logs
        var completedCount = dayLogs.Count(l => l.Status == ChoreStatus.Completed);
        var approvedCount = dayLogs.Count(l => l.Status == ChoreStatus.Approved);
        var missedCount = dayLogs.Count(l => l.Status == ChoreStatus.Missed);
        var skippedCount = dayLogs.Count(l => l.Status == ChoreStatus.Skipped);
        var pendingCount = totalChores - completedCount - approvedCount - missedCount - skippedCount;

        // Calculate earnings - handle potential null ChoreDefinition
        var earnedAmount = dayLogs
            .Where(l => l.Status == ChoreStatus.Approved && l.ChoreDefinition != null)
            .Sum(l => l.ChoreDefinition.Value);

        var potentialAmount = scheduledChores.Sum(c => c.Value);

        // Determine status
        DayCompletionStatus status;
        var doneCount = completedCount + approvedCount + skippedCount;

        if (doneCount == totalChores)
        {
            status = DayCompletionStatus.AllComplete;
        }
        else if (doneCount > 0)
        {
            status = DayCompletionStatus.PartialComplete;
        }
        else
        {
            status = DayCompletionStatus.NoneComplete;
        }

        return new DaySummary
        {
            Date = date,
            Status = status,
            TotalChores = totalChores,
            CompletedChores = completedCount,
            ApprovedChores = approvedCount,
            MissedChores = missedCount,
            PendingChores = pendingCount,
            EarnedAmount = earnedAmount,
            PotentialAmount = potentialAmount
        };
    }

    private static bool IsChoreScheduledForDate(ChoreDefinition chore, DateOnly date)
    {
        if (!chore.IsActive)
            return false;

        // Check date range
        if (chore.StartDate.HasValue && date < chore.StartDate.Value)
            return false;
        if (chore.EndDate.HasValue && date > chore.EndDate.Value)
            return false;

        // Check day of week
        var dayFlag = date.DayOfWeek switch
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

        return (chore.ActiveDays & dayFlag) != 0;
    }

    private static (int currentStreak, int longestStreak) CalculateStreaks(List<DaySummary> days, DateOnly today)
    {
        int currentStreak = 0;
        int longestStreak = 0;
        int runningStreak = 0;
        bool countingCurrent = true;

        // Sort days descending (most recent first) for current streak calculation
        var sortedDays = days
            .Where(d => d.Date <= today && d.Status != DayCompletionStatus.NoChores && d.Status != DayCompletionStatus.Future)
            .OrderByDescending(d => d.Date)
            .ToList();

        foreach (var day in sortedDays)
        {
            if (day.Status == DayCompletionStatus.AllComplete)
            {
                runningStreak++;
                if (countingCurrent)
                {
                    currentStreak = runningStreak;
                }
            }
            else
            {
                countingCurrent = false;
                longestStreak = Math.Max(longestStreak, runningStreak);
                runningStreak = 0;
            }
        }

        longestStreak = Math.Max(longestStreak, runningStreak);

        return (currentStreak, longestStreak);
    }
}
