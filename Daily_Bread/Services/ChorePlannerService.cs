using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

#region DTOs

/// <summary>
/// Represents the completion status of a chore cell in the planner.
/// </summary>
public enum ChoreCellStatus
{
    /// <summary>Not scheduled for this day.</summary>
    NotScheduled,
    /// <summary>Scheduled but in the future.</summary>
    Scheduled,
    /// <summary>Available day for weekly frequency chore.</summary>
    Available,
    /// <summary>Completed by child, awaiting approval.</summary>
    Pending,
    /// <summary>Approved by parent.</summary>
    Completed,
    /// <summary>Missed (past date, not done).</summary>
    Missed,
    /// <summary>Skipped/excused by parent.</summary>
    Skipped
}

/// <summary>
/// Represents a single cell in the chore planner matrix (chore × day).
/// </summary>
public class ChorePlannerCell
{
    public DateOnly Date { get; init; }
    public ChoreCellStatus Status { get; init; }
    public bool IsScheduled { get; init; }
    public bool HasOverride { get; init; }
    public ScheduleOverrideType? OverrideType { get; init; }
    public bool IsToday { get; init; }
    public bool IsPast { get; init; }
    public decimal? EarnedAmount { get; init; }
}

/// <summary>
/// Represents a single row in the chore planner (one chore across all days).
/// </summary>
public class ChorePlannerRow
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public decimal Value { get; init; }
    public ChoreScheduleType ScheduleType { get; init; }
    public DaysOfWeek ActiveDays { get; init; }
    public int WeeklyTargetCount { get; init; }
    public string? AssignedUserId { get; init; }
    public string? AssignedUserName { get; init; }
    
    /// <summary>
    /// Cells for each day of the week (indexed 0-6 for Sun-Sat).
    /// </summary>
    public List<ChorePlannerCell> Cells { get; init; } = [];
    
    /// <summary>
    /// For weekly frequency chores: how many times completed this week.
    /// </summary>
    public int WeeklyCompletedCount { get; init; }
    
    /// <summary>
    /// For weekly frequency chores: how many times approved this week.
    /// </summary>
    public int WeeklyApprovedCount { get; init; }
    
    /// <summary>
    /// Whether this chore earns money (Value > 0).
    /// </summary>
    public bool IsEarningChore => Value > 0;
    
    /// <summary>
    /// Whether this is a "daily" chore (scheduled all 7 days).
    /// </summary>
    public bool IsDailyChore => ScheduleType == ChoreScheduleType.SpecificDays && ActiveDays == DaysOfWeek.All;
    
    /// <summary>
    /// Human-readable schedule description for inline display.
    /// </summary>
    public string ScheduleDescription
    {
        get
        {
            if (ScheduleType == ChoreScheduleType.WeeklyFrequency)
            {
                return $"{WeeklyTargetCount}x/week";
            }
            
            if (ActiveDays == DaysOfWeek.All)
            {
                return "daily";
            }
            
            if (ActiveDays == DaysOfWeek.Weekdays)
            {
                return "weekdays";
            }
            
            if (ActiveDays == DaysOfWeek.Weekends)
            {
                return "weekends";
            }
            
            // Build list of day abbreviations
            var days = new List<string>();
            if (ActiveDays.HasFlag(DaysOfWeek.Sunday)) days.Add("Sun");
            if (ActiveDays.HasFlag(DaysOfWeek.Monday)) days.Add("Mon");
            if (ActiveDays.HasFlag(DaysOfWeek.Tuesday)) days.Add("Tue");
            if (ActiveDays.HasFlag(DaysOfWeek.Wednesday)) days.Add("Wed");
            if (ActiveDays.HasFlag(DaysOfWeek.Thursday)) days.Add("Thu");
            if (ActiveDays.HasFlag(DaysOfWeek.Friday)) days.Add("Fri");
            if (ActiveDays.HasFlag(DaysOfWeek.Saturday)) days.Add("Sat");
            
            return string.Join("/", days);
        }
    }
    
    /// <summary>
    /// Total earned this week for this chore.
    /// </summary>
    public decimal WeeklyEarned => Cells.Sum(c => c.EarnedAmount ?? 0);
    
    /// <summary>
    /// Maximum potential earnings this week for this chore.
    /// </summary>
    public decimal WeeklyPotential => ScheduleType == ChoreScheduleType.WeeklyFrequency 
        ? Value * WeeklyTargetCount 
        : Cells.Count(c => c.IsScheduled) * Value;
}

