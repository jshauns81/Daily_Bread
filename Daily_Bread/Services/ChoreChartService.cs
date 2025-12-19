using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Represents a chore entry for a specific day in the chart.
/// </summary>
public class ChoreChartEntry
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public decimal EarnValue { get; init; }
    public decimal PenaltyValue { get; init; }
    public decimal Value => EarnValue > 0 ? EarnValue : PenaltyValue; // Backward compatibility
    public ChoreStatus? Status { get; init; }
    public bool IsScheduled { get; init; }
    public bool IsWeeklyFrequency { get; init; }
    public int? WeeklyTargetCount { get; init; }
    public int? WeeklyCompletedCount { get; init; }
}

/// <summary>
/// Represents a single day column in the chore chart.
/// </summary>
public class ChoreChartDay
{
    public DateOnly Date { get; init; }
    public string DayName => Date.DayOfWeek.ToString();
    public string ShortDayName => Date.DayOfWeek.ToString()[..3];
    public bool IsToday { get; init; }
    public bool IsPast { get; init; }
    public List<ChoreChartEntry> Chores { get; init; } = [];
}

/// <summary>
/// Represents a child's weekly chore chart data.
/// </summary>
public class ChildChoreChart
{
    public int ProfileId { get; init; }
    public required string ChildName { get; init; }
    public string? AvatarEmoji { get; init; }
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public List<ChoreChartDay> Days { get; init; } = [];
    public List<WeeklyFrequencyChoreProgress> WeeklyChores { get; init; } = [];
    
    // Summary stats
    public int TotalChoresScheduled => Days.Sum(d => d.Chores.Count(c => c.IsScheduled && !c.IsWeeklyFrequency));
    public int TotalChoresCompleted => Days.Sum(d => d.Chores.Count(c => c.Status == ChoreStatus.Approved || c.Status == ChoreStatus.Completed));
    public decimal TotalPotentialEarnings => Days.SelectMany(d => d.Chores.Where(c => c.IsScheduled && !c.IsWeeklyFrequency)).Sum(c => c.Value)
        + WeeklyChores.Sum(w => w.Value * w.TargetCount);
    public decimal TotalEarned => Days.SelectMany(d => d.Chores.Where(c => c.Status == ChoreStatus.Approved && !c.IsWeeklyFrequency)).Sum(c => c.Value)
        + WeeklyChores.Sum(w => w.Value * w.ApprovedCount);
}

/// <summary>
/// Progress for a weekly frequency chore.
/// </summary>
public class WeeklyFrequencyChoreProgress
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public decimal EarnValue { get; init; }
    public decimal PenaltyValue { get; init; }
    public decimal Value => EarnValue > 0 ? EarnValue : PenaltyValue; // Backward compatibility
    public int TargetCount { get; init; }
    public int CompletedCount { get; init; }
    public int ApprovedCount { get; init; }
    public List<DateOnly> CompletedDates { get; init; } = [];
    public bool IsTargetMet => ApprovedCount >= TargetCount;
}

/// <summary>
/// Full printable chore chart with all children.
/// </summary>
public class PrintableChoreChart
{
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public string WeekLabel => $"{WeekStart:MMM d} - {WeekEnd:MMM d, yyyy}";
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public List<ChildChoreChart> Children { get; init; } = [];
}

/// <summary>
/// Service for generating printable chore charts.
/// </summary>
public interface IChoreChartService
{
    /// <summary>
    /// Generates a printable chore chart for all children for the specified week.
    /// </summary>
    Task<PrintableChoreChart> GenerateWeeklyChartAsync(DateOnly? weekStartDate = null);

    /// <summary>
    /// Generates a chore chart for a specific child.
    /// </summary>
    Task<ChildChoreChart?> GenerateChildChartAsync(int profileId, DateOnly? weekStartDate = null);

    /// <summary>
    /// Gets the start of the week (Sunday) for a given date.
    /// </summary>
    DateOnly GetWeekStart(DateOnly date);
}

