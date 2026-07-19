using Daily_Bread.Data;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Read endpoints for chores. Phase 0 scope: today's list.
/// Household isolation: children see only themselves; parents may query any
/// member of their own household; cross-household access is always denied.
/// </summary>
[ApiController]
[Route("api/v1/chores")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ChoresController : ControllerBase
{
    private readonly ITrackerService _trackerService;
    private readonly IWeeklyProgressService _weeklyProgressService;
    private readonly IDateProvider _dateProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHouseholdGuard _guard;

    public ChoresController(
        ITrackerService trackerService,
        IWeeklyProgressService weeklyProgressService,
        IDateProvider dateProvider,
        ICurrentUserContext currentUser,
        UserManager<ApplicationUser> userManager,
        IHouseholdGuard guard)
    {
        _trackerService = trackerService;
        _weeklyProgressService = weeklyProgressService;
        _dateProvider = dateProvider;
        _currentUser = currentUser;
        _userManager = userManager;
        _guard = guard;
    }

    /// <summary>
    /// Chores for a date (default: server-computed "today" in the family's
    /// timezone — the server owns the day boundary; see plan §Phase 1).
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<TodayChoresResponse>> Today(
        [FromQuery] DateOnly? date,
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        await _currentUser.InitializeAsync();

        var targetUserId = _currentUser.UserId;
        string? targetUserName = null;

        if (!string.IsNullOrEmpty(userId) && userId != _currentUser.UserId)
        {
            // Only parents/admins may look at another user, and only within
            // their own household.
            if (!_currentUser.IsInRole("Parent") && !_currentUser.IsInRole("Admin"))
            {
                return Forbid(JwtBearerDefaults.AuthenticationScheme);
            }

            var target = await _userManager.FindByIdAsync(userId);
            if (target == null
                || target.HouseholdId == null
                || target.HouseholdId != _currentUser.HouseholdId)
            {
                // Not-found rather than forbidden: don't leak other households' user ids.
                return NotFound(new ApiError("UserNotFound", "User not found."));
            }

            targetUserId = target.Id;
            targetUserName = target.UserName;
        }
        else
        {
            var self = await _userManager.FindByIdAsync(targetUserId);
            targetUserName = self?.UserName;
        }

        var effectiveDate = date ?? _dateProvider.Today;
        var items = await _trackerService.GetTrackerItemsForUserOnDateAsync(targetUserId, effectiveDate);

        var dtos = items.Select(i => new ChoreItemDto(
            i.ChoreDefinitionId,
            i.ChoreLogId,
            i.ChoreName,
            i.Description,
            i.Icon,
            i.EarnValue,
            i.PenaltyValue,
            i.Status.ToString(),
            i.ScheduleType.ToString(),
            i.WeeklyTargetCount,
            i.WeeklyCompletedCount,
            i.IsRepeatable,
            i.HelpReason,
            i.HelpRequestedAt,
            i.ApprovedByUserName,
            i.ApprovedAt)).ToList();

        return Ok(new TodayChoresResponse(effectiveDate, targetUserId, targetUserName, dtos));
    }

    /// <summary>
    /// Toggles completion of a chore. Children toggle their own; parents may
    /// toggle for a child in their household (body.userId).
    /// </summary>
    [HttpPost("{choreDefinitionId:int}/toggle")]
    public async Task<ActionResult<ChoreToggleResponse>> Toggle(
        int choreDefinitionId,
        [FromBody] ChoreToggleRequest request,
        CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(request.UserId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        var date = request.Date ?? _dateProvider.Today;
        var isParent = _currentUser.IsInRole("Parent") || _currentUser.IsInRole("Admin");

        var result = await _trackerService.ToggleChoreCompletionAsync(
            choreDefinitionId, date, target.User!.Id, isParent);

        if (!result.Success)
        {
            return BadRequest(new ApiError("ToggleFailed", result.ErrorMessage ?? "Could not update the chore."));
        }

        return Ok(new ChoreToggleResponse(result.Data.ToString()));
    }

    /// <summary>Raises Help on a chore (self only) — protects it from the end-of-day penalty.</summary>
    [HttpPost("{choreDefinitionId:int}/help")]
    public async Task<IActionResult> RaiseHelp(
        int choreDefinitionId,
        [FromBody] HelpRaiseRequest request,
        CancellationToken ct)
    {
        await _currentUser.InitializeAsync();

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new ApiError("ReasonRequired", "A reason is required when raising Help."));
        }

        var date = request.Date ?? _dateProvider.Today;
        var result = await _trackerService.RequestHelpAsync(
            choreDefinitionId, date, _currentUser.UserId, request.Reason.Trim());

        if (!result.Success)
        {
            return BadRequest(new ApiError("HelpFailed", result.ErrorMessage ?? "Could not raise Help."));
        }

        return NoContent();
    }

    /// <summary>Weekly-frequency chore progress for the week containing asOf (default today).</summary>
    [HttpGet("week")]
    public async Task<ActionResult<WeekProgressResponse>> Week(
        [FromQuery] DateOnly? asOf,
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        var summary = await _weeklyProgressService.GetWeeklyProgressForUserAsync(
            target.User!.Id, asOf ?? _dateProvider.Today);

        var chores = summary.ChoreProgress.Select(p => new WeekChoreProgressDto(
            p.ChoreDefinition.Id,
            p.ChoreDefinition.Name,
            p.CompletedCount,
            p.TargetCount)).ToList();

        return Ok(new WeekProgressResponse(summary.WeekStart, summary.WeekEnd, target.User!.Id, chores));
    }
}
