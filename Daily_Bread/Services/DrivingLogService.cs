using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// A driving-log entry shaped for display in the history table and approval queue.
/// </summary>
public record DrivingLogEntryDisplay
{
    public int Id { get; init; }
    public string ChildUserId { get; init; } = "";
    public string ChildName { get; init; } = "";
    public DateOnly Date { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public int DurationMinutes { get; init; }
    public bool IsNightDriving { get; init; }
    public string SupervisorLabel { get; init; } = "";
    public WeatherCondition Weather { get; init; }
    public string? RouteNotes { get; init; }
    public string CreatedByUserId { get; init; } = "";
    public bool CreatedByParent { get; init; }
    public DrivingLogStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? DecidedByLabel { get; init; }
    public string? RejectionReason { get; init; }
}

/// <summary>
/// Progress toward a child's configured driving-hours goal. Null goal fields mean the
/// goal hasn't been configured yet - the UI should hide the progress bar in that case.
/// </summary>
public record DrivingLogProgress
{
    public decimal TotalHours { get; init; }
    public decimal? TotalGoalHours { get; init; }
    public decimal NightHours { get; init; }
    public decimal? NightGoalHours { get; init; }
}

/// <summary>
/// Service for logging supervised driving-practice sessions and the parent approval flow
/// on top of them. Either the child or a parent can create an entry: parent-created
/// entries auto-approve (the parent is asserting the record directly), child-created
/// entries start PendingApproval and only count toward hour totals once a parent approves.
/// </summary>
public interface IDrivingLogService
{
    Task<ServiceResult<int>> CreateEntryAsync(DrivingLogEntry draft, string createdByUserId, bool createdByParent);
    Task<List<DrivingLogEntryDisplay>> GetEntriesAsync(string childUserId, DateOnly? from = null, DateOnly? to = null);
    Task<List<DrivingLogEntryDisplay>> GetPendingApprovalsAsync();
    Task<DrivingLogProgress> GetProgressAsync(string childUserId);
    Task<ServiceResult> ApproveEntryAsync(int entryId, string parentUserId);
    Task<ServiceResult> RejectEntryAsync(int entryId, string parentUserId, string? reason = null);
    Task<ServiceResult> DeleteEntryAsync(int entryId, string requestingUserId, bool isParent);
}

public class DrivingLogService : IDrivingLogService
{
    private static readonly TimeOnly NightWindowStart = new(21, 0);
    private static readonly TimeOnly NightWindowEnd = new(6, 0);

    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<DrivingLogService> _logger;

    public DrivingLogService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<DrivingLogService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<ServiceResult<int>> CreateEntryAsync(DrivingLogEntry draft, string createdByUserId, bool createdByParent)
    {
        if (draft.EndTime == draft.StartTime)
            return ServiceResult<int>.Fail("Start and end time can't be the same.");

        if (string.IsNullOrWhiteSpace(draft.SupervisorUserId) && string.IsNullOrWhiteSpace(draft.SupervisorName))
            return ServiceResult<int>.Fail("A supervising adult (registered user or name) is required.");

        await using var context = await _contextFactory.CreateDbContextAsync();

        var duration = draft.EndTime - draft.StartTime;
        if (duration < TimeSpan.Zero)
            duration += TimeSpan.FromHours(24);
        draft.DurationMinutes = (int)duration.TotalMinutes;

        if (draft.NightDrivingSource == NightDrivingSource.DerivedFromTime)
        {
            draft.IsNightDriving = IsWithinNightWindow(draft.StartTime) || IsWithinNightWindow(draft.EndTime);
        }

        draft.CreatedByUserId = createdByUserId;
        draft.CreatedAt = DateTime.UtcNow;

        if (createdByParent)
        {
            draft.Status = DrivingLogStatus.Approved;
            draft.DecidedAt = DateTime.UtcNow;
            draft.DecidedByUserId = createdByUserId;
        }
        else
        {
            draft.Status = DrivingLogStatus.PendingApproval;
        }

        context.DrivingLogEntries.Add(draft);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Driving log entry {EntryId} created for child {ChildUserId} by {CreatedByUserId} (parent={CreatedByParent})",
            draft.Id, draft.ChildUserId, createdByUserId, createdByParent);

        return ServiceResult<int>.Ok(draft.Id);
    }

    public async Task<List<DrivingLogEntryDisplay>> GetEntriesAsync(string childUserId, DateOnly? from = null, DateOnly? to = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entries = await context.DrivingLogEntries
            .Include(e => e.SupervisorUser)
            .Include(e => e.CreatedByUser)
            .Include(e => e.DecidedByUser)
            .Where(e => e.ChildUserId == childUserId)
            .Where(e => from == null || e.Date >= from)
            .Where(e => to == null || e.Date <= to)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.StartTime)
            .ToListAsync();

        var childName = await GetChildNameAsync(context, childUserId);