/// <summary>
/// Represents the status of a day column in the planner.
/// </summary>
public enum DayColumnStatus
{
    /// <summary>Future day, no status yet.</summary>
    Future,
    /// <summary>All chores completed.</summary>
    AllComplete,
    /// <summary>Some chores completed.</summary>
    PartialComplete,
    /// <summary>No chores completed.</summary>
    NoneComplete,
    /// <summary>No chores scheduled.</summary>
    NoChores
}

/// <summary>
/// Represents a day column header with summary info.
/// </summary>
public class ChorePlannerDayColumn
{
    public DateOnly Date { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public string DayName => DayOfWeek.ToString()[..3].ToUpperInvariant();
    public int DayNumber => Date.Day;
    public bool IsToday { get; init; }
    public bool IsPast { get; init; }
    public DayColumnStatus Status { get; init; }
    public int TotalChores { get; init; }
    public int CompletedChores { get; init; }
    public decimal EarnedAmount { get; init; }
    public decimal PotentialAmount { get; init; }
    
    /// <summary>
    /// Completion percentage (0-100).
    /// </summary>
    public int CompletionPercent => TotalChores > 0 
        ? (int)Math.Round(CompletedChores * 100.0 / TotalChores) 
        : 0;
}

/// <summary>
/// A group of chores in the planner (e.g., "Daily Routines", "Weekly Chores").
/// </summary>
public class ChorePlannerGroup
{
    public required string GroupName { get; init; }
    public required string GroupKey { get; init; }
    public List<ChorePlannerRow> Chores { get; init; } = [];
    public bool IsCollapsible { get; init; } = true;
    public bool IsCollapsedByDefault { get; init; }
    
    /// <summary>
    /// Total daily earnings for this group (for daily chores).
    /// </summary>
    public decimal DailyTotal => Chores.Where(c => c.IsDailyChore).Sum(c => c.Value);
    
    /// <summary>
    /// Total weekly potential for this group.
    /// </summary>
    public decimal WeeklyPotential => Chores.Sum(c => c.WeeklyPotential);
    
    /// <summary>
    /// Total earned this week for this group.
    /// </summary>
    public decimal WeeklyEarned => Chores.Sum(c => c.WeeklyEarned);
}

/// <summary>
/// Summary of overrides for the current week.
/// </summary>
public class OverrideSummary
{
    public int ChoreDefinitionId { get; init; }
    public required string ChoreName { get; init; }
    public DateOnly Date { get; init; }
    public ScheduleOverrideType Type { get; init; }
    public DateOnly? MovedFromDate { get; init; }
    public string? CreatedByUserName { get; init; }
}

/// <summary>
/// Complete data for the chore planner view.
/// </summary>
public class ChorePlannerData
{
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public string WeekLabel => $"{WeekStart:MMM d} - {WeekEnd:MMM d, yyyy}";
    public DateOnly Today { get; init; }
    
    /// <summary>
    /// Optional: specific child being viewed.
    /// </summary>
    public string? ChildUserId { get; init; }
    public string? ChildName { get; init; }
    
    /// <summary>
    /// Day columns (7 days, Sun-Sat).
    /// </summary>
    public List<ChorePlannerDayColumn> DayColumns { get; init; } = [];
    
    /// <summary>
    /// Chore groups (Daily, Weekly, etc.).
    /// </summary>
    public List<ChorePlannerGroup> Groups { get; init; } = [];
    
    /// <summary>
    /// Override changes for this week.
    /// </summary>
    public List<OverrideSummary> Overrides { get; init; } = [];
    
    /// <summary>
    /// All rows flattened (for easy iteration).
    /// </summary>
    public IEnumerable<ChorePlannerRow> AllChores => Groups.SelectMany(g => g.Chores);
    
