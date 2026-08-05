using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// The parent's at-a-glance view: today's family state, week earnings, and
/// everything waiting on them.
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Parent,Admin")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ICurrentUserContext _currentUser;

    public DashboardController(IDashboardService dashboardService, ICurrentUserContext currentUser)
    {
        _dashboardService = dashboardService;
        _currentUser = currentUser;
    }

    [HttpGet("parent")]
    public async Task<ActionResult<ParentDashboardResponse>> Parent(CancellationToken ct)
    {
        // Scope everything to the caller's family. A household-less caller gets
        // an empty dashboard, not everyone's — the unscoped call leaked every
        // family's balances and help texts (2026-08-04 audit).
        await _currentUser.InitializeAsync();
        var data = _currentUser.HouseholdId == null
            ? new ParentDashboardData()
            : await _dashboardService.GetParentDashboardAsync(_currentUser.HouseholdId);

        return Ok(new ParentDashboardResponse(
            data.TodayCompletedCount,
            data.TodayPendingCount,
            data.TodayApprovedCount,
            data.TodayHelpCount,
            data.TodayTotalChores,
            data.ThisWeekEarnings,
            data.WeeklyPotential,
            data.WeekEarnings.Select(w => new DailyEarningDto(w.Date, w.Amount)).ToList(),
            data.ChildrenProgress.Select(p => new ChildProgressDto(
                p.UserId, p.DisplayName, p.TotalChores, p.CompletedChores,
                p.ApprovedChores, p.PendingChores, p.HelpRequests)).ToList(),
            data.ChildrenBalances.Select(b => new ChildBalanceDto(b.DisplayName, b.Balance, b.CanCashOut)).ToList(),
            data.PendingApprovals.Select(a => new ApprovalItemDto(
                a.ChoreLogId, a.ChoreDefinitionId, a.ChoreName, a.ChildName, a.ChildUserId, a.EarnValue)).ToList(),
            data.HelpRequests.Select(h => new HelpRequestDto(
                h.ChoreLogId, h.ChoreDefinitionId, h.ChoreName, h.ChildName, h.ChildUserId,
                h.EarnValue, h.Reason, h.Date, h.RequestedAt)).ToList()));
    }
}
