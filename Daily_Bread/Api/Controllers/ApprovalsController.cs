using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Parent approval queue: pending chore approvals, Help requests, and the
/// actions on them. Parent/Admin only; log ids are guarded to the caller's
/// household before any action.
/// </summary>
[ApiController]
[Route("api/v1/approvals")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Parent,Admin")]
public class ApprovalsController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ITrackerService _trackerService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IHouseholdGuard _guard;

    public ApprovalsController(
        IDashboardService dashboardService,
        ITrackerService trackerService,
        ICurrentUserContext currentUser,
        IHouseholdGuard guard)
    {
        _dashboardService = dashboardService;
        _trackerService = trackerService;
        _currentUser = currentUser;
        _guard = guard;
    }

    /// <summary>Everything waiting on a parent: completed chores + open Help requests.</summary>
    [HttpGet]
    public async Task<ActionResult<ApprovalsResponse>> Queue(CancellationToken ct)
    {
        var dashboard = await _dashboardService.GetParentDashboardAsync();

        var approvals = dashboard.PendingApprovals.Select(a => new ApprovalItemDto(
            a.ChoreLogId, a.ChoreDefinitionId, a.ChoreName, a.ChildName, a.ChildUserId, a.EarnValue)).ToList();

        var helps = dashboard.HelpRequests.Select(h => new HelpRequestDto(
            h.ChoreLogId, h.ChoreDefinitionId, h.ChoreName, h.ChildName, h.ChildUserId,
            h.EarnValue, h.Reason, h.Date, h.RequestedAt)).ToList();

        return Ok(new ApprovalsResponse(approvals, helps));
    }

    /// <summary>Approves a completed chore — the gold-glow moment.</summary>
    [HttpPost("{choreLogId:int}/approve")]
    public async Task<IActionResult> Approve(int choreLogId, CancellationToken ct)
    {
        if (!await _guard.ChoreLogIsInCallerHouseholdAsync(choreLogId, ct))
        {
            return NotFound(new ApiError("NotFound", "Chore log not found."));
        }

        await _currentUser.InitializeAsync();
        var result = await _dashboardService.QuickApproveAsync(choreLogId, _currentUser.UserId);
        if (!result.Success)
        {
            return BadRequest(new ApiError("ApproveFailed", result.ErrorMessage ?? "Could not approve the chore."));
        }

        return NoContent();
    }

    /// <summary>
    /// Responds to a Help request. Body.response: "CompletedByParent"
    /// (child gets credit), "Excused" (no penalty, no earning), or "Denied".
    /// </summary>
    [HttpPost("{choreLogId:int}/help/respond")]
    public async Task<IActionResult> RespondToHelp(
        int choreLogId,
        [FromBody] HelpRespondRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<HelpResponse>(request.Response, ignoreCase: true, out var response))
        {
            return BadRequest(new ApiError("InvalidResponse",
                "Response must be CompletedByParent, Excused, or Denied."));
        }

        if (!await _guard.ChoreLogIsInCallerHouseholdAsync(choreLogId, ct))
        {
            return NotFound(new ApiError("NotFound", "Chore log not found."));
        }

        await _currentUser.InitializeAsync();
        var result = await _trackerService.RespondToHelpRequestAsync(
            choreLogId, _currentUser.UserId, response, request.Note);

        if (!result.Success)
        {
            return BadRequest(new ApiError("HelpRespondFailed", result.ErrorMessage ?? "Could not respond to the Help request."));
        }

        return NoContent();
    }
}