    /// <summary>
    /// Total potential earnings for the week.
    /// </summary>
    public decimal TotalWeeklyPotential => Groups.Sum(g => g.WeeklyPotential);
    
    /// <summary>
    /// Total earned this week.
    /// </summary>
    public decimal TotalWeeklyEarned => Groups.Sum(g => g.WeeklyEarned);
    
    /// <summary>
    /// Current streak of perfect days.
    /// </summary>
    public int CurrentStreak { get; init; }
    
    /// <summary>
    /// Longest streak of perfect days.
    /// </summary>
    public int LongestStreak { get; init; }
}

/// <summary>
/// Data for a single child's printable chart.
/// </summary>
public class PrintableChildChart
{
    public string? ChildUserId { get; init; }
    public required string ChildName { get; init; }
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public string WeekLabel => $"{WeekStart:MMM d} - {WeekEnd:MMM d, yyyy}";
    public List<ChorePlannerGroup> Groups { get; init; } = [];
    public List<ChorePlannerDayColumn> DayColumns { get; init; } = [];
    public List<OverrideSummary> Overrides { get; init; } = [];
    public decimal TotalWeeklyPotential { get; init; }
}

/// <summary>
/// Complete printable chart data for all children.
/// </summary>
public class PrintablePlannerChart
{
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public string WeekLabel => $"{WeekStart:MMM d} - {WeekEnd:MMM d, yyyy}";
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public List<PrintableChildChart> Children { get; init; } = [];
}

#endregion

/// <summary>
/// Service interface for the unified chore planner.
/// </summary>
public interface IChorePlannerService
{
    /// <summary>
    /// Gets the complete planner data for a week.
    /// </summary>
    /// <param name="weekStart">Start of the week (Sunday). Null = current week.</param>
    /// <param name="userId">Optional: filter to specific user's chores.</param>
    /// <param name="includeStreaks">Whether to calculate streak data (can be slow).</param>
    Task<ChorePlannerData> GetPlannerDataAsync(DateOnly? weekStart = null, string? userId = null, bool includeStreaks = true);
    
    /// <summary>
    /// Gets printable chart data for all children for a week.
    /// </summary>
    /// <param name="weekStart">Start of the week (Sunday). Null = current week.</param>
    Task<PrintablePlannerChart> GetPrintableChartAsync(DateOnly? weekStart = null);
    
    /// <summary>
    /// Gets the week start date (Sunday) for a given date.
    /// </summary>
    DateOnly GetWeekStart(DateOnly date);
    
    /// <summary>
    /// Toggles a chore's schedule for a specific day (permanent change).
    /// </summary>
    Task<ServiceResult> ToggleDayScheduleAsync(int choreDefinitionId, DayOfWeek dayOfWeek, string modifiedByUserId);
    
    /// <summary>
    /// Creates or removes an override for a specific date.
    /// </summary>
    Task<ServiceResult> ToggleOverrideAsync(int choreDefinitionId, DateOnly date, string createdByUserId);
}

/// <summary>
/// Unified service for chore planner functionality.
/// </summary>
public class ChorePlannerService : IChorePlannerService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly IChildProfileService _profileService;

