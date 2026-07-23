using System.Globalization;
using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Supervised-driving log: a child logs practice drives (self-reported, pending
/// a parent's approval), a parent logs or approves them, and both see progress
/// toward the hour goals. The service isn't household-scoped, so — as with
/// reward claims — this controller scopes every read and action to the caller's
/// household. Delete is intentionally not surfaced here yet.
/// </summary>
[ApiController]
[Route("api/v1/driving")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DrivingLogController : ControllerBase
{
    private readonly IDrivingLogService _driving;
    private readonly IChoreManagementService _choreManagement;
    private readonly IHouseholdGuard _guard;
    private readonly ICurrentUserContext _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public DrivingLogController(
        IDrivingLogService driving,
        IChoreManagementService choreManagement,
        IHouseholdGuard guard,
        ICurrentUserContext currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _driving = driving;
        _choreManagement = choreManagement;
        _guard = guard;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    private bool CallerIsParent => User.IsInRole("Parent") || User.IsInRole("Admin");

    private static DrivingLogEntryDto ToDto(DrivingLogEntryDisplay e) => new(
        e.Id,
        e.ChildUserId,
        e.ChildName,
        e.Date,
        e.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        e.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        e.DurationMinutes,
        e.IsNightDriving,
        e.SupervisorLabel,
        e.Weather.ToString(),
        e.RouteNotes,
        e.CreatedByParent,
        e.Status.ToString(),
        e.CreatedAt,
        e.DecidedAt,
        e.DecidedByLabel,
        e.RejectionReason);

    /// <summary>A child's drives (self, or a child in the caller's household).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DrivingLogEntryDto>>> List([FromQuery] string? userId, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden) return Forbid(JwtBearerDefaults.AuthenticationScheme);
        if (target.Outcome == GuardOutcome.NotFound) return NotFound(new ApiError("UserNotFound", "User not found."));

        var entries = await _driving.GetEntriesAsync(target.User!.Id);
        return Ok(entries.Select(ToDto).ToList());
    }

    /// <summary>Hours-to-goal progress for a child.</summary>
    [HttpGet("progress")]
    public async Task<ActionResult<DrivingLogProgressDto>> Progress([FromQuery] string? userId, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden) return Forbid(JwtBearerDefaults.AuthenticationScheme);
        if (target.Outcome == GuardOutcome.NotFound) return NotFound(new ApiError("UserNotFound", "User not found."));

        var p = await _driving.GetProgressAsync(target.User!.Id);
        return Ok(new DrivingLogProgressDto(p.TotalHours, p.TotalGoalHours, p.NightHours, p.NightGoalHours));
    }

    /// <summary>The household's drives waiting on a parent. Parent/Admin only.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<ActionResult<IReadOnlyList<DrivingLogEntryDto>>> Pending()
    {
        var householdChildIds = await HouseholdChildIdsAsync();
        var all = await _driving.GetPendingApprovalsAsync();
        return Ok(all.Where(e => householdChildIds.Contains(e.ChildUserId)).Select(ToDto).ToList());
    }

    /// <summary>Log a drive. A child logs their own; a parent logs for a child.</summary>
    [HttpPost]
    public async Task<ActionResult<DrivingLogEntryDto>> Create([FromBody] DrivingLogCreateRequest request, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(request.ChildUserId, ct);
        if (target.Outcome == GuardOutcome.Forbidden) return Forbid(JwtBearerDefaults.AuthenticationScheme);
        if (target.Outcome == GuardOutcome.NotFound) return NotFound(new ApiError("UserNotFound", "User not found."));

        if (!TimeOnly.TryParseExact(request.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(request.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            return BadRequest(new ApiError("InvalidTime", "Start and end must be HH:mm."));
        }

        if (!Enum.TryParse<WeatherCondition>(request.Weather, ignoreCase: true, out var weather))
        {
            weather = WeatherCondition.Clear;
        }

        await _currentUser.InitializeAsync();
        var draft = new DrivingLogEntry
        {
            ChildUserId = target.User!.Id,
            CreatedByUserId = _currentUser.UserId,
            Date = request.Date,
            StartTime = start,
            EndTime = end,
            NightDrivingSource = request.NightOverride is null
                ? NightDrivingSource.DerivedFromTime
                : NightDrivingSource.ManualOverride,
            IsNightDriving = request.NightOverride ?? false,
            SupervisorName = string.IsNullOrWhiteSpace(request.SupervisorName) ? null : request.SupervisorName.Trim(),
            Weather = weather,
            RouteNotes = string.IsNullOrWhiteSpace(request.RouteNotes) ? null : request.RouteNotes.Trim()
        };

        var result = await _driving.CreateEntryAsync(draft, _currentUser.UserId, CallerIsParent);
        if (!result.Success)
        {
            return BadRequest(new ApiError("CreateFailed", result.ErrorMessage ?? "Could not log the drive."));
        }

        var saved = (await _driving.GetEntriesAsync(target.User!.Id)).FirstOrDefault(e => e.Id == result.Data);
        return saved == null ? NoContent() : Ok(ToDto(saved));
    }

    [HttpPost("{entryId:int}/approve")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<IActionResult> Approve(int entryId)
    {
        if (!await EntryIsInCallerHouseholdPendingAsync(entryId))
        {
            return NotFound(new ApiError("NotFound", "Driving entry not found."));
        }

        await _currentUser.InitializeAsync();
        var result = await _driving.ApproveEntryAsync(entryId, _currentUser.UserId);
        if (!result.Success) return BadRequest(new ApiError("ApproveFailed", result.ErrorMessage ?? "Could not approve the drive."));
        return NoContent();
    }

    [HttpPost("{entryId:int}/reject")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<IActionResult> Reject(int entryId, [FromBody] RewardClaimRejectRequest? request)
    {
        if (!await EntryIsInCallerHouseholdPendingAsync(entryId))
        {
            return NotFound(new ApiError("NotFound", "Driving entry not found."));
        }

        await _currentUser.InitializeAsync();
        var result = await _driving.RejectEntryAsync(entryId, _currentUser.UserId, request?.Reason);
        if (!result.Success) return BadRequest(new ApiError("RejectFailed", result.ErrorMessage ?? "Could not reject the drive."));
        return NoContent();
    }

    // ── Household scoping ────────────────────────────────────────────────

    private async Task<HashSet<string>> HouseholdChildIdsAsync()
    {
        await _currentUser.InitializeAsync();
        var household = _currentUser.HouseholdId;
        var ids = new HashSet<string>();
        if (household == null) return ids;

        var candidates = await _choreManagement.GetAssignableUsersAsync();
        foreach (var candidate in candidates)
        {
            var user = await _userManager.FindByIdAsync(candidate.Id);
            if (user?.HouseholdId != null && user.HouseholdId == household)
            {
                ids.Add(candidate.Id);
            }
        }
        return ids;
    }

    private async Task<bool> EntryIsInCallerHouseholdPendingAsync(int entryId)
    {
        var householdChildIds = await HouseholdChildIdsAsync();
        if (householdChildIds.Count == 0) return false;
        var pending = await _driving.GetPendingApprovalsAsync();
        return pending.Any(e => e.Id == entryId && householdChildIds.Contains(e.ChildUserId));
    }
}
