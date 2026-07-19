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

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("parent")]
    public async Task<ActionResult<ParentDashboardResponse>> Parent(CancellationToken ct)
    {
        var data = await _dashboardService.GetParentDashboardAsync();

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
                h.Reason, h.Date, h.RequestedAt)).ToList()));
    }
}
