using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Day-summary data for the year heatmap and calendar views.
/// </summary>
[ApiController]
[Route("api/v1/calendar")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CalendarController : ControllerBase
{
    private const int MaxRangeDays = 400; // a full year view + slack

    private readonly ICalendarService _calendarService;
    private readonly IHouseholdGuard _guard;

    public CalendarController(ICalendarService calendarService, IHouseholdGuard guard)
    {
        _calendarService = calendarService;
        _guard = guard;
    }

    /// <summary>Per-day summaries for [from, to] — the heatmap's data source.</summary>
    [HttpGet("range")]
    public async Task<ActionResult<CalendarRangeResponse>> Range(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        if (to < from)
        {
            return BadRequest(new ApiError("InvalidRange", "'to' must be on or after 'from'."));
        }
        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            return BadRequest(new ApiError("RangeTooLarge", $"Range may not exceed {MaxRangeDays} days."));
        }

        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        var days = await _calendarService.GetDateRangeSummaryAsync(from, to, target.User!.Id);

        var dtos = days.Select(d => new DaySummaryDto(
            d.Date,
            d.Status.ToString(),
            d.TotalChores,
            d.CompletedChores,
            d.ApprovedChores,
            d.MissedChores,
            d.PendingChores,
            d.EarnedAmount)).ToList();

        return Ok(new CalendarRangeResponse(target.User!.Id, from, to, dtos));
    }
}