    public ChorePlannerService(
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

    public async Task<ChorePlannerData> GetPlannerDataAsync(DateOnly? weekStart = null, string? userId = null, bool includeStreaks = true)
    {
        var today = _dateProvider.Today;
        var start = weekStart ?? GetWeekStart(today);
        var end = start.AddDays(6);

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Get child name if userId provided
        string? childName = null;
        if (!string.IsNullOrEmpty(userId))
        {
            var profile = await context.ChildProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);
            childName = profile?.DisplayName;
        }

        // Get all chore definitions - use AsNoTracking for read-only queries
        var chores = await context.ChoreDefinitions
            .AsNoTracking()
            .Include(c => c.AssignedUser)
            .Where(c => c.IsActive)
            .Where(c => c.StartDate == null || c.StartDate <= end)
            .Where(c => c.EndDate == null || c.EndDate >= start)
            .Where(c => string.IsNullOrEmpty(userId) || c.AssignedUserId == userId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        if (chores.Count == 0)
        {
            // Fast path: no chores, return empty data
            return CreateEmptyPlannerData(start, end, today, userId, childName);
        }

        // Get all chore logs for the week - single query
        var choreIds = chores.Select(c => c.Id).ToList();
        var logs = await context.ChoreLogs
            .AsNoTracking()
            .Where(cl => choreIds.Contains(cl.ChoreDefinitionId))
            // .Where(cl => cl.Date >= start && cl.Date <= end)
            .ToListAsync();

        var logsByChoreAndDate = logs
            .GroupBy(l => (l.ChoreDefinitionId, l.Date))
            .ToDictionary(g => g.Key, g => g.First());

        // Get all overrides for the week
        var overrides = await context.ChoreScheduleOverrides
            .AsNoTracking()
            .Include(o => o.ChoreDefinition)
            .Include(o => o.CreatedByUser)
            .Where(o => o.Date >= start && o.Date <= end)
            .Where(o => choreIds.Contains(o.ChoreDefinitionId))
            .ToListAsync();

        var overridesByChoreAndDate = overrides
            .GroupBy(o => (o.ChoreDefinitionId, o.Date))
            .ToDictionary(g => g.Key, g => g.First());

        // Build day columns
        var dayColumns = new List<ChorePlannerDayColumn>(7);
        for (int i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            dayColumns.Add(new ChorePlannerDayColumn
            {
                Date = date,
                DayOfWeek = date.DayOfWeek,
                IsToday = date == today,
                IsPast = date < today,
                Status = DayColumnStatus.Future,
                TotalChores = 0,
                CompletedChores = 0
            });
        }

        // Build chore rows
        var rows = new List<ChorePlannerRow>(chores.Count);
        foreach (var chore in chores)
        {
            var cells = new List<ChorePlannerCell>(7);
            int weeklyCompleted = 0;
            int weeklyApproved = 0;

            for (int i = 0; i < 7; i++)
            {
                var date = start.AddDays(i);
                
                // Check base schedule
                var isBaseScheduled = ChoreScheduleHelper.IsChoreScheduledForDate(chore, date);
                
                // Check for overrides
                overridesByChoreAndDate.TryGetValue((chore.Id, date), out var choreOverride);
                var hasOverride = choreOverride != null;
                
                // Determine effective schedule
                bool isEffectivelyScheduled = isBaseScheduled;
                if (hasOverride)
                {
                    isEffectivelyScheduled = choreOverride!.Type == ScheduleOverrideType.Add || 
                                              choreOverride.Type == ScheduleOverrideType.Move;
                }

                // Get log status
                logsByChoreAndDate.TryGetValue((chore.Id, date), out var log);
                
                // Determine cell status
                var cellStatus = DetermineCellStatus(
                    isEffectivelyScheduled, 
                    chore.ScheduleType, 
                    date, 
                    today, 
                    log?.Status);

                decimal? earnedAmount = null;
                if (log?.Status == ChoreStatus.Approved)
                {
                    earnedAmount = chore.EarnValue;
                    weeklyApproved++;
                }
                if (log?.Status == ChoreStatus.Completed || log?.Status == ChoreStatus.Approved)
                {
                    weeklyCompleted++;
                }

                cells.Add(new ChorePlannerCell
                {
                    Date = date,
                    Status = cellStatus,
                    IsScheduled = isEffectivelyScheduled,
                    HasOverride = hasOverride,
                    OverrideType = choreOverride?.Type,
                    IsToday = date == today,
                    IsPast = date < today,
                    EarnedAmount = earnedAmount
                });
            }

            rows.Add(new ChorePlannerRow
            {
                ChoreDefinitionId = chore.Id,
                ChoreName = chore.Name,
                Description = chore.Description,
                Icon = chore.Icon,
                Value = chore.EarnValue,
                ScheduleType = chore.ScheduleType,
                ActiveDays = chore.ActiveDays,
                WeeklyTargetCount = chore.WeeklyTargetCount,
                AssignedUserId = chore.AssignedUserId,
                AssignedUserName = chore.AssignedUser?.UserName,
                Cells = cells,
                WeeklyCompletedCount = weeklyCompleted,
                WeeklyApprovedCount = weeklyApproved
            });
        }

        // Group chores
        var groups = GroupChores(rows);

        // Update day column statistics
        for (int i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            var dayCells = rows.SelectMany(r => r.Cells.Where(c => c.Date == date && c.IsScheduled)).ToList();
            var totalChores = dayCells.Count;
            var completedChores = dayCells.Count(c => c.Status == ChoreCellStatus.Completed || c.Status == ChoreCellStatus.Skipped);
            var earnedAmount = dayCells.Sum(c => c.EarnedAmount ?? 0);
            var potentialAmount = rows.Where(r => r.Cells[i].IsScheduled).Sum(r => r.Value);

            var status = DetermineDayColumnStatus(date, today, totalChores, completedChores, dayCells);

            dayColumns[i] = new ChorePlannerDayColumn
            {
                Date = date,
                DayOfWeek = date.DayOfWeek,
                IsToday = date == today,
                IsPast = date < today,
                Status = status,
                TotalChores = totalChores,
                CompletedChores = completedChores,
                EarnedAmount = earnedAmount,
                PotentialAmount = potentialAmount
            };
        }

        // Build override summaries
        var overrideSummaries = overrides
            .Where(o => o.Type != ScheduleOverrideType.Remove || !overrides.Any(o2 => 
                o2.ChoreDefinitionId == o.ChoreDefinitionId && 
                o2.Type == ScheduleOverrideType.Move))
            .Select(o => new OverrideSummary
            {
                ChoreDefinitionId = o.ChoreDefinitionId,
                ChoreName = o.ChoreDefinition.Name,
                Date = o.Date,
                Type = o.Type,
                CreatedByUserName = o.CreatedByUser?.UserName
            })
            .ToList();

        // Calculate streaks only if requested and viewing current/past week
        int currentStreak = 0;
        int longestStreak = 0;
        
        if (includeStreaks && start <= today)
        {
            (currentStreak, longestStreak) = await CalculateStreaksAsync(context, userId, today, chores);
        }

        return new ChorePlannerData
        {
            WeekStart = start,
            WeekEnd = end,
            Today = today,
            ChildUserId = userId,
            ChildName = childName,
            DayColumns = dayColumns,
            Groups = groups,
            Overrides = overrideSummaries,
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak
        };
    }

    private static ChorePlannerData CreateEmptyPlannerData(DateOnly start, DateOnly end, DateOnly today, string? userId, string? childName)
    {
        var dayColumns = new List<ChorePlannerDayColumn>(7);
        for (int i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            dayColumns.Add(new ChorePlannerDayColumn
            {
                Date = date,
                DayOfWeek = date.DayOfWeek,
                IsToday = date == today,
                IsPast = date < today,
                Status = DayColumnStatus.NoChores,
                TotalChores = 0,
                CompletedChores = 0
            });
        }

        return new ChorePlannerData
        {
            WeekStart = start,
            WeekEnd = end,
            Today = today,
            ChildUserId = userId,
            ChildName = childName,
            DayColumns = dayColumns,
            Groups = [],
            Overrides = [],
            CurrentStreak = 0,
            LongestStreak = 0
        };
    }

    public async Task<PrintablePlannerChart> GetPrintableChartAsync(DateOnly? weekStart = null)
    {
        var today = _dateProvider.Today;
        var start = weekStart ?? GetWeekStart(today);
        var end = start.AddDays(6);

        var profiles = await _profileService.GetAllChildProfilesAsync();
        var children = new List<PrintableChildChart>();

        foreach (var profile in profiles)
        {
            var plannerData = await GetPlannerDataAsync(start, profile.UserId);
            
            // For printing, we want to show empty checkboxes for future dates
            // and strip completion status
            var printGroups = plannerData.Groups.Select(g => new ChorePlannerGroup
            {
                GroupName = g.GroupName,
                GroupKey = g.GroupKey,
                IsCollapsible = false,
                IsCollapsedByDefault = false,
                Chores = g.Chores.Select(c => new ChorePlannerRow
                {
                    ChoreDefinitionId = c.ChoreDefinitionId,
                    ChoreName = c.ChoreName,
                    Description = c.Description,
                    Icon = c.Icon,
                    Value = c.Value,
                    ScheduleType = c.ScheduleType,
                    ActiveDays = c.ActiveDays,
                    WeeklyTargetCount = c.WeeklyTargetCount,
                    AssignedUserId = c.AssignedUserId,
                    AssignedUserName = c.AssignedUserName,
                    WeeklyCompletedCount = 0,
                    WeeklyApprovedCount = 0,
                    Cells = c.Cells.Select(cell => new ChorePlannerCell
                    {
                        Date = cell.Date,
                        Status = cell.IsScheduled ? ChoreCellStatus.Scheduled : ChoreCellStatus.NotScheduled,
                        IsScheduled = cell.IsScheduled,
                        HasOverride = cell.HasOverride,
                        OverrideType = cell.OverrideType,
                        IsToday = cell.IsToday,
                        IsPast = false,
                        EarnedAmount = null
                    }).ToList()
                }).ToList()
            }).ToList();

            children.Add(new PrintableChildChart
            {
                ChildUserId = profile.UserId,
                ChildName = profile.DisplayName,
                WeekStart = start,
                WeekEnd = end,
                Groups = printGroups,
                DayColumns = plannerData.DayColumns,
                Overrides = plannerData.Overrides.Where(o => o.Type != ScheduleOverrideType.Remove).ToList(),
                TotalWeeklyPotential = plannerData.TotalWeeklyPotential
            });
        }

        return new PrintablePlannerChart
        {
            WeekStart = start,
            WeekEnd = end,
            Children = children
        };
    }

    public async Task<ServiceResult> ToggleDayScheduleAsync(int choreDefinitionId, DayOfWeek dayOfWeek, string modifiedByUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chore = await context.ChoreDefinitions.FindAsync(choreDefinitionId);
        if (chore == null)
        {
            return ServiceResult.Fail("Chore not found.");
        }

        var dayFlag = ChoreScheduleHelper.GetDayOfWeekFlag(dayOfWeek);
        
        // Toggle the day flag
        if ((chore.ActiveDays & dayFlag) != 0)
        {
            // Remove the day
            chore.ActiveDays &= ~dayFlag;
        }
        else
        {
            // Add the day
            chore.ActiveDays |= dayFlag;
        }

        chore.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ToggleOverrideAsync(int choreDefinitionId, DateOnly date, string createdByUserId)
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
            // Remove existing override
            context.ChoreScheduleOverrides.Remove(existingOverride);
        }
        else
        {
            // Check if chore is normally scheduled for this day
            var isNormallyScheduled = ChoreScheduleHelper.IsChoreScheduledForDate(chore, date);

            // Create appropriate override
            var overrideType = isNormallyScheduled ? ScheduleOverrideType.Remove : ScheduleOverrideType.Add;
            
            context.ChoreScheduleOverrides.Add(new ChoreScheduleOverride
            {
                ChoreDefinitionId = choreDefinitionId,
                Date = date,
                Type = overrideType,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    #region Private Helpers

    private static ChoreCellStatus DetermineCellStatus(
        bool isScheduled, 
        ChoreScheduleType scheduleType,
        DateOnly date, 
        DateOnly today, 
        ChoreStatus? logStatus)
    {
        if (!isScheduled)
        {
            return scheduleType == ChoreScheduleType.WeeklyFrequency 
                ? ChoreCellStatus.Available 
                : ChoreCellStatus.NotScheduled;
        }

        // Check log status first
        if (logStatus.HasValue)
        {
            return logStatus.Value switch
            {
                ChoreStatus.Approved => ChoreCellStatus.Completed,
                ChoreStatus.Completed => ChoreCellStatus.Pending,
                ChoreStatus.Missed => ChoreCellStatus.Missed,
                ChoreStatus.Skipped => ChoreCellStatus.Skipped,
                _ => date >= today ? ChoreCellStatus.Scheduled : ChoreCellStatus.Missed
            };
        }

        // No log entry
        if (date > today)
        {
            return ChoreCellStatus.Scheduled;
        }
        else if (date == today)
        {
            return ChoreCellStatus.Scheduled;
        }
        else
        {
            // Past date with no log = missed
            return ChoreCellStatus.Missed;
        }
    }

    private static DayColumnStatus DetermineDayColumnStatus(
        DateOnly date, 
        DateOnly today, 
        int totalChores, 
        int completedChores,
        List<ChorePlannerCell> cells)
    {
        if (date > today)
        {
            return DayColumnStatus.Future;
        }

        if (totalChores == 0)
        {
            return DayColumnStatus.NoChores;
        }

        // Count missed separately (not completed and not skipped)
        var missedCount = cells.Count(c => c.Status == ChoreCellStatus.Missed);
        var pendingCount = cells.Count(c => c.Status == ChoreCellStatus.Pending || c.Status == ChoreCellStatus.Scheduled);

        if (completedChores == totalChores)
        {
            return DayColumnStatus.AllComplete;
        }
        else if (completedChores > 0 || pendingCount > 0)
        {
            return DayColumnStatus.PartialComplete;
        }
        else
        {
            return DayColumnStatus.NoneComplete;
        }
    }

    private static List<ChorePlannerGroup> GroupChores(List<ChorePlannerRow> rows)
    {
        var groups = new List<ChorePlannerGroup>();

        // Group 1: Earning Chores (chores with Value > 0)
        var earningChores = rows.Where(r => r.IsEarningChore).ToList();
        if (earningChores.Count > 0)
        {
            groups.Add(new ChorePlannerGroup
            {
                GroupName = "Earning Chores",
                GroupKey = "earning",
                Chores = earningChores,
                IsCollapsible = true,
                IsCollapsedByDefault = false
            });
        }

        // Group 2: Daily Expectations (chores with Value == 0, no earnings)
        var expectationChores = rows.Where(r => !r.IsEarningChore).ToList();
        if (expectationChores.Count > 0)
        {
            groups.Add(new ChorePlannerGroup
            {
                GroupName = "Daily Expectations",
                GroupKey = "expectations",
                Chores = expectationChores,
                IsCollapsible = true,
                IsCollapsedByDefault = false
            });
        }

        return groups;
    }

    private async Task<(int currentStreak, int longestStreak)> CalculateStreaksAsync(
        ApplicationDbContext context, 
        string? userId, 
        DateOnly today,
        List<ChoreDefinition>? preloadedChores = null)
    {
        // Only look back 30 days instead of 60 for better performance
        var startDate = today.AddDays(-30);
        
        // Reuse preloaded chores if available, otherwise query
        var choreDefs = preloadedChores ?? await context.ChoreDefinitions
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Where(c => string.IsNullOrEmpty(userId) || c.AssignedUserId == userId)
            .ToListAsync();

        if (choreDefs.Count == 0)
        {
            return (0, 0);
        }

        var choreIds = choreDefs.Select(c => c.Id).ToList();

        // Single optimized query: get only the fields we need
        var logs = await context.ChoreLogs
            .AsNoTracking()
            .Where(cl => choreIds.Contains(cl.ChoreDefinitionId))
            .Where(cl => cl.Date >= startDate && cl.Date <= today)
            .Select(cl => new { cl.Date, cl.ChoreDefinitionId, cl.Status })
            .ToListAsync();

        var logsByDate = logs.GroupBy(l => l.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        int currentStreak = 0;
        int longestStreak = 0;
        int runningStreak = 0;
        bool countingCurrent = true;

        // Go backwards from today
        for (var date = today; date >= startDate; date = date.AddDays(-1))
        {
            // Count scheduled chores for this date
            var scheduledCount = choreDefs.Count(c => ChoreScheduleHelper.IsChoreScheduledForDate(c, date));
            
            if (scheduledCount == 0)
            {
                continue; // Skip days with no chores
            }

            // Count completed (approved) chores
            logsByDate.TryGetValue(date, out var dayLogs);
            var completedCount = dayLogs?.Count(l => l.Status == ChoreStatus.Approved || l.Status == ChoreStatus.Skipped) ?? 0;

            if (completedCount >= scheduledCount)
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

    #endregion
}