        return entries.Select(e => ToDisplay(e, childName)).ToList();
    }

    public async Task<List<DrivingLogEntryDisplay>> GetPendingApprovalsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entries = await context.DrivingLogEntries
            .Include(e => e.SupervisorUser)
            .Include(e => e.CreatedByUser)
            .Where(e => e.Status == DrivingLogStatus.PendingApproval)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        var childNames = await GetChildNamesAsync(context, entries.Select(e => e.ChildUserId));

        return entries.Select(e => ToDisplay(e, childNames.GetValueOrDefault(e.ChildUserId, "Unknown"))).ToList();
    }

    public async Task<DrivingLogProgress> GetProgressAsync(string childUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var approved = await context.DrivingLogEntries
            .Where(e => e.ChildUserId == childUserId && e.Status == DrivingLogStatus.Approved)
            .Select(e => new { e.DurationMinutes, e.IsNightDriving })
            .ToListAsync();

        var totalMinutes = approved.Sum(e => e.DurationMinutes);
        var nightMinutes = approved.Where(e => e.IsNightDriving).Sum(e => e.DurationMinutes);

        var profile = await context.ChildProfiles.FirstOrDefaultAsync(p => p.UserId == childUserId);

        return new DrivingLogProgress
        {
            TotalHours = Math.Round(totalMinutes / 60m, 2),
            TotalGoalHours = profile?.DrivingGoalTotalHours,
            NightHours = Math.Round(nightMinutes / 60m, 2),
            NightGoalHours = profile?.DrivingGoalNightHours
        };
    }

    public async Task<ServiceResult> ApproveEntryAsync(int entryId, string parentUserId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entry = await context.DrivingLogEntries.FirstOrDefaultAsync(e => e.Id == entryId);
        if (entry == null)
            return ServiceResult.Fail("Driving log entry not found.");

        if (entry.Status != DrivingLogStatus.PendingApproval)
            return ServiceResult.Fail($"This entry was already {(entry.Status == DrivingLogStatus.Approved ? "approved" : "rejected")}.");

        entry.Status = DrivingLogStatus.Approved;
        entry.DecidedAt = DateTime.UtcNow;
        entry.DecidedByUserId = parentUserId;

        // Manual concurrency increment (this repo's pattern - see AchievementRewardClaimService,
        // LedgerService). Without this a second concurrent approval would never fail.
        entry.Version++;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail("This entry was just decided by someone else. Refresh and try again.");
        }

        _logger.LogInformation("Parent {ParentUserId} approved driving log entry {EntryId}", parentUserId, entryId);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectEntryAsync(int entryId, string parentUserId, string? reason = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entry = await context.DrivingLogEntries.FirstOrDefaultAsync(e => e.Id == entryId);
        if (entry == null)
            return ServiceResult.Fail("Driving log entry not found.");

        if (entry.Status != DrivingLogStatus.PendingApproval)
            return ServiceResult.Fail("This entry was already decided.");

        entry.Status = DrivingLogStatus.Rejected;
        entry.RejectionReason = reason;
        entry.DecidedAt = DateTime.UtcNow;
        entry.DecidedByUserId = parentUserId;
        entry.Version++;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail("This entry was just decided by someone else. Refresh and try again.");
        }

        _logger.LogInformation("Parent {ParentUserId} rejected driving log entry {EntryId}", parentUserId, entryId);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteEntryAsync(int entryId, string requestingUserId, bool isParent)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entry = await context.DrivingLogEntries.FirstOrDefaultAsync(e => e.Id == entryId);
        if (entry == null)
            return ServiceResult.Fail("Driving log entry not found.");

        if (!isParent)
        {
            if (entry.ChildUserId != requestingUserId)
                return ServiceResult.Fail("You can only delete your own entries.");

            if (entry.Status != DrivingLogStatus.PendingApproval)
                return ServiceResult.Fail("You can only delete an entry while it's still pending approval.");
        }

        context.DrivingLogEntries.Remove(entry);
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private static bool IsWithinNightWindow(TimeOnly time) => time >= NightWindowStart || time < NightWindowEnd;

    private static async Task<string> GetChildNameAsync(ApplicationDbContext context, string childUserId)
    {
        var names = await GetChildNamesAsync(context, [childUserId]);
        return names.GetValueOrDefault(childUserId, "Unknown");
    }

    private static async Task<Dictionary<string, string>> GetChildNamesAsync(ApplicationDbContext context, IEnumerable<string> userIds)
    {
        var ids = userIds.Distinct().ToList();
        return await context.ChildProfiles
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName);
    }

    private static DrivingLogEntryDisplay ToDisplay(DrivingLogEntry e, string childName)
    {
        var supervisorLabel = e.SupervisorUser?.UserName ?? e.SupervisorName ?? "Unknown";

        return new DrivingLogEntryDisplay
        {
            Id = e.Id,
            ChildUserId = e.ChildUserId,
            ChildName = childName,
            Date = e.Date,
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            DurationMinutes = e.DurationMinutes,
            IsNightDriving = e.IsNightDriving,
            SupervisorLabel = supervisorLabel,
            Weather = e.Weather,
            RouteNotes = e.RouteNotes,
            CreatedByUserId = e.CreatedByUserId,
            CreatedByParent = e.CreatedByUserId != e.ChildUserId,
            Status = e.Status,
            CreatedAt = e.CreatedAt,
            DecidedAt = e.DecidedAt,
            DecidedByLabel = e.DecidedByUser?.UserName,
            RejectionReason = e.RejectionReason
        };
    }
}