public class ChoreChartService : IChoreChartService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IChildProfileService _profileService;

    public ChoreChartService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider,
        IChildProfileService profileService)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _profileService = profileService;
    }

    public DateOnly GetWeekStart(DateOnly date)
    {
        return ChoreScheduleHelper.GetWeekStartDate(date);
    }

    public async Task<PrintableChoreChart> GenerateWeeklyChartAsync(DateOnly? weekStartDate = null)
    {
        var weekStart = weekStartDate ?? GetWeekStart(_dateProvider.Today);
        var weekEnd = weekStart.AddDays(6);

        var profiles = await _profileService.GetAllChildProfilesAsync();
        var children = new List<ChildChoreChart>();

        foreach (var profile in profiles)
        {
            var childChart = await GenerateChildChartInternalAsync(profile.ProfileId, profile.DisplayName, weekStart, weekEnd);
            if (childChart != null)
            {
                children.Add(childChart);
            }
        }

        return new PrintableChoreChart
        {
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            Children = children
        };
    }

    public async Task<ChildChoreChart?> GenerateChildChartAsync(int profileId, DateOnly? weekStartDate = null)
    {
        var weekStart = weekStartDate ?? GetWeekStart(_dateProvider.Today);
        var weekEnd = weekStart.AddDays(6);

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var profile = await context.ChildProfiles
            .FirstOrDefaultAsync(p => p.Id == profileId);

        if (profile == null) return null;

        return await GenerateChildChartInternalAsync(profileId, profile.DisplayName, weekStart, weekEnd);
    }

    private async Task<ChildChoreChart?> GenerateChildChartInternalAsync(
        int profileId,
        string displayName,
        DateOnly weekStart,
        DateOnly weekEnd)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var profile = await context.ChildProfiles
            .FirstOrDefaultAsync(p => p.Id == profileId);

        if (profile == null) return null;

        var userId = profile.UserId;
        var today = _dateProvider.Today;

        // Get all chore definitions for this user
        var userChores = await context.ChoreDefinitions
            .Where(c => c.AssignedUserId == userId && c.IsActive)
            .Where(c => c.StartDate == null || c.StartDate <= weekEnd)
            .Where(c => c.EndDate == null || c.EndDate >= weekStart)
            .ToListAsync();

        var dailyChores = userChores.Where(c => c.ScheduleType == ChoreScheduleType.SpecificDays).ToList();
        var weeklyChores = userChores.Where(c => c.ScheduleType == ChoreScheduleType.WeeklyFrequency).ToList();

        // Get all chore logs for the week
        var choreLogs = await context.ChoreLogs
            .Where(cl => cl.ChoreDefinition.AssignedUserId == userId)
            .Where(cl => cl.Date >= weekStart && cl.Date <= weekEnd)
            .ToListAsync();

        var logsByDateAndChore = choreLogs
            .GroupBy(cl => (cl.Date, cl.ChoreDefinitionId))
            .ToDictionary(g => g.Key, g => g.First());

        // Build days
        var days = new List<ChoreChartDay>();
        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            var dayChores = new List<ChoreChartEntry>();

            // Add daily scheduled chores using shared helper
            foreach (var chore in dailyChores)
            {
                var isScheduled = ChoreScheduleHelper.IsChoreScheduledForDate(chore, date);
                logsByDateAndChore.TryGetValue((date, chore.Id), out var log);

                if (isScheduled || log != null)
                {
                    dayChores.Add(new ChoreChartEntry
                    {
                        ChoreDefinitionId = chore.Id,
                        ChoreName = chore.Name,
                        EarnValue = chore.EarnValue,
                        PenaltyValue = chore.PenaltyValue,
                        Status = log?.Status,
                        IsScheduled = isScheduled,
                        IsWeeklyFrequency = false
                    });
                }
            }

            // Add weekly frequency chores (they appear every available day) using shared helper
            foreach (var chore in weeklyChores)
            {
                var isAvailableDay = ChoreScheduleHelper.IsChoreScheduledForDate(chore, date);
                logsByDateAndChore.TryGetValue((date, chore.Id), out var log);

                if (isAvailableDay)
                {
                    // Get weekly completion count for context
                    var weeklyLogs = choreLogs.Where(cl => cl.ChoreDefinitionId == chore.Id).ToList();
                    var completedCount = weeklyLogs.Count(l => l.Status == ChoreStatus.Approved || l.Status == ChoreStatus.Completed);

                    dayChores.Add(new ChoreChartEntry
                    {
                        ChoreDefinitionId = chore.Id,
                        ChoreName = chore.Name,
                        EarnValue = chore.EarnValue,
                        PenaltyValue = chore.PenaltyValue,
                        Status = log?.Status,
                        IsScheduled = true,
                        IsWeeklyFrequency = true,
                        WeeklyTargetCount = chore.WeeklyTargetCount,
                        WeeklyCompletedCount = completedCount
                    });
                }
            }

            days.Add(new ChoreChartDay
            {
                Date = date,
                IsToday = date == today,
                IsPast = date < today,
                Chores = dayChores.OrderBy(c => c.ChoreName).ToList()
            });
        }

        // Build weekly frequency progress
        var weeklyProgress = weeklyChores.Select(chore =>
        {
            var weeklyLogs = choreLogs.Where(cl => cl.ChoreDefinitionId == chore.Id).ToList();
            return new WeeklyFrequencyChoreProgress
            {
                ChoreDefinitionId = chore.Id,
                ChoreName = chore.Name,
                EarnValue = chore.EarnValue,
                PenaltyValue = chore.PenaltyValue,
                TargetCount = chore.WeeklyTargetCount,
                CompletedCount = weeklyLogs.Count(l => l.Status == ChoreStatus.Completed || l.Status == ChoreStatus.Approved),
                ApprovedCount = weeklyLogs.Count(l => l.Status == ChoreStatus.Approved),
                CompletedDates = weeklyLogs
                    .Where(l => l.Status == ChoreStatus.Completed || l.Status == ChoreStatus.Approved)
                    .Select(l => l.Date)
                    .OrderBy(d => d)
                    .ToList()
            };
        }).ToList();

        return new ChildChoreChart
        {
            ProfileId = profileId,
            ChildName = displayName,
            AvatarEmoji = GetAvatarEmoji(displayName),
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            Days = days,
            WeeklyChores = weeklyProgress
        };
    }

    private static string GetAvatarEmoji(string name)
    {
        // Use centralized EmojiConstants for reliable rendering
        return EmojiConstants.GetAvatarForName(name);
    }
}
